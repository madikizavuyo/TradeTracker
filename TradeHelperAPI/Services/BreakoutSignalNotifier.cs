using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    public interface IBreakoutSignalNotifier
    {
        /// <summary>Sends at most one email per instrument per 24h for STRONG_BUY / STRONG_SELL when SMTP is configured.</summary>
        Task TryNotifyStrongSignalAsync(TrailBlazerScore score, string instrumentName, CancellationToken cancellationToken = default);
    }

    public class BreakoutSignalNotifier : IBreakoutSignalNotifier
    {
        private const string SettingPrefix = "BreakoutAlertSent_";

        private readonly IConfiguration _config;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BreakoutSignalNotifier> _logger;

        public BreakoutSignalNotifier(IConfiguration config, IServiceScopeFactory scopeFactory, ILogger<BreakoutSignalNotifier> logger)
        {
            _config = config;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task TryNotifyStrongSignalAsync(TrailBlazerScore score, string instrumentName, CancellationToken cancellationToken = default)
        {
            var sig = score.TradeSetupSignal ?? "";
            if (!string.Equals(sig, "STRONG_BUY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sig, "STRONG_SELL", StringComparison.OrdinalIgnoreCase))
                return;

            var to = _config["TrailBlazer:SignalAlertEmail"] ?? _config["Email:AlertTo"];
            if (string.IsNullOrWhiteSpace(to))
            {
                _logger.LogDebug("Breakout alert skipped: TrailBlazer:SignalAlertEmail / Email:AlertTo not set");
                return;
            }

            var host = _config["Email:SmtpHost"];
            var user = _config["Email:Username"];
            var pass = _config["Email:Password"];
            var from = _config["Email:From"] ?? user;
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(from))
            {
                _logger.LogDebug("Breakout alert skipped: Email SMTP not configured");
                return;
            }

            var key = SettingPrefix + instrumentName.ToUpperInvariant();
            var now = DateTime.UtcNow;

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
                if (existing != null && DateTime.TryParse(existing.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last) &&
                    (now - last).TotalHours < 24)
                    return;
            }

            var port = _config.GetValue("Email:Port", 587);
            var enableSsl = _config.GetValue("Email:EnableSsl", true);

            try
            {
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    Credentials = !string.IsNullOrEmpty(user) ? new NetworkCredential(user, pass) : null
                };
                var subject = $"[{sig}] {instrumentName} — Asset Scanner {score.OverallScore:F1} ({score.Bias})";
                var body = $"{score.TradeSetupDetail}\r\n\r\nOverall: {score.OverallScore:F1} | Bias: {score.Bias} | Technical: {score.TechnicalScore:F1} | Fundamental: {score.FundamentalScore:F1}\r\nTime (UTC): {score.DateComputed:O}";
                using var msg = new MailMessage(from, to, subject, body);
                await client.SendMailAsync(msg, cancellationToken);

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
                        db.SystemSettings.Add(new SystemSetting { Key = key, Value = now.ToString("o"), UpdatedAt = now });
                    await db.SaveChangesAsync(cancellationToken);
                }

                _logger.LogInformation("Breakout alert email sent for {Instrument} ({Signal})", instrumentName, sig);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Breakout alert email failed for {Instrument}", instrumentName);
            }
        }
    }
}
