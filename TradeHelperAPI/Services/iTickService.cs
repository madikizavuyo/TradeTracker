using System.Text.Json;

namespace TradeHelper.Services
{
    /// <summary>
    /// Fetches forex/metals (and optional indices) from iTick API: kline for RSI/SMA/EMA, quote for last price.
    /// Auth: header "token". Endpoints: /forex/kline, /forex/quote (region=GB, code=EURUSD style).
    /// </summary>
    public class iTickService
    {
        private const string BaseUrl = "https://api.itick.org";
        private static DateTime _lastCall = DateTime.MinValue;
        private static readonly SemaphoreSlim _throttle = new(1, 1);
        private const double MinSecondsBetweenCalls = 0.3;

        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<iTickService> _logger;
        private readonly ApiRateLimitService _rateLimit;

        private string ApiKey => _config["TrailBlazer:iTickApiKey"] ?? _config["iTickApiKey"] ?? "";

        public iTickService(HttpClient client, IConfiguration config, ILogger<iTickService> logger, ApiRateLimitService rateLimit)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _rateLimit = rateLimit;
            _client.Timeout = TimeSpan.FromSeconds(30);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/json");
        }

        /// <summary>Maps our instrument name to iTick (region, code). Forex/metals use region=GB.</summary>
        public static (string region, string code)? ToiTickSymbol(string instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName)) return null;
            var upper = instrumentName.ToUpperInvariant().Replace("/", "").Replace("_", "").Replace(" ", "");

            // Forex: 6–8 letter pairs
            if (upper.Length >= 6 && upper.Length <= 8 && upper.All(char.IsLetter))
                return ("GB", upper);

            // Precious metals (often under forex on iTick)
            if (upper.StartsWith("XAU")) return ("GB", "XAUUSD");
            if (upper.StartsWith("XAG")) return ("GB", "XAGUSD");
            if (upper.StartsWith("XPT")) return ("GB", "XPTUSD");
            if (upper.StartsWith("XPD")) return ("GB", "XPDUSD");

            // Indices: US index symbols (if iTick supports index/fund endpoint)
            if (upper == "US500" || upper == "SPX") return ("US", "SPX");
            if (upper == "US30") return ("US", "DJI");
            if (upper == "US100") return ("US", "NDX");

            return null;
        }

        /// <summary>Whether this instrument is forex or metals (iTick forex endpoint).</summary>
        public static bool IsForexOrMetals(string instrumentName)
        {
            var t = ToiTickSymbol(instrumentName);
            if (t == null) return false;
            return t.Value.region == "GB";
        }

        private async Task ThrottleAsync()
        {
            await _throttle.WaitAsync();
            try
            {
                var elapsed = (DateTime.UtcNow - _lastCall).TotalSeconds;
                if (elapsed < MinSecondsBetweenCalls)
                    await Task.Delay(TimeSpan.FromMilliseconds((int)((MinSecondsBetweenCalls - elapsed) * 1000)));
                _lastCall = DateTime.UtcNow;
            }
            finally { _throttle.Release(); }
        }

        private async Task<List<double>?> FetchKlineClosesAsync(string region, string code, int limit = 300)
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                _logger.LogWarning("iTick API key not configured");
                return null;
            }
            if (await _rateLimit.IsBlockedAsync("iTick"))
                return null;

            await ThrottleAsync();

            // kType: 1–10 minute to monthly; use 7 for daily (common encoding). If 7 fails, try 5 or 6.
            var url = $"{BaseUrl}/forex/kline?region={Uri.EscapeDataString(region)}&code={Uri.EscapeDataString(code)}&kType=7&limit={limit}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("token", ApiKey);

            try
            {
                var response = await _client.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("iTick kline failed {StatusCode}: {Url}", response.StatusCode, url);
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        await _rateLimit.SetBlockedAsync("iTick");
                    return null;
                }
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return null;
                var closes = new List<double>();
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("c", out var close) && close.TryGetDouble(out var c))
                        closes.Add(c);
                }
                // API may return oldest-first; we need newest-first for RSI/SMA/EMA (index 0 = latest).
                if (closes.Count >= 14)
                {
                    closes.Reverse();
                    return closes;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "iTick kline fetch failed: {Url}", url);
                return null;
            }
        }

        private static double RsiFromCloses(IReadOnlyList<double> closes, int period = 14)
        {
            if (closes.Count < period + 1) return 50;
            double gains = 0, losses = 0;
            for (var i = 0; i < period; i++)
            {
                var change = closes[i] - closes[i + 1];
                if (change > 0) gains += change;
                else losses -= change;
            }
            var avgGain = gains / period;
            var avgLoss = losses / period;
            if (avgLoss == 0) return 100;
            var rs = avgGain / avgLoss;
            return 100 - (100 / (1 + rs));
        }

        private static double Sma(IReadOnlyList<double> closes, int period)
        {
            if (closes.Count < period) return 0;
            double sum = 0;
            for (var i = 0; i < period; i++)
                sum += closes[i];
            return sum / period;
        }

        private static double SmaOldest(IReadOnlyList<double> closes, int period)
        {
            if (closes.Count < period) return 0;
            double sum = 0;
            for (var i = closes.Count - period; i < closes.Count; i++)
                sum += closes[i];
            return sum / period;
        }

        private static double Ema(IReadOnlyList<double> closes, int period)
        {
            if (closes.Count < period) return 0;
            var k = 2.0 / (period + 1);
            var ema = SmaOldest(closes, period);
            for (var i = closes.Count - period - 1; i >= 0; i--)
                ema = closes[i] * k + ema * (1 - k);
            return ema;
        }

        /// <summary>Fetches daily kline and computes RSI(14), SMA14, SMA50, EMA50, EMA200. Same shape as Twelve Data for scoring.</summary>
        public async Task<Dictionary<string, double>> FetchTechnicalIndicatorsAsync(string instrumentName)
        {
            var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RSI"] = 50,
                ["SMA14"] = 0,
                ["SMA50"] = 0,
                ["EMA50"] = 0,
                ["EMA200"] = 0
            };

            var t = ToiTickSymbol(instrumentName);
            if (t == null || t.Value.region != "GB") return results; // only forex/metals use /forex/kline

            var (region, code) = t.Value;
            var closes = await FetchKlineClosesAsync(region, code);
            if (closes == null || closes.Count < 14) return results;

            results["RSI"] = RsiFromCloses(closes, 14);
            results["SMA14"] = Sma(closes, 14);
            if (closes.Count >= 50) results["SMA50"] = Sma(closes, 50);
            if (closes.Count >= 50) results["EMA50"] = Ema(closes, 50);
            if (closes.Count >= 200) results["EMA200"] = Ema(closes, 200);

            return results;
        }

        /// <summary>Fetches last price for forex/metals (region=GB). Returns 0 if not available.</summary>
        public async Task<double> FetchForexQuoteAsync(string symbol)
        {
            if (string.IsNullOrEmpty(ApiKey)) return 0;
            if (await _rateLimit.IsBlockedAsync("iTick")) return 0;

            var t = ToiTickSymbol(symbol);
            if (t == null || t.Value.region != "GB") return 0;

            await ThrottleAsync();
            var (region, code) = t.Value;
            var url = $"{BaseUrl}/forex/quote?region={Uri.EscapeDataString(region)}&code={Uri.EscapeDataString(code)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("token", ApiKey);

            try
            {
                var response = await _client.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return 0;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("ld", out var ld) && ld.TryGetDouble(out var last))
                    return last > 0 ? last : 0;
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "iTick quote failed for {Symbol}", symbol);
                return 0;
            }
        }
    }
}
