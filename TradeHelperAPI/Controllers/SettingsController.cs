using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradeHelper.Data;
using TradeHelper.Models;
using TradeHelper.Services;

namespace TradeHelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SettingsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrencyService _currencyService;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            ApplicationDbContext context,
            CurrencyService currencyService,
            ILogger<SettingsController> logger)
        {
            _context = context;
            _currencyService = currencyService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var settings = await GetOrCreateSettingsAsync(userId);

            return Ok(new
            {
                displayCurrency = settings.DisplayCurrency,
                displayCurrencySymbol = settings.DisplayCurrencySymbol
            });
        }

        [HttpPut("UpdateCurrency")]
        public async Task<IActionResult> UpdateCurrency([FromBody] UpdateCurrencyRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var symbol = CurrencyService.GetSymbol(request.Currency);

            var settings = await GetOrCreateSettingsAsync(userId);
            var previousCurrency = settings.DisplayCurrency;
            settings.DisplayCurrency = request.Currency;
            settings.DisplayCurrencySymbol = symbol;
            settings.UpdatedAt = DateTime.UtcNow;

            // Recalculate ProfitLossDisplay for all user trades
            var trades = await _context.Trades
                .Where(t => t.UserId == userId && t.ProfitLoss.HasValue)
                .ToListAsync();

            foreach (var trade in trades)
            {
                trade.DisplayCurrency = request.Currency;
                trade.DisplayCurrencySymbol = symbol;
                trade.ProfitLossDisplay = await _currencyService.ConvertAsync(
                    trade.ProfitLoss!.Value, trade.Currency, request.Currency);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "User {UserId} changed display currency from {Old} to {New}, updated {Count} trades",
                userId, previousCurrency, request.Currency, trades.Count);

            return Ok(new
            {
                displayCurrency = request.Currency,
                displayCurrencySymbol = symbol,
                tradesUpdated = trades.Count
            });
        }

        [HttpGet("currencies")]
        public IActionResult GetCurrencies()
        {
            return Ok(CurrencyService.GetAllCurrencies());
        }

        [HttpGet("currency/test")]
        public async Task<IActionResult> TestConversion(
            [FromQuery] string fromCurrency,
            [FromQuery] string toCurrency,
            [FromQuery] decimal amount)
        {
            try
            {
                var rate = await _currencyService.GetExchangeRateAsync(fromCurrency, toCurrency);
                var converted = Math.Round(amount * rate, 2);
                var fromSymbol = CurrencyService.GetSymbol(fromCurrency);
                var toSymbol = CurrencyService.GetSymbol(toCurrency);

                return Ok(new
                {
                    success = true,
                    originalAmount = $"{fromSymbol}{amount:N2} {fromCurrency}",
                    convertedAmount = $"{toSymbol}{converted:N2} {toCurrency}",
                    exchangeRate = $"1 {fromCurrency} = {rate:N4} {toCurrency}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Currency conversion test failed");
                return Ok(new { success = false, error = "Conversion failed" });
            }
        }

        private async Task<UserSettings> GetOrCreateSettingsAsync(string userId)
        {
            var settings = await _context.UserSettings
                .FirstOrDefaultAsync(s => s.UserId == userId);

            if (settings == null)
            {
                settings = new UserSettings
                {
                    UserId = userId,
                    DisplayCurrency = "USD",
                    DisplayCurrencySymbol = "$"
                };
                _context.UserSettings.Add(settings);
                await _context.SaveChangesAsync();
            }

            return settings;
        }
    }

    public class UpdateCurrencyRequest
    {
        public string Currency { get; set; } = "USD";
    }
}
