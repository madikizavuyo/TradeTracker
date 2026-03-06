using Microsoft.EntityFrameworkCore;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    /// <summary>
    /// Tracks API credit/rate limit blocks. When an API returns a credit limit message,
    /// we block further calls for 12 hours to avoid spamming.
    /// </summary>
    public class ApiRateLimitService
    {
        private const string KeyPrefix = "ApiBlock_";
        private static readonly TimeSpan DefaultBlockDuration = TimeSpan.FromHours(12);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<ApiRateLimitService> _logger;

        public ApiRateLimitService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ApiRateLimitService> logger)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
        }

        private TimeSpan BlockDuration => TimeSpan.FromHours(_config.GetValue("ApiRateLimit:BlockHours", 12));

        /// <summary>Returns true if the API is currently blocked (credit limit hit).</summary>
        public async Task<bool> IsBlockedAsync(string apiName)
        {
            var key = KeyPrefix + apiName;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting?.Value == null) return false;
            if (!DateTime.TryParse(setting.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var until))
                return false;
            var blocked = DateTime.UtcNow < until;
            if (blocked)
                _logger.LogDebug("API {Api} blocked until {Until}", apiName, until);
            return blocked;
        }

        /// <summary>Records that the API returned a credit limit. Blocks for 12 hours.</summary>
        public async Task SetBlockedAsync(string apiName)
        {
            var until = DateTime.UtcNow.Add(BlockDuration);
            var key = KeyPrefix + apiName;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            var value = until.ToString("o");
            if (setting != null)
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
            _logger.LogWarning("API {Api} blocked until {Until} (credit limit). Will not retry for {Hours}h.", apiName, until, BlockDuration.TotalHours);
        }

        /// <summary>Returns true if the error message indicates a credit/rate limit.</summary>
        public static bool IsCreditLimitMessage(string? message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            var m = message.ToLowerInvariant();
            return m.Contains("credit") || m.Contains("rate limit") || m.Contains("quota") || m.Contains("limit exceeded")
                || m.Contains("too many requests") || m.Contains("429") || m.Contains("calls per minute")
                || m.Contains("thank you for using alpha vantage"); // Alpha Vantage "Note" message
        }
    }
}
