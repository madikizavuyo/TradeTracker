using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeHelper.Services;

namespace TradeHelper.Controllers
{
    /// <summary>
    /// World Bank Data360 API - GDP and inflation (annual) for 266 countries.
    /// Free API, no key required. https://data360.worldbank.org/en/api
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class WorldBankController : ControllerBase
    {
        private readonly WorldBankDataService _service;
        private readonly ILogger<WorldBankController> _logger;

        public WorldBankController(WorldBankDataService service, ILogger<WorldBankController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>Get GDP growth (annual %) and inflation (annual %) for a currency (e.g. USD, EUR).</summary>
        [HttpGet("currency/{currency}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetByCurrency(string currency)
        {
            if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
                return BadRequest(new { error = "Provide a 3-letter currency code (e.g. USD, EUR)" });

            var data = await _service.FetchForCurrencyAsync(currency.ToUpperInvariant());
            if (data == null)
                return NotFound(new { error = $"No World Bank data for currency {currency}" });

            return Ok(new
            {
                data.Currency,
                data.RefArea,
                gdpGrowthAnnualPct = data.GdpGrowthAnnualPct,
                inflationAnnualPct = data.InflationAnnualPct,
                data.FetchedAt
            });
        }

        /// <summary>Get GDP growth and inflation for multiple currencies.</summary>
        [HttpPost("currencies")]
        [AllowAnonymous]
        public async Task<IActionResult> GetByCurrencies([FromBody] string[] currencies)
        {
            if (currencies == null || currencies.Length == 0)
                return BadRequest(new { error = "Provide an array of currency codes" });

            var normalized = currencies.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).Take(20).ToList();
            var results = await _service.FetchForCurrenciesAsync(normalized);
            return Ok(results);
        }

        /// <summary>Get GDP growth (annual %) for a country by ISO3 code (e.g. USA, GBR, JPN). Covers 266 countries.</summary>
        [HttpGet("gdp/{iso3}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetGdpByCountry(string iso3, [FromQuery] int yearsBack = 5)
        {
            if (string.IsNullOrWhiteSpace(iso3) || iso3.Length != 3)
                return BadRequest(new { error = "Provide a 3-letter ISO3 country code (e.g. USA, GBR)" });

            var value = await _service.FetchGdpGrowthByCountryAsync(iso3.ToUpperInvariant(), yearsBack);
            if (!value.HasValue)
                return NotFound(new { error = $"No GDP data for {iso3}" });

            return Ok(new { country = iso3.ToUpperInvariant(), gdpGrowthAnnualPct = value.Value });
        }

        /// <summary>Get inflation (annual %) for a country by ISO3 code.</summary>
        [HttpGet("inflation/{iso3}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetInflationByCountry(string iso3, [FromQuery] int yearsBack = 5)
        {
            if (string.IsNullOrWhiteSpace(iso3) || iso3.Length != 3)
                return BadRequest(new { error = "Provide a 3-letter ISO3 country code (e.g. USA, GBR)" });

            var value = await _service.FetchInflationByCountryAsync(iso3.ToUpperInvariant(), yearsBack);
            if (!value.HasValue)
                return NotFound(new { error = $"No inflation data for {iso3}" });

            return Ok(new { country = iso3.ToUpperInvariant(), inflationAnnualPct = value.Value });
        }

        /// <summary>Test World Bank Data360 API connectivity.</summary>
        [HttpGet("test")]
        [AllowAnonymous]
        public async Task<IActionResult> Test()
        {
            var (ok, message) = await _service.TestConnectivityAsync();
            return Ok(new { ok, message });
        }
    }
}
