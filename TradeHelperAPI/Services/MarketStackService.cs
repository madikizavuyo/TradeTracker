using System.Text.Json;

namespace TradeHelper.Services
{
    /// <summary>
    /// Fetches EOD data from Market Stack API and computes RSI, SMA, EMA for indices (and optionally stocks).
    /// Free plan: EOD on all plans; indices on Basic+. Rate limit: 5 req/s.
    /// </summary>
    public class MarketStackService
    {
        private const string BaseUrl = "https://api.marketstack.com/v1";
        private static DateTime _lastCall = DateTime.MinValue;
        private static readonly SemaphoreSlim _throttle = new(1, 1);
        private const double MinSecondsBetweenCalls = 0.25; // 5 req/s max

        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<MarketStackService> _logger;
        private readonly ApiRateLimitService _rateLimit;

        private string ApiKey => _config["TrailBlazer:MarketStackApiKey"] ?? _config["MarketStackApiKey"] ?? "";

        public MarketStackService(HttpClient client, IConfiguration config, ILogger<MarketStackService> logger, ApiRateLimitService rateLimit)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _rateLimit = rateLimit;
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>Maps our instrument name to Market Stack EOD symbol. Indices use SYMBOL.INDX; stocks use ticker.</summary>
        public static string? ToMarketStackSymbol(string instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName)) return null;
            var upper = instrumentName.ToUpperInvariant().Replace("/", "").Replace("_", "").Replace(" ", "");

            // Indices (EOD available; free plan may have limited indices)
            if (upper == "US500" || upper == "SPX") return "SPX.INDX";
            if (upper == "US30") return "DJI.INDX";
            if (upper == "US100") return "NDX.INDX";
            if (upper == "DE40") return "GDAXI.INDX";
            if (upper == "UK100") return "FTSE.INDX";
            if (upper == "JP225") return "N225.INDX";

            // Forex: Market Stack free tier is stocks/indices; no forex EOD in standard doc. Skip.
            // Metals/commodities: could add XAU/USD if they support it - skip for now to avoid 404s.
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

        /// <summary>OHLC bar: index 0 = newest (most recent).</summary>
        private record OhlcBar(double Open, double High, double Low, double Close);

        /// <summary>Fetches EOD OHLC. Returns newest-first. One API call.</summary>
        private async Task<List<OhlcBar>?> FetchEodOhlcAsync(string symbol, int days = 260)
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                _logger.LogWarning("Market Stack API key not configured");
                return null;
            }
            if (await _rateLimit.IsBlockedAsync("MarketStack"))
                return null;

            await ThrottleAsync();

            var dateTo = DateTime.UtcNow.Date;
            var dateFrom = dateTo.AddDays(-days);
            var url = $"{BaseUrl}/eod?access_key={Uri.EscapeDataString(ApiKey)}&symbols={Uri.EscapeDataString(symbol)}&date_from={dateFrom:yyyy-MM-dd}&date_to={dateTo:yyyy-MM-dd}&sort=DESC&limit=300";

            try
            {
                var json = await _client.GetStringAsync(url);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    var code = err.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                    var msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    _logger.LogWarning("Market Stack API error {Code}: {Msg}", code, msg);
                    if (code == "429" || ApiRateLimitService.IsCreditLimitMessage(msg))
                        await _rateLimit.SetBlockedAsync("MarketStack");
                    doc.Dispose();
                    return null;
                }
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    doc.Dispose();
                    return null;
                }
                var bars = new List<OhlcBar>();
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("open", out var o) && o.TryGetDouble(out var open) &&
                        item.TryGetProperty("high", out var h) && h.TryGetDouble(out var high) &&
                        item.TryGetProperty("low", out var l) && l.TryGetDouble(out var low) &&
                        item.TryGetProperty("close", out var c) && c.TryGetDouble(out var close))
                        bars.Add(new OhlcBar(open, high, low, close));
                }
                doc.Dispose();
                return bars.Count >= 14 ? bars : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Market Stack fetch failed: {Url}", url);
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

        /// <summary>MACD = EMA12 - EMA26, signal = EMA9 of MACD. Closes newest-first. Returns (macd, signal) or null if insufficient data.</summary>
        private static (double macd, double signal)? MacdFromCloses(IReadOnlyList<double> closes)
        {
            if (closes.Count < 35) return null; // 26 for slow EMA + 9 for signal
            var chrono = closes.Reverse().ToList();
            var macdLine = new List<double>();
            for (var i = 25; i < chrono.Count; i++)
            {
                var slice = chrono.Take(i + 1).Reverse().ToList();
                var e12 = Ema(slice, 12);
                var e26 = Ema(slice, 26);
                macdLine.Add(e12 - e26);
            }
            if (macdLine.Count < 9) return null;
            var k9 = 2.0 / 10;
            var signalEma = macdLine.Take(9).Average();
            for (var i = 9; i < macdLine.Count; i++)
                signalEma = macdLine[i] * k9 + signalEma * (1 - k9);
            return (macdLine[^1], signalEma);
        }

        /// <summary>Stochastic %K (slow). OHLC newest-first. %K = (close - low14) / (high14 - low14) * 100, SMA3 smoothed for slow %K.</summary>
        private static double? StochasticFromOhlc(IReadOnlyList<OhlcBar> bars, int period = 14)
        {
            if (bars.Count < period + 2) return null;
            var rawK = new List<double>();
            for (var i = 0; i <= bars.Count - period; i++)
            {
                var window = bars.Skip(i).Take(period).ToList();
                var high14 = window.Max(b => b.High);
                var low14 = window.Min(b => b.Low);
                var close = bars[i].Close;
                var range = high14 - low14;
                rawK.Add(range == 0 ? 50 : (close - low14) / range * 100);
            }
            if (rawK.Count < 3) return null;
            return rawK.Take(3).Average();
        }

        /// <summary>Fetches EOD and computes RSI, SMA, EMA, MACD, Stochastic. Returns same shape as Twelve Data for scoring.</summary>
        public async Task<Dictionary<string, double>> FetchTechnicalIndicatorsAsync(string instrumentName)
        {
            var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RSI"] = 50,
                ["SMA14"] = 0,
                ["SMA50"] = 0,
                ["EMA50"] = 0,
                ["EMA200"] = 0,
                ["MACD"] = 0,
                ["MACDSignal"] = 0,
                ["StochasticK"] = 50
            };

            var symbol = ToMarketStackSymbol(instrumentName);
            if (symbol == null) return results;

            var bars = await FetchEodOhlcAsync(symbol);
            if (bars == null || bars.Count < 14) return results;

            var closes = bars.Select(b => b.Close).ToList();
            results["RSI"] = RsiFromCloses(closes, 14);
            results["SMA14"] = Sma(closes, 14);
            if (closes.Count >= 50) results["SMA50"] = Sma(closes, 50);
            if (closes.Count >= 50) results["EMA50"] = Ema(closes, 50);
            if (closes.Count >= 200) results["EMA200"] = Ema(closes, 200);
            if (closes.Count > 0) results["Close"] = closes[0];

            var macd = MacdFromCloses(closes);
            if (macd.HasValue)
            {
                results["MACD"] = macd.Value.macd;
                results["MACDSignal"] = macd.Value.signal;
            }

            var stoch = StochasticFromOhlc(bars, 14);
            if (stoch.HasValue) results["StochasticK"] = stoch.Value;

            return results;
        }
    }
}
