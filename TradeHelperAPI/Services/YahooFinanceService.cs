using System.Globalization;
using System.Text.Json;

namespace TradeHelper.Services
{
    /// <summary>
    /// Unofficial Yahoo Finance chart/quote/search endpoints (no API key). Used as primary source for OHLC-based technicals,
    /// live quotes, and market news when other providers fail or are rate-limited.
    /// </summary>
    public class YahooFinanceService
    {
        private static readonly SemaphoreSlim Throttle = new(1, 1);
        private static DateTime _lastCall = DateTime.MinValue;
        private const double MinSecondsBetweenCalls = 0.35;

        private readonly HttpClient _client;
        private readonly IConfiguration _config;
        private readonly ILogger<YahooFinanceService> _logger;

        private bool Enabled => _config.GetValue("TrailBlazer:YahooFinanceEnabled", true);

        public YahooFinanceService(HttpClient client, IConfiguration config, ILogger<YahooFinanceService> logger)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _client.Timeout = TimeSpan.FromSeconds(35);
            if (_client.DefaultRequestHeaders.UserAgent.Count == 0)
                _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        }

        /// <summary>Maps internal instrument codes to Yahoo symbols (chart + quote compatible).</summary>
        public static string? ToYahooSymbol(string instrumentName)
        {
            if (string.IsNullOrEmpty(instrumentName)) return null;
            var upper = instrumentName.ToUpperInvariant().Replace("/", "").Replace("_", "").Replace(" ", "");

            if (upper.StartsWith("XAU", StringComparison.OrdinalIgnoreCase)) return "GC=F";
            if (upper.StartsWith("XAG", StringComparison.OrdinalIgnoreCase)) return "SI=F";
            if (upper.StartsWith("XPT", StringComparison.OrdinalIgnoreCase)) return "PL=F";
            if (upper.StartsWith("XPD", StringComparison.OrdinalIgnoreCase)) return "PA=F";
            if (upper == "USOIL" || upper == "OIL" || upper == "WTI") return "CL=F";
            if (upper == "UKOIL" || upper == "BRENT") return "BZ=F";

            if (upper == "US500" || upper == "SPX") return "^GSPC";
            if (upper == "US30") return "^DJI";
            if (upper == "US100") return "^IXIC";
            if (upper == "DE40") return "^GDAXI";
            if (upper == "UK100") return "^FTSE";
            if (upper == "JP225") return "^N225";

            if (upper == "BTC") return "BTC-USD";
            if (upper == "ETH") return "ETH-USD";
            if (upper == "SOL") return "SOL-USD";

            // 6-letter forex majors and crosses (e.g. EURUSD, GBPJPY); excludes XAUUSD etc. (handled above)
            if (upper.Length == 6 && upper.All(char.IsLetter))
                return $"{upper}=X";

            return null;
        }

        private async Task ThrottleAsync()
        {
            await Throttle.WaitAsync();
            try
            {
                var elapsed = (DateTime.UtcNow - _lastCall).TotalSeconds;
                if (elapsed < MinSecondsBetweenCalls)
                    await Task.Delay(TimeSpan.FromMilliseconds((int)((MinSecondsBetweenCalls - elapsed) * 1000)));
                _lastCall = DateTime.UtcNow;
            }
            finally { Throttle.Release(); }
        }

        /// <summary>Fetches daily OHLC from Yahoo chart API. Returns bars newest-first (same convention as MarketStack).</summary>
        public async Task<List<OhlcTechnicalCalculator.OhlcBar>?> FetchDailyOhlcAsync(string instrumentName, int minBars = 200)
        {
            if (!Enabled) return null;
            var yahoo = ToYahooSymbol(instrumentName);
            if (yahoo == null) return null;
            var range = minBars > 300 ? "5y" : "2y";
            return await FetchChartOhlcAsync(instrumentName, yahoo, "1d", range);
        }

        /// <summary>
        /// Fetches 4H OHLC by pulling Yahoo 60m bars and aggregating them into 4-bar candles.
        /// Returns newest-first.
        /// </summary>
        public async Task<List<OhlcTechnicalCalculator.OhlcBar>?> Fetch4HourOhlcAsync(string instrumentName, int minBars = 120)
        {
            if (!Enabled) return null;
            var yahoo = ToYahooSymbol(instrumentName);
            if (yahoo == null) return null;

            var range = minBars > 240 ? "730d" : "180d";
            var hourlyNewestFirst = await FetchChartOhlcAsync(instrumentName, yahoo, "60m", range);
            if (hourlyNewestFirst == null || hourlyNewestFirst.Count < 16)
                return null;

            var hourlyChrono = hourlyNewestFirst.AsEnumerable().Reverse().ToList();
            var bars4hChrono = new List<OhlcTechnicalCalculator.OhlcBar>();
            for (var i = 0; i + 3 < hourlyChrono.Count; i += 4)
            {
                var chunk = hourlyChrono.Skip(i).Take(4).ToList();
                if (chunk.Count < 4) break;
                bars4hChrono.Add(new OhlcTechnicalCalculator.OhlcBar(
                    chunk[0].Open,
                    chunk.Max(b => b.High),
                    chunk.Min(b => b.Low),
                    chunk[^1].Close));
            }

            if (bars4hChrono.Count < Math.Max(20, minBars))
                return null;

            bars4hChrono.Reverse();
            return bars4hChrono;
        }

        private async Task<List<OhlcTechnicalCalculator.OhlcBar>?> FetchChartOhlcAsync(
            string instrumentName,
            string yahooSymbol,
            string interval,
            string range)
        {
            await ThrottleAsync();
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?interval={Uri.EscapeDataString(interval)}&range={Uri.EscapeDataString(range)}";

            try
            {
                using var response = await _client.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Yahoo chart HTTP {Status} for {Symbol} ({Interval}, {Range})", (int)response.StatusCode, yahooSymbol, interval, range);
                    return null;
                }
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
                    !chart.TryGetProperty("result", out var resultArr) ||
                    resultArr.ValueKind != JsonValueKind.Array ||
                    resultArr.GetArrayLength() == 0)
                    return null;

                var result = resultArr[0];
                if (result.TryGetProperty("error", out var err))
                {
                    _logger.LogDebug("Yahoo chart error for {Symbol}: {Err}", yahooSymbol, err.ToString());
                    return null;
                }

                if (!result.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array)
                    return null;
                if (!result.TryGetProperty("indicators", out var indicators) ||
                    !indicators.TryGetProperty("quote", out var quotes) ||
                    quotes.ValueKind != JsonValueKind.Array ||
                    quotes.GetArrayLength() == 0)
                    return null;

                var quote = quotes[0];
                if (!quote.TryGetProperty("open", out var opens) || !quote.TryGetProperty("high", out var highs) ||
                    !quote.TryGetProperty("low", out var lows) || !quote.TryGetProperty("close", out var closes))
                    return null;

                var n = timestamps.GetArrayLength();
                if (opens.GetArrayLength() < n || highs.GetArrayLength() < n || lows.GetArrayLength() < n || closes.GetArrayLength() < n)
                    n = Math.Min(Math.Min(opens.GetArrayLength(), highs.GetArrayLength()), Math.Min(lows.GetArrayLength(), closes.GetArrayLength()));

                var barsChrono = new List<OhlcTechnicalCalculator.OhlcBar>();
                for (var i = 0; i < n; i++)
                {
                    if (!TryGetDouble(closes, i, out var c) || c <= 0) continue;
                    if (!TryGetDouble(opens, i, out var o)) o = c;
                    if (!TryGetDouble(highs, i, out var h)) h = c;
                    if (!TryGetDouble(lows, i, out var l)) l = c;
                    barsChrono.Add(new OhlcTechnicalCalculator.OhlcBar(o, h, l, c));
                }

                if (barsChrono.Count < 14) return null;
                barsChrono.Reverse();
                return barsChrono;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Yahoo chart fetch failed for {Instrument} ({Yahoo}, {Interval}, {Range})", instrumentName, yahooSymbol, interval, range);
                return null;
            }
        }

        private static bool TryGetDouble(JsonElement arr, int index, out double value)
        {
            value = 0;
            if (index >= arr.GetArrayLength()) return false;
            var el = arr[index];
            if (el.ValueKind == JsonValueKind.Null) return false;
            if (el.ValueKind == JsonValueKind.Number)
            {
                value = el.GetDouble();
                return value > 0 || value == 0;
            }
            return el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        public async Task<Dictionary<string, double>> FetchTechnicalIndicatorsAsync(string instrumentName)
        {
            var defaults = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
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
            var bars = await FetchDailyOhlcAsync(instrumentName, 220);
            if (bars == null || bars.Count < 14)
                return defaults;
            return OhlcTechnicalCalculator.Compute(bars);
        }

        /// <summary>Latest regular market price (forex, futures, indices, crypto).</summary>
        public async Task<double> FetchQuoteAsync(string instrumentName)
        {
            if (!Enabled) return 0;
            var yahoo = ToYahooSymbol(instrumentName);
            if (yahoo == null) return 0;

            await ThrottleAsync();
            var url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={Uri.EscapeDataString(yahoo)}";
            try
            {
                var json = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("quoteResponse", out var qr) ||
                    !qr.TryGetProperty("result", out var res) || res.ValueKind != JsonValueKind.Array || res.GetArrayLength() == 0)
                    return 0;
                var item = res[0];
                if (item.TryGetProperty("regularMarketPrice", out var p) && p.ValueKind == JsonValueKind.Number)
                {
                    var v = p.GetDouble();
                    if (v > 0) return v;
                }
                if (item.TryGetProperty("postMarketPrice", out var pp) && pp.ValueKind == JsonValueKind.Number)
                {
                    var v = pp.GetDouble();
                    if (v > 0) return v;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Yahoo quote failed for {Instrument}", instrumentName);
            }
            return 0;
        }

        /// <summary>News titles/snippets for scoring (no Brave quota). Uses Yahoo finance search news facet.</summary>
        public async Task<List<NewsItem>> FetchNewsForQueryAsync(string searchQuery, int count = 8)
        {
            var items = new List<NewsItem>();
            if (!Enabled || string.IsNullOrWhiteSpace(searchQuery)) return items;

            await ThrottleAsync();
            var q = Uri.EscapeDataString(searchQuery.Trim());
            var url = $"https://query2.finance.yahoo.com/v1/finance/search?q={q}&quotesCount=0&newsCount={Math.Clamp(count, 1, 30)}";

            try
            {
                var json = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("news", out var news) || news.ValueKind != JsonValueKind.Array)
                    return items;

                foreach (var el in news.EnumerateArray())
                {
                    var title = el.TryGetProperty("title", out var t) ? t.GetString()?.Trim() ?? "" : "";
                    if (string.IsNullOrEmpty(title)) continue;
                    var publisher = el.TryGetProperty("publisher", out var pub) ? pub.GetString() ?? "" : "";
                    var link = el.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                    var published = DateTime.UtcNow;
                    if (el.TryGetProperty("providerPublishTime", out var ppt))
                    {
                        if (ppt.ValueKind == JsonValueKind.Number && ppt.TryGetInt64(out var ts))
                            published = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                    }
                    items.Add(new NewsItem
                    {
                        Headline = title,
                        Summary = "",
                        Source = string.IsNullOrEmpty(publisher) ? "Yahoo Finance" : publisher,
                        Url = link,
                        ImageUrl = "",
                        PublishedAt = published
                    });
                    if (items.Count >= count) break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Yahoo news search failed for query {Query}", searchQuery);
            }

            return items;
        }

        public async Task<List<NewsItem>> FetchNewsForInstrumentAsync(string symbol, string? assetClass)
        {
            var q = BuildYahooNewsQuery(symbol, assetClass);
            return await FetchNewsForQueryAsync(q, 10);
        }

        private static string BuildYahooNewsQuery(string symbol, string? assetClass)
        {
            var upper = symbol.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
            if (upper.Length >= 6 && upper.All(char.IsLetter))
            {
                var b = upper[..3];
                var q = upper[3..];
                return $"{b} {q} forex currency";
            }
            if (upper.StartsWith("XAU")) return "gold price commodities";
            if (upper.StartsWith("XAG")) return "silver price commodities";
            if (upper == "US500" || upper == "SPX") return "S&P 500 stock market";
            if (upper == "US30") return "Dow Jones stock market";
            if (upper == "US100") return "Nasdaq stock market";
            if (upper == "BTC") return "Bitcoin cryptocurrency";
            if (upper == "ETH") return "Ethereum cryptocurrency";
            if (upper == "USOIL" || upper == "OIL") return "crude oil WTI price";
            return $"{symbol} market";
        }

        public async Task<List<string>> FetchGlobalMacroHeadlinesAsync(int count = 25)
        {
            var headlines = new List<string>();
            if (!Enabled) return headlines;

            var items = await FetchNewsForQueryAsync("Federal Reserve inflation GDP forex dollar ECB global economy", count);
            foreach (var i in items)
            {
                if (!string.IsNullOrWhiteSpace(i.Headline))
                    headlines.Add(i.Headline);
            }
            return headlines;
        }

        /// <summary>Daily closes, newest first — for correlation and relative strength (same convention as FMP after fix).</summary>
        public async Task<List<double>> FetchHistoricalClosesNewestFirstAsync(string instrumentName, int minDays = 90)
        {
            var bars = await FetchDailyOhlcAsync(instrumentName, Math.Min(500, Math.Max(220, minDays + 30)));
            if (bars == null || bars.Count == 0)
                return new List<double>();
            return bars.Select(b => b.Close).Where(c => c > 0).ToList();
        }
    }
}
