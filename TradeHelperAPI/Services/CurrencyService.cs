using System.Globalization;
using System.Text.Json;

namespace TradeHelper.Services
{
    public class CurrencyInfo
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Symbol { get; set; } = "";
    }

    public class CurrencyService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CurrencyService> _logger;

        private static readonly Dictionary<string, CurrencyInfo> _currencies = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = new() { Code = "USD", Name = "US Dollar",           Symbol = "$" },
            ["EUR"] = new() { Code = "EUR", Name = "Euro",                Symbol = "€" },
            ["GBP"] = new() { Code = "GBP", Name = "British Pound",      Symbol = "£" },
            ["JPY"] = new() { Code = "JPY", Name = "Japanese Yen",       Symbol = "¥" },
            ["ZAR"] = new() { Code = "ZAR", Name = "South African Rand", Symbol = "R" },
            ["AUD"] = new() { Code = "AUD", Name = "Australian Dollar",  Symbol = "A$" },
            ["CAD"] = new() { Code = "CAD", Name = "Canadian Dollar",    Symbol = "C$" },
            ["CHF"] = new() { Code = "CHF", Name = "Swiss Franc",        Symbol = "CHF" },
            ["NZD"] = new() { Code = "NZD", Name = "New Zealand Dollar", Symbol = "NZ$" },
            ["SGD"] = new() { Code = "SGD", Name = "Singapore Dollar",   Symbol = "S$" },
            ["HKD"] = new() { Code = "HKD", Name = "Hong Kong Dollar",   Symbol = "HK$" },
            ["PLN"] = new() { Code = "PLN", Name = "Polish Zloty",       Symbol = "zł" },
            ["CZK"] = new() { Code = "CZK", Name = "Czech Koruna",      Symbol = "Kč" },
            ["HUF"] = new() { Code = "HUF", Name = "Hungarian Forint",  Symbol = "Ft" },
            ["SEK"] = new() { Code = "SEK", Name = "Swedish Krona",     Symbol = "kr" },
            ["NOK"] = new() { Code = "NOK", Name = "Norwegian Krone",   Symbol = "kr" },
            ["DKK"] = new() { Code = "DKK", Name = "Danish Krone",      Symbol = "kr" },
            ["CNY"] = new() { Code = "CNY", Name = "Chinese Yuan",      Symbol = "¥" },
        };

        // Cached rates: key = "USD_EUR", value = rate, with TTL
        private static readonly Dictionary<string, (decimal rate, DateTime fetched)> _rateCache = new();
        private static readonly TimeSpan _cacheTtl = TimeSpan.FromHours(4);

        public CurrencyService(HttpClient httpClient, ILogger<CurrencyService> logger)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _logger = logger;
        }

        public static List<CurrencyInfo> GetAllCurrencies() =>
            _currencies.Values.OrderBy(c => c.Code).ToList();

        public static string GetSymbol(string currencyCode) =>
            _currencies.TryGetValue(currencyCode, out var info) ? info.Symbol : currencyCode;

        public async Task<decimal> GetExchangeRateAsync(string from, string to)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return 1m;

            var cacheKey = $"{from.ToUpper()}_{to.ToUpper()}";
            if (_rateCache.TryGetValue(cacheKey, out var cached) &&
                DateTime.UtcNow - cached.fetched < _cacheTtl)
                return cached.rate;

            try
            {
                var url = $"https://api.frankfurter.app/latest?from={from.ToUpper()}&to={to.ToUpper()}";
                var response = await _httpClient.GetStringAsync(url);
                var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("rates", out var rates) &&
                    rates.TryGetProperty(to.ToUpper(), out var rateVal))
                {
                    var rate = rateVal.GetDecimal();
                    _rateCache[cacheKey] = (rate, DateTime.UtcNow);
                    _logger.LogInformation("Fetched rate {From}->{To} = {Rate}", from, to, rate);
                    return rate;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch exchange rate {From}->{To}, using fallback", from, to);
            }

            return GetFallbackRate(from, to);
        }

        public async Task<decimal> ConvertAsync(decimal amount, string from, string to)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return amount;

            var rate = await GetExchangeRateAsync(from, to);
            return Math.Round(amount * rate, 2);
        }

        private static decimal GetFallbackRate(string from, string to)
        {
            var toUsd = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 1m,     ["EUR"] = 1.08m,  ["GBP"] = 1.27m,
                ["JPY"] = 0.0067m,["ZAR"] = 0.055m, ["AUD"] = 0.65m,
                ["CAD"] = 0.74m,  ["CHF"] = 1.13m,  ["NZD"] = 0.61m,
                ["SGD"] = 0.75m,  ["HKD"] = 0.128m, ["PLN"] = 0.25m,
                ["CZK"] = 0.043m, ["HUF"] = 0.0027m,["SEK"] = 0.096m,
                ["NOK"] = 0.094m, ["DKK"] = 0.145m, ["CNY"] = 0.14m,
            };

            if (!toUsd.TryGetValue(from, out var fromRate)) fromRate = 1m;
            if (!toUsd.TryGetValue(to, out var toRate)) toRate = 1m;

            return fromRate / toRate;
        }
    }
}
