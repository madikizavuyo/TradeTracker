using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    public class TrailBlazerDataService
    {
        private static string? _myFxBookSessionCache;
        private static readonly object _myFxBookSessionLock = new();
        private static DateTime _lastBraveCall = DateTime.MinValue;
        private static readonly SemaphoreSlim _braveThrottle = new(1, 1);
        private const int BraveMinSecondsBetweenCalls = 1;

        private const string MyFxBookSessionKey = "MyFXBookSession";

        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<TrailBlazerDataService> _logger;
        private readonly TwelveDataService? _twelveDataService;
        private readonly MarketStackService? _marketStackService;
        private readonly iTickService? _iTickService;
        private readonly EodhdService? _eodhdService;
        private readonly FmpService? _fmpService;
        private readonly NasdaqDataLinkService? _nasdaqDataLinkService;
        private readonly IMemoryCache? _cache;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ApiRateLimitService _rateLimit;

        private string FredApiKey => _config["TrailBlazer:FredApiKey"] ?? "";
        private string BraveApiKey => _config["TrailBlazer:BraveApiKey"] ?? "";
        private string TaapiApiKey => _config["TrailBlazer:TaapiApiKey"] ?? "";
        private string ExchangeRateApiKey => _config["TrailBlazer:ExchangeRateApiKey"] ?? "";
        private string FinnhubApiKey => _config["TrailBlazer:FinnhubApiKey"] ?? _config["FinnhubApiKey"] ?? "";
        private string MyFxBookEmail => _config["TrailBlazer:MyFXBookEmail"] ?? _config["MyFXBook:Email"] ?? "";
        private string MyFxBookPassword => _config["TrailBlazer:MyFXBookPassword"] ?? _config["MyFXBook:Password"] ?? "";

        public TrailBlazerDataService(HttpClient client, IConfiguration config, ILogger<TrailBlazerDataService> logger, IServiceScopeFactory scopeFactory, ApiRateLimitService rateLimit, TwelveDataService? twelveDataService = null, MarketStackService? marketStackService = null, iTickService? iTickService = null, EodhdService? eodhdService = null, FmpService? fmpService = null, NasdaqDataLinkService? nasdaqDataLinkService = null, IMemoryCache? cache = null)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _rateLimit = rateLimit;
            _twelveDataService = twelveDataService;
            _marketStackService = marketStackService;
            _iTickService = iTickService;
            _eodhdService = eodhdService;
            _fmpService = fmpService;
            _nasdaqDataLinkService = nasdaqDataLinkService;
            _cache = cache;
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        // ────── FRED Economic Data ──────

        public async Task<double> FetchFredDataAsync(string seriesId)
        {
            if (string.IsNullOrEmpty(FredApiKey)) return 0;
            if (await _rateLimit.IsBlockedAsync("FRED")) return 0;
            try
            {
                var url = $"https://api.stlouisfed.org/fred/series/observations?series_id={seriesId}&api_key={FredApiKey}&file_type=json&sort_order=desc&limit=1";
                var json = await _client.GetStringAsync(url);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error_message", out var errMsg))
                {
                    var msg = errMsg.GetString();
                    if (ApiRateLimitService.IsCreditLimitMessage(msg ?? ""))
                        await _rateLimit.SetBlockedAsync("FRED");
                    return 0;
                }
                var observations = doc.RootElement.GetProperty("observations");
                if (observations.GetArrayLength() > 0)
                {
                    var val = observations[0].GetProperty("value").GetString();
                    if (double.TryParse(val, out var result)) return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FRED fetch failed for {SeriesId}", seriesId);
            }
            return 0;
        }

        /// <summary>Fetches FRED observations and computes year-over-year percentage change. For quarterly data uses 5 obs (current + 4 quarters).</summary>
        public async Task<double?> FetchFredYoYPercentAsync(string seriesId, int observationCount = 5)
        {
            if (string.IsNullOrEmpty(FredApiKey)) return null;
            if (await _rateLimit.IsBlockedAsync("FRED")) return null;
            try
            {
                var url = $"https://api.stlouisfed.org/fred/series/observations?series_id={seriesId}&api_key={FredApiKey}&file_type=json&sort_order=desc&limit={observationCount}";
                var json = await _client.GetStringAsync(url);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error_message", out var errMsg))
                {
                    var msg = errMsg.GetString();
                    if (ApiRateLimitService.IsCreditLimitMessage(msg ?? ""))
                        await _rateLimit.SetBlockedAsync("FRED");
                    return null;
                }
                var observations = doc.RootElement.GetProperty("observations");
                if (observations.GetArrayLength() < observationCount) return null;

                var currentVal = observations[0].GetProperty("value").GetString();
                var previousVal = observations[observationCount - 1].GetProperty("value").GetString();
                if (string.IsNullOrEmpty(currentVal) || string.IsNullOrEmpty(previousVal) || currentVal == "." || previousVal == ".") return null;
                if (!double.TryParse(currentVal, out var current) || !double.TryParse(previousVal, out var previous)) return null;
                if (previous == 0) return null;

                var yoyPercent = ((current - previous) / previous) * 100.0;
                return yoyPercent;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FRED YoY fetch failed for {SeriesId}", seriesId);
                return null;
            }
        }

        public async Task<Dictionary<string, double>> FetchFredEconomicDataAsync()
        {
            var series = new Dictionary<string, string>
            {
                ["GDP"] = "GDPC1",
                ["CPI"] = "CPIAUCSL",
                ["Unemployment"] = "UNRATE",
                ["FedRate"] = "FEDFUNDS",
                ["Nonfarm"] = "PAYEMS",
                ["ConsumerSentiment"] = "UMCSENT"
            };

            var results = new Dictionary<string, double>();
            foreach (var kv in series)
            {
                results[kv.Key] = await FetchFredDataAsync(kv.Value);
                await Task.Delay(100); // rate limiting
            }
            return results;
        }

        // ────── COT Data (CFTC direct) ──────
        // Sole source: https://www.cftc.gov/dea/options/financial_lof.htm

        private static readonly Dictionary<string, string> CftcToSymbol = new(StringComparer.OrdinalIgnoreCase)
        {
            ["EURO FX"] = "EURUSD",
            ["BRITISH POUND"] = "GBPUSD",
            ["JAPANESE YEN"] = "USDJPY",
            ["SWISS FRANC"] = "USDCHF",
            ["CANADIAN DOLLAR"] = "USDCAD",
            ["AUSTRALIAN DOLLAR"] = "AUDUSD",
            ["NZ DOLLAR"] = "NZDUSD",
            ["MEXICAN PESO"] = "USDMXN",
            ["BRAZILIAN REAL"] = "USDBRL",
            ["SO AFRICAN RAND"] = "USDZAR",
            ["EURO FX/BRITISH POUND XRATE"] = "EURGBP",
            ["EURO FX/JAPANESE YEN XRATE"] = "EURJPY",
        };

        public async Task<Dictionary<string, COTReport>> FetchCOTReportBatchAsync()
        {
            var result = new Dictionary<string, COTReport>();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.cftc.gov/dea/options/financial_lof.htm");
                req.Headers.TryAddWithoutValidation("User-Agent", "TradeTracker/1.0 (https://github.com/tradetracker)");
                var response = await _client.SendAsync(req);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var text = doc.DocumentNode.InnerText;
                var reports = ParseCftcFinancialLof(text);
                foreach (var r in reports)
                    if (!result.ContainsKey(r.Symbol) || r.ReportDate > result[r.Symbol].ReportDate)
                        result[r.Symbol] = r;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CFTC COT fetch failed");
            }
            return result;
        }

        private static List<COTReport> ParseCftcFinancialLof(string text)
        {
            var reports = new List<COTReport>();
            var reportDateMatch = Regex.Match(text, @"as of ([A-Za-z]+) (\d{1,2}), (\d{4})");
            var reportDate = reportDateMatch.Success && DateTime.TryParse($"{reportDateMatch.Groups[1].Value} {reportDateMatch.Groups[2].Value}, {reportDateMatch.Groups[3].Value}", out var rd)
                ? rd : DateTime.UtcNow;

            var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var exchangeIdx = line.IndexOf(" - CHICAGO MERCANTILE EXCHANGE", StringComparison.Ordinal);
                if (exchangeIdx < 0) exchangeIdx = line.IndexOf(" - CHICAGO BOARD OF TRADE", StringComparison.Ordinal);
                if (exchangeIdx < 0) continue;

                var name = line[..exchangeIdx].Trim();
                if (!CftcToSymbol.TryGetValue(name, out var symbol)) continue;

                long openInterest = 0;
                for (var j = i + 1; j < Math.Min(i + 5, lines.Length); j++)
                {
                    var oiMatch = Regex.Match(lines[j], @"Open Interest is\s+([\d,]+)");
                    if (oiMatch.Success && long.TryParse(oiMatch.Groups[1].Value.Replace(",", ""), out openInterest))
                        break;
                }
                if (openInterest == 0) continue;

                string? positionsLine = null;
                for (var j = i + 1; j < Math.Min(i + 10, lines.Length); j++)
                {
                    if (lines[j].Trim().Equals("Positions", StringComparison.OrdinalIgnoreCase) && j + 1 < lines.Length)
                    {
                        positionsLine = lines[j + 1];
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(positionsLine)) continue;

                var nums = Regex.Matches(positionsLine, @"-?\d[\d,]*")
                    .Select(x => long.TryParse(x.Value.Replace(",", ""), out var v) ? v : 0L)
                    .ToList();
                if (nums.Count < 14) continue;

                var commercialLong = nums[0];
                var commercialShort = nums[1];
                var nonCommercialLong = nums[3] + nums[6] + nums[9];
                var nonCommercialShort = nums[4] + nums[7] + nums[10];

                reports.Add(new COTReport
                {
                    Symbol = symbol,
                    CommercialLong = commercialLong,
                    CommercialShort = commercialShort,
                    NonCommercialLong = nonCommercialLong,
                    NonCommercialShort = nonCommercialShort,
                    OpenInterest = openInterest,
                    ReportDate = reportDate
                });
            }
            return reports;
        }

        public async Task<COTReport?> FetchCOTDataAsync(string symbol)
        {
            var batch = await FetchCOTReportBatchAsync();
            return batch.TryGetValue(symbol, out var r) ? r : null;
        }

        // ────── News (Finnhub first to save Brave quota; Brave fallback for instrument-specific) ──────

        public async Task<List<NewsItem>> FetchNewsForSymbolAsync(string symbol, string? assetClass)
        {
            var items = new List<NewsItem>();

            // Finnhub first (free) — saves Brave API calls during background refresh
            if (!string.IsNullOrEmpty(FinnhubApiKey) && !await _rateLimit.IsBlockedAsync("Finnhub"))
            {
                var category = (assetClass ?? "").StartsWith("Forex", StringComparison.OrdinalIgnoreCase) ? "forex" : "general";
                try
                {
                    var url = $"https://finnhub.io/api/v1/news?category={category}&token={FinnhubApiKey}";
                    var json = await _client.GetStringAsync(url);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("error", out var errEl))
                    {
                        var errMsg = errEl.GetString();
                        if (ApiRateLimitService.IsCreditLimitMessage(errMsg ?? ""))
                            await _rateLimit.SetBlockedAsync("Finnhub");
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            items.Add(new NewsItem
                            {
                                Headline = el.TryGetProperty("headline", out var h) ? h.GetString() ?? "" : "",
                                Summary = el.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
                                Source = el.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "",
                                Url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                                ImageUrl = el.TryGetProperty("image", out var img) ? img.GetString() ?? "" : "",
                                PublishedAt = el.TryGetProperty("datetime", out var dt) && dt.TryGetInt64(out var ts)
                                    ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime
                                    : DateTime.UtcNow
                            });
                        }
                        if (items.Count > 0) return items;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Finnhub news fetch failed for {Symbol}, trying Brave", symbol);
                }
            }

            // Brave fallback (instrument-specific, paid) — only when Finnhub empty/fails
            if (items.Count == 0 && !string.IsNullOrEmpty(BraveApiKey) && !await _rateLimit.IsBlockedAsync("Brave"))
            {
                try
                {
                    await BraveThrottleAsync();
                    var query = BuildNewsSearchQuery(symbol, assetClass);
                    var url = $"https://api.search.brave.com/res/v1/news/search?q={Uri.EscapeDataString(query)}&count=5&freshness=pw";
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("X-Subscription-Token", BraveApiKey);
                    var response = await _client.SendAsync(req);
                    var json = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode == 429 || ApiRateLimitService.IsCreditLimitMessage(json))
                            await _rateLimit.SetBlockedAsync("Brave");
                    }
                    else
                    {
                        var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var el in results.EnumerateArray())
                            {
                                var urlStr = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                                var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                                var desc = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                                var source = "";
                                if (el.TryGetProperty("metadata", out var meta))
                                {
                                    if (meta.TryGetProperty("source", out var src)) source = src.GetString() ?? "";
                                    if (string.IsNullOrEmpty(source) && meta.TryGetProperty("host", out var host)) source = host.GetString() ?? "";
                                }
                                if (string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(urlStr) && Uri.TryCreate(urlStr, UriKind.Absolute, out var uri))
                                    source = uri.Host;
                                var published = DateTime.UtcNow;
                                if (el.TryGetProperty("published", out var pub))
                                {
                                    var pubStr = pub.GetString();
                                    if (!string.IsNullOrEmpty(pubStr) && DateTime.TryParse(pubStr, out var dt))
                                        published = dt.ToUniversalTime();
                                }
                                items.Add(new NewsItem
                                {
                                    Headline = title,
                                    Summary = desc,
                                    Source = source,
                                    Url = urlStr,
                                    ImageUrl = "",
                                    PublishedAt = published
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Brave news fetch failed for {Symbol}", symbol);
                }
            }
            return items;
        }

        private static async Task BraveThrottleAsync()
        {
            await _braveThrottle.WaitAsync();
            try
            {
                var elapsed = (DateTime.UtcNow - _lastBraveCall).TotalSeconds;
                if (elapsed < BraveMinSecondsBetweenCalls)
                    await Task.Delay(TimeSpan.FromSeconds(BraveMinSecondsBetweenCalls - elapsed));
                _lastBraveCall = DateTime.UtcNow;
            }
            finally { _braveThrottle.Release(); }
        }

        /// <summary>Fetches news for an instrument and computes a sentiment score (1-10) from headlines using keyword analysis.</summary>
        public async Task<(double score, bool hasData)> FetchNewsSentimentScoreAsync(string symbol, string? assetClass)
        {
            var news = await FetchNewsForSymbolAsync(symbol, assetClass);
            if (news == null || news.Count == 0)
                return (5.0, false);

            var score = ComputeNewsSentimentFromHeadlines(news.Select(n => n.Headline + " " + n.Summary).Where(s => !string.IsNullOrWhiteSpace(s)).ToList());
            return (score, true);
        }

        /// <summary>Fetches news and returns items for storage. Use when caller needs to persist to DB.</summary>
        public async Task<(double score, bool hasData, List<NewsItem> items)> FetchNewsSentimentScoreWithItemsAsync(string symbol, string? assetClass)
        {
            var news = await FetchNewsForSymbolAsync(symbol, assetClass);
            if (news == null || news.Count == 0)
                return (5.0, false, new List<NewsItem>());

            var score = ComputeNewsSentimentFromHeadlines(news.Select(n => n.Headline + " " + n.Summary).Where(s => !string.IsNullOrWhiteSpace(s)).ToList());
            return (score, true, news);
        }

        private static double ComputeNewsSentimentFromHeadlines(IReadOnlyList<string> texts)
        {
            if (texts.Count == 0) return 5.0;

            var positive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "rally", "surge", "strong", "gains", "bullish", "positive", "breakout", "rise", "soar", "jump", "climb",
                "recovery", "rebound", "outperform", "upgrade", "optimistic", "growth", "record high", "all-time high"
            };
            var negative = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "slump", "crash", "fall", "drop", "bearish", "negative", "recession", "tumble", "plunge", "decline",
                "sell-off", "downgrade", "pessimistic", "weak", "loss", "record low", "collapse", "crisis"
            };

            double sum = 0;
            int count = 0;
            foreach (var text in texts)
            {
                var words = text.Split([' ', '.', ',', '!', '?', ':', ';', '-', '\'', '"'], StringSplitOptions.RemoveEmptyEntries);
                var posCount = words.Count(w => positive.Contains(w));
                var negCount = words.Count(w => negative.Contains(w));
                var diff = posCount - negCount;
                if (diff != 0 || posCount + negCount > 0)
                {
                    sum += Math.Clamp(5.0 + diff * 1.5, 1.0, 10.0);
                    count++;
                }
            }

            if (count == 0) return 5.0;
            return Math.Clamp(sum / count, 1.0, 10.0);
        }

        /// <summary>Brave Web Search - returns title, url, description for each result. Rate-limited and cacheable.</summary>
        public async Task<List<WebSearchResult>> BraveWebSearchAsync(string query, int count = 3, string freshness = "pw")
        {
            var results = new List<WebSearchResult>();
            if (string.IsNullOrEmpty(BraveApiKey)) return results;
            if (await _rateLimit.IsBlockedAsync("Brave")) return results;
            try
            {
                await BraveThrottleAsync();
                var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={count}&freshness={freshness}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("X-Subscription-Token", BraveApiKey);
                var response = await _client.SendAsync(req);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 429 || ApiRateLimitService.IsCreditLimitMessage(json))
                        await _rateLimit.SetBlockedAsync("Brave");
                    return results;
                }
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("web", out var web) && web.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        var urlStr = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                        var desc = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                        var source = "";
                        if (el.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("host", out var host))
                            source = host.GetString() ?? "";
                        if (string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(urlStr) && Uri.TryCreate(urlStr, UriKind.Absolute, out var uri))
                            source = uri.Host;
                        results.Add(new WebSearchResult { Title = title, Url = urlStr, Description = desc, Source = source });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Brave web search failed for query: {Query}", query);
            }
            return results;
        }

        /// <summary>Fetches market outlook/forecast snippets for an instrument via Brave web search. Cached 1h per symbol.</summary>
        public async Task<List<WebSearchResult>> FetchInstrumentOutlookAsync(string symbol, string? assetClass)
        {
            var cacheKey = $"brave_outlook:{symbol}:{assetClass ?? ""}";
            if (_cache != null && _cache.TryGetValue(cacheKey, out List<WebSearchResult>? cached))
                return cached ?? new List<WebSearchResult>();

            var query = BuildOutlookSearchQuery(symbol, assetClass);
            var results = await BraveWebSearchAsync(query, 3, "pm");
            if (_cache != null && results.Count > 0)
                _cache.Set(cacheKey, results, TimeSpan.FromMinutes(60));
            return results;
        }

        private static string BuildOutlookSearchQuery(string symbol, string? assetClass)
        {
            if (symbol.Length >= 6 && symbol.All(char.IsLetter))
            {
                var baseCcy = symbol[..3];
                var quoteCcy = symbol[3..];
                return $"{baseCcy} {quoteCcy} forex forecast outlook";
            }
            if (symbol.StartsWith("XAU", StringComparison.OrdinalIgnoreCase)) return "gold price forecast outlook";
            if (symbol.StartsWith("XAG", StringComparison.OrdinalIgnoreCase)) return "silver price forecast";
            if (symbol.StartsWith("USOIL", StringComparison.OrdinalIgnoreCase)) return "crude oil price forecast";
            if (symbol == "US500" || symbol == "SPX") return "S&P 500 forecast outlook";
            if (symbol == "US30") return "Dow Jones forecast";
            if (symbol == "US100") return "Nasdaq forecast";
            return $"{symbol} forecast outlook";
        }

        private static string BuildNewsSearchQuery(string symbol, string? assetClass)
        {
            if (symbol.Length >= 6 && symbol.All(char.IsLetter))
            {
                var baseCcy = symbol[..3];
                var quoteCcy = symbol[3..];
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EUR"] = "euro", ["USD"] = "dollar", ["GBP"] = "pound", ["JPY"] = "yen", ["CHF"] = "franc",
                    ["AUD"] = "australian dollar", ["CAD"] = "canadian dollar", ["NZD"] = "new zealand dollar",
                    ["ZAR"] = "rand", ["SEK"] = "krona", ["CNY"] = "yuan"
                };
                var b = names.TryGetValue(baseCcy, out var bn) ? bn : baseCcy;
                var q = names.TryGetValue(quoteCcy, out var qn) ? qn : quoteCcy;
                return $"{b} {q} forex";
            }
            if (symbol.StartsWith("XAU", StringComparison.OrdinalIgnoreCase)) return "gold price forex";
            if (symbol.StartsWith("XAG", StringComparison.OrdinalIgnoreCase)) return "silver price";
            if (symbol.StartsWith("XPT", StringComparison.OrdinalIgnoreCase)) return "platinum price";
            if (symbol.StartsWith("XPD", StringComparison.OrdinalIgnoreCase)) return "palladium price";
            if (symbol.StartsWith("USOIL", StringComparison.OrdinalIgnoreCase) || symbol == "OIL") return "crude oil price";
            if (symbol == "US500" || symbol == "SPX") return "S&P 500";
            if (symbol == "US30") return "Dow Jones";
            if (symbol == "US100") return "Nasdaq";
            if (symbol == "DE40") return "DAX Germany";
            if (symbol == "UK100") return "FTSE 100";
            if (symbol == "JP225") return "Nikkei 225";
            return $"{symbol} market news";
        }

        // ────── Technical Indicators (Twelve Data only) ──────

        /// <summary>Fetches technical indicators from Twelve Data only (RSI, EMA50, EMA200). MACD not used.</summary>
        public async Task<Dictionary<string, double>> FetchTechnicalIndicatorsAsync(string symbol)
        {
            var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RSI"] = 50,
                ["MACD"] = 0,
                ["MACDSignal"] = 0,
                ["EMA50"] = 0,
                ["EMA200"] = 0
            };

            if (_twelveDataService == null) return results;

            var td = await _twelveDataService.FetchTechnicalIndicatorsAsync(symbol);
            if (td.TryGetValue("RSI", out var rsi)) results["RSI"] = rsi;
            if (td.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
            if (td.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;
            return results;
        }

        /// <summary>Fetches technical indicators (Twelve Data, then Market Stack fallback), stores in DB, returns dict and source name.</summary>
        public async Task<(Dictionary<string, double> technicals, string? source)> FetchAndStoreTechnicalIndicatorsAsync(ApplicationDbContext db, int instrumentId, string symbol)
        {
            var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RSI"] = 50,
                ["MACD"] = 0,
                ["MACDSignal"] = 0,
                ["EMA50"] = 0,
                ["EMA200"] = 0
            };
            string? sourceUsed = null;

            if (_twelveDataService != null)
            {
                var td = await _twelveDataService.FetchTechnicalIndicatorsAsync(symbol);
                if (td.TryGetValue("RSI", out var rsi)) results["RSI"] = rsi;
                if (td.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
                if (td.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;

                var rsiVal = td.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                var sma14 = td.TryGetValue("SMA14", out var s14) && s14 != 0 ? s14 : (double?)null;
                var sma50 = td.TryGetValue("SMA50", out var s50) && s50 != 0 ? s50 : (double?)null;
                var ema50Val = td.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                var ema200Val = td.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;

                if (rsiVal != null || sma14 != null || sma50 != null || ema50Val != null || ema200Val != null)
                {
                    sourceUsed = "TwelveData";
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, "TwelveData");
                }
                else
                    _logger.LogDebug("Technicals: TwelveData returned no usable data for {Symbol}, skipping DB write", symbol);
            }

            // Fallback: Market Stack (indices: US500, US30, US100, DE40, UK100, JP225)
            if (sourceUsed == null && _marketStackService != null && results.Values.All(v => v == 0 || v == 50))
            {
                var ms = await _marketStackService.FetchTechnicalIndicatorsAsync(symbol);
                if (ms.TryGetValue("RSI", out var rsi) && rsi > 0 && rsi < 100) results["RSI"] = rsi;
                if (ms.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
                if (ms.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;
                if (ms.TryGetValue("SMA14", out var s14) && s14 != 0) { /* optional for scoring */ }

                if (results.Values.Any(v => v != 0 && v != 50))
                {
                    sourceUsed = "MarketStack";
                    var rsiVal = results.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                    var ema50Val = results.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                    var ema200Val = results.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;
                    double? sma14 = ms.TryGetValue("SMA14", out var s) && s != 0 ? s : null;
                    double? sma50 = ms.TryGetValue("SMA50", out var s5) && s5 != 0 ? s5 : null;
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, "MarketStack");
                }
                else
                    _logger.LogDebug("Technicals: MarketStack returned no usable data for {Symbol}, skipping DB write", symbol);
            }

            // Fallback: iTick (forex & metals: region=GB; kline → RSI/SMA/EMA)
            if (sourceUsed == null && _iTickService != null && results.Values.All(v => v == 0 || v == 50) && iTickService.IsForexOrMetals(symbol))
            {
                var it = await _iTickService.FetchTechnicalIndicatorsAsync(symbol);
                if (it.TryGetValue("RSI", out var rsi) && rsi > 0 && rsi < 100) results["RSI"] = rsi;
                if (it.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
                if (it.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;
                if (it.TryGetValue("SMA14", out var s14) && s14 != 0) { /* optional */ }

                if (results.Values.Any(v => v != 0 && v != 50))
                {
                    sourceUsed = "iTick";
                    var rsiVal = results.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                    var ema50Val = results.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                    var ema200Val = results.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;
                    double? sma14 = it.TryGetValue("SMA14", out var s) && s != 0 ? s : null;
                    double? sma50 = it.TryGetValue("SMA50", out var s5) && s5 != 0 ? s5 : null;
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, "iTick");
                }
                else
                    _logger.LogDebug("Technicals: iTick returned no usable data for {Symbol}, skipping DB write", symbol);
            }

            // Fallback: EODHD (forex, metals, indices: technical API RSI/SMA/EMA)
            if (sourceUsed == null && _eodhdService != null && results.Values.All(v => v == 0 || v == 50) && EodhdService.ToEodhdSymbol(symbol) != null)
            {
                var eod = await _eodhdService.FetchTechnicalIndicatorsAsync(symbol);
                if (eod.TryGetValue("RSI", out var rsi) && rsi > 0 && rsi < 100) results["RSI"] = rsi;
                if (eod.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
                if (eod.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;
                if (eod.TryGetValue("SMA14", out var s14) && s14 != 0) { /* optional */ }

                if (results.Values.Any(v => v != 0 && v != 50))
                {
                    sourceUsed = "EODHD";
                    var rsiVal = results.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                    var ema50Val = results.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                    var ema200Val = results.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;
                    double? sma14 = eod.TryGetValue("SMA14", out var s) && s != 0 ? s : null;
                    double? sma50 = eod.TryGetValue("SMA50", out var s5) && s5 != 0 ? s5 : null;
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, "EODHD");
                }
                else
                    _logger.LogDebug("Technicals: EODHD returned no usable data for {Symbol}, skipping DB write", symbol);
            }

            // Fallback: FMP (quote priceAvg50/200 + daily RSI/SMA/EMA for forex, indices, commodities)
            if (sourceUsed == null && _fmpService != null && results.Values.All(v => v == 0 || v == 50) && FmpService.ToFmpSymbol(symbol) != null)
            {
                var fmp = await _fmpService.FetchTechnicalIndicatorsAsync(symbol);
                if (fmp.TryGetValue("RSI", out var rsi) && rsi > 0 && rsi < 100) results["RSI"] = rsi;
                if (fmp.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
                if (fmp.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;
                if (fmp.TryGetValue("SMA14", out var s14) && s14 != 0) { /* optional */ }

                if (results.Values.Any(v => v != 0 && v != 50))
                {
                    sourceUsed = "FMP";
                    var rsiVal = results.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                    var ema50Val = results.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                    var ema200Val = results.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;
                    double? sma14 = fmp.TryGetValue("SMA14", out var s) && s != 0 ? s : null;
                    double? sma50 = fmp.TryGetValue("SMA50", out var s5) && s5 != 0 ? s5 : null;
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, "FMP");
                }
                else
                    _logger.LogDebug("Technicals: FMP returned no usable data for {Symbol}, skipping DB write", symbol);
            }

            // Fallback: Nasdaq Data Link (FRED forex time series → RSI/SMA/EMA)
            if (sourceUsed == null && _nasdaqDataLinkService != null && results.Values.All(v => v == 0 || v == 50) && NasdaqDataLinkService.ToNasdaqDataset(symbol) != null)
            {
                var ndl = await _nasdaqDataLinkService.FetchTechnicalIndicatorsAsync(symbol);
                if (ndl.TryGetValue("RSI", out var rsi) && rsi > 0 && rsi < 100) results["RSI"] = rsi;
                if (ndl.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
                if (ndl.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;
                if (ndl.TryGetValue("SMA14", out var s14) && s14 != 0) { /* optional */ }

                if (results.Values.Any(v => v != 0 && v != 50))
                {
                    sourceUsed = "NasdaqDataLink";
                    var rsiVal = results.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                    var ema50Val = results.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                    var ema200Val = results.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;
                    double? sma14 = ndl.TryGetValue("SMA14", out var s) && s != 0 ? s : null;
                    double? sma50 = ndl.TryGetValue("SMA50", out var s5) && s5 != 0 ? s5 : null;
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, "NasdaqDataLink");
                }
                else
                    _logger.LogDebug("Technicals: NasdaqDataLink returned no usable data for {Symbol}, skipping DB write", symbol);
            }

            // If still no usable data, use latest from DB (scoring only; no DB write)
            if (sourceUsed == null && !results.Values.Any(v => v != 0 && v != 50))
            {
                _logger.LogDebug("Technicals: no provider had data for {Symbol}; using latest from DB for scoring only (not posting to database)", symbol);
                var latest = await db.TechnicalIndicators
                    .Where(t => t.InstrumentId == instrumentId)
                    .OrderByDescending(t => t.DateCollected)
                    .FirstOrDefaultAsync();
                if (latest != null)
                {
                    if (latest.RSI.HasValue && latest.RSI > 0 && latest.RSI < 100) results["RSI"] = latest.RSI.Value;
                    if (latest.EMA50.HasValue && latest.EMA50 != 0) results["EMA50"] = latest.EMA50.Value;
                    if (latest.EMA200.HasValue && latest.EMA200 != 0) results["EMA200"] = latest.EMA200.Value;
                }
            }

            return (results, sourceUsed);
        }

        /// <summary>Saves technical indicators only when we have at least one value. When updating, only overwrites fields we received (non-null); preserves existing DB values for missing data.</summary>
        private async Task SaveTechnicalToDbAsync(ApplicationDbContext db, int instrumentId, string symbol, double? rsiVal, double? sma14, double? sma50, double? ema50Val, double? ema200Val, string source)
        {
            if (!rsiVal.HasValue && !sma14.HasValue && !sma50.HasValue && !ema50Val.HasValue && !ema200Val.HasValue)
            {
                _logger.LogDebug("Not posting technicals to database: no data received for {Symbol} from {Source}", symbol, source);
                return;
            }
            var today = DateTime.UtcNow.Date;
            var now = DateTime.UtcNow;
            var existing = await db.TechnicalIndicators.FirstOrDefaultAsync(t => t.InstrumentId == instrumentId && t.Date == today);
            if (existing != null)
            {
                if (rsiVal.HasValue) existing.RSI = rsiVal;
                if (sma14.HasValue) existing.SMA14 = sma14;
                if (sma50.HasValue) existing.SMA50 = sma50;
                if (ema50Val.HasValue) existing.EMA50 = ema50Val;
                if (ema200Val.HasValue) existing.EMA200 = ema200Val;
                existing.DateCollected = now;
            }
            else
            {
                db.TechnicalIndicators.Add(new TechnicalIndicator
                {
                    InstrumentId = instrumentId,
                    Date = today,
                    RSI = rsiVal,
                    SMA14 = sma14,
                    SMA50 = sma50,
                    EMA50 = ema50Val,
                    EMA200 = ema200Val,
                    DateCollected = now
                });
            }
            _logger.LogInformation("Technicals: saved for {Symbol} from {Source} (RSI={HasRsi}, SMA14={HasSma14}, SMA50={HasSma50}, EMA50={HasEma50}, EMA200={HasEma200})", symbol, source, rsiVal.HasValue, sma14.HasValue, sma50.HasValue, ema50Val.HasValue, ema200Val.HasValue);
        }

        /// <summary>Loads technical data from Twelve Data only into TechnicalIndicators table.</summary>
        public async Task<int> LoadTechnicalDataToDatabaseAsync(ApplicationDbContext db, int? limit = null)
        {
            if (_twelveDataService == null) return 0;
            return await _twelveDataService.LoadTechnicalDataToDatabaseAsync(db, limit);
        }

        // ────── Forex & Economic Calendar ──────
        // Finnhub (primary when key set), ExchangeRate-API, EODHD as fallbacks.

        /// <summary>Normalizes forex symbol to 6-char format (e.g. EUR/USD -> EURUSD).</summary>
        private static string NormalizeForexSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return "";
            return symbol.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
        }

        private static string MapToFinnhubForexSymbol(string symbol)
        {
            if (symbol.Length < 6) return "";
            var baseCcy = symbol[..3];
            var quoteCcy = symbol[3..];
            return $"OANDA:{baseCcy}_{quoteCcy}";
        }

        public async Task<double> FetchForexQuoteAsync(string symbol)
        {
            if (symbol.Length < 6) return 0;
            var baseCcy = symbol[..3];
            var quoteCcy = symbol[3..];

            // Try Finnhub first (real-time forex quote)
            if (!string.IsNullOrEmpty(FinnhubApiKey))
            {
                try
                {
                    var finnhubSymbol = MapToFinnhubForexSymbol(symbol);
                    var url = $"https://finnhub.io/api/v1/quote?symbol={Uri.EscapeDataString(finnhubSymbol)}&token={FinnhubApiKey}";
                    var json = await _client.GetStringAsync(url);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("c", out var c) && c.ValueKind == JsonValueKind.Number)
                    {
                        var v = c.GetDouble();
                        if (v > 0) return v;
                    }
                    // Fallback: try forex/candle for last close if quote returns 0
                    var to = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var from = to - 86400 * 2; // 2 days
                    var candleUrl = $"https://finnhub.io/api/v1/forex/candle?symbol={Uri.EscapeDataString(finnhubSymbol)}&resolution=D&from={from}&to={to}&token={FinnhubApiKey}";
                    var candleJson = await _client.GetStringAsync(candleUrl);
                    var candleDoc = JsonDocument.Parse(candleJson);
                    if (candleDoc.RootElement.TryGetProperty("c", out var closes) && closes.GetArrayLength() > 0)
                    {
                        var lastClose = closes[closes.GetArrayLength() - 1].GetDouble();
                        if (lastClose > 0) return lastClose;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Finnhub forex fetch failed for {Symbol}, falling back", symbol);
                }
            }

            // Fallback: Twelve Data (rate-limited ~8 req/min)
            if (_twelveDataService != null)
            {
                try
                {
                    var rate = await _twelveDataService.FetchExchangeRateAsync(symbol);
                    if (rate.HasValue && rate.Value > 0) return rate.Value;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Twelve Data forex fetch failed for {Symbol}, falling back", symbol);
                }
            }

            // Fallback: iTick (forex/metals quote)
            if (_iTickService != null && iTickService.IsForexOrMetals(symbol))
            {
                try
                {
                    var quote = await _iTickService.FetchForexQuoteAsync(symbol);
                    if (quote > 0) return quote;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "iTick forex quote failed for {Symbol}, falling back", symbol);
                }
            }

            // Fallback: EODHD (real-time quote for forex, metals, indices)
            if (_eodhdService != null && EodhdService.ToEodhdSymbol(symbol) != null)
            {
                try
                {
                    var quote = await _eodhdService.FetchRealTimeQuoteAsync(symbol);
                    if (quote > 0) return quote;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "EODHD quote failed for {Symbol}, falling back", symbol);
                }
            }

            // Fallback: FMP (stable quote for forex, indices, commodities)
            if (_fmpService != null && FmpService.ToFmpSymbol(symbol) != null)
            {
                try
                {
                    var quote = await _fmpService.FetchQuoteAsync(symbol);
                    if (quote > 0) return quote;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "FMP quote failed for {Symbol}, falling back", symbol);
                }
            }

            // Fallback: Nasdaq Data Link (FRED forex latest value)
            if (_nasdaqDataLinkService != null && NasdaqDataLinkService.ToNasdaqDataset(symbol) != null)
            {
                try
                {
                    var quote = await _nasdaqDataLinkService.FetchQuoteAsync(symbol);
                    if (quote > 0) return quote;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Nasdaq Data Link quote failed for {Symbol}, falling back", symbol);
                }
            }

            // Fallback: ExchangeRate-API
            try
            {
                var url = !string.IsNullOrEmpty(ExchangeRateApiKey)
                    ? $"https://v6.exchangerate-api.com/v6/{ExchangeRateApiKey}/latest/{baseCcy}"
                    : $"https://open.er-api.com/v6/latest/{baseCcy}";
                var json = await _client.GetStringAsync(url);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("rates", out var rates) && rates.TryGetProperty(quoteCcy, out var rate))
                {
                    var v = rate.GetDouble();
                    if (v > 0) return v;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ExchangeRate-API fetch failed for {Symbol}", symbol);
            }
            return 0;
        }

        public async Task<List<Dictionary<string, object>>> FetchEconomicCalendarAsync()
        {
            var events = new List<Dictionary<string, object>>();

            // Try Finnhub first
            if (!string.IsNullOrEmpty(FinnhubApiKey))
            {
                try
                {
                    var from = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    var to = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
                    var url = $"https://finnhub.io/api/v1/calendar/economic?from={from}&to={to}&token={FinnhubApiKey}";
                    var json = await _client.GetStringAsync(url);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("economicCalendar", out var cal) && cal.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ev in cal.EnumerateArray())
                        {
                            events.Add(new Dictionary<string, object>
                            {
                                ["country"] = ev.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
                                ["event"] = ev.TryGetProperty("event", out var e) ? e.GetString() ?? "" : "",
                                ["impact"] = ev.TryGetProperty("impact", out var i) ? i.GetString() ?? "" : "",
                                ["date"] = ev.TryGetProperty("time", out var tm) ? tm.GetString() ?? "" : ""
                            });
                        }
                        if (events.Count > 0) return events;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Finnhub economic calendar fetch failed, falling back to EODHD");
                }
            }

            // Fallback: EODHD
            var eodhdKey = _config["TrailBlazer:EodhdApiKey"] ?? _config["EodhdApiKey"] ?? "";
            if (!string.IsNullOrEmpty(eodhdKey))
            {
                try
                {
                    var from = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    var to = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
                    var url = $"https://eodhd.com/api/economic-events?api_token={Uri.EscapeDataString(eodhdKey)}&fmt=json&from={from}&to={to}";
                    var json = await _client.GetStringAsync(url);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ev in doc.RootElement.EnumerateArray())
                        {
                            events.Add(new Dictionary<string, object>
                            {
                                ["country"] = ev.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
                                ["event"] = ev.TryGetProperty("event", out var e) ? e.GetString() ?? "" : ev.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                                ["impact"] = ev.TryGetProperty("impact", out var i) ? i.GetString() ?? "" : "",
                                ["date"] = ev.TryGetProperty("time", out var tm) ? tm.GetString() ?? "" : ev.TryGetProperty("date", out var d) ? d.GetString() ?? "" : ""
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "EODHD economic calendar fetch failed");
                }
            }

            // Fallback: Brave web search for economic events when APIs fail
            if (events.Count == 0 && !string.IsNullOrEmpty(BraveApiKey))
            {
                try
                {
                    var weekStart = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    var weekEnd = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
                    var query = $"forex economic calendar FOMC NFP Fed ECB {weekStart}";
                    var webResults = await BraveWebSearchAsync(query, 5, "pd");
                    foreach (var r in webResults)
                    {
                        if (!string.IsNullOrWhiteSpace(r.Title))
                        {
                            events.Add(new Dictionary<string, object>
                            {
                                ["country"] = "",
                                ["event"] = r.Title,
                                ["impact"] = "web",
                                ["date"] = r.Url
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Brave economic calendar fallback failed");
                }
            }
            return events;
        }

        // ────── Retail Sentiment (MyFXBook API) ──────

        /// <summary>Looks up retail short percentage from a pre-fetched MyFXBook batch. Returns 50.0 if not found.</summary>
        public static double GetShortPctFromBatch(string symbol, IReadOnlyDictionary<string, (double longPct, double shortPct)> batch)
        {
            var key = symbol.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
            return batch.TryGetValue(key, out var val) ? val.shortPct : 50.0;
        }

        /// <summary>Fetches retail long/short sentiment from MyFXBook community outlook batch.</summary>
        public Task<(double longPct, double shortPct)?> FetchForexRetailSentimentAsync(string symbol, IReadOnlyDictionary<string, (double longPct, double shortPct)>? myFxBookBatch = null)
        {
            if (myFxBookBatch == null) return Task.FromResult<(double longPct, double shortPct)?>(null);
            var key = symbol.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
            if (key.Length < 4) return Task.FromResult<(double longPct, double shortPct)?>(null);
            if (myFxBookBatch.TryGetValue(key, out var myFx)) return Task.FromResult<(double longPct, double shortPct)?>(myFx);
            var myFxAlias = key switch { "JP225" => "JPN225", "DE40" => "GER30", "US100" => "NAS100", _ => null };
            if (myFxAlias != null && myFxBookBatch.TryGetValue(myFxAlias, out myFx)) return Task.FromResult<(double longPct, double shortPct)?>(myFx);
            return Task.FromResult<(double longPct, double shortPct)?>(null);
        }

        /// <summary>Fetches retail sentiment from MyFXBook API. Returns combined data for use without TrailBlazer refresh.</summary>
        public async Task<ManualSentimentResult> GetManualSentimentDataAsync()
        {
            var myFxBook = await FetchMyFxBookSentimentBatchAsync();
            var combined = myFxBook
                .Select(kv => new CombinedSentimentItem { Symbol = kv.Key, LongPct = kv.Value.longPct, ShortPct = kv.Value.shortPct, Source = "myfxbook" })
                .OrderBy(c => c.Symbol)
                .ToList();

            return new ManualSentimentResult
            {
                MyFxBook = myFxBook.ToDictionary(k => k.Key, v => (object)new { longPct = v.Value.longPct, shortPct = v.Value.shortPct }),
                Combined = combined,
                ScrapedAt = DateTime.UtcNow
            };
        }

        /// <summary>Fetches retail sentiment using optional session override. When sessionOverride is set, skips login and uses it directly.</summary>
        public async Task<Dictionary<string, (double longPct, double shortPct)>> FetchMyFxBookSentimentBatchWithSessionAsync(string? sessionOverride = null)
        {
            if (!string.IsNullOrEmpty(sessionOverride))
            {
                try
                {
                    var url = $"https://www.myfxbook.com/api/get-community-outlook.json?session={Uri.EscapeDataString(sessionOverride)}";
                    var json = await _client.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True)
                    {
                        var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                        _logger.LogWarning("MyFXBook API error with session: {Msg}", msg);
                        return new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
                    }
                    var result = new Dictionary<string, (double longPct, double shortPct)>(StringComparer.OrdinalIgnoreCase);
                    var diag = new Dictionary<string, object>();
                    return ParseCommunityOutlook(doc.RootElement, result, diag).Item1;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MyFXBook fetch with session failed");
                    return new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
                }
            }
            return await FetchMyFxBookSentimentBatchAsync();
        }

        /// <summary>Fetches retail sentiment. Uses stored DB session first; re-logs in only when session is invalid.</summary>
        public async Task<Dictionary<string, (double longPct, double shortPct)>> FetchMyFxBookSentimentBatchAsync()
        {
            var envSession = Environment.GetEnvironmentVariable("MYFXBOOK_SESSION");
            if (!string.IsNullOrEmpty(envSession))
            {
                var r = await FetchMyFxBookSentimentBatchWithSessionAsync(envSession);
                if (r.Count > 0)
                {
                    _logger.LogInformation("MyFXBook: used MYFXBOOK_SESSION env — {Count} symbols", r.Count);
                    return r;
                }
                _logger.LogWarning("MyFXBook: MYFXBOOK_SESSION returned 0 symbols (session may be invalid)");
            }
            var (result, diag) = await FetchMyFxBookSentimentBatchWithDiagnosticAsync();
            if (result.Count == 0)
                _logger.LogWarning("MyFXBook: batch empty after login. Diagnostic: {Diagnostic}", System.Text.Json.JsonSerializer.Serialize(diag));
            else
                _logger.LogInformation("MyFXBook: fetched {Count} symbols via login", result.Count);
            return result;
        }

        /// <summary>Same as FetchMyFxBookSentimentBatchAsync but returns diagnostic info for debugging.</summary>
        /// <remarks>Uses cached session; only logs in when no session or API returns "Invalid session". MyFXBook is sensitive to login frequency.</remarks>
        public async Task<(Dictionary<string, (double longPct, double shortPct)> result, object diagnostic)> FetchMyFxBookSentimentBatchWithDiagnosticAsync()
        {
            var result = new Dictionary<string, (double longPct, double shortPct)>(StringComparer.OrdinalIgnoreCase);
            var diag = new Dictionary<string, object>();

            if (string.IsNullOrEmpty(MyFxBookEmail) || string.IsNullOrEmpty(MyFxBookPassword))
            {
                diag["api"] = new { status = "SKIPPED", reason = "MyFXBook credentials not configured (TrailBlazer:MyFXBookEmail, TrailBlazer:MyFXBookPassword)" };
                return (result, diag);
            }

            try
            {
                var session = await GetOrRefreshMyFxBookSessionAsync(forceLogin: false);
                if (string.IsNullOrEmpty(session))
                {
                    diag["api"] = new { status = "FAILED", reason = "Login failed - no session returned." };
                    return (result, diag);
                }

                var url = $"https://www.myfxbook.com/api/get-community-outlook.json?session={session}";
                var json = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                // Check for invalid session error (re-login only when needed)
                if (doc.RootElement.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.True)
                {
                    var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    if (msg.Contains("Invalid session", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("MyFXBook session expired, re-logging in");
                        await ClearMyFxBookSessionCacheAsync();
                        session = await GetOrRefreshMyFxBookSessionAsync(forceLogin: true);
                        if (!string.IsNullOrEmpty(session))
                        {
                            url = $"https://www.myfxbook.com/api/get-community-outlook.json?session={session}";
                            json = await _client.GetStringAsync(url);
                            doc.Dispose();
                            using var doc2 = JsonDocument.Parse(json);
                            return ParseCommunityOutlook(doc2.RootElement, result, diag);
                        }
                    }
                    diag["api"] = new { status = "FAILED", reason = msg };
                    return (result, diag);
                }

                return ParseCommunityOutlook(doc.RootElement, result, diag);
            }
            catch (Exception ex)
            {
                diag["api"] = new { status = "FAILED", reason = ex.Message };
            }
            return (result, diag);
        }

        private static (Dictionary<string, (double longPct, double shortPct)>, Dictionary<string, object>) ParseCommunityOutlook(
            JsonElement root, Dictionary<string, (double longPct, double shortPct)> result, Dictionary<string, object> diag)
        {
            if (root.TryGetProperty("symbols", out var symbols) && symbols.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in symbols.EnumerateArray())
                {
                    var symbol = item.TryGetProperty("symbol", out var s) ? s.GetString() : (item.TryGetProperty("name", out var n) ? n.GetString() : null);
                    var shortPct = item.TryGetProperty("shortPercentage", out var sp) && sp.TryGetDouble(out var spVal) ? spVal : -1.0;
                    if (!string.IsNullOrEmpty(symbol) && shortPct >= 0 && shortPct <= 100)
                    {
                        var key = symbol.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
                        if (key.Length >= 4)
                        {
                            var longPct = item.TryGetProperty("longPercentage", out var lp) && lp.TryGetDouble(out var lpVal) ? lpVal : 100.0 - shortPct;
                            result[key] = (longPct, shortPct);
                        }
                    }
                }
            }
            diag["api"] = new { status = "OK", count = result.Count };
            return (result, diag);
        }

        private async Task ClearMyFxBookSessionCacheAsync()
        {
            lock (_myFxBookSessionLock)
            {
                _myFxBookSessionCache = null;
            }
            await ClearStoredSessionAsync();
        }

        private async Task<string?> GetStoredSessionAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == MyFxBookSessionKey);
            return setting?.Value;
        }

        private async Task SaveSessionAsync(string session)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == MyFxBookSessionKey);
            var now = DateTime.UtcNow;
            if (setting != null)
            {
                setting.Value = session;
                setting.UpdatedAt = now;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting { Key = MyFxBookSessionKey, Value = session, UpdatedAt = now });
            }
            await db.SaveChangesAsync();
            _logger.LogInformation("MyFXBook session saved to database");
        }

        private async Task ClearStoredSessionAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == MyFxBookSessionKey);
            if (setting != null)
            {
                db.SystemSettings.Remove(setting);
                await db.SaveChangesAsync();
                _logger.LogInformation("MyFXBook session cleared from database");
            }
        }

        /// <summary>Returns session from DB or in-memory cache. Logs in only when no valid session exists. On success, saves to DB.</summary>
        private async Task<string?> GetOrRefreshMyFxBookSessionAsync(bool forceLogin)
        {
            if (!forceLogin)
            {
                lock (_myFxBookSessionLock)
                {
                    if (!string.IsNullOrEmpty(_myFxBookSessionCache))
                        return _myFxBookSessionCache;
                }
                var stored = await GetStoredSessionAsync();
                if (!string.IsNullOrEmpty(stored))
                {
                    lock (_myFxBookSessionLock)
                    {
                        _myFxBookSessionCache = stored;
                    }
                    _logger.LogDebug("MyFXBook: using stored session from database");
                    return stored;
                }
            }

            var (session, error) = await LoginMyFxBookAsync();
            if (!string.IsNullOrEmpty(session))
            {
                lock (_myFxBookSessionLock)
                {
                    _myFxBookSessionCache = session;
                }
                await SaveSessionAsync(session);
            }
            else if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("MyFXBook login failed: {Error}", error);
            }
            return session;
        }

        private async Task<(string? session, string? error)> LoginMyFxBookAsync()
        {
            try
            {
                // GET with URL-encoded params: ! -> %21, + -> %2B (critical - + as space breaks password)
                var url = $"https://www.myfxbook.com/api/login.json?email={Uri.EscapeDataString(MyFxBookEmail)}&password={Uri.EscapeDataString(MyFxBookPassword)}";
                var json = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var session = doc.RootElement.TryGetProperty("session", out var s) ? s.GetString() : null;
                var error = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
                var hasError = doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.True;
                return (string.IsNullOrEmpty(session) ? null : session, hasError ? (error ?? "Login failed") : null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        // ────── Economic Heatmap Builder ──────

        public async Task<List<EconomicHeatmapEntry>> BuildEconomicHeatmapAsync()
        {
            var entries = new List<EconomicHeatmapEntry>();

            const double FedInflationTarget = 2.0;
            const double CpiTargetBand = 0.5; // 1.5%–2.5% = yellow (at target)
            const double AvgGdpGrowth = 2.0;  // typical trend growth for developed economies
            const double GdpGrowthBand = 0.5; // 1.5%–2.5% = yellow (average growth)

            // FRED series IDs. PMI uses OECD Business Confidence (100=neutral, not 50). See docs/FUNDAMENTAL_DATA_ANALYSIS.md
            var currencyIndicators = new Dictionary<string, Dictionary<string, string>>
            {
                ["USD"] = new() { ["GDP"] = "GDPC1", ["CPI"] = "CPIAUCSL", ["Unemployment"] = "UNRATE", ["InterestRate"] = "FEDFUNDS", ["PMI"] = "BSCICP03USM665S" },
                ["EUR"] = new() { ["GDP"] = "EUNNGDP", ["CPI"] = "CP0000EZ19M086NEST", ["Unemployment"] = "LRHUTTTTEZM156S", ["InterestRate"] = "ECBDFR", ["PMI"] = "BSCICP03EZM665S" },
                ["GBP"] = new() { ["GDP"] = "NAEXKP01GBQ189S", ["CPI"] = "GBRCPIALLMINMEI", ["Unemployment"] = "LRHUTTTTGBM156S", ["InterestRate"] = "BOERUKM", ["PMI"] = "BSCICP03GBM665S" },
                ["JPY"] = new() { ["GDP"] = "JPNRGDPEXP", ["CPI"] = "JPNCPIALLMINMEI", ["Unemployment"] = "LRHUTTTTJPM156S", ["InterestRate"] = "IRSTCB01JPM156N", ["PMI"] = "BSCICP03JPM665S" },
                ["AUD"] = new() { ["GDP"] = "AUSGDPNQDSMEI", ["CPI"] = "AUSCPIALLQINMEI", ["Unemployment"] = "LRHUTTTTAUM156S", ["InterestRate"] = "IRSTCI01AUM156N", ["PMI"] = "BSCICP03AUM665S" },
                ["NZD"] = new() { ["GDP"] = "NZLGDPNQDSMEI", ["CPI"] = "NZLCPIALLQINMEI", ["Unemployment"] = "LRHUTTTTNZM156S", ["InterestRate"] = "IRSTCI01NZM156N" },
                ["CAD"] = new() { ["GDP"] = "NAEXKP01CAQ189S", ["CPI"] = "CANCPIALLMINMEI", ["Unemployment"] = "LRHUTTTTCAM156S", ["InterestRate"] = "IRSTCB01CAM156N", ["PMI"] = "BSCICP03CAM665S" },
                ["CHF"] = new() { ["GDP"] = "NAEXKP01CHQ189S", ["CPI"] = "CHECPIALLMINMEI", ["Unemployment"] = "LRHUTTTTCHM156S", ["InterestRate"] = "IRSTCI01CHM156N", ["PMI"] = "BSCICP03CHM665S" },
                ["SEK"] = new() { ["GDP"] = "CLVMNACSCAB1GQSE", ["CPI"] = "CP0000SEM086NEST", ["Unemployment"] = "LRHUTTTTSEM156S" },
                ["ZAR"] = new() { ["GDP"] = "ZAFGDPRQPSMEI", ["CPI"] = "ZAFCPIALLMINMEI", ["Unemployment"] = "LRUN64TTZAQ156S", ["InterestRate"] = "IRSTCB01ZAM156N", ["PMI"] = "BSCICP03ZAM665S" },
                ["CNY"] = new() { ["GDP"] = "CHNGDPRAPSMEI", ["CPI"] = "CHNCPIALLMINMEI", ["InterestRate"] = "IRSTCB01CNM156N", ["PMI"] = "BSCICP03CNM665S" }
            };

            // CPI series that are quarterly (obs count 5); others are monthly (13)
            var cpiQuarterlySeries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AUSCPIALLQINMEI", "NZLCPIALLQINMEI" };
            // GDP series that are already YoY growth rates (use raw fetch, not YoY calc)
            var gdpIsGrowthRate = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ZAFGDPRQPSMEI", "CHNGDPRAPSMEI" };
            // CPI series that are already YoY growth rates (use raw fetch, not YoY calc)
            var cpiIsGrowthRate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // PMI uses OECD Business Confidence: 100=neutral (not 50 like ISM PMI)
            const double PmiNeutral = 100.0;

            foreach (var (currency, indicators) in currencyIndicators)
            {
                foreach (var (indicator, seriesId) in indicators)
                {
                    double value;
                    if (indicator == "GDP")
                    {
                        if (gdpIsGrowthRate.Contains(seriesId))
                            value = await FetchFredDataAsync(seriesId);
                        else
                        {
                            var yoy = await FetchFredYoYPercentAsync(seriesId, 5);
                            value = yoy ?? 0;
                        }
                    }
                    else if (indicator == "CPI")
                    {
                        if (cpiIsGrowthRate.Contains(seriesId))
                            value = await FetchFredDataAsync(seriesId);
                        else
                        {
                            var obsCount = cpiQuarterlySeries.Contains(seriesId) ? 5 : 13;
                            var yoy = await FetchFredYoYPercentAsync(seriesId, obsCount);
                            value = yoy ?? 0;
                        }
                    }
                    else
                    {
                        value = await FetchFredDataAsync(seriesId);
                    }
                    await Task.Delay(100);

                    var impact = indicator switch
                    {
                        "GDP" => value >= AvgGdpGrowth - GdpGrowthBand && value <= AvgGdpGrowth + GdpGrowthBand ? "Neutral" : value > AvgGdpGrowth + GdpGrowthBand ? "Positive" : "Negative",
                        "CPI" => value >= FedInflationTarget - CpiTargetBand && value <= FedInflationTarget + CpiTargetBand ? "Neutral" : value > FedInflationTarget + CpiTargetBand ? "Negative" : "Positive",
                        "Unemployment" => value < 5 ? "Positive" : "Negative",
                        "InterestRate" => value > 2 ? "Positive" : "Neutral",
                        "PMI" => value > PmiNeutral ? "Positive" : value < PmiNeutral ? "Negative" : "Neutral",
                        _ => "Neutral"
                    };

                    entries.Add(new EconomicHeatmapEntry
                    {
                        Currency = currency,
                        Indicator = indicator,
                        Value = value,
                        PreviousValue = 0,
                        Impact = impact,
                        DateCollected = DateTime.UtcNow
                    });
                }
            }

            return entries;
        }

        private static string MapToTaapiSymbol(string symbol) => symbol switch
        {
            "XAUUSD" => "XAU/USDT",
            "XAGUSD" => "XAG/USDT",
            _ when symbol.Length >= 6 => $"{symbol[..3]}/{symbol[3..]}",
            _ => symbol
        };

        /// <summary>Returns diagnostic status for each data source (OK, FAILED, or NOT_CONFIGURED).</summary>
        /// <param name="worldBankService">Optional World Bank service for diagnostic. Pass from controller if available.</param>
        public async Task<Dictionary<string, object>> GetDiagnosticStatusAsync(WorldBankDataService? worldBankService = null)
        {
            var results = new Dictionary<string, object>();
            var tasks = new List<Task>();

            // FRED
            tasks.Add(Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(FredApiKey))
                {
                    results["fred"] = new { status = "NOT_CONFIGURED", message = "API key not set" };
                    return;
                }
                try
                {
                    var val = await FetchFredDataAsync("GDPC1");
                    results["fred"] = val > 0
                        ? new { status = "OK", message = $"GDP data: {val:N0}", sample = val }
                        : new { status = "FAILED", message = "No data returned" };
                }
                catch (Exception ex)
                {
                    results["fred"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            // COT (CFTC direct - published weekly, typically Fridays)
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var batch = await FetchCOTReportBatchAsync();
                    var hasData = batch.Count > 0 && batch.Values.Any(r => r.CommercialLong > 0 || r.NonCommercialLong > 0);
                    if (hasData)
                    {
                        var reportDate = batch.Values.Max(r => r.ReportDate);
                        results["cot"] = new { status = "OK", message = $"{batch.Count} instruments, report {reportDate:yyyy-MM-dd} (CFTC publishes weekly)", sample = batch.Count, reportDate = reportDate };
                    }
                    else
                    {
                        results["cot"] = new { status = "FAILED", message = "No COT data parsed from CFTC" };
                    }
                }
                catch (Exception ex)
                {
                    results["cot"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            // Retail Sentiment (MyFXBook)
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var myFxBatch = await FetchMyFxBookSentimentBatchAsync();
                    var sentiment = await FetchForexRetailSentimentAsync("EURUSD", myFxBatch);
                    if (sentiment.HasValue)
                        results["retailSentiment"] = new { status = "OK", message = $"EUR/USD {sentiment.Value.longPct:F0}% long / {sentiment.Value.shortPct:F0}% short", sample = sentiment };
                    else
                        results["retailSentiment"] = new { status = "FAILED", message = "Could not get sentiment from MyFXBook" };
                }
                catch (Exception ex)
                {
                    results["retailSentiment"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            // Twelve Data Technical (RSI, EMA - used by TrailBlazer scoring)
            tasks.Add(Task.Run(async () =>
            {
                if (_twelveDataService == null)
                {
                    results["twelveDataTechnical"] = new { status = "SKIPPED", message = "TwelveDataService not registered" };
                    return;
                }
                try
                {
                    var tech = await FetchTechnicalIndicatorsAsync("EURUSD");
                    var rsi = tech.GetValueOrDefault("RSI", 50);
                    var hasData = rsi > 0 && rsi < 100;
                    results["twelveDataTechnical"] = hasData
                        ? new { status = "OK", message = $"RSI: {rsi:F1}", sample = rsi }
                        : new { status = "FAILED", message = "No valid technical data (rate limit ~8 req/min?)" };
                }
                catch (Exception ex)
                {
                    results["twelveDataTechnical"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            // Exchange Rate API (forex rates)
            tasks.Add(Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(ExchangeRateApiKey))
                {
                    results["exchangeRateApi"] = new { status = "NOT_CONFIGURED", message = "API key not set (uses open access with stricter limits)" };
                    return;
                }
                try
                {
                    var quote = await FetchForexQuoteAsync("EURUSD");
                    var hasData = quote > 0;
                    results["exchangeRateApi"] = hasData
                        ? new { status = "OK", message = $"EUR/USD: {quote:F4}", sample = quote }
                        : new { status = "FAILED", message = "No rate returned" };
                }
                catch (Exception ex)
                {
                    results["exchangeRateApi"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            // Twelve Data (forex, technical indicators - rate limit ~8 req/min)
            tasks.Add(Task.Run(async () =>
            {
                if (_twelveDataService == null)
                {
                    results["twelveData"] = new { status = "SKIPPED", message = "TwelveDataService not registered" };
                    return;
                }
                try
                {
                    var (ok, message) = await _twelveDataService.TestConnectivityAsync();
                    results["twelveData"] = ok
                        ? new { status = "OK", message, rateLimit = "~8 req/min" }
                        : new { status = "FAILED", message };
                }
                catch (Exception ex)
                {
                    results["twelveData"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            // Economic Calendar (Finnhub primary, EODHD fallback)
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var cal = await FetchEconomicCalendarAsync();
                    var ok = cal.Count > 0;
                    results["economicCalendar"] = ok
                        ? new { status = "OK", message = $"{cal.Count} events", sample = cal.Count }
                        : new { status = "FAILED", message = "No calendar data" };
                }
                catch (Exception ex)
                {
                    results["economicCalendar"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            // Brave (news search) - instrument-specific news
            tasks.Add(Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(BraveApiKey))
                {
                    results["brave"] = new { status = "NOT_CONFIGURED", message = "API key not set" };
                    return;
                }
                try
                {
                    var news = await FetchNewsForSymbolAsync("EURUSD", "ForexMajor");
                    results["brave"] = news.Count > 0
                        ? new { status = "OK", message = $"{news.Count} news articles for EURUSD", sample = news.Count }
                        : new { status = "FAILED", message = "No news returned (check API key)" };
                }
                catch (Exception ex)
                {
                    results["brave"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            // Finnhub (forex + calendar) - tests connectivity after email verification
            tasks.Add(Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(FinnhubApiKey))
                {
                    results["finnhub"] = new { status = "NOT_CONFIGURED", message = "API key not set" };
                    return;
                }
                try
                {
                    var quote = await FetchForexQuoteAsync("EURUSD");
                    var cal = await FetchEconomicCalendarAsync();
                    var quoteOk = quote > 0;
                    var calOk = cal.Count > 0;
                    if (quoteOk || calOk)
                    {
                        var parts = new List<string>();
                        if (quoteOk) parts.Add($"EUR/USD: {quote:F4}");
                        if (calOk) parts.Add($"{cal.Count} calendar events");
                        results["finnhub"] = new { status = "OK", message = string.Join("; ", parts), sample = quoteOk ? quote : cal.Count };
                    }
                    else
                    {
                        results["finnhub"] = new { status = "FAILED", message = "No quote or calendar data (check email verification)" };
                    }
                }
                catch (Exception ex)
                {
                    results["finnhub"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            // World Bank Data360 (free, no key - GDP + inflation for 266 countries)
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var wb = worldBankService;
                    if (wb == null)
                    {
                        results["worldBank"] = new { status = "SKIPPED", message = "WorldBankDataService not passed to diagnostic" };
                        return;
                    }
                    var (ok, message) = await wb.TestConnectivityAsync();
                    results["worldBank"] = ok
                        ? new { status = "OK", message, source = "Data360 (free, 266 countries)" }
                        : new { status = "FAILED", message };
                }
                catch (Exception ex)
                {
                    results["worldBank"] = new { status = "FAILED", message = ex.Message };
                }
            }));

            await Task.WhenAll(tasks);
            return results;
        }
    }

    public class ManualSentimentResult
    {
        public Dictionary<string, object> MyFxBook { get; set; } = new();
        public List<CombinedSentimentItem> Combined { get; set; } = new();
        public DateTime ScrapedAt { get; set; }
    }

    public class CombinedSentimentItem
    {
        public string Symbol { get; set; } = "";
        public double LongPct { get; set; }
        public double ShortPct { get; set; }
        public string Source { get; set; } = "";
    }

    public class NewsItem
    {
        public string Headline { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Source { get; set; } = "";
        public string Url { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public DateTime PublishedAt { get; set; }
    }

    public class WebSearchResult
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Description { get; set; } = "";
        public string Source { get; set; } = "";
    }
}
