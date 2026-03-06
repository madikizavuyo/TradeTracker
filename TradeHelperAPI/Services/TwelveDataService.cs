using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    /// <summary>
    /// Fetches market data from Twelve Data API (forex, commodities, technical indicators).
    /// Rate limit: Free tier ~8 requests/min; we throttle to 1 request per 8 seconds.
    /// </summary>
    public class TwelveDataService
    {
        private const string BaseUrl = "https://api.twelvedata.com";
        private static DateTime _lastCall = DateTime.MinValue;
        private static readonly SemaphoreSlim _throttle = new(1, 1);
        private const int MinSecondsBetweenCalls = 8; // ~8 req/min for free tier

        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<TwelveDataService> _logger;
        private readonly ApiRateLimitService _rateLimit;

        private string ApiKey => _config["TrailBlazer:TwelveDataApiKey"] ?? _config["TwelveDataApiKey"] ?? "";

        public TwelveDataService(HttpClient client, IConfiguration config, ILogger<TwelveDataService> logger, ApiRateLimitService rateLimit)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _rateLimit = rateLimit;
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>Maps instrument name to Twelve Data symbol (EURUSD -> EUR/USD, XAUUSD -> XAU/USD).</summary>
        public static string? ToTwelveDataSymbol(string instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName)) return null;
            var upper = instrumentName.ToUpperInvariant().Replace("/", "").Replace("_", "").Replace(" ", "");

            // Forex: EURUSD -> EUR/USD
            if (upper.Length == 6 && upper.All(char.IsLetter))
                return $"{upper[..3]}/{upper[3..]}";

            // 7-char forex (e.g. USDZAR)
            if (upper.Length >= 6 && upper.Length <= 8 && upper.All(char.IsLetter))
                return $"{upper[..3]}/{upper[3..]}";

            // Metals
            if (upper.StartsWith("XAU")) return "XAU/USD";
            if (upper.StartsWith("XAG")) return "XAG/USD";
            if (upper.StartsWith("XPT")) return "XPT/USD";
            if (upper.StartsWith("XPD")) return "XPD/USD";

            // Oil - Twelve Data uses WTI/USD or similar
            if (upper == "USOIL" || upper == "OIL") return "WTI/USD";

            // Indices - Twelve Data uses different symbols
            if (upper == "US500" || upper == "SPX") return "SPX";
            if (upper == "US30") return "DJI";
            if (upper == "US100") return "NDX";
            if (upper == "DE40") return "GDAXI";
            if (upper == "UK100") return "FTSE";
            if (upper == "JP225") return "N225";

            return null;
        }

        private async Task ThrottleAsync()
        {
            await _throttle.WaitAsync();
            try
            {
                var elapsed = (DateTime.UtcNow - _lastCall).TotalSeconds;
                if (elapsed < MinSecondsBetweenCalls)
                    await Task.Delay(TimeSpan.FromSeconds(MinSecondsBetweenCalls - elapsed));
                _lastCall = DateTime.UtcNow;
            }
            finally { _throttle.Release(); }
        }

        private async Task<JsonDocument?> GetAsync(string endpoint)
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                _logger.LogWarning("Twelve Data API key not configured");
                return null;
            }
            if (await _rateLimit.IsBlockedAsync("TwelveData"))
                return null;

            await ThrottleAsync();

            var url = endpoint.Contains("?") ? $"{BaseUrl}{endpoint}&apikey={ApiKey}" : $"{BaseUrl}{endpoint}?apikey={ApiKey}";
            try
            {
                var json = await _client.GetStringAsync(url);
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "error")
                {
                    var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                    var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    _logger.LogWarning("Twelve Data API error {Code}: {Msg}", code, msg);
                    if (code == 429 || ApiRateLimitService.IsCreditLimitMessage(msg))
                        await _rateLimit.SetBlockedAsync("TwelveData");
                    doc.Dispose();
                    return null;
                }
                return doc;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Twelve Data fetch failed: {Url}", url);
                return null;
            }
        }

        /// <summary>Fetches real-time exchange rate for a forex/commodity pair.</summary>
        public async Task<double?> FetchExchangeRateAsync(string symbol)
        {
            var tdSymbol = ToTwelveDataSymbol(symbol);
            if (tdSymbol == null) return null;

            using var doc = await GetAsync($"/exchange_rate?symbol={Uri.EscapeDataString(tdSymbol)}");
            if (doc == null) return null;

            if (doc.RootElement.TryGetProperty("rate", out var rate))
            {
                if (rate.TryGetDouble(out var v) && v > 0) return v;
            }
            return null;
        }

        /// <summary>Fetches RSI for a symbol. interval=1day for daily.</summary>
        public async Task<double?> FetchRSIAsync(string symbol, int period = 14)
        {
            var tdSymbol = ToTwelveDataSymbol(symbol);
            if (tdSymbol == null) return null;

            using var doc = await GetAsync($"/rsi?symbol={Uri.EscapeDataString(tdSymbol)}&interval=1day&time_period={period}&series_type=close");
            if (doc == null) return null;

            return ExtractLatestValue(doc.RootElement, "rsi");
        }

        /// <summary>Fetches SMA for a symbol.</summary>
        public async Task<double?> FetchSMAAsync(string symbol, int period = 14)
        {
            var tdSymbol = ToTwelveDataSymbol(symbol);
            if (tdSymbol == null) return null;

            using var doc = await GetAsync($"/sma?symbol={Uri.EscapeDataString(tdSymbol)}&interval=1day&time_period={period}&series_type=close");
            if (doc == null) return null;

            return ExtractLatestValue(doc.RootElement, "sma");
        }

        /// <summary>Fetches EMA for a symbol.</summary>
        public async Task<double?> FetchEMAAsync(string symbol, int period = 50)
        {
            var tdSymbol = ToTwelveDataSymbol(symbol);
            if (tdSymbol == null) return null;

            using var doc = await GetAsync($"/ema?symbol={Uri.EscapeDataString(tdSymbol)}&interval=1day&time_period={period}&series_type=close");
            if (doc == null) return null;

            return ExtractLatestValue(doc.RootElement, "ema");
        }

        private static double? ExtractLatestValue(JsonElement root, string key)
        {
            if (!root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0)
                return null;

            var first = values[0];
            if (!first.TryGetProperty(key, out var valProp)) return null;
            var str = valProp.GetString();
            return double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        /// <summary>Fetches technical indicators (RSI, SMA14, SMA50, EMA50, EMA200) in one batch. Uses multiple API calls with throttling.</summary>
        public async Task<Dictionary<string, double>> FetchTechnicalIndicatorsAsync(string symbol)
        {
            var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RSI"] = 50,
                ["SMA14"] = 0,
                ["SMA50"] = 0,
                ["EMA50"] = 0,
                ["EMA200"] = 0
            };

            var rsi = await FetchRSIAsync(symbol, 14);
            if (rsi.HasValue) results["RSI"] = rsi.Value;

            var sma14 = await FetchSMAAsync(symbol, 14);
            if (sma14.HasValue) results["SMA14"] = sma14.Value;

            var sma50 = await FetchSMAAsync(symbol, 50);
            if (sma50.HasValue) results["SMA50"] = sma50.Value;

            var ema50 = await FetchEMAAsync(symbol, 50);
            if (ema50.HasValue) results["EMA50"] = ema50.Value;

            var ema200 = await FetchEMAAsync(symbol, 200);
            if (ema200.HasValue) results["EMA200"] = ema200.Value;

            return results;
        }

        /// <summary>Loads technical data from Twelve Data for supported instruments and saves to database.</summary>
        public async Task<int> LoadTechnicalDataToDatabaseAsync(ApplicationDbContext db, int? limit = 8)
        {
            var query = db.Instruments
                .Where(i => i.Type == "Currency" || i.Name.StartsWith("XAU") || i.Name.StartsWith("XAG"))
                .OrderBy(i => i.Id);
            var instruments = limit.HasValue ? await query.Take(limit.Value).ToListAsync() : await query.ToListAsync();
            var loaded = 0;
            var now = DateTime.UtcNow;
            var today = now.Date;

            foreach (var instrument in instruments)
            {
                if (ToTwelveDataSymbol(instrument.Name) == null) continue;

                try
                {
                    var tech = await FetchTechnicalIndicatorsAsync(instrument.Name);
                    var rsi = tech.TryGetValue("RSI", out var r) && r > 0 && r < 100 ? r : (double?)null;
                    var sma14 = tech.TryGetValue("SMA14", out var s14) && s14 != 0 ? s14 : (double?)null;
                    var sma50 = tech.TryGetValue("SMA50", out var s50) && s50 != 0 ? s50 : (double?)null;
                    var ema50 = tech.TryGetValue("EMA50", out var e50) && e50 != 0 ? e50 : (double?)null;
                    var ema200 = tech.TryGetValue("EMA200", out var e200) && e200 != 0 ? e200 : (double?)null;

                    if (rsi == null && sma14 == null && sma50 == null && ema50 == null && ema200 == null)
                        continue;

                    var existing = await db.TechnicalIndicators
                        .FirstOrDefaultAsync(t => t.InstrumentId == instrument.Id && t.Date == today);
                    if (existing != null)
                    {
                        if (rsi.HasValue) existing.RSI = rsi;
                        if (sma14.HasValue) existing.SMA14 = sma14;
                        if (sma50.HasValue) existing.SMA50 = sma50;
                        if (ema50.HasValue) existing.EMA50 = ema50;
                        if (ema200.HasValue) existing.EMA200 = ema200;
                        existing.DateCollected = now;
                    }
                    else
                    {
                        db.TechnicalIndicators.Add(new TechnicalIndicator
                        {
                            InstrumentId = instrument.Id,
                            Date = today,
                            RSI = rsi,
                            SMA14 = sma14,
                            SMA50 = sma50,
                            EMA50 = ema50,
                            EMA200 = ema200,
                            DateCollected = now
                        });
                    }
                    loaded++;
                    _logger.LogInformation("Twelve Data: loaded technical data for {Name}: RSI={RSI}, SMA14={SMA14}", instrument.Name, rsi, sma14);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Twelve Data: failed to load technical data for {Name}", instrument.Name);
                }
            }

            await db.SaveChangesAsync();
            return loaded;
        }

        /// <summary>Tests connectivity and returns status for diagnostics.</summary>
        public async Task<(bool ok, string message)> TestConnectivityAsync()
        {
            if (string.IsNullOrEmpty(ApiKey))
                return (false, "API key not configured");

            var rate = await FetchExchangeRateAsync("EURUSD");
            if (rate.HasValue && rate.Value > 0)
                return (true, $"EUR/USD: {rate.Value:F4}");
            return (false, "No exchange rate returned (check API key or rate limit)");
        }
    }
}
