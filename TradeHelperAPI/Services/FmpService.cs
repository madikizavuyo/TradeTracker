using System.Text.Json;

namespace TradeHelper.Services
{
    /// <summary>
    /// Financial Modeling Prep API: quote (price + priceAvg50/200) and daily technical indicators (RSI, SMA, EMA).
    /// Stable quote: symbol=EURUSD or ^GSPC. Legacy technical: /api/v3/technical_indicator/daily/SYMBOL.
    /// </summary>
    public class FmpService
    {
        private const string BaseUrl = "https://financialmodelingprep.com";
        private static DateTime _lastCall = DateTime.MinValue;
        private static readonly SemaphoreSlim _throttle = new(1, 1);
        private const double MinSecondsBetweenCalls = 0.35;

        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<FmpService> _logger;
        private readonly ApiRateLimitService _rateLimit;

        private string ApiKey => _config["TrailBlazer:FmpApiKey"] ?? _config["FmpApiKey"] ?? "";

        public FmpService(HttpClient client, IConfiguration config, ILogger<FmpService> logger, ApiRateLimitService rateLimit)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _rateLimit = rateLimit;
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>Maps our instrument name to FMP symbol (forex EURUSD, indices ^GSPC, etc.).</summary>
        public static string? ToFmpSymbol(string instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName)) return null;
            var upper = instrumentName.ToUpperInvariant().Replace("/", "").Replace("_", "").Replace(" ", "");

            // Forex: 6–8 letter pairs
            if (upper.Length >= 6 && upper.Length <= 8 && upper.All(char.IsLetter))
                return upper;

            // Precious metals / commodities (FMP may list as forex or commodity)
            if (upper.StartsWith("XAU")) return "XAUUSD";
            if (upper.StartsWith("XAG")) return "XAGUSD";
            if (upper.StartsWith("XPT")) return "XPTUSD";
            if (upper.StartsWith("XPD")) return "XPDUSD";
            if (upper == "USOIL" || upper == "OIL") return "USOIL";

            // Indices (FMP uses ^ prefix)
            if (upper == "US500" || upper == "SPX") return "^GSPC";
            if (upper == "US30") return "^DJI";
            if (upper == "US100") return "^NDX";
            if (upper == "DE40") return "^GDAXI";
            if (upper == "UK100") return "^FTSE";
            if (upper == "JP225") return "^N225";

            return null;
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

        /// <summary>Fetches stable quote; returns (price, priceAvg50, priceAvg200).</summary>
        private async Task<(double price, double? avg50, double? avg200)> FetchQuoteInternalAsync(string fmpSymbol)
        {
            if (string.IsNullOrEmpty(ApiKey)) return (0, null, null);
            if (await _rateLimit.IsBlockedAsync("FMP")) return (0, null, null);

            await ThrottleAsync();
            var url = $"{BaseUrl}/stable/quote?symbol={Uri.EscapeDataString(fmpSymbol)}&apikey={Uri.EscapeDataString(ApiKey)}";

            try
            {
                var json = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return (0, null, null);
                var item = doc.RootElement[0];
                double price = 0;
                if (item.TryGetProperty("price", out var p) && p.TryGetDouble(out var pv)) price = pv;
                else if (item.TryGetProperty("close", out var c) && c.TryGetDouble(out var cv)) price = cv;
                double? avg50 = null, avg200 = null;
                if (item.TryGetProperty("priceAvg50", out var a50) && a50.TryGetDouble(out var v50) && v50 > 0) avg50 = v50;
                if (item.TryGetProperty("priceAvg200", out var a200) && a200.TryGetDouble(out var v200) && v200 > 0) avg200 = v200;
                return (price, avg50, avg200);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "FMP quote failed for {Symbol}", fmpSymbol);
                return (0, null, null);
            }
        }

        /// <summary>Fetches last value from daily technical indicator API. Returns null on failure.</summary>
        private async Task<double?> FetchDailyTechnicalAsync(string fmpSymbol, string type, int period)
        {
            if (string.IsNullOrEmpty(ApiKey)) return null;
            if (await _rateLimit.IsBlockedAsync("FMP")) return null;

            await ThrottleAsync();
            var url = $"{BaseUrl}/api/v3/technical_indicator/daily/{Uri.EscapeDataString(fmpSymbol)}?type={Uri.EscapeDataString(type)}&period={period}&apikey={Uri.EscapeDataString(ApiKey)}";

            try
            {
                var json = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Error Message", out var errMsg))
                {
                    var msg = errMsg.GetString() ?? "";
                    if (msg.Contains("limit", StringComparison.OrdinalIgnoreCase) || msg.Contains("429", StringComparison.OrdinalIgnoreCase))
                        await _rateLimit.SetBlockedAsync("FMP");
                    return null;
                }
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return null;
                var first = doc.RootElement[0];
                if (first.TryGetProperty(type, out var v) && v.TryGetDouble(out var d)) return d;
                if (first.TryGetProperty("value", out var v2) && v2.TryGetDouble(out var d2)) return d2;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "FMP technical {Type} failed for {Symbol}", type, fmpSymbol);
            }
            return null;
        }

        /// <summary>Fetches technical indicators: quote (priceAvg50/200) + daily RSI/SMA/EMA. Same shape as other providers.</summary>
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

            var symbol = ToFmpSymbol(instrumentName);
            if (symbol == null) return results;

            var (price, avg50, avg200) = await FetchQuoteInternalAsync(symbol);
            if (price > 0) results["Close"] = price;
            if (avg50.HasValue && avg50.Value != 0) results["EMA50"] = avg50.Value;
            if (avg200.HasValue && avg200.Value != 0) results["EMA200"] = avg200.Value;

            var rsi = await FetchDailyTechnicalAsync(symbol, "rsi", 14);
            if (rsi.HasValue && rsi.Value > 0 && rsi.Value < 100) results["RSI"] = rsi.Value;

            var sma14 = await FetchDailyTechnicalAsync(symbol, "sma", 14);
            if (sma14.HasValue && sma14.Value != 0) results["SMA14"] = sma14.Value;

            var sma50 = await FetchDailyTechnicalAsync(symbol, "sma", 50);
            if (sma50.HasValue && sma50.Value != 0) results["SMA50"] = sma50.Value;

            if (!results.ContainsKey("EMA50") || results["EMA50"] == 0)
            {
                var ema50 = await FetchDailyTechnicalAsync(symbol, "ema", 50);
                if (ema50.HasValue && ema50.Value != 0) results["EMA50"] = ema50.Value;
            }
            if (!results.ContainsKey("EMA200") || results["EMA200"] == 0)
            {
                var ema200 = await FetchDailyTechnicalAsync(symbol, "ema", 200);
                if (ema200.HasValue && ema200.Value != 0) results["EMA200"] = ema200.Value;
            }

            return results;
        }

        /// <summary>Fetches real-time (or EOD) price for the symbol. Returns 0 if not available.</summary>
        public async Task<double> FetchQuoteAsync(string instrumentName)
        {
            var symbol = ToFmpSymbol(instrumentName);
            if (symbol == null) return 0;
            var (price, _, _) = await FetchQuoteInternalAsync(symbol);
            return price > 0 ? price : 0;
        }

        /// <summary>Maps instrument to FMP symbol for historical prices. Commodities use liquid ETFs (USO, GLD, SLV) that work with stock historical API.</summary>
        private static string? ToFmpHistoricalSymbol(string? instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName)) return null;
            var upper = instrumentName.ToUpperInvariant().Replace("/", "").Replace("_", "").Replace(" ", "");
            if (upper == "USOIL" || upper == "OIL") return "USO";   // US Oil Fund ETF - tracks WTI
            if (upper.StartsWith("XAU")) return "GLD";              // Gold ETF
            if (upper.StartsWith("XAG")) return "SLV";               // Silver ETF
            return ToFmpSymbol(instrumentName);
        }

        /// <summary>Fetches historical daily closes (newest first) for correlation/relative strength. Oil uses USO proxy; commodities use ETF proxies.</summary>
        public async Task<List<double>> FetchHistoricalClosesAsync(string instrumentName, int days = 90)
        {
            var symbol = ToFmpHistoricalSymbol(instrumentName);
            if (symbol == null || string.IsNullOrEmpty(ApiKey)) return new List<double>();
            if (await _rateLimit.IsBlockedAsync("FMP")) return new List<double>();

            await ThrottleAsync();
            var to = DateTime.UtcNow.Date;
            var from = to.AddDays(-days);
            var url = $"{BaseUrl}/api/v3/historical-price-full/{Uri.EscapeDataString(symbol)}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&apikey={Uri.EscapeDataString(ApiKey)}";

            try
            {
                var json = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("historical", out var hist) || hist.ValueKind != JsonValueKind.Array)
                    return new List<double>();
                var closes = new List<double>();
                foreach (var item in hist.EnumerateArray())
                {
                    if (item.TryGetProperty("close", out var c) && c.TryGetDouble(out var cv) && cv > 0)
                        closes.Add(cv);
                }
                // FMP returns oldest-first; correlation/returns expect newest-first (index 0 = latest bar).
                closes.Reverse();
                return closes;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "FMP historical failed for {Symbol}", symbol);
                return new List<double>();
            }
        }
    }
}
