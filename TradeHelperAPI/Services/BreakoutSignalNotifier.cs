using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    public interface IBreakoutSignalNotifier
    {
        /// <summary>Sends at most one email per (instrument, signal) per 24h to all users with email addresses.</summary>
        Task TryNotifyStrongSignalAsync(TrailBlazerScore score, string instrumentName, CancellationToken cancellationToken = default);
    }

    public class BreakoutSignalNotifier : IBreakoutSignalNotifier
    {
        private const string SettingPrefix = "BreakoutAlertSent_";

        private readonly IConfiguration _config;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBrevoEmailService _email;
        private readonly ILogger<BreakoutSignalNotifier> _logger;

        public BreakoutSignalNotifier(
            IConfiguration config,
            IServiceScopeFactory scopeFactory,
            IBrevoEmailService email,
            ILogger<BreakoutSignalNotifier> logger)
        {
            _config = config;
            _scopeFactory = scopeFactory;
            _email = email;
            _logger = logger;
        }

        public async Task TryNotifyStrongSignalAsync(TrailBlazerScore score, string instrumentName, CancellationToken cancellationToken = default)
        {
            var sig = score.TradeSetupSignal ?? "";
            var emailReversalBuy = _config.GetValue("TrailBlazer:EmailReversalBuyAlerts", true);
            var notify = string.Equals(sig, "STRONG_BUY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sig, "STRONG_SELL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sig, "STRONG_REVERSAL_BUY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sig, "STRONG_REVERSAL_SELL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sig, "BUY NOW", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sig, "SELL NOW", StringComparison.OrdinalIgnoreCase)
                || (emailReversalBuy && (
                        string.Equals(sig, "REVERSAL_BUY", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(sig, "REVERSAL_SELL", StringComparison.OrdinalIgnoreCase)));
            if (!notify)
                return;

            var key = $"{SettingPrefix}{instrumentName.ToUpperInvariant()}_{sig.ToUpperInvariant().Replace(" ", "_")}";
            var now = DateTime.UtcNow;
            List<string> recipients;

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
                if (existing != null
                    && DateTime.TryParse(existing.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last)
                    && (now - last).TotalHours < 24)
                    return;

                recipients = await db.Users
                    .AsNoTracking()
                    .Where(u => u.Email != null && u.Email != "")
                    .Select(u => u.Email!)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }

            var fallback = _config["TrailBlazer:SignalAlertEmail"] ?? _config["Email:AlertTo"];
            if (!string.IsNullOrWhiteSpace(fallback))
                recipients.Add(fallback);
            recipients = recipients
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (recipients.Count == 0)
            {
                _logger.LogDebug("Breakout alert skipped: no recipients with an email address");
                return;
            }

            var subject = $"[{sig}] {instrumentName} — Asset Scanner {score.OverallScore:F1} ({score.Bias})";
            var text = $"{score.TradeSetupDetail}\r\n\r\n"
                     + $"Overall: {score.OverallScore:F1} | Bias: {score.Bias} | "
                     + $"Technical: {score.TechnicalScore:F1} | Fundamental: {score.FundamentalScore:F1}\r\n"
                     + $"Time (UTC): {score.DateComputed:O}";

            var sent = await _email.SendAsync(subject, text, recipients, cancellationToken);
            if (!sent)
            {
                _logger.LogWarning("Breakout alert delivery failed for {Instrument} ({Signal})", instrumentName, sig);
                return;
            }

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
                if (existing != null)
                {
                    existing.Value = now.ToString("o");
                    existing.UpdatedAt = now;
                }
                else
                {
                    db.SystemSettings.Add(new SystemSetting { Key = key, Value = now.ToString("o"), UpdatedAt = now });
                }
                await db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("Breakout alert email sent for {Instrument} ({Signal}) to {RecipientCount} recipients", instrumentName, sig, recipients.Count);
        }
    }
}
