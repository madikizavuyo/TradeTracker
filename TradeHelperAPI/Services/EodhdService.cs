using System.Text.Json;

namespace TradeHelper.Services
{
    /// <summary>
    /// EODHD API: technical indicators (RSI, SMA, EMA) and real-time quote.
    /// Symbol format: EURUSD.FOREX, GSPC.INDX, XAUUSD.FOREX. Each technical request = 5 API calls.
    /// </summary>
    public class EodhdService
    {
        private const string BaseUrl = "https://eodhd.com/api";
        private static DateTime _lastCall = DateTime.MinValue;
        private static readonly SemaphoreSlim _throttle = new(1, 1);
        private const double MinSecondsBetweenCalls = 0.5;

        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<EodhdService> _logger;
        private readonly ApiRateLimitService _rateLimit;

        private string ApiKey => _config["TrailBlazer:EodhdApiKey"] ?? _config["EodhdApiKey"] ?? "";

        public EodhdService(HttpClient client, IConfiguration config, ILogger<EodhdService> logger, ApiRateLimitService rateLimit)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _rateLimit = rateLimit;
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>Maps our instrument name to EODHD symbol (e.g. EURUSD -> EURUSD.FOREX, US500 -> GSPC.INDX).</summary>
        public static string? ToEodhdSymbol(string instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName)) return null;
            var upper = instrumentName.ToUpperInvariant().Replace("/", "").Replace("_", "").Replace(" ", "");

            // Forex: 6–8 letter pairs
            if (upper.Length >= 6 && upper.Length <= 8 && upper.All(char.IsLetter))
                return $"{upper}.FOREX";

            // Precious metals (EODHD often as .FOREX or .COMM)
            if (upper.StartsWith("XAU")) return "XAUUSD.FOREX";
            if (upper.StartsWith("XAG")) return "XAGUSD.FOREX";
            if (upper.StartsWith("XPT")) return "XPTUSD.FOREX";
            if (upper.StartsWith("XPD")) return "XPDUSD.FOREX";

            // Indices
            if (upper == "US500" || upper == "SPX") return "GSPC.INDX";
            if (upper == "US30") return "DJI.INDX";
            if (upper == "US100") return "NDX.INDX";
            if (upper == "DE40") return "GDAXI.INDX";
            if (upper == "UK100") return "FTSE.INDX";
            if (upper == "JP225") return "N225.INDX";

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

        /// <summary>Fetches last value from technical API. Returns null on failure or no data.</summary>
        private async Task<double?> FetchLastTechnicalAsync(string eodhdSymbol, string function, int period)
        {
            if (string.IsNullOrEmpty(ApiKey)) return null;
            if (await _rateLimit.IsBlockedAsync("EODHD")) return null;

            await ThrottleAsync();
            var to = DateTime.UtcNow.Date;
            var from = to.AddDays(-Math.Max(period * 2, 60));
            var url = $"{BaseUrl}/technical/{Uri.EscapeDataString(eodhdSymbol)}?order=d&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&function={Uri.EscapeDataString(function)}&period={period}&api_token={Uri.EscapeDataString(ApiKey)}&fmt=json";

            try
            {
                var json = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    var msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    if (msg.Contains("limit", StringComparison.OrdinalIgnoreCase) || msg.Contains("429", StringComparison.OrdinalIgnoreCase))
                        await _rateLimit.SetBlockedAsync("EODHD");
                    return null;
                }
                // Response: array of { "date": "...", "rsi"|"sma"|"ema": value } or single value when filter=last_*
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    var key = function == "rsi" ? "rsi" : function == "sma" ? "sma" : "ema";
                    if (first.TryGetProperty(key, out var v) && v.TryGetDouble(out var d))
                        return d;
                }
                if (doc.RootElement.ValueKind == JsonValueKind.Number && doc.RootElement.TryGetDouble(out var single))
                    return single;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "EODHD technical {Function} failed for {Symbol}", function, eodhdSymbol);
            }
            return null;
        }

        /// <summary>Fetches technical indicators (RSI, SMA14, SMA50, EMA50, EMA200). Same shape as Twelve Data. Only updates when we get data.</summary>
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

            var symbol = ToEodhdSymbol(instrumentName);
            if (symbol == null) return results;

            var rsi = await FetchLastTechnicalAsync(symbol, "rsi", 14);
            if (rsi.HasValue && rsi.Value > 0 && rsi.Value < 100) results["RSI"] = rsi.Value;

            var sma14 = await FetchLastTechnicalAsync(symbol, "sma", 14);
            if (sma14.HasValue && sma14.Value != 0) results["SMA14"] = sma14.Value;

            var sma50 = await FetchLastTechnicalAsync(symbol, "sma", 50);
            if (sma50.HasValue && sma50.Value != 0) results["SMA50"] = sma50.Value;

            var ema50 = await FetchLastTechnicalAsync(symbol, "ema", 50);
            if (ema50.HasValue && ema50.Value != 0) results["EMA50"] = ema50.Value;

            var ema200 = await FetchLastTechnicalAsync(symbol, "ema", 200);
            if (ema200.HasValue && ema200.Value != 0) results["EMA200"] = ema200.Value;

            return results;
        }

        /// <summary>Fetches real-time (or EOD) last price for the symbol. Returns 0 if not available.</summary>
        public async Task<double> FetchRealTimeQuoteAsync(string instrumentName)
        {
            if (string.IsNullOrEmpty(ApiKey)) return 0;
            if (await _rateLimit.IsBlockedAsync("EODHD")) return 0;

            var symbol = ToEodhdSymbol(instrumentName);
            if (symbol == null) return 0;

            await ThrottleAsync();
            var url = $"{BaseUrl}/real-time/{Uri.EscapeDataString(symbol)}?api_token={Uri.EscapeDataString(ApiKey)}&fmt=json";

            try
            {
                var response = await _client.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    await _rateLimit.SetBlockedAsync("EODHD");
                    return 0;
                }
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out _))
                    return 0;
                // Typical: { "close": 1.085, "last": 1.085 } or similar
                if (doc.RootElement.TryGetProperty("close", out var close) && close.TryGetDouble(out var c) && c > 0)
                    return c;
                if (doc.RootElement.TryGetProperty("last", out var last) && last.TryGetDouble(out var l) && l > 0)
                    return l;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "EODHD real-time quote failed for {Symbol}", symbol);
            }
            return 0;
        }
    }
}
