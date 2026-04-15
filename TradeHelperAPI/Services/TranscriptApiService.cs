using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace TradeHelper.Services
{
    /// <summary>TranscriptAPI.com — latest video transcript for configured TraderNick channel (Bearer API key).</summary>
    public class TranscriptApiService
    {
        private const string CacheKey = "TraderNickInsight_v1";
        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TranscriptApiService> _logger;

        private string ApiKey => _config["TrailBlazer:TranscriptApiKey"] ?? "";
        private string BaseUrl => (_config["TrailBlazer:TranscriptApiBaseUrl"] ?? "https://transcriptapi.com/api/v2").TrimEnd('/');
        private string? ChannelId => _config["TrailBlazer:TraderNickYoutubeChannelId"];
        private string? ChannelUrl => _config["TrailBlazer:TraderNickYoutubeChannelUrl"];

        public TranscriptApiService(HttpClient client, IConfiguration config, IMemoryCache cache, ILogger<TranscriptApiService> logger)
        {
            _client = client;
            _config = config;
            _cache = cache;
            _logger = logger;
            _client.Timeout = TimeSpan.FromSeconds(45);
        }

        /// <summary>Fetches or returns cached insight (default 6h). One call per refresh is enough.</summary>
        public async Task<TraderNickInsight?> GetInsightAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(ApiKey))
                return null;

            var cacheMinutes = _config.GetValue("TrailBlazer:TraderNickTranscriptCacheMinutes", 360);
            if (_cache.TryGetValue(CacheKey, out TraderNickInsight? cached) && cached != null)
                return cached;

            var insight = await FetchInsightCoreAsync(cancellationToken);
            if (insight != null && insight.HasData)
                _cache.Set(CacheKey, insight, TimeSpan.FromMinutes(cacheMinutes));
            return insight;
        }

        private async Task<TraderNickInsight?> FetchInsightCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                var channelId = ChannelId;
                if (string.IsNullOrEmpty(channelId) && !string.IsNullOrEmpty(ChannelUrl))
                    channelId = await ResolveChannelIdAsync(ChannelUrl, cancellationToken);

                if (string.IsNullOrEmpty(channelId))
                {
                    _logger.LogDebug("TranscriptAPI: no TraderNick channel id (set TrailBlazer:TraderNickYoutubeChannelId or TraderNickYoutubeChannelUrl)");
                    return null;
                }

                var videoId = await FetchLatestVideoIdAsync(channelId, cancellationToken);
                if (string.IsNullOrEmpty(videoId))
                {
                    _logger.LogWarning("TranscriptAPI: could not resolve latest video for channel {ChannelId}", channelId);
                    return null;
                }

                var (text, title) = await FetchTranscriptAsync(videoId, cancellationToken);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("TranscriptAPI: empty transcript for video {VideoId}", videoId);
                    return new TraderNickInsight { HasData = false, VideoId = videoId, VideoTitle = title };
                }

                var score = NewsSentimentHelper.ComputeFromTexts(new[] { text });
                return new TraderNickInsight
                {
                    HasData = true,
                    SentimentScore = score,
                    VideoId = videoId,
                    VideoTitle = title,
                    TranscriptText = text.Length > 120_000 ? text[..120_000] : text
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TranscriptAPI: TraderNick fetch failed");
                return null;
            }
        }

        private async Task<string?> ResolveChannelIdAsync(string channelUrl, CancellationToken cancellationToken)
        {
            var url = $"{BaseUrl}/youtube/channel/resolve?url={Uri.EscapeDataString(channelUrl)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            using var resp = await _client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogDebug("TranscriptAPI resolve failed: {Status} {Body}", (int)resp.StatusCode, json.Length > 200 ? json[..200] : json);
                return null;
            }
            return TryGetStringProperty(json, "channel_id", "channelId", "id");
        }

        private async Task<string?> FetchLatestVideoIdAsync(string channelId, CancellationToken cancellationToken)
        {
            var urls = new[]
            {
                $"{BaseUrl}/youtube/channel/latest?channel_id={Uri.EscapeDataString(channelId)}",
                $"{BaseUrl}/youtube/channel/latest?channelId={Uri.EscapeDataString(channelId)}",
            };
            foreach (var url in urls)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
                using var resp = await _client.SendAsync(req, cancellationToken);
                var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogDebug("TranscriptAPI latest {Url}: HTTP {Status}", url, (int)resp.StatusCode);
                    continue;
                }
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var id = ParseLatestVideoIdFromJson(doc.RootElement);
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "TranscriptAPI latest JSON parse failed");
                }
            }
            return null;
        }

        private static string? ParseLatestVideoIdFromJson(JsonElement root)
        {
            if (root.TryGetProperty("videos", out var vids) && vids.ValueKind == JsonValueKind.Array && vids.GetArrayLength() > 0)
            {
                var first = vids[0];
                if (first.TryGetProperty("id", out var idEl))
                    return idEl.GetString();
                if (first.TryGetProperty("video_id", out var vid))
                    return vid.GetString();
            }
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                var first = data[0];
                if (first.TryGetProperty("id", out var idEl))
                    return idEl.GetString();
            }
            if (root.TryGetProperty("video_id", out var topVid))
                return topVid.GetString();
            if (root.TryGetProperty("id", out var singleId) && root.TryGetProperty("title", out _))
                return singleId.GetString();
            return TryGetFirstVideoIdFromJson(root);
        }

        private static string? TryGetFirstVideoIdFromJson(JsonElement el)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0)
                {
                    var item = p.Value[0];
                    if (item.TryGetProperty("id", out var id)) return id.GetString();
                    if (item.TryGetProperty("video_id", out var vid)) return vid.GetString();
                }
            }
            return null;
        }

        private async Task<(string? Text, string? Title)> FetchTranscriptAsync(string videoId, CancellationToken cancellationToken)
        {
            var url = $"{BaseUrl}/youtube/transcript?video_url={Uri.EscapeDataString(videoId)}&format=text";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            using var resp = await _client.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogDebug("TranscriptAPI transcript HTTP {Status}", (int)resp.StatusCode);
                return (null, null);
            }

            var trimmed = body.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    var text = ExtractTranscriptText(root);
                    var title = TryGetStringFromElement(root, "title", "video_title");
                    return (text, title);
                }
                catch
                {
                    return (body, null);
                }
            }

            return (body, null);
        }

        private static string? ExtractTranscriptText(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.String)
                return root.GetString();
            var t = TryGetStringFromElement(root, "transcript", "text", "content", "data");
            if (!string.IsNullOrEmpty(t)) return t;
            if (root.TryGetProperty("segments", out var seg) && seg.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var s in seg.EnumerateArray())
                {
                    if (s.TryGetProperty("text", out var te))
                        parts.Add(te.GetString() ?? "");
                }
                if (parts.Count > 0) return string.Join(" ", parts);
            }
            return null;
        }

        private static string? TryGetStringFromElement(JsonElement root, params string[] names)
        {
            foreach (var n in names)
            {
                if (root.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
                    return p.GetString();
            }
            return null;
        }

        private static string? TryGetStringProperty(string json, params string[] names)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return TryGetStringFromElement(doc.RootElement, names);
            }
            catch
            {
                return null;
            }
        }
    }
}
