using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    /// <summary>Fetches technical indicators from Alpha Vantage API and stores them in the database.</summary>
    public class AlphaVantageService
    {
        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<AlphaVantageService> _logger;
        private static DateTime _lastCall = DateTime.MinValue;
        private static readonly SemaphoreSlim _throttle = new(1, 1);
        private const int MinSecondsBetweenCalls = 13; // Free tier: 5 calls/min

        private string ApiKey => _config["TrailBlazer:AlphaVantageApiKey"] ?? _config["AlphaVantageApiKey"] ?? "";

        private readonly ApiRateLimitService _rateLimit;

        public AlphaVantageService(HttpClient client, IConfiguration config, ILogger<AlphaVantageService> logger, ApiRateLimitService rateLimit)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _rateLimit = rateLimit;
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>Maps instrument name to Alpha Vantage forex symbol (FX_EURUSD). Returns null for unsupported symbols.</summary>
        public static string? ToAlphaVantageSymbol(string instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName) || instrumentName.Length < 6) return null;
            var upper = instrumentName.ToUpperInvariant();
            // Forex: EURUSD -> FX_EURUSD
            if (upper.Length == 6 && upper.All(char.IsLetter))
                return $"FX_{upper}";
            // Some brokers use 7 chars e.g. USDZAR
            if (upper.Length >= 6 && upper.All(char.IsLetter))
                return $"FX_{upper}";
            return null;
        }

        /// <summary>Throttle to respect Alpha Vantage free tier (5 calls/min).</summary>
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

        /// <summary>Fetches a technical indicator from Alpha Vantage. Returns the latest value or null.</summary>
        /// <param name="valueKey">JSON key for the value (e.g. "RSI", "SMA", "EMA")</param>
        private async Task<double?> FetchIndicatorAsync(string function, string symbol, string timePeriod = "14", string? valueKey = null)
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                _logger.LogWarning("Alpha Vantage API key not configured");
                return null;
            }
            if (await _rateLimit.IsBlockedAsync("AlphaVantage"))
                return null;

            await ThrottleAsync();

            var url = $"https://www.alphavantage.co/query?function={function}&symbol={symbol}&interval=daily&time_period={timePeriod}&series_type=close&apikey={ApiKey}";
            var vk = valueKey ?? function;
            try
            {
                var json = await _client.GetStringAsync(url);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("Error Message", out var err))
                {
                    _logger.LogWarning("Alpha Vantage error for {Function} {Symbol}: {Msg}", function, symbol, err.GetString());
                    return null;
                }
                if (root.TryGetProperty("Note", out var note))
                {
                    var noteStr = note.GetString();
                    _logger.LogWarning("Alpha Vantage rate limit: {Note}", noteStr);
                    await _rateLimit.SetBlockedAsync("AlphaVantage");
                    return null;
                }

                string? analysisKey = null;
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name.Contains("Technical Analysis", StringComparison.OrdinalIgnoreCase))
                    {
                        analysisKey = prop.Name;
                        break;
                    }
                }
                if (analysisKey == null) return null;

                var series = root.GetProperty(analysisKey);
                JsonElement firstDateElement = default;
                foreach (var prop in series.EnumerateObject())
                {
                    firstDateElement = prop.Value;
                    break;
                }
                if (firstDateElement.ValueKind == JsonValueKind.Undefined || firstDateElement.ValueKind == JsonValueKind.Null) return null;
                if (!firstDateElement.TryGetProperty(vk, out var valProp)) return null;
                var str = valProp.GetString();
                return double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alpha Vantage fetch failed for {Function} {Symbol}", function, symbol);
                return null;
            }
        }

        /// <summary>Fetches RSI for a symbol. Returns latest value or null.</summary>
        public async Task<double?> FetchRSIAsync(string symbol, int period = 14) =>
            await FetchIndicatorAsync("RSI", symbol, period.ToString());

        /// <summary>Fetches SMA for a symbol. Returns latest value or null.</summary>
        public async Task<double?> FetchSMAAsync(string symbol, int period = 14) =>
            await FetchIndicatorAsync("SMA", symbol, period.ToString());

        /// <summary>Loads technical data from Alpha Vantage for supported instruments and saves to database.</summary>
        /// <param name="limit">Optional limit (e.g. 8 for forex majors only). Null = all forex.</param>
        public async Task<int> LoadTechnicalDataToDatabaseAsync(ApplicationDbContext db, int? limit = null)
        {
            var query = db.Instruments.Where(i => i.Type == "Currency").OrderBy(i => i.Id);
            var instruments = limit.HasValue ? await query.Take(limit.Value).ToListAsync() : await query.ToListAsync();
            var loaded = 0;
            var now = DateTime.UtcNow;
            var today = now.Date;

            foreach (var instrument in instruments)
            {
                var avSymbol = ToAlphaVantageSymbol(instrument.Name);
                if (avSymbol == null) continue;

                try
                {
                    var rsi = await FetchRSIAsync(avSymbol, 14);
                    var sma14 = await FetchSMAAsync(avSymbol, 14);
                    var sma50 = await FetchSMAAsync(avSymbol, 50);
                    var ema50 = await FetchIndicatorAsync("EMA", avSymbol, "50");
                    var ema200 = await FetchIndicatorAsync("EMA", avSymbol, "200");

                    if (rsi == null && sma14 == null && sma50 == null && ema50 == null && ema200 == null)
                    {
                        _logger.LogDebug("No technical data for {Symbol}", instrument.Name);
                        continue;
                    }

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
                    _logger.LogInformation("Loaded technical data for {Name}: RSI={RSI}, SMA14={SMA14}", instrument.Name, rsi, sma14);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load technical data for {Name}", instrument.Name);
                }
            }

            await db.SaveChangesAsync();
            return loaded;
        }
    }
}
