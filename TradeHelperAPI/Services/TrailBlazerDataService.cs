using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
        private const string BraveLastCallKey = "BraveLastCallUtc";

        /// <summary>When true, Brave calls set a flag instead of recording immediately; block is recorded at end of refresh (after currency strength).</summary>
        private static volatile bool _braveRefreshContext;
        private static volatile bool _braveUsedThisRefresh;
        /// <summary>When true (manual refresh with useBrave), try Brave for currency strength even when Finnhub has data. Ensures Brave runs on demand.</summary>
        private static volatile bool _forceBraveForRefresh;

        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<TrailBlazerDataService> _logger;
        private readonly TwelveDataService? _twelveDataService;
        private readonly MarketStackService? _marketStackService;
        private readonly iTickService? _iTickService;
        private readonly EodhdService? _eodhdService;
        private readonly FmpService? _fmpService;
        private readonly NasdaqDataLinkService? _nasdaqDataLinkService;
        private readonly YahooFinanceService? _yahooFinanceService;
        private readonly GoogleNewsService? _googleNewsService;
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
        private string? MyFxBookSession => _config["TrailBlazer:MyFXBookSession"] ?? _config["MyFXBook:Session"];
        private int BraveCooldownHours => _config.GetValue("TrailBlazer:BraveCooldownHours", 48);
        private int BraveOutlookCacheMinutes => _config.GetValue("TrailBlazer:BraveOutlookCacheMinutes", 2880);

        public TrailBlazerDataService(HttpClient client, IConfiguration config, ILogger<TrailBlazerDataService> logger, IServiceScopeFactory scopeFactory, ApiRateLimitService rateLimit, TwelveDataService? twelveDataService = null, MarketStackService? marketStackService = null, iTickService? iTickService = null, EodhdService? eodhdService = null, FmpService? fmpService = null, NasdaqDataLinkService? nasdaqDataLinkService = null, YahooFinanceService? yahooFinanceService = null, GoogleNewsService? googleNewsService = null, IMemoryCache? cache = null)
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
            _yahooFinanceService = yahooFinanceService;
            _googleNewsService = googleNewsService;
            _cache = cache;
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        // ────── FRED Economic Data ──────

        private string BuildFredObservationsUrl(string seriesId, int limit) =>
            "https://api.stlouisfed.org/fred/series/observations?series_id=" +
            Uri.EscapeDataString(seriesId) +
            "&api_key=" + Uri.EscapeDataString(FredApiKey) +
            "&file_type=json&sort_order=desc&limit=" + limit;

        /// <summary>GET /fred/series/observations JSON, or null on HTTP failure (FRED error body logged).</summary>
        /// <remarks>Retries on transient FRED/server errors (429, 5xx). Uses GetAsync (never throws on 4xx/5xx).</remarks>
        private async Task<string?> FetchFredObservationsBodyAsync(string seriesId, int limit)
        {
            if (string.IsNullOrEmpty(FredApiKey)) return null;
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var url = BuildFredObservationsUrl(seriesId, limit);
                using var resp = await _client.GetAsync(url);
                var json = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode) return json;

                var status = (int)resp.StatusCode;
                if (attempt < maxAttempts && (status == 429 || status is >= 500 and <= 599))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt + Random.Shared.Next(50, 200)));
                    continue;
                }

                var detail = json;
                try
                {
                    using var errDoc = JsonDocument.Parse(json);
                    if (errDoc.RootElement.TryGetProperty("error_message", out var em))
                        detail = em.GetString() ?? json;
                }
                catch { /* keep raw body */ }

                if (detail.Length > 400) detail = detail[..400] + "…";
                // 400 = unknown/invalid series or API constraint — avoid Warning spam in DB logs
                if (status == 400)
                    _logger.LogInformation("FRED HTTP 400 for {SeriesId}: {Detail}", seriesId, detail);
                else
                    _logger.LogWarning("FRED HTTP {Status} for {SeriesId}: {Detail}", status, seriesId, detail);
                return null;
            }

            return null;
        }

        public async Task<double> FetchFredDataAsync(string seriesId)
        {
            if (string.IsNullOrEmpty(FredApiKey)) return 0;
            if (await _rateLimit.IsBlockedAsync("FRED")) return 0;
            try
            {
                var json = await FetchFredObservationsBodyAsync(seriesId, 1);
                if (json == null) return 0;
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
                _logger.LogDebug(ex, "FRED parse/unexpected error for {SeriesId}", seriesId);
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
                var json = await FetchFredObservationsBodyAsync(seriesId, observationCount);
                if (json == null) return null;
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
                _logger.LogDebug(ex, "FRED YoY parse/unexpected error for {SeriesId}", seriesId);
                return null;
            }
        }

        /// <summary>
        /// YoY % using observation dates: latest value vs closest observation on or before ~1 year earlier.
        /// Handles missing FRED values ("."), mixed revisions, and avoids wrong offsets when dots break fixed indices.
        /// </summary>
        private async Task<double?> FetchFredYoYPercentCalendarAsync(string seriesId, int fetchLimit = 120)
        {
            if (string.IsNullOrEmpty(FredApiKey)) return null;
            if (await _rateLimit.IsBlockedAsync("FRED")) return null;
            try
            {
                var json = await FetchFredObservationsBodyAsync(seriesId, fetchLimit);
                if (json == null) return null;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error_message", out var errMsg))
                {
                    var msg = errMsg.GetString();
                    if (ApiRateLimitService.IsCreditLimitMessage(msg ?? ""))
                        await _rateLimit.SetBlockedAsync("FRED");
                    return null;
                }
                var observations = doc.RootElement.GetProperty("observations");
                var points = new List<(DateTime Date, double Val)>();
                foreach (var el in observations.EnumerateArray())
                {
                    var ds = el.GetProperty("date").GetString();
                    var vs = el.GetProperty("value").GetString();
                    if (string.IsNullOrEmpty(ds) || vs == "." || string.IsNullOrEmpty(vs)) continue;
                    if (!double.TryParse(vs, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)) continue;
                    if (!DateTime.TryParse(ds, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) continue;
                    points.Add((d, v));
                }
                if (points.Count < 2) return null;
                points.Sort((a, b) => b.Date.CompareTo(a.Date));
                var (d0, v0) = points[0];
                if (v0 == 0) return null;
                var cutoff = d0.AddYears(-1);
                var prior = points.Where(p => p.Date <= cutoff).OrderByDescending(p => p.Date).FirstOrDefault();
                if (prior == default || prior.Val == 0) return null;
                return ((v0 - prior.Val) / prior.Val) * 100.0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "FRED calendar YoY failed for {SeriesId}", seriesId);
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
        // Multiple sources: financial_lof (forex), CME (crypto), COMEX (metals), NYMEX (oil), etc.
        // ZAR pairs: USDZAR = "SO AFRICAN RAND" or "SOUTH AFRICAN RAND - CHICAGO MERCANTILE EXCHANGE"
        // Cross pairs: EURZAR, GBPZAR, AUDZAR, etc. = "EURO FX/SO AFRICAN RAND XRATE" etc.

        private static readonly string[] CftcCotUrls =
        {
            "https://www.cftc.gov/dea/options/financial_lof.htm",
            "https://www.cftc.gov/dea/options/deacmelof.htm",
            "https://www.cftc.gov/dea/options/deafrexlof.htm",
            "https://www.cftc.gov/dea/options/deanymelof.htm",
            "https://www.cftc.gov/dea/options/deacmxlof.htm",
            "https://www.cftc.gov/dea/options/deacbtlof.htm",
            "https://www.cftc.gov/dea/options/deaiceulof.htm",
            "https://www.cftc.gov/dea/options/deacboelof.htm",
            "https://www.cftc.gov/dea/options/deanybtlof.htm",
            "https://www.cftc.gov/dea/options/deaifedlof.htm",
            "https://www.cftc.gov/dea/options/deaviewcit.htm",
            "https://www.cftc.gov/dea/options/other_lof.htm",
            "https://www.cftc.gov/dea/futures/deaiceulf.htm",
            "https://www.cftc.gov/dea/futures/petroleum_lf.htm",
            "https://www.cftc.gov/dea/futures/deacmxlf.htm",
            "https://www.cftc.gov/dea/futures/deanymelf.htm",
        };

        private static readonly string[] CftcExchangePatterns =
        {
            " - CHICAGO MERCANTILE EXCHANGE",
            " - CHICAGO BOARD OF TRADE",
            " - COMMODITY EXCHANGE INC",
            " - NEW YORK MERCANTILE EXCHANGE",
            " - ICE FUTURES U.S.",
            " - ICE FUTURES EUROPE",
            " - ICE FUTURES ENERGY DIV",
            " - CBOE FUTURES EXCHANGE",
            " - NEW YORK BOARD OF TRADE",
        };

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
            ["SOUTH AFRICAN RAND"] = "USDZAR",
            ["EURO FX/BRITISH POUND XRATE"] = "EURGBP",
            ["EURO FX/JAPANESE YEN XRATE"] = "EURJPY",
            ["BITCOIN"] = "BTC",
            ["MICRO BITCOIN"] = "BTC",
            ["ETHER"] = "ETH",
            ["MICRO ETHER"] = "ETH",
            ["ETHEREUM"] = "ETH",
            ["GOLD"] = "XAUUSD",
            ["GOLD 100 OZ"] = "XAUUSD",
            ["GOLD - 100 TROY OZ"] = "XAUUSD",
            ["SILVER"] = "XAGUSD",
            ["SILVER 5000 OZ"] = "XAGUSD",
            ["SILVER - 5000 TROY OZ"] = "XAGUSD",
            ["PLATINUM"] = "XPTUSD",
            ["PLATINUM 50 OZ"] = "XPTUSD",
            ["PLATINUM - 50 TROY OZ"] = "XPTUSD",
            ["PALLADIUM"] = "XPDUSD",
            ["PALLADIUM 100 OZ"] = "XPDUSD",
            ["PALLADIUM - 100 TROY OZ"] = "XPDUSD",
            ["CRUDE OIL"] = "USOIL",
            ["LIGHT CRUDE OIL"] = "USOIL",
            ["WTI CRUDE OIL"] = "USOIL",
            ["CRUDE OIL, LIGHT SWEET"] = "USOIL",
            ["CRUDE OIL, LIGHT SWEET-WTI"] = "USOIL",
            ["LIGHT SWEET CRUDE OIL"] = "USOIL",
            ["WTI-PHYSICAL"] = "USOIL",
            ["WTI FINANCIAL CRUDE OIL"] = "USOIL",
            ["WTI CRUDE OIL 1ST LINE"] = "USOIL",
            ["10-YEAR T-NOTES"] = "US10Y",
            ["5-YEAR T-NOTES"] = "US5Y",
            ["30-YEAR T-BONDS"] = "US30Y",
            ["UST 10Y NOTE"] = "US10Y",
            ["UST 5Y NOTE"] = "US5Y",
            ["UST BOND"] = "US30Y",
            ["ULTRA UST 10Y"] = "US10Y",
            ["ULTRA UST BOND"] = "US30Y",
            ["DJIA Consolidated"] = "US30",
            ["DJIA x $5"] = "US30",
            ["DJIA"] = "US30",
            ["MICRO E-MINI DJIA (x$0.5)"] = "US30",
            ["EURO FX/SO AFRICAN RAND XRATE"] = "EURZAR",
            ["EURO FX/SOUTH AFRICAN RAND XRATE"] = "EURZAR",
            ["BRITISH POUND/SO AFRICAN RAND XRATE"] = "GBPZAR",
            ["BRITISH POUND/SOUTH AFRICAN RAND XRATE"] = "GBPZAR",
            ["AUSTRALIAN DOLLAR/SO AFRICAN RAND XRATE"] = "AUDZAR",
            ["AUSTRALIAN DOLLAR/SOUTH AFRICAN RAND XRATE"] = "AUDZAR",
            ["NZ DOLLAR/SO AFRICAN RAND XRATE"] = "NZDZAR",
            ["NZ DOLLAR/SOUTH AFRICAN RAND XRATE"] = "NZDZAR",
            ["CANADIAN DOLLAR/SO AFRICAN RAND XRATE"] = "CADZAR",
            ["CANADIAN DOLLAR/SOUTH AFRICAN RAND XRATE"] = "CADZAR",
            ["SWISS FRANC/SO AFRICAN RAND XRATE"] = "CHFZAR",
            ["SWISS FRANC/SOUTH AFRICAN RAND XRATE"] = "CHFZAR",
            ["JAPANESE YEN/SO AFRICAN RAND XRATE"] = "JPYZAR",
            ["JAPANESE YEN/SOUTH AFRICAN RAND XRATE"] = "JPYZAR",
            ["E-MINI S&P 500"] = "US500",
            ["S&P 500 Consolidated"] = "US500",
            ["MICRO E-MINI S&P 500 INDEX"] = "US500",
            ["NASDAQ MINI"] = "US100",
            ["NASDAQ-100 Consolidated"] = "US100",
            ["MICRO E-MINI NASDAQ-100 INDEX"] = "US100",
            ["NIKKEI STOCK AVERAGE YEN DENOM"] = "JP225",
            ["E-MINI DAX"] = "DE40",
            ["DAX"] = "DE40",
            ["FTSE 100"] = "UK100",
        };

        /// <summary>Resolves CFTC contract name to symbol. Uses exact match first, then prefix/contains for metals and oil.</summary>
        private static bool TryResolveCftcSymbol(string name, out string symbol)
        {
            if (CftcToSymbol.TryGetValue(name, out symbol!))
                return true;
            var upper = name.ToUpperInvariant();
            if (upper.StartsWith("GOLD") && !upper.Contains("MICRO"))
                { symbol = "XAUUSD"; return true; }
            if (upper.StartsWith("SILVER") && !upper.Contains("MICRO"))
                { symbol = "XAGUSD"; return true; }
            if (upper.StartsWith("PLATINUM"))
                { symbol = "XPTUSD"; return true; }
            if (upper.StartsWith("PALLADIUM"))
                { symbol = "XPDUSD"; return true; }
            if ((upper.Contains("CRUDE") && upper.Contains("OIL")) || upper.Contains("LIGHT SWEET") || (upper.Contains("WTI") && (upper.Contains("OIL") || upper.Contains("PHYSICAL") || upper.Contains("CRUDE"))))
                { symbol = "USOIL"; return true; }
            if ((upper.Contains("E-MINI") || upper.Contains("EMINI")) && upper.Contains("S&P") && (upper.Contains("500") || upper.Contains("SP")))
                { symbol = "US500"; return true; }
            if ((upper.Contains("NASDAQ") || upper.Contains("NAS")) && (upper.Contains("100") || upper.Contains("MINI")))
                { symbol = "US100"; return true; }
            if (upper.Contains("NIKKEI"))
                { symbol = "JP225"; return true; }
            if (upper.Contains("UST 10Y") || upper.Contains("ULTRA UST 10Y"))
                { symbol = "US10Y"; return true; }
            if (upper.Contains("UST 5Y"))
                { symbol = "US5Y"; return true; }
            if (upper.Contains("UST BOND") || upper.Contains("ULTRA UST BOND"))
                { symbol = "US30Y"; return true; }
            if (upper.Contains("DJIA"))
                { symbol = "US30"; return true; }
            if (upper.Contains("DAX"))
                { symbol = "DE40"; return true; }
            if (upper.Contains("FTSE") && upper.Contains("100"))
                { symbol = "UK100"; return true; }
            if ((upper.Contains("SO AFRICAN RAND") || upper.Contains("SOUTH AFRICAN RAND")) && !upper.Contains("/"))
                { symbol = "USDZAR"; return true; }
            if (upper.Contains("EURO FX") && (upper.Contains("SO AFRICAN RAND") || upper.Contains("SOUTH AFRICAN RAND")))
                { symbol = "EURZAR"; return true; }
            if (upper.Contains("BRITISH POUND") && (upper.Contains("SO AFRICAN RAND") || upper.Contains("SOUTH AFRICAN RAND")))
                { symbol = "GBPZAR"; return true; }
            if (upper.Contains("AUSTRALIAN DOLLAR") && (upper.Contains("SO AFRICAN RAND") || upper.Contains("SOUTH AFRICAN RAND")))
                { symbol = "AUDZAR"; return true; }
            if (upper.Contains("NZ DOLLAR") && (upper.Contains("SO AFRICAN RAND") || upper.Contains("SOUTH AFRICAN RAND")))
                { symbol = "NZDZAR"; return true; }
            if (upper.Contains("CANADIAN DOLLAR") && (upper.Contains("SO AFRICAN RAND") || upper.Contains("SOUTH AFRICAN RAND")))
                { symbol = "CADZAR"; return true; }
            if (upper.Contains("SWISS FRANC") && (upper.Contains("SO AFRICAN RAND") || upper.Contains("SOUTH AFRICAN RAND")))
                { symbol = "CHFZAR"; return true; }
            if (upper.Contains("JAPANESE YEN") && (upper.Contains("SO AFRICAN RAND") || upper.Contains("SOUTH AFRICAN RAND")))
                { symbol = "JPYZAR"; return true; }
            symbol = null!;
            return false;
        }

        public async Task<Dictionary<string, COTReport>> FetchCOTReportBatchAsync()
        {
            var result = new Dictionary<string, COTReport>();
            foreach (var url in CftcCotUrls)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; TradeTracker/1.0; +https://github.com/tradetracker)");
                    req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
                    var response = await _client.SendAsync(req);
                    if (!response.IsSuccessStatusCode) continue;
                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    var text = doc.DocumentNode.InnerText;
                    var reports = ParseCftcFinancialLof(text);
                    if (reports.Count == 0)
                        reports = ParseCftcLegacyOrDisaggregated(text);
                    foreach (var r in reports)
                        if (!result.ContainsKey(r.Symbol) || r.ReportDate > result[r.Symbol].ReportDate)
                            result[r.Symbol] = r;
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "CFTC COT fetch failed for {Url}", url);
                }
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
                var exchangeIdx = -1;
                foreach (var pattern in CftcExchangePatterns)
                {
                    var idx = line.IndexOf(pattern, StringComparison.Ordinal);
                    if (idx >= 0) { exchangeIdx = idx; break; }
                }
                if (exchangeIdx < 0) continue;

                var name = line[..exchangeIdx].Trim();
                if (!TryResolveCftcSymbol(name, out var symbol)) continue;

                long openInterest = 0;
                for (var j = i + 1; j < Math.Min(i + 8, lines.Length); j++)
                {
                    var oiMatch = Regex.Match(lines[j], @"Open Interest\s*(?:is)?\s*:?\s*([\d,]+)");
                    if (oiMatch.Success && long.TryParse(oiMatch.Groups[1].Value.Replace(",", ""), out openInterest))
                        break;
                }
                if (openInterest == 0) continue;

                string? positionsLine = null;
                for (var j = i + 1; j < Math.Min(i + 15, lines.Length); j++)
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

        /// <summary>Parses COMEX (Gold, Silver) legacy format and petroleum disaggregated format. Different layout than financial_lof.</summary>
        private static List<COTReport> ParseCftcLegacyOrDisaggregated(string text)
        {
            var reports = new List<COTReport>();
            var reportDateMatch = Regex.Match(text, @"(?:Futures Only|Commitments)[^.]*(\w+) (\d{1,2}), (\d{4})");
            var reportDate = reportDateMatch.Success && DateTime.TryParse($"{reportDateMatch.Groups[1].Value} {reportDateMatch.Groups[2].Value}, {reportDateMatch.Groups[3].Value}", out var rd)
                ? rd : DateTime.UtcNow;

            var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var exchangeIdx = -1;
                foreach (var pattern in CftcExchangePatterns)
                {
                    var idx = line.IndexOf(pattern, StringComparison.Ordinal);
                    if (idx >= 0) { exchangeIdx = idx; break; }
                }
                if (exchangeIdx < 0) continue;

                var name = line[..exchangeIdx].Trim();
                if (!TryResolveCftcSymbol(name, out var symbol)) continue;

                // Find "All  :" line with numbers (legacy COMEX or disaggregated petroleum)
                string? allLine = null;
                for (var j = i + 1; j < Math.Min(i + 25, lines.Length); j++)
                {
                    if (lines[j].StartsWith("All  :", StringComparison.Ordinal) || lines[j].StartsWith("All :", StringComparison.Ordinal))
                    {
                        allLine = lines[j];
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(allLine)) continue;

                var nums = Regex.Matches(allLine, @"-?\d[\d,]*")
                    .Select(x => long.TryParse(x.Value.Replace(",", ""), out var v) ? v : 0L)
                    .ToList();
                if (nums.Count < 6) continue;

                long openInterest = nums[0];
                if (openInterest == 0) continue;

                long commercialLong, commercialShort, nonCommercialLong, nonCommercialShort;
                // Legacy COMEX (Gold, Silver): OI | NC_L, NC_S, NC_Spread | Comm_L, Comm_S | Total_L, Total_S | Nonrep_L, Nonrep_S (10 nums)
                if (nums.Count >= 10 && nums.Count <= 11)
                {
                    commercialLong = nums[4];
                    commercialShort = nums[5];
                    nonCommercialLong = nums[1];
                    nonCommercialShort = nums[2];
                }
                else
                {
                    // Disaggregated (Petroleum, etc.): OI | Producer_L, Producer_S | Swap_L, Swap_S, Swap_Spread | MM_L, MM_S, MM_Spread | Other_L, Other_S, Other_Spread | Nonrep_L, Nonrep_S
                    commercialLong = nums.Count > 2 ? nums[1] : 0;
                    commercialShort = nums.Count > 2 ? nums[2] : 0;
                    nonCommercialLong = nums.Count >= 12 ? nums[4] + nums[7] + nums[10] : 0;
                    nonCommercialShort = nums.Count >= 12 ? nums[5] + nums[8] + nums[11] : 0;
                }

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

        // ────── News (Google News RSS first — free; Yahoo; Finnhub; Brave last) ──────

        private static void MergeNewsDedupe(List<NewsItem> target, IEnumerable<NewsItem> incoming)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in target)
            {
                var h = (x.Headline ?? "").Trim();
                if (h.Length > 0) seen.Add(h.Length > 96 ? h[..96] : h);
            }
            foreach (var n in incoming)
            {
                var h = (n.Headline ?? "").Trim();
                if (h.Length == 0) continue;
                var key = h.Length > 96 ? h[..96] : h;
                if (seen.Add(key)) target.Add(n);
            }
        }

        public async Task<List<NewsItem>> FetchNewsForSymbolAsync(string symbol, string? assetClass)
        {
            var items = new List<NewsItem>();

            if (_googleNewsService != null)
            {
                try
                {
                    var gn = await _googleNewsService.FetchNewsAsync(symbol, assetClass);
                    MergeNewsDedupe(items, gn);
                    if (items.Count > 0)
                        _logger.LogDebug("News for {Symbol}: {Count} items from Google News RSS", symbol, items.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Google News failed for {Symbol}, continuing", symbol);
                }
            }

            if (_yahooFinanceService != null)
            {
                try
                {
                    var yh = await _yahooFinanceService.FetchNewsForInstrumentAsync(symbol, assetClass);
                    MergeNewsDedupe(items, yh);
                    if (yh.Count > 0)
                        _logger.LogDebug("News for {Symbol}: merged Yahoo ({Count} new items in batch)", symbol, yh.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Yahoo news failed for {Symbol}, trying Finnhub", symbol);
                }
            }

            if (items.Count > 0)
                return items;

            // Finnhub (free) when Google+Yahoo produced nothing
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

            // Brave fallback (instrument-specific, paid) — only when Finnhub empty/fails. 24h cooldown to reduce costs.
            if (items.Count == 0 && !string.IsNullOrEmpty(BraveApiKey) && !await _rateLimit.IsBlockedAsync("Brave") && !await IsBraveCooldownActiveAsync())
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
                        if (_braveRefreshContext) _braveUsedThisRefresh = true; else await RecordBraveCallAsync();
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

        /// <summary>Call at start of full TrailBlazer refresh. Brave calls will defer 24h block until RecordBraveRefreshCompleteIfUsed.</summary>
        public static void SetBraveRefreshContext(bool inRefresh)
        {
            _braveRefreshContext = inRefresh;
            if (inRefresh) _braveUsedThisRefresh = false;
        }

        /// <summary>When true, manual refresh will try Brave for currency strength even when Finnhub has data. Ensures Brave runs on demand; block is set after refresh.</summary>
        public static void SetForceBraveForRefresh(bool force) => _forceBraveForRefresh = force;

        /// <summary>Call at end of refresh (after currency strength). Records cooldown block if Brave was used during this refresh.</summary>
        public async Task RecordBraveRefreshCompleteIfUsedAsync()
        {
            if (!_braveUsedThisRefresh) return;
            await RecordBraveCallAsync();
            _braveUsedThisRefresh = false;
            _logger.LogInformation("Brave API: {Hours}h cooldown set after refresh (news/currency strength completed)", BraveCooldownHours);
        }

        /// <summary>Returns true if Brave was called within the configured cooldown window (default 48h). Skips Brave to reduce API costs.</summary>
        private async Task<bool> IsBraveCooldownActiveAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == BraveLastCallKey);
            if (setting == null || string.IsNullOrEmpty(setting.Value)) return false;
            if (!DateTime.TryParse(setting.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastCall))
                return false;
            var elapsed = (DateTime.UtcNow - lastCall).TotalHours;
            if (elapsed < BraveCooldownHours)
            {
                _logger.LogDebug("Brave API: skipping (cooldown {Elapsed:F1}h / {Hours}h)", elapsed, BraveCooldownHours);
                return true;
            }
            return false;
        }

        private async Task RecordBraveCallAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow.ToString("o");
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == BraveLastCallKey);
            if (setting != null)
            {
                setting.Value = now;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting { Key = BraveLastCallKey, Value = now, UpdatedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
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

            var score = NewsSentimentHelper.ComputeFromTexts(news.Select(n => n.Headline + " " + n.Summary).Where(s => !string.IsNullOrWhiteSpace(s)).ToList());
            return (score, true);
        }

        /// <summary>Fetches news and returns items for storage. Use when caller needs to persist to DB. When db is provided, uses existing news from DB if collected within 24h (avoids Brave/Finnhub calls).</summary>
        public async Task<(double score, bool hasData, List<NewsItem> items)> FetchNewsSentimentScoreWithItemsAsync(string symbol, string? assetClass, ApplicationDbContext? db = null)
        {
            const int newsReuseHours = 24;
            if (db != null)
            {
                var cutoff = DateTime.UtcNow.AddHours(-newsReuseHours);
                var fromDb = await db.NewsArticles
                    .Where(n => n.Symbol == symbol && n.DateCollected >= cutoff)
                    .OrderByDescending(n => n.DateCollected)
                    .Take(20)
                    .ToListAsync();
                if (fromDb.Count > 0)
                {
                    var texts = fromDb.Select(a => a.Headline + " " + a.Summary).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    var score = NewsSentimentHelper.ComputeFromTexts(texts);
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var ml = scope.ServiceProvider.GetService<MLModelService>();
                        if (ml != null)
                        {
                            var gem = await ml.TryGeminiInstrumentNewsSentimentAsync(symbol, assetClass, texts);
                            if (gem.HasValue)
                                score = Math.Clamp(score * 0.42 + gem.Value * 0.58, 1.0, 10.0);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Gemini news sentiment blend skipped (DB cache) for {Symbol}", symbol);
                    }

                    _logger.LogDebug("News for {Symbol}: using {Count} articles from DB (collected within {Hours}h, skipping Brave/Finnhub)", symbol, fromDb.Count, newsReuseHours);
                    return (score, true, new List<NewsItem>()); // Empty items = already in DB, don't re-persist
                }
            }

            var news = await FetchNewsForSymbolAsync(symbol, assetClass);
            if (news == null || news.Count == 0)
                return (5.0, false, new List<NewsItem>());

            var newsTexts = news.Select(n => n.Headline + " " + n.Summary).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            var computedScore = NewsSentimentHelper.ComputeFromTexts(newsTexts);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ml = scope.ServiceProvider.GetService<MLModelService>();
                if (ml != null)
                {
                    var gem = await ml.TryGeminiInstrumentNewsSentimentAsync(symbol, assetClass, newsTexts);
                    if (gem.HasValue)
                        computedScore = Math.Clamp(computedScore * 0.42 + gem.Value * 0.58, 1.0, 10.0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Gemini news sentiment blend skipped for {Symbol}", symbol);
            }

            return (computedScore, true, news);
        }

        /// <summary>Brave Web Search - returns title, url, description for each result. Rate-limited and cacheable.</summary>
        public async Task<List<WebSearchResult>> BraveWebSearchAsync(string query, int count = 3, string freshness = "pw")
        {
            var results = new List<WebSearchResult>();
            if (string.IsNullOrEmpty(BraveApiKey)) return results;
            if (await _rateLimit.IsBlockedAsync("Brave")) return results;
            if (await IsBraveCooldownActiveAsync()) return results;
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
                if (_braveRefreshContext) _braveUsedThisRefresh = true; else await RecordBraveCallAsync();
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

        /// <summary>Fetches market outlook/forecast snippets for an instrument via Brave web search. Cached per TrailBlazer:BraveOutlookCacheMinutes (default 48h).</summary>
        public async Task<List<WebSearchResult>> FetchInstrumentOutlookAsync(string symbol, string? assetClass)
        {
            var cacheKey = $"brave_outlook:{symbol}:{assetClass ?? ""}";
            if (_cache != null && _cache.TryGetValue(cacheKey, out List<WebSearchResult>? cached))
                return cached ?? new List<WebSearchResult>();

            var query = BuildOutlookSearchQuery(symbol, assetClass);
            var results = await BraveWebSearchAsync(query, 3, "pm");
            var ttl = TimeSpan.FromMinutes(Math.Max(1, BraveOutlookCacheMinutes));
            if (_cache != null && results.Count > 0)
                _cache.Set(cacheKey, results, ttl);
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
            if (symbol == "BTC") return "Bitcoin price forecast outlook";
            if (symbol == "ETH") return "Ethereum price forecast outlook";
            if (symbol == "SOL") return "Solana price forecast outlook";
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
            if (symbol == "BTC") return "Bitcoin price crypto";
            if (symbol == "ETH") return "Ethereum price crypto";
            if (symbol == "SOL") return "Solana price crypto";
            return $"{symbol} market news";
        }

        // ────── Technical Indicators (Yahoo first when mappable, then MarketStack / Twelve Data / fallbacks) ──────

        /// <summary>Fetches technical indicators. Yahoo Finance first for mapped symbols; then indices via MarketStack; then Twelve Data.</summary>
        public async Task<Dictionary<string, double>> FetchTechnicalIndicatorsAsync(string symbol)
        {
            var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RSI"] = 50,
                ["MACD"] = 0,
                ["MACDSignal"] = 0,
                ["EMA50"] = 0,
                ["EMA200"] = 0,
                ["StochasticK"] = 50
            };

            if (_yahooFinanceService != null && YahooFinanceService.ToYahooSymbol(symbol) != null)
            {
                var y = await _yahooFinanceService.FetchTechnicalIndicatorsAsync(symbol);
                if (y.TryGetValue("RSI", out var yRsi) && yRsi > 0 && yRsi < 100) results["RSI"] = yRsi;
                if (y.TryGetValue("EMA50", out var yE50) && yE50 != 0) results["EMA50"] = yE50;
                if (y.TryGetValue("EMA200", out var yE200) && yE200 != 0) results["EMA200"] = yE200;
                if (y.TryGetValue("MACD", out var yMacd) && yMacd != 0) results["MACD"] = yMacd;
                if (y.TryGetValue("MACDSignal", out var ySig) && ySig != 0) results["MACDSignal"] = ySig;
                if (y.TryGetValue("StochasticK", out var yStoch) && yStoch != 50) results["StochasticK"] = yStoch;
                if (y.TryGetValue("Close", out var yClose) && yClose > 0) results["Close"] = yClose;
                if (results.Values.Any(v => v != 0 && v != 50))
                    return results;
            }

            if (MarketStackService.ToMarketStackSymbol(symbol) != null && _marketStackService != null)
            {
                var ms = await _marketStackService.FetchTechnicalIndicatorsAsync(symbol);
                if (ms.TryGetValue("RSI", out var rsi)) results["RSI"] = rsi;
                if (ms.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
                if (ms.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;
                if (ms.TryGetValue("MACD", out var macd) && macd != 0) results["MACD"] = macd;
                if (ms.TryGetValue("MACDSignal", out var sig) && sig != 0) results["MACDSignal"] = sig;
                if (ms.TryGetValue("StochasticK", out var stoch) && stoch != 50) results["StochasticK"] = stoch;
                if (ms.TryGetValue("Close", out var close) && close > 0) results["Close"] = close;
                return results;
            }

            if (_twelveDataService == null) return results;
            var td = await _twelveDataService.FetchTechnicalIndicatorsAsync(symbol);
            if (td.TryGetValue("RSI", out var r)) results["RSI"] = r;
            if (td.TryGetValue("EMA50", out var e50) && e50 != 0) results["EMA50"] = e50;
            if (td.TryGetValue("EMA200", out var e200) && e200 != 0) results["EMA200"] = e200;
            if (td.TryGetValue("MACD", out var m) && m != 0) results["MACD"] = m;
            if (td.TryGetValue("MACDSignal", out var s) && s != 0) results["MACDSignal"] = s;
            if (td.TryGetValue("StochasticK", out var sk) && sk != 50) results["StochasticK"] = sk;
            return results;
        }

        /// <summary>Fetches technical indicators. Yahoo first; then MarketStack for indices; then Twelve Data and other fallbacks. Stores in DB.</summary>
        public async Task<(Dictionary<string, double> technicals, string? source)> FetchAndStoreTechnicalIndicatorsAsync(ApplicationDbContext db, int instrumentId, string symbol)
        {
            var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RSI"] = 50,
                ["MACD"] = 0,
                ["MACDSignal"] = 0,
                ["EMA50"] = 0,
                ["EMA200"] = 0,
                ["StochasticK"] = 50
            };
            string? sourceUsed = null;

            // Primary: Yahoo Finance (free OHLC → same indicators as MarketStack; strong forex/crypto/index coverage)
            if (_yahooFinanceService != null && YahooFinanceService.ToYahooSymbol(symbol) != null)
            {
                var y = await _yahooFinanceService.FetchTechnicalIndicatorsAsync(symbol);
                if (y.TryGetValue("RSI", out var yrsi) && yrsi > 0 && yrsi < 100) results["RSI"] = yrsi;
                if (y.TryGetValue("EMA50", out var ye50) && ye50 != 0) results["EMA50"] = ye50;
                if (y.TryGetValue("EMA200", out var ye200) && ye200 != 0) results["EMA200"] = ye200;
                if (y.TryGetValue("MACD", out var ym) && ym != 0) results["MACD"] = ym;
                if (y.TryGetValue("MACDSignal", out var ys) && ys != 0) results["MACDSignal"] = ys;
                if (y.TryGetValue("StochasticK", out var yst) && yst >= 0 && yst <= 100) results["StochasticK"] = yst;
                if (y.TryGetValue("Close", out var ycl) && ycl > 0) results["Close"] = ycl;

                var rsiVal = y.TryGetValue("RSI", out var yfR) && yfR > 0 && yfR < 100 ? yfR : (double?)null;
                var sma14 = y.TryGetValue("SMA14", out var yfS14) && yfS14 != 0 ? yfS14 : (double?)null;
                var sma50 = y.TryGetValue("SMA50", out var yfS50) && yfS50 != 0 ? yfS50 : (double?)null;
                var ema50Val = y.TryGetValue("EMA50", out var yfE50) && yfE50 != 0 ? yfE50 : (double?)null;
                var ema200Val = y.TryGetValue("EMA200", out var yfE200) && yfE200 != 0 ? yfE200 : (double?)null;
                double? macdVal = y.TryGetValue("MACD", out var yfM) && yfM != 0 ? yfM : null;
                double? macdSigVal = y.TryGetValue("MACDSignal", out var yfSg) && yfSg != 0 ? yfSg : null;
                double? stochVal = y.TryGetValue("StochasticK", out var yfSk) && yfSk >= 0 && yfSk <= 100 ? yfSk : null;

                if (rsiVal != null || sma14 != null || sma50 != null || ema50Val != null || ema200Val != null)
                {
                    sourceUsed = "YahooFinance";
                    double? closeVal = y.TryGetValue("Close", out var yfC) && yfC > 0 ? yfC : (double?)null;
                    if (!closeVal.HasValue && ema200Val.HasValue) { var q = await TryFetchCloseAsync(symbol); if (q > 0) closeVal = q; }
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, closeVal, "YahooFinance", macdVal, macdSigVal, stochVal);
                }
                else
                    _logger.LogDebug("Technicals: Yahoo returned no usable data for {Symbol}, trying other providers", symbol);
            }

            // Indices: MarketStack when Yahoo did not persist (API key path)
            if (sourceUsed == null && MarketStackService.ToMarketStackSymbol(symbol) != null && _marketStackService != null)
            {
                var ms = await _marketStackService.FetchTechnicalIndicatorsAsync(symbol);
                if (ms.TryGetValue("RSI", out var rsi) && rsi > 0 && rsi < 100) results["RSI"] = rsi;
                if (ms.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
                if (ms.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;
                if (ms.TryGetValue("MACD", out var macd) && macd != 0) results["MACD"] = macd;
                if (ms.TryGetValue("MACDSignal", out var sig) && sig != 0) results["MACDSignal"] = sig;
                if (ms.TryGetValue("StochasticK", out var stoch) && stoch >= 0 && stoch <= 100) results["StochasticK"] = stoch;
                if (ms.TryGetValue("Close", out var close) && close > 0) results["Close"] = close;

                if (results.Values.Any(v => v != 0 && v != 50))
                {
                    sourceUsed = "MarketStack";
                    var rsiVal = results.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                    var ema50Val = results.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                    var ema200Val = results.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;
                    double? sma14 = ms.TryGetValue("SMA14", out var s) && s != 0 ? s : null;
                    double? sma50 = ms.TryGetValue("SMA50", out var s5) && s5 != 0 ? s5 : null;
                    double? closeVal = ms.TryGetValue("Close", out var cl) && cl > 0 ? cl : (double?)null;
                    double? macdVal = ms.TryGetValue("MACD", out var m) && m != 0 ? m : null;
                    double? macdSigVal = ms.TryGetValue("MACDSignal", out var sg) && sg != 0 ? sg : null;
                    double? stochVal = ms.TryGetValue("StochasticK", out var sk) && sk >= 0 && sk <= 100 ? sk : null;
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, closeVal, "MarketStack", macdVal, macdSigVal, stochVal);
                }
                else
                {
                    if (MarketStackService.ToMarketStackSymbol(symbol) != null)
                        _logger.LogInformation("Technicals: MarketStack failed for index {Symbol}, falling back to TwelveData", symbol);
                    else
                        _logger.LogDebug("Technicals: MarketStack returned no usable data for {Symbol}, skipping DB write", symbol);
                }
            }

            // Fallback: Twelve Data (forex, metals, indices when MarketStack fails)
            if (sourceUsed == null && _twelveDataService != null)
            {
                var td = await _twelveDataService.FetchTechnicalIndicatorsAsync(symbol);
                if (td.TryGetValue("RSI", out var rsi)) results["RSI"] = rsi;
                if (td.TryGetValue("EMA50", out var ema50) && ema50 != 0) results["EMA50"] = ema50;
                if (td.TryGetValue("EMA200", out var ema200) && ema200 != 0) results["EMA200"] = ema200;
                if (td.TryGetValue("MACD", out var macd) && macd != 0) results["MACD"] = macd;
                if (td.TryGetValue("MACDSignal", out var sig) && sig != 0) results["MACDSignal"] = sig;
                if (td.TryGetValue("StochasticK", out var stoch) && stoch >= 0 && stoch <= 100) results["StochasticK"] = stoch;

                var rsiVal = td.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                var sma14 = td.TryGetValue("SMA14", out var s14) && s14 != 0 ? s14 : (double?)null;
                var sma50 = td.TryGetValue("SMA50", out var s50) && s50 != 0 ? s50 : (double?)null;
                var ema50Val = td.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                var ema200Val = td.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;
                double? macdVal = td.TryGetValue("MACD", out var m) && m != 0 ? m : null;
                double? macdSigVal = td.TryGetValue("MACDSignal", out var sg) && sg != 0 ? sg : null;
                double? stochVal = td.TryGetValue("StochasticK", out var sk) && sk >= 0 && sk <= 100 ? sk : null;

                if (rsiVal != null || sma14 != null || sma50 != null || ema50Val != null || ema200Val != null)
                {
                    sourceUsed = "TwelveData";
                    double? closeVal = td.TryGetValue("Close", out var c) && c > 0 ? c : (double?)null;
                    if (!closeVal.HasValue && ema200Val.HasValue) { var q = await TryFetchCloseAsync(symbol); if (q > 0) closeVal = q; }
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, closeVal, "TwelveData", macdVal, macdSigVal, stochVal);
                }
                else
                    _logger.LogDebug("Technicals: TwelveData returned no usable data for {Symbol}, skipping DB write", symbol);
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
                    double? closeVal = it.TryGetValue("Close", out var cl) && cl > 0 ? cl : (double?)null;
                    if (!closeVal.HasValue && ema200Val.HasValue) { var q = await TryFetchCloseAsync(symbol); if (q > 0) closeVal = q; }
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, closeVal, "iTick");
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
                    double? closeVal = eod.TryGetValue("Close", out var cl) && cl > 0 ? cl : (double?)null;
                    if (!closeVal.HasValue && ema200Val.HasValue) { var q = await TryFetchCloseAsync(symbol); if (q > 0) closeVal = q; }
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, closeVal, "EODHD");
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
                if (fmp.TryGetValue("Close", out var close) && close > 0) results["Close"] = close;

                if (results.Values.Any(v => v != 0 && v != 50))
                {
                    sourceUsed = "FMP";
                    var rsiVal = results.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                    var ema50Val = results.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                    var ema200Val = results.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;
                    double? sma14 = fmp.TryGetValue("SMA14", out var s) && s != 0 ? s : null;
                    double? sma50 = fmp.TryGetValue("SMA50", out var s5) && s5 != 0 ? s5 : null;
                    double? closeVal = fmp.TryGetValue("Close", out var cl) && cl > 0 ? cl : (double?)null;
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, closeVal, "FMP");
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
                    double? closeVal = ndl.TryGetValue("Close", out var cl) && cl > 0 ? cl : (double?)null;
                    if (!closeVal.HasValue && ema200Val.HasValue) { var q = await TryFetchCloseAsync(symbol); if (q > 0) closeVal = q; }
                    await SaveTechnicalToDbAsync(db, instrumentId, symbol, rsiVal, sma14, sma50, ema50Val, ema200Val, closeVal, "NasdaqDataLink");
                }
                else
                    _logger.LogDebug("Technicals: NasdaqDataLink returned no usable data for {Symbol}, skipping DB write", symbol);
            }

            // When no provider returns data: do NOT default to 5. Do not write to DB. Scoring will use latest from TechnicalIndicators table.
            if (sourceUsed == null)
                _logger.LogDebug("Technicals: no provider had data for {Symbol}; not posting to database; scoring will use latest from TechnicalIndicators if available", symbol);

            return (results, sourceUsed);
        }

        /// <summary>Loads latest technical data from TechnicalIndicators table for scoring. Scoring must use only DB data. Returns null when no data exists (do not default to 5).</summary>
        public static async Task<(Dictionary<string, double>? technicals, string? source, DateTime? dateCollected)> LoadTechnicalFromDbForScoringAsync(ApplicationDbContext db, int instrumentId)
        {
            var latest = await db.TechnicalIndicators
                .Where(t => t.InstrumentId == instrumentId)
                .OrderByDescending(t => t.DateCollected)
                .FirstOrDefaultAsync();

            if (latest == null)
                return (null, null, null);

            var hasUsableData = (latest.RSI.HasValue && latest.RSI > 0 && latest.RSI < 100) || (latest.EMA50.HasValue && latest.EMA50 != 0) || (latest.EMA200.HasValue && latest.EMA200 != 0);
            if (!hasUsableData)
                return (null, null, null);

            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RSI"] = latest.RSI.HasValue && latest.RSI > 0 && latest.RSI < 100 ? latest.RSI.Value : 50,
                ["MACD"] = latest.MACD ?? 0,
                ["MACDSignal"] = latest.MACDSignal ?? 0,
                ["EMA50"] = latest.EMA50 ?? 0,
                ["EMA200"] = latest.EMA200 ?? 0,
                ["Close"] = latest.Close ?? 0,
                ["StochasticK"] = latest.StochasticK ?? 50
            };
            return (dict, latest.Source, latest.DateCollected);
        }

        /// <summary>Saves technical indicators only when we have at least one value. When updating, only overwrites fields we received (non-null); preserves existing DB values for missing data.</summary>
        private async Task SaveTechnicalToDbAsync(ApplicationDbContext db, int instrumentId, string symbol, double? rsiVal, double? sma14, double? sma50, double? ema50Val, double? ema200Val, double? closeVal, string source, double? macdVal = null, double? macdSignalVal = null, double? stochasticKVal = null)
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
                if (closeVal.HasValue && closeVal.Value > 0) existing.Close = closeVal;
                if (macdVal.HasValue) existing.MACD = macdVal;
                if (macdSignalVal.HasValue) existing.MACDSignal = macdSignalVal;
                if (stochasticKVal.HasValue && stochasticKVal.Value >= 0 && stochasticKVal.Value <= 100) existing.StochasticK = stochasticKVal;
                existing.DateCollected = now;
                existing.Source = source;
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
                    Close = closeVal.HasValue && closeVal.Value > 0 ? closeVal : null,
                    MACD = macdVal,
                    MACDSignal = macdSignalVal,
                    StochasticK = stochasticKVal.HasValue && stochasticKVal.Value >= 0 && stochasticKVal.Value <= 100 ? stochasticKVal : null,
                    DateCollected = now,
                    Source = source
                });
            }
            _logger.LogInformation("Technicals: saved for {Symbol} from {Source} (RSI={HasRsi}, MACD={HasMacd}, StochasticK={HasStoch})", symbol, source, rsiVal.HasValue, macdVal.HasValue, stochasticKVal.HasValue);
        }

        /// <summary>Tries to fetch latest close price for price vs EMA200 comparison. Yahoo first when mapped; then forex chain / EODHD / FMP.</summary>
        private async Task<double> TryFetchCloseAsync(string symbol)
        {
            var norm = symbol.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
            if (_yahooFinanceService != null && YahooFinanceService.ToYahooSymbol(norm) != null)
            {
                try
                {
                    var yq = await _yahooFinanceService.FetchQuoteAsync(norm);
                    if (yq > 0) return yq;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Yahoo quote failed for {Symbol} in TryFetchClose", norm);
                }
            }
            if (norm.Length >= 6 && norm.All(char.IsLetter))
            {
                var quote = await FetchForexQuoteAsync(norm);
                if (quote > 0) return quote;
            }
            if (_eodhdService != null && EodhdService.ToEodhdSymbol(symbol) != null)
            {
                var q = await _eodhdService.FetchRealTimeQuoteAsync(symbol);
                if (q > 0) return q;
            }
            if (_fmpService != null && FmpService.ToFmpSymbol(symbol) != null)
            {
                var q = await _fmpService.FetchQuoteAsync(symbol);
                if (q > 0) return q;
            }
            return 0;
        }

        /// <summary>Loads technical data from Twelve Data only into TechnicalIndicators table.</summary>
        public async Task<int> LoadTechnicalDataToDatabaseAsync(ApplicationDbContext db, int? limit = null)
        {
            if (_twelveDataService == null) return 0;
            return await _twelveDataService.LoadTechnicalDataToDatabaseAsync(db, limit);
        }

        /// <summary>Yahoo Finance first (CL=F, ^GSPC, etc.), then FMP. Closes are newest-first.</summary>
        private async Task<List<double>> FetchHistoricalClosesForAnalyticsAsync(string instrumentName, int tradingDays)
        {
            var minNeed = Math.Max(25, tradingDays + 5);
            if (_yahooFinanceService != null && YahooFinanceService.ToYahooSymbol(instrumentName) != null)
            {
                var y = await _yahooFinanceService.FetchHistoricalClosesNewestFirstAsync(instrumentName, minNeed + 30);
                if (y.Count >= Math.Min(minNeed, Math.Max(25, tradingDays)))
                    return y;
            }
            if (_fmpService != null)
            {
                var f = await _fmpService.FetchHistoricalClosesAsync(instrumentName, tradingDays + 15);
                if (f.Count > 0)
                    return f;
            }
            return new List<double>();
        }

        /// <summary>Runs the Asset Scanner analyzer with full diagnostic output (all intermediate values). Returns null when no bars are available.</summary>
        public async Task<AssetScannerSignalAnalyzer.SignalDiagnostic?> DiagnoseSignalAsync(string instrumentName, double overallScore, string? bias)
        {
            if (_yahooFinanceService == null || YahooFinanceService.ToYahooSymbol(instrumentName) == null)
                return null;
            var dailyBars = await _yahooFinanceService.FetchDailyOhlcAsync(instrumentName, 120);
            var fourHourBars = await _yahooFinanceService.Fetch4HourOhlcAsync(instrumentName, 160);
            if (dailyBars == null || dailyBars.Count < 25 || fourHourBars == null || fourHourBars.Count < 25)
                return null;
            return AssetScannerSignalAnalyzer.Diagnose(
                dailyBars,
                fourHourBars,
                overallScore,
                bias,
                instrumentName,
                _config.GetValue("TrailBlazer:SignalSwingLookback", 30),
                _config.GetValue("TrailBlazer:SignalResistanceLookback", 20),
                _config.GetValue("TrailBlazer:SignalFibTolerancePct", 0.8),
                _config.GetValue("TrailBlazer:SignalResistanceTolerancePct", 0.5),
                _config.GetValue("TrailBlazer:SignalBuyThreshold", 6.0),
                _config.GetValue("TrailBlazer:SignalSellThreshold", 4.0),
                _config.GetValue("TrailBlazer:SignalTrendlinePivot", 2),
                _config.GetValue("TrailBlazer:SignalMinLegSizePct", 1.2),
                _config.GetValue("TrailBlazer:SignalConfluencePctBuffer", 0.1),
                _config.GetValue("TrailBlazer:SignalConfluenceAtrMultiplier", 0.5),
                _config.GetValue("TrailBlazer:SignalConfluenceMaxPips", 15.0));
        }

        /// <summary>Asset Scanner signal engine (score-led direction + Fib/resistance alignment). Mutates score.</summary>
        public async Task ApplyBoxBreakoutTradeSetupAsync(TrailBlazerScore score, string instrumentName, TraderNickInsight? traderNick = null)
        {
            score.TradeSetupSignal = "NONE";
            score.TradeSetupDetail = null;
            if (_yahooFinanceService == null || YahooFinanceService.ToYahooSymbol(instrumentName) == null)
                return;
            try
            {
                var dailyBars = await _yahooFinanceService.FetchDailyOhlcAsync(instrumentName, 120);
                var fourHourBars = await _yahooFinanceService.Fetch4HourOhlcAsync(instrumentName, 160);
                if (dailyBars == null || dailyBars.Count < 25 || fourHourBars == null || fourHourBars.Count < 25)
                    return;
                var swingLookback = _config.GetValue("TrailBlazer:SignalSwingLookback", 30);
                var continuationLookback = _config.GetValue("TrailBlazer:SignalResistanceLookback", 20);
                var fibTolerance = _config.GetValue("TrailBlazer:SignalFibTolerancePct", 0.8);
                var continuationTolerance = _config.GetValue("TrailBlazer:SignalResistanceTolerancePct", 0.5);
                var buyThreshold = _config.GetValue("TrailBlazer:SignalBuyThreshold", 6.0);
                var sellThreshold = _config.GetValue("TrailBlazer:SignalSellThreshold", 4.0);
                var pivotStrength = _config.GetValue("TrailBlazer:SignalTrendlinePivot", 2);
                var minLegSizePct = _config.GetValue("TrailBlazer:SignalMinLegSizePct", 1.2);
                var confluencePctBuffer = _config.GetValue("TrailBlazer:SignalConfluencePctBuffer", 0.1);
                var confluenceAtrMultiplier = _config.GetValue("TrailBlazer:SignalConfluenceAtrMultiplier", 0.5);
                var confluenceMaxPips = _config.GetValue("TrailBlazer:SignalConfluenceMaxPips", 15.0);
                var (finalSig, finalDet) = AssetScannerSignalAnalyzer.Analyze(
                    dailyBars,
                    fourHourBars,
                    score.OverallScore,
                    score.Bias,
                    instrumentName,
                    swingLookback,
                    continuationLookback,
                    fibTolerance,
                    continuationTolerance,
                    buyThreshold,
                    sellThreshold,
                    pivotStrength,
                    minLegSizePct,
                    confluencePctBuffer,
                    confluenceAtrMultiplier,
                    confluenceMaxPips);
                score.TradeSetupSignal = finalSig;
                score.TradeSetupDetail = finalDet != null && finalDet.Length > 500 ? finalDet[..500] : finalDet;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Box breakout setup failed for {Instrument}", instrumentName);
            }
        }

        private const string OilIndexCorrelationCacheKeyPrefix = "OilIndexCorrelation_";

        /// <summary>Computes rolling correlation between USOIL and US500/US30. Yahoo historical closes first; FMP fallback. Cached 1h.</summary>
        public async Task<object> ComputeOilIndexCorrelationAsync(int window30 = 30, int window60 = 60)
        {
            if (_yahooFinanceService == null && _fmpService == null)
                return new { usoilUs500_30d = (double?)null, usoilUs500_60d = (double?)null, usoilUs30_30d = (double?)null, usoilUs30_60d = (double?)null, message = "No price history provider (enable Yahoo or FMP)" };

            var cacheKey = $"{OilIndexCorrelationCacheKeyPrefix}{window30}_{window60}";
            if (_cache != null && _cache.TryGetValue(cacheKey, out object? cached))
                return cached!;

            var days = Math.Max(window30, window60) + 10;
            var oil = await FetchHistoricalClosesForAnalyticsAsync("USOIL", days);
            var sp500 = await FetchHistoricalClosesForAnalyticsAsync("US500", days);
            var dji = await FetchHistoricalClosesForAnalyticsAsync("US30", days);

            static List<double> ToReturns(List<double> closes)
            {
                var ret = new List<double>();
                for (var i = 0; i < closes.Count - 1; i++)
                    ret.Add(closes[i] != 0 ? (closes[i] - closes[i + 1]) / closes[i + 1] : 0);
                return ret;
            }
            static double? PearsonCorr(List<double> a, List<double> b, int n)
            {
                if (a.Count < n || b.Count < n) return null;
                var sa = a.Take(n).ToList();
                var sb = b.Take(n).ToList();
                var meanA = sa.Average();
                var meanB = sb.Average();
                double sum = 0, sumA = 0, sumB = 0;
                for (var i = 0; i < n; i++)
                {
                    var da = sa[i] - meanA;
                    var db = sb[i] - meanB;
                    sum += da * db;
                    sumA += da * da;
                    sumB += db * db;
                }
                var denom = Math.Sqrt(sumA * sumB);
                return denom > 1e-10 ? sum / denom : (double?)null;
            }

            var retOil = ToReturns(oil);
            var retSp500 = ToReturns(sp500);
            var retDji = ToReturns(dji);

            var n30 = Math.Min(window30, Math.Min(retOil.Count, Math.Min(retSp500.Count, retDji.Count)));
            var n60 = Math.Min(window60, Math.Min(retOil.Count, Math.Min(retSp500.Count, retDji.Count)));

            var result = new
            {
                usoilUs500_30d = n30 >= 10 ? PearsonCorr(retOil, retSp500, n30) : (double?)null,
                usoilUs500_60d = n60 >= 20 ? PearsonCorr(retOil, retSp500, n60) : (double?)null,
                usoilUs30_30d = n30 >= 10 ? PearsonCorr(retOil, retDji, n30) : (double?)null,
                usoilUs30_60d = n60 >= 20 ? PearsonCorr(retOil, retDji, n60) : (double?)null,
                dataPoints30 = n30,
                dataPoints60 = n60,
                message = retOil.Count == 0 || retSp500.Count == 0 ? "Insufficient overlapping history (check Yahoo/FMP)" : (string?)null
            };
            if (_cache != null)
                _cache.Set(cacheKey, result, TimeSpan.FromHours(1));
            return result;
        }

        private const string RelativeStrengthCacheKeyPrefix = "RelativeStrength_";

        /// <summary>Ranks assets by % price change over N days. Yahoo closes first; FMP fallback. Cached 1h.</summary>
        public async Task<List<(string symbol, double pctChange5d, double pctChange20d)>> GetRelativeStrengthRankingAsync(int days5 = 5, int days20 = 20)
        {
            var cacheKey = $"{RelativeStrengthCacheKeyPrefix}{days5}_{days20}";
            if (_cache != null && _cache.TryGetValue(cacheKey, out List<(string symbol, double pctChange5d, double pctChange20d)>? cached))
                return cached ?? new List<(string, double, double)>();

            var symbols = new[] { "US500", "US30", "US100", "USOIL", "XAUUSD", "XAGUSD" };
            var results = new List<(string symbol, double pctChange5d, double pctChange20d)>();

            if (_yahooFinanceService == null && _fmpService == null) return results;

            foreach (var sym in symbols)
            {
                var closes = await FetchHistoricalClosesForAnalyticsAsync(sym, Math.Max(days5, days20) + 10);
                if (closes.Count < Math.Max(days5, days20) + 1) continue;

                var pct5 = closes.Count > days5 && closes[days5] > 0
                    ? (closes[0] - closes[days5]) / closes[days5] * 100
                    : 0.0;
                var pct20 = closes.Count > days20 && closes[days20] > 0
                    ? (closes[0] - closes[days20]) / closes[days20] * 100
                    : 0.0;

                results.Add((sym, pct5, pct20));
            }

            var ordered = results.OrderByDescending(r => r.pctChange20d).ToList();
            if (_cache != null && ordered.Count > 0)
                _cache.Set(cacheKey, ordered, TimeSpan.FromHours(1));
            return ordered;
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

            if (_yahooFinanceService != null && YahooFinanceService.ToYahooSymbol(symbol) != null)
            {
                try
                {
                    var yq = await _yahooFinanceService.FetchQuoteAsync(symbol);
                    if (yq > 0) return yq;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Yahoo forex quote failed for {Symbol}, falling back", symbol);
                }
            }

            // Finnhub (real-time forex quote)
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

        /// <summary>Fetches global economic news headlines for currency strength. Yahoo first (no key), then optional Brave-first on manual refresh, Finnhub, Brave fallback.</summary>
        public async Task<List<string>> FetchGlobalEconomicNewsAsync()
        {
            var headlines = new List<string>();
            if (_yahooFinanceService != null)
            {
                try
                {
                    var yh = await _yahooFinanceService.FetchGlobalMacroHeadlinesAsync(25);
                    foreach (var h in yh)
                    {
                        if (!string.IsNullOrWhiteSpace(h))
                            headlines.Add(h);
                    }
                    if (headlines.Count > 0)
                    {
                        _logger.LogInformation("FetchGlobalEconomicNewsAsync: Yahoo returned {Count} headlines for currency strength", headlines.Count);
                        return headlines;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "FetchGlobalEconomicNewsAsync Yahoo failed, continuing");
                }
            }

            var tryBraveFirst = _forceBraveForRefresh && _braveRefreshContext && !string.IsNullOrEmpty(BraveApiKey) && !await _rateLimit.IsBlockedAsync("Brave") && !await IsBraveCooldownActiveAsync();
            if (tryBraveFirst)
            {
                try
                {
                    var webResults = await BraveWebSearchAsync("global economic news forex GDP inflation Fed ECB", 15, "pd");
                    foreach (var r in webResults)
                    {
                        if (!string.IsNullOrWhiteSpace(r.Title))
                            headlines.Add(string.IsNullOrWhiteSpace(r.Description) ? r.Title : $"{r.Title} {r.Description}");
                    }
                    if (headlines.Count > 0)
                    {
                        _logger.LogInformation("FetchGlobalEconomicNewsAsync: Brave (forced for manual refresh) returned {Count} headlines for currency strength", headlines.Count);
                        return headlines;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "FetchGlobalEconomicNewsAsync Brave (forced) failed, falling back to Finnhub");
                }
            }
            if (!string.IsNullOrEmpty(FinnhubApiKey) && !await _rateLimit.IsBlockedAsync("Finnhub"))
            {
                try
                {
                    var url = $"https://finnhub.io/api/v1/news?category=general&token={FinnhubApiKey}";
                    var json = await _client.GetStringAsync(url);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray().Take(20))
                        {
                            var h = el.TryGetProperty("headline", out var hh) ? hh.GetString() ?? "" : "";
                            var s = el.TryGetProperty("summary", out var ss) ? ss.GetString() ?? "" : "";
                            if (!string.IsNullOrWhiteSpace(h))
                                headlines.Add(string.IsNullOrWhiteSpace(s) ? h : $"{h} {s}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "FetchGlobalEconomicNewsAsync Finnhub failed");
                }
            }

            if (headlines.Count == 0 && !string.IsNullOrEmpty(BraveApiKey) && !await _rateLimit.IsBlockedAsync("Brave") && !await IsBraveCooldownActiveAsync())
            {
                try
                {
                    var webResults = await BraveWebSearchAsync("global economic news forex GDP inflation Fed ECB", 15, "pd");
                    foreach (var r in webResults)
                    {
                        if (!string.IsNullOrWhiteSpace(r.Title))
                            headlines.Add(string.IsNullOrWhiteSpace(r.Description) ? r.Title : $"{r.Title} {r.Description}");
                    }
                    _logger.LogInformation("FetchGlobalEconomicNewsAsync: Brave fallback returned {Count} headlines for currency strength", headlines.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "FetchGlobalEconomicNewsAsync Brave fallback failed");
                }
            }
            return headlines;
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

        /// <summary>Fetches retail sentiment. Uses configured session, env, stored DB, or login.</summary>
        public async Task<Dictionary<string, (double longPct, double shortPct)>> FetchMyFxBookSentimentBatchAsync()
        {
            var configSession = MyFxBookSession;
            if (!string.IsNullOrEmpty(configSession))
            {
                var r = await FetchMyFxBookSentimentBatchWithSessionAsync(configSession);
                if (r.Count > 0)
                {
                    _logger.LogInformation("MyFXBook: used TrailBlazer:MyFXBookSession — {Count} symbols", r.Count);
                    await EnrichWithCryptoRetailFromBinanceAsync(r);
                    await EnrichWithIndexRetailFromFinnhubAsync(r);
                    return r;
                }
                _logger.LogWarning("MyFXBook: configured session returned 0 symbols (session may be invalid)");
            }
            var envSession = Environment.GetEnvironmentVariable("MYFXBOOK_SESSION");
            if (!string.IsNullOrEmpty(envSession))
            {
                var r = await FetchMyFxBookSentimentBatchWithSessionAsync(envSession);
                if (r.Count > 0)
                {
                    _logger.LogInformation("MyFXBook: used MYFXBOOK_SESSION env — {Count} symbols", r.Count);
                    await EnrichWithCryptoRetailFromBinanceAsync(r);
                    await EnrichWithIndexRetailFromFinnhubAsync(r);
                    return r;
                }
                _logger.LogWarning("MyFXBook: MYFXBOOK_SESSION returned 0 symbols (session may be invalid)");
            }
            var (result, diag) = await FetchMyFxBookSentimentBatchWithDiagnosticAsync();
            if (result.Count == 0)
                _logger.LogWarning("MyFXBook: batch empty after login. Diagnostic: {Diagnostic}", System.Text.Json.JsonSerializer.Serialize(diag));
            else
                _logger.LogInformation("MyFXBook: fetched {Count} symbols via login", result.Count);
            await EnrichWithCryptoRetailFromBinanceAsync(result);
            await EnrichWithIndexRetailFromFinnhubAsync(result);
            return result;
        }

        /// <summary>Enriches retail batch with US500, US100, US30 from Finnhub social sentiment (SPY, QQQ, DIA proxies). MyFXBook does not provide indices.</summary>
        private async Task EnrichWithIndexRetailFromFinnhubAsync(Dictionary<string, (double longPct, double shortPct)> batch)
        {
            if (string.IsNullOrEmpty(FinnhubApiKey)) return;
            var proxies = new[] { ("SPY", "US500"), ("QQQ", "US100"), ("DIA", "US30"), ("EWG", "DE40"), ("EWU", "UK100"), ("EWJ", "JP225") };
            var to = DateTime.UtcNow;
            var from = to.AddDays(-7);
            foreach (var (finnhubSymbol, ourSymbol) in proxies)
            {
                if (batch.ContainsKey(ourSymbol)) continue;
                try
                {
                    var url = $"https://finnhub.io/api/v1/stock/social-sentiment?symbol={Uri.EscapeDataString(finnhubSymbol)}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&token={Uri.EscapeDataString(FinnhubApiKey)}";
                    var json = await _client.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
                        continue;
                    double sumReddit = 0, sumTwitter = 0;
                    int countReddit = 0, countTwitter = 0;
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("redditScore", out var rs) && rs.TryGetDouble(out var rv) && rv >= 0 && rv <= 1)
                        { sumReddit += rv; countReddit++; }
                        if (item.TryGetProperty("twitterScore", out var ts) && ts.TryGetDouble(out var tv) && tv >= 0 && tv <= 1)
                        { sumTwitter += tv; countTwitter++; }
                    }
                    var avg = (countReddit + countTwitter) > 0
                        ? (sumReddit + sumTwitter) / (countReddit + countTwitter)
                        : -1.0;
                    if (avg >= 0 && avg <= 1)
                    {
                        var longPct = Math.Round(avg * 100, 1);
                        var shortPct = Math.Round(100 - longPct, 1);
                        batch[ourSymbol] = (longPct, shortPct);
                        _logger.LogDebug("Finnhub: added {Symbol} retail (proxy {Proxy}) long={Long:F1}% short={Short:F1}%", ourSymbol, finnhubSymbol, longPct, shortPct);
                    }
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Finnhub sentiment fetch failed for {Symbol}", ourSymbol);
                }
            }
        }

        /// <summary>Enriches retail batch with BTC, ETH, SOL and XAUUSD, XAGUSD from Binance Futures global long/short ratio. MyFXBook does not provide crypto or metals.</summary>
        private async Task EnrichWithCryptoRetailFromBinanceAsync(Dictionary<string, (double longPct, double shortPct)> batch)
        {
            var symbols = new[] { ("BTCUSDT", "BTC"), ("ETHUSDT", "ETH"), ("SOLUSDT", "SOL"), ("XAUUSDT", "XAUUSD"), ("XAGUSDT", "XAGUSD") };
            foreach (var (binanceSymbol, ourSymbol) in symbols)
            {
                if (batch.ContainsKey(ourSymbol)) continue;
                try
                {
                    var url = $"https://fapi.binance.com/futures/data/globalLongShortAccountRatio?symbol={binanceSymbol}&period=1d&limit=1";
                    var json = await _client.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) continue;
                    var item = doc.RootElement[0];
                    var longAccount = item.TryGetProperty("longAccount", out var la) && la.TryGetDouble(out var laVal) ? laVal * 100 : -1.0;
                    var shortAccount = item.TryGetProperty("shortAccount", out var sa) && sa.TryGetDouble(out var saVal) ? saVal * 100 : -1.0;
                    if (longAccount >= 0 && shortAccount >= 0 && Math.Abs(longAccount + shortAccount - 100) < 5)
                    {
                        batch[ourSymbol] = (longAccount, shortAccount);
                        _logger.LogDebug("Binance: added {Symbol} retail long={Long:F1}% short={Short:F1}%", ourSymbol, longAccount, shortAccount);
                    }
                    await Task.Delay(200);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Binance retail fetch failed for {Symbol}", ourSymbol);
                }
            }
        }

        /// <summary>Same as FetchMyFxBookSentimentBatchAsync but returns diagnostic info for debugging.</summary>
        /// <remarks>Uses cached session; only logs in when no session or API returns "Invalid session". MyFXBook is sensitive to login frequency.</remarks>
        public async Task<(Dictionary<string, (double longPct, double shortPct)> result, object diagnostic)> FetchMyFxBookSentimentBatchWithDiagnosticAsync()
        {
            var result = new Dictionary<string, (double longPct, double shortPct)>(StringComparer.OrdinalIgnoreCase);
            var diag = new Dictionary<string, object>();

            if (string.IsNullOrEmpty(MyFxBookEmail) || string.IsNullOrEmpty(MyFxBookPassword))
            {
                diag["api"] = new
                {
                    status = "SKIPPED",
                    reason = "MyFXBook credentials not configured. Set TrailBlazer:MyFXBookEmail and TrailBlazer:MyFXBookPassword (appsettings / Production), or env TrailBlazer__MyFXBookEmail / TrailBlazer__MyFXBookPassword, or MYFXBOOK_EMAIL / MYFXBOOK_PASSWORD. Optional: TrailBlazer:MyFXBookSession for a browser session id."
                };
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
                // Config may store password URL-encoded (e.g. dCP7WkV%21T%2BcMd.2). Decode first to avoid double-encoding.
                var rawPassword = Uri.UnescapeDataString(MyFxBookPassword);
                var url = $"https://www.myfxbook.com/api/login.json?email={Uri.EscapeDataString(MyFxBookEmail)}&password={Uri.EscapeDataString(rawPassword)}";
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
                ["USD"] = new() { ["GDP"] = "GDPC1", ["CPI"] = "CPIAUCSL", ["Unemployment"] = "UNRATE", ["InterestRate"] = "FEDFUNDS", ["PMI"] = "BSCICP03USM665S", ["Treasury10Y"] = "DGS10", ["DollarIndex"] = "DTWEXBGS", ["PCE"] = "PCEPI", ["JOLTs"] = "JTSJOL", ["JoblessClaims"] = "ICSA" },
                ["EUR"] = new() { ["GDP"] = "EUNNGDP", ["CPI"] = "CP0000EZ19M086NEST", ["Unemployment"] = "LRHUTTTTEZM156S", ["InterestRate"] = "ECBDFR", ["PMI"] = "BSCICP03EZM665S" },
                ["GBP"] = new() { ["GDP"] = "NAEXKP01GBQ661S", ["CPI"] = "GBRCPIALLMINMEI", ["Unemployment"] = "LRHUTTTTGBM156S", ["InterestRate"] = "BOERUKM", ["PMI"] = "BSCICP03GBM665S" },
                ["JPY"] = new() { ["GDP"] = "JPNRGDPEXP", ["CPI"] = "JPNCPIALLMINMEI", ["Unemployment"] = "LRHUTTTTJPM156S", ["InterestRate"] = "IRSTCB01JPM156N", ["PMI"] = "BSCICP03JPM665S" },
                ["AUD"] = new() { ["GDP"] = "AUSGDPNQDSMEI", ["CPI"] = "AUSCPIALLQINMEI", ["Unemployment"] = "LRHUTTTTAUM156S", ["InterestRate"] = "IRSTCI01AUM156N", ["PMI"] = "BSCICP03AUM665S" },
                ["NZD"] = new() { ["GDP"] = "NZLGDPNQDSMEI", ["CPI"] = "NZLCPIALLQINMEI", ["Unemployment"] = "LRHUTTTTNZA156N", ["InterestRate"] = "IRSTCI01NZM156N", ["PMI"] = "BSCICP03NZM665S" },
                // Canada: OECD BCI (BSCICP03CAM665S) — active. CANLOCOBSNOSTSAM (legacy Normalised BCI) was discontinued Jan 2024.
                ["CAD"] = new() { ["GDP"] = "NAEXKP01CAQ189S", ["CPI"] = "CANCPIALLMINMEI", ["Unemployment"] = "LRHUTTTTCAM156S", ["InterestRate"] = "IRSTCB01CAM156N", ["PMI"] = "BSCICP03CAM665S" },
                ["CHF"] = new() { ["GDP"] = "CLVMNACSCAB1GQCH", ["CPI"] = "CHECPIALLMINMEI", ["Unemployment"] = "LRHUTTTTCHQ156S", ["InterestRate"] = "IRSTCI01CHM156N", ["PMI"] = "BSCICP03CHM665S" },
                ["SEK"] = new() { ["GDP"] = "CLVMNACSCAB1GQSE", ["CPI"] = "CP0000SEM086NEST", ["Unemployment"] = "LRHUTTTTSEM156S", ["InterestRate"] = "IRSTCI01SEM156N", ["PMI"] = "BSCICP03SEM665S" },
                ["ZAR"] = new() { ["GDP"] = "ZAFGDPRQPSMEI", ["CPI"] = "ZAFCPIALLMINMEI", ["Unemployment"] = "LRUN64TTZAQ156S", ["InterestRate"] = "IRSTCB01ZAM156N", ["PMI"] = "BSCICP03ZAM665S" },
                ["CNY"] = new() { ["GDP"] = "CHNGDPRAPSMEI", ["CPI"] = "CHNCPIALLMINMEI", ["Unemployment"] = "SLUEM1524ZSCHN", ["InterestRate"] = "IRSTCB01CNM156N", ["PMI"] = "BSCICP03CNM665S" }
            };

            // CPI series that are quarterly (legacy index YoY fallback uses 5 obs); others default monthly (13)
            var cpiQuarterlySeries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AUSCPIALLQINMEI", "NZLCPIALLQINMEI" };
            // GDP series that are already YoY growth rates (use raw fetch, not YoY calc)
            var gdpIsGrowthRate = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ZAFGDPRQPSMEI", "CHNGDPRAPSMEI" };
            // CPI/PCE: YoY from index (incl. Euro area CP0000EZ19M086NEST & Sweden CP0000SEM086NEST — HICP 2015=100, not raw %)
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
                            var cal = await FetchFredYoYPercentCalendarAsync(seriesId);
                            var yoy = cal ?? await FetchFredYoYPercentAsync(seriesId, 5);
                            value = yoy ?? 0;
                        }
                    }
                    else if (indicator == "CPI" || indicator == "PCE")
                    {
                        var obsCount = (indicator == "PCE" || cpiQuarterlySeries.Contains(seriesId)) ? 5 : 13;
                        var calLimit = seriesId.Contains("CP0000", StringComparison.OrdinalIgnoreCase) ? 240 : 120;
                        var cal = await FetchFredYoYPercentCalendarAsync(seriesId, calLimit);
                        var yoy = cal ?? await FetchFredYoYPercentAsync(seriesId, obsCount);
                        value = yoy ?? 0;
                        // Secondary fallback: FRED "growth rate, same period previous year" series for Eurostat HICP (EUR area, SEK).
                        if ((value == 0 || Math.Abs(value) < 1e-6) && indicator == "CPI")
                        {
                            string? altYoYSeries = null;
                            if (string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase)) altYoYSeries = "CPHPTT01EZM659N";
                            else if (string.Equals(currency, "SEK", StringComparison.OrdinalIgnoreCase)) altYoYSeries = "CPALTT01SEM659N";
                            if (altYoYSeries != null)
                            {
                                var direct = await FetchFredDataAsync(altYoYSeries);
                                if (direct != 0 && Math.Abs(direct) < 50) value = direct;
                            }
                        }
                    }
                    else
                    {
                        value = await FetchFredDataAsync(seriesId);
                        // CAD PMI: BSCICP03CAM665S is primary; fall back through legacy series if empty.
                        if (string.Equals(currency, "CAD", StringComparison.OrdinalIgnoreCase) && indicator == "PMI" && (value == 0 || Math.Abs(value) < 1e-6))
                        {
                            foreach (var altPmiSeries in new[] { "CANLOCOBSNOSTSAM", "BSCICP02CAM460S", "CSCICP03CAM665S" })
                            {
                                var altPmi = await FetchFredDataAsync(altPmiSeries);
                                if (altPmi != 0) { value = altPmi; break; }
                            }
                        }
                        if (indicator == "JoblessClaims" && value > 5000)
                            value /= 1000.0;
                    }

                    if ((indicator == "CPI" || indicator == "PCE") && value != 0 && (Math.Abs(value) > 25 || double.IsNaN(value) || double.IsInfinity(value)))
                    {
                        _logger.LogWarning("Heatmap CPI/PCE implausible for {Currency} ({SeriesId}): {Value} — cleared for World Bank fallback", currency, seriesId, value);
                        value = 0;
                    }

                    if (indicator == "GDP" && value != 0 && (Math.Abs(value) > 22 || double.IsNaN(value) || double.IsInfinity(value)))
                    {
                        _logger.LogWarning("Heatmap GDP YoY implausible for {Currency} ({SeriesId}): {Value} — suppressed (check FRED series / frequency)", currency, seriesId, value);
                        value = 0;
                    }

                    await Task.Delay(100);

                    var impact = indicator switch
                    {
                        "GDP" => value >= AvgGdpGrowth - GdpGrowthBand && value <= AvgGdpGrowth + GdpGrowthBand ? "Neutral" : value > AvgGdpGrowth + GdpGrowthBand ? "Positive" : "Negative",
                        "CPI" => value >= FedInflationTarget - CpiTargetBand && value <= FedInflationTarget + CpiTargetBand ? "Neutral" : value > FedInflationTarget + CpiTargetBand ? "Negative" : "Positive",
                        "Unemployment" => value < 5 ? "Positive" : "Negative",
                        "InterestRate" => value > 2 ? "Positive" : "Neutral",
                        "PMI" => value > PmiNeutral ? "Positive" : value < PmiNeutral ? "Negative" : "Neutral",
                        "Treasury10Y" => value > 4.5 ? "Negative" : value < 3 ? "Positive" : "Neutral", // High yield = bearish for stocks
                        "DollarIndex" => value > 125 ? "Negative" : value < 118 ? "Positive" : "Neutral", // High DXY proxy = Risk-Off (bearish risk assets)
                        "PCE" => value >= FedInflationTarget - CpiTargetBand && value <= FedInflationTarget + CpiTargetBand ? "Neutral" : value > FedInflationTarget + CpiTargetBand ? "Negative" : "Positive",
                        "JOLTs" => value > 9 ? "Positive" : value < 7 ? "Negative" : "Neutral", // Job openings (millions)
                        "JoblessClaims" => value < 250 ? "Positive" : value > 350 ? "Negative" : "Neutral", // Initial claims (thousands)
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
