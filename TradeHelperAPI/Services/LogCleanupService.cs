using Microsoft.EntityFrameworkCore;
using TradeHelper.Data;

namespace TradeHelper.Services
{
    /// <summary>
    /// Background service that deletes UserLogs older than the configured retention period (default 3 days).
    /// </summary>
    public class LogCleanupService : BackgroundService
    {
        private readonly IServiceProvider _provider;
        private readonly IConfiguration _config;
        private readonly ILogger<LogCleanupService> _logger;

        public LogCleanupService(
            IServiceProvider provider,
            IConfiguration config,
            ILogger<LogCleanupService> logger)
        {
            _provider = provider;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait 1 minute after startup before first run
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            var intervalHours = _config.GetValue("LogCleanup:IntervalHours", 24);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCleanupAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Log cleanup failed");
                }

                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
        }

        public async Task RunCleanupAsync()
        {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var retentionDays = _config.GetValue("LogCleanup:RetentionDays", 5);
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            var userLogsToDelete = await db.UserLogs.Where(l => l.Timestamp < cutoff).CountAsync();
            var appLogsToDelete = await db.ApplicationLogs.Where(l => l.Timestamp < cutoff).CountAsync();

            if (userLogsToDelete == 0 && appLogsToDelete == 0)
            {
                _logger.LogDebug("Log cleanup: no logs older than {Days} days", retentionDays);
                return;
            }

            await db.UserLogs.Where(l => l.Timestamp < cutoff).ExecuteDeleteAsync();
            await db.ApplicationLogs.Where(l => l.Timestamp < cutoff).ExecuteDeleteAsync();

            _logger.LogInformation("Log cleanup: deleted {UserCount} UserLogs and {AppCount} ApplicationLogs older than {Days} days",
                userLogsToDelete, appLogsToDelete, retentionDays);
        }
    }
}
