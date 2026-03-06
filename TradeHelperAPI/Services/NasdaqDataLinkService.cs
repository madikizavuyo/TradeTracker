using System.Text.Json;

namespace TradeHelper.Services
{
    /// <summary>
    /// Nasdaq Data Link (formerly Quandl) API: time-series from FRED and other databases.
    /// Fetches OHLC/Value series and computes RSI, SMA, EMA. Quote = latest value.
    /// Endpoint: data.nasdaq.com/api/v3/datasets/{database}/{dataset}/data.json
    /// </summary>
    public class NasdaqDataLinkService
    {
        private const string BaseUrl = "https://data.nasdaq.com/api/v3";
        private static DateTime _lastCall = DateTime.MinValue;
        private static readonly SemaphoreSlim _throttle = new(1, 1);
        private const double MinSecondsBetweenCalls = 0.4;

        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<NasdaqDataLinkService> _logger;
        private readonly ApiRateLimitService _rateLimit;

        private string ApiKey => _config["TrailBlazer:NasdaqDataLinkApiKey"] ?? _config["NasdaqDataLinkApiKey"] ?? "";

        public NasdaqDataLinkService(HttpClient client, IConfiguration config, ILogger<NasdaqDataLinkService> logger, ApiRateLimitService rateLimit)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _rateLimit = rateLimit;
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>Maps our instrument name to (database_code, dataset_code). Uses FRED exchange-rate series where available.</summary>
        public static (string database, string dataset)? ToNasdaqDataset(string instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName)) return null;
            var upper = instrumentName.ToUpperInvariant().Replace("/", "").Replace("_", "").Replace(" ", "");

            // Forex: FRED H.10 exchange rates (USD per foreign currency for XXXUSD; foreign per USD for USDXXX)
            if (upper == "EURUSD") return ("FRED", "DEXUSEU");   // USD per EUR
            if (upper == "GBPUSD") return ("FRED", "DEXUSUK");   // USD per GBP
            if (upper == "USDJPY") return ("FRED", "DEXJPUS");   // JPY per USD
            if (upper == "AUDUSD") return ("FRED", "DEXUSAL");   // USD per AUD
            if (upper == "USDCAD") return ("FRED", "DEXCAUS");   // CAD per USD
            if (upper == "NZDUSD") return ("FRED", "DEXUSNZ");   // USD per NZD
            if (upper == "USDCHF") return ("FRED", "DEXSZUS");   // CHF per USD
            if (upper == "USDZAR") return ("FRED", "DEXSFUS");   // ZAR per USD (South African Rand)
            if (upper == "USDMXN") return ("FRED", "DEXMXUS");   // MXN per USD
            if (upper == "USDNOK") return ("FRED", "DEXNOUS");   // NOK per USD
            if (upper == "USDSEK") return ("FRED", "DEXSDUS");   // SEK per USD

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

        /// <summary>Fetches time-series data and returns close/value column as list (newest first).</summary>
        private async Task<List<double>?> FetchTimeSeriesClosesAsync(string database, string dataset, int limit = 300)
        {
            if (string.IsNullOrEmpty(ApiKey)) return null;
            if (await _rateLimit.IsBlockedAsync("NasdaqDataLink")) return null;

            await ThrottleAsync();
            var url = $"{BaseUrl}/datasets/{Uri.EscapeDataString(database)}/{Uri.EscapeDataString(dataset)}/data.json?api_key={Uri.EscapeDataString(ApiKey)}&limit={limit}&order=desc";

            try
            {
                var json = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("quandl_error", out var qErr))
                {
                    var msg = qErr.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    if (msg.Contains("limit", StringComparison.OrdinalIgnoreCase) || msg.Contains("429", StringComparison.OrdinalIgnoreCase))
                        await _rateLimit.SetBlockedAsync("NasdaqDataLink");
                    return null;
                }
                if (!doc.RootElement.TryGetProperty("dataset_data", out var datasetData))
                    return null;
                if (!datasetData.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
                    return null;
                if (!datasetData.TryGetProperty("column_names", out var columnNames) || columnNames.ValueKind != JsonValueKind.Array)
                    return null;
                int valueIndex = -1;
                for (var i = 0; i < columnNames.GetArrayLength(); i++)
                {
                    var name = columnNames[i].GetString() ?? "";
                    if (name.Equals("Value", StringComparison.OrdinalIgnoreCase) || name.Equals("Close", StringComparison.OrdinalIgnoreCase))
                    {
                        valueIndex = i;
                        break;
                    }
                }
                if (valueIndex < 0 && columnNames.GetArrayLength() >= 2)
                    valueIndex = 1;
                if (valueIndex < 0) return null;

                var closes = new List<double>();
                foreach (var row in data.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() <= valueIndex) continue;
                    var cell = row[valueIndex];
                    if (cell.ValueKind == JsonValueKind.Number && cell.TryGetDouble(out var v) && v > 0)
                        closes.Add(v);
                    else if (cell.ValueKind == JsonValueKind.String && double.TryParse(cell.GetString(), out var vs) && vs > 0)
                        closes.Add(vs);
                }
                return closes.Count >= 14 ? closes : null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Nasdaq Data Link fetch failed: {Db}/{Ds}", database, dataset);
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

        /// <summary>Fetches time series and computes RSI(14), SMA14, SMA50, EMA50, EMA200. Same shape as other providers.</summary>
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

            var ds = ToNasdaqDataset(instrumentName);
            if (ds == null) return results;

            var (database, dataset) = ds.Value;
            var closes = await FetchTimeSeriesClosesAsync(database, dataset);
            if (closes == null || closes.Count < 14) return results;

            results["RSI"] = RsiFromCloses(closes, 14);
            results["SMA14"] = Sma(closes, 14);
            if (closes.Count >= 50) results["SMA50"] = Sma(closes, 50);
            if (closes.Count >= 50) results["EMA50"] = Ema(closes, 50);
            if (closes.Count >= 200) results["EMA200"] = Ema(closes, 200);

            return results;
        }

        /// <summary>Fetches latest value (quote) for the instrument. Returns 0 if not available.</summary>
        public async Task<double> FetchQuoteAsync(string instrumentName)
        {
            var ds = ToNasdaqDataset(instrumentName);
            if (ds == null) return 0;
            var closes = await FetchTimeSeriesClosesAsync(ds.Value.database, ds.Value.dataset, limit: 1);
            if (closes == null || closes.Count == 0) return 0;
            return closes[0];
        }
    }
}
