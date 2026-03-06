using System.Text.Json;

namespace TradeHelper.Services
{
    /// <summary>
    /// Fetches GDP and inflation (annual) data from World Bank Data360 API.
    /// Free API, no key required. Covers 266 countries.
    /// API: https://data360api.worldbank.org | https://data360.worldbank.org/en/api
    /// </summary>
    public class WorldBankDataService
    {
        private const string BaseUrl = "https://data360api.worldbank.org";
        private const string DatabaseId = "WB_WDI";

        // Data360 indicator IDs (WB_WDI = World Development Indicators)
        private const string GdpGrowthIndicator = "WB_WDI_NY_GDP_MKTP_KD_ZG";  // GDP growth (annual %)
        private const string InflationIndicator = "WB_WDI_FP_CPI_TOTL_ZG";     // Inflation, consumer prices (annual %)

        // Currency -> World Bank ISO3 country/region code
        private static readonly Dictionary<string, string> CurrencyToRefArea = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = "USA",
            ["EUR"] = "EMU",  // Euro area
            ["GBP"] = "GBR",
            ["JPY"] = "JPN",
            ["AUD"] = "AUS",
            ["NZD"] = "NZL",
            ["CAD"] = "CAN",
            ["CHF"] = "CHE",
            ["SEK"] = "SWE",
            ["ZAR"] = "ZAF",
            ["SGD"] = "SGP",
            ["HKD"] = "HKG",
            ["PLN"] = "POL",
            ["CZK"] = "CZE",
            ["HUF"] = "HUN",
            ["NOK"] = "NOR",
            ["DKK"] = "DNK",
            ["MXN"] = "MEX",
            ["BRL"] = "BRA",
            ["CNY"] = "CHN",  // Chinese Yuan
        };

        private readonly HttpClient _client;
        private readonly ILogger<WorldBankDataService> _logger;

        public WorldBankDataService(HttpClient client, ILogger<WorldBankDataService> logger)
        {
            _client = client;
            _logger = logger;
            _client.BaseAddress = new Uri(BaseUrl);
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>Gets the REF_AREA (country/region) code for a currency, or null if not mapped.</summary>
        public static string? GetRefAreaForCurrency(string currency) =>
            CurrencyToRefArea.TryGetValue(currency, out var code) ? code : null;

        /// <summary>Fetches latest GDP growth (annual %) for a country. Returns null if not found.</summary>
        public async Task<double?> FetchGdpGrowthAsync(string refArea, int yearsBack = 5)
        {
            var from = DateTime.UtcNow.Year - yearsBack;
            var to = DateTime.UtcNow.Year;
            var url = $"/data360/data?DATABASE_ID={DatabaseId}&INDICATOR={GdpGrowthIndicator}&REF_AREA={refArea}&timePeriodFrom={from}&timePeriodTo={to}&format=json";
            return await FetchLatestValueAsync(url, "GDP growth");
        }

        /// <summary>Fetches latest inflation (annual %) for a country. Returns null if not found.</summary>
        public async Task<double?> FetchInflationAsync(string refArea, int yearsBack = 5)
        {
            var from = DateTime.UtcNow.Year - yearsBack;
            var to = DateTime.UtcNow.Year;
            var url = $"/data360/data?DATABASE_ID={DatabaseId}&INDICATOR={InflationIndicator}&REF_AREA={refArea}&timePeriodFrom={from}&timePeriodTo={to}&format=json";
            return await FetchLatestValueAsync(url, "Inflation");
        }

        /// <summary>Fetches GDP growth and inflation for a currency (uses mapped country).</summary>
        public async Task<WorldBankEconomicData?> FetchForCurrencyAsync(string currency)
        {
            var refArea = GetRefAreaForCurrency(currency);
            if (string.IsNullOrEmpty(refArea))
            {
                _logger.LogDebug("No World Bank ref area for currency {Currency}", currency);
                return null;
            }

            var gdpTask = FetchGdpGrowthAsync(refArea);
            var inflTask = FetchInflationAsync(refArea);
            await Task.WhenAll(gdpTask, inflTask);

            var gdp = await gdpTask;
            var infl = await inflTask;
            if (gdp == null && infl == null) return null;

            return new WorldBankEconomicData
            {
                Currency = currency,
                RefArea = refArea,
                GdpGrowthAnnualPct = gdp,
                InflationAnnualPct = infl,
                FetchedAt = DateTime.UtcNow
            };
        }

        /// <summary>Fetches GDP and inflation for multiple currencies in parallel (with rate limiting).</summary>
        public async Task<List<WorldBankEconomicData>> FetchForCurrenciesAsync(IEnumerable<string> currencies)
        {
            var results = new List<WorldBankEconomicData>();
            foreach (var c in currencies.Distinct())
            {
                var data = await FetchForCurrencyAsync(c);
                if (data != null) results.Add(data);
                await Task.Delay(150); // gentle rate limiting
            }
            return results;
        }

        /// <summary>Fetches GDP growth for any country by ISO3 code. Use for 266 countries.</summary>
        public async Task<double?> FetchGdpGrowthByCountryAsync(string iso3CountryCode, int yearsBack = 5) =>
            await FetchGdpGrowthAsync(iso3CountryCode, yearsBack);

        /// <summary>Fetches inflation for any country by ISO3 code.</summary>
        public async Task<double?> FetchInflationByCountryAsync(string iso3CountryCode, int yearsBack = 5) =>
            await FetchInflationAsync(iso3CountryCode, yearsBack);

        private async Task<double?> FetchLatestValueAsync(string url, string indicatorName)
        {
            try
            {
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("value", out var valueArr) || valueArr.GetArrayLength() == 0)
                {
                    _logger.LogDebug("World Bank {Name}: no data", indicatorName);
                    return null;
                }

                // Data is typically sorted by TIME_PERIOD desc; take first (latest)
                var first = valueArr[0];
                var obsVal = first.TryGetProperty("OBS_VALUE", out var v) ? v.GetString() : null;
                if (string.IsNullOrEmpty(obsVal) || !double.TryParse(obsVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result))
                    return null;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "World Bank {Name} fetch failed: {Url}", indicatorName, url);
                return null;
            }
        }

        /// <summary>Tests connectivity to World Bank Data360 API.</summary>
        public async Task<(bool ok, string? message)> TestConnectivityAsync()
        {
            try
            {
                var gdp = await FetchGdpGrowthAsync("USA", 2);
                if (gdp.HasValue)
                    return (true, $"USA GDP growth: {gdp:F2}%");
                return (false, "No data returned for USA");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }

    public class WorldBankEconomicData
    {
        public string Currency { get; set; } = "";
        public string RefArea { get; set; } = "";
        public double? GdpGrowthAnnualPct { get; set; }
        public double? InflationAnnualPct { get; set; }
        public DateTime FetchedAt { get; set; }
    }
}
