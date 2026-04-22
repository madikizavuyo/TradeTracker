using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    /// <summary>Emails when a currency strength score crosses the neutral threshold (default 5).</summary>
    public interface ICurrencyStrengthAlertNotifier
    {
        Task TryNotifyCrossThresholdAsync(IReadOnlyDictionary<string, double> currencyStrength, CancellationToken cancellationToken = default);
    }

    public sealed class CurrencyStrengthAlertNotifier : ICurrencyStrengthAlertNotifier
    {
        private const string SnapshotKey = "CurrencyStrengthAlertSnapshot";

        private readonly IConfiguration _config;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CurrencyStrengthAlertNotifier> _logger;

        public CurrencyStrengthAlertNotifier(IConfiguration config, IServiceScopeFactory scopeFactory, ILogger<CurrencyStrengthAlertNotifier> logger)
        {
            _config = config;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task TryNotifyCrossThresholdAsync(IReadOnlyDictionary<string, double> currencyStrength, CancellationToken cancellationToken = default)
        {
            if (currencyStrength == null || currencyStrength.Count == 0)
                return;

            var threshold = _config.GetValue("TrailBlazer:CurrencyCrossThreshold", 5.0);
            var alertEnabled = _config.GetValue("TrailBlazer:CurrencyCrossAlertEnabled", false);

            Dictionary<string, double> previous;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var row = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SnapshotKey, cancellationToken);
                previous = ParseSnapshot(row?.Value);
            }

            var lines = new List<string>();
            if (alertEnabled)
            {
                foreach (var kv in currencyStrength.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var ccy = kv.Key;
                    var now = kv.Value;
                    if (!previous.TryGetValue(ccy, out var prev))
                        continue;
                    if (double.IsNaN(now) || double.IsNaN(prev))
                        continue;
                    if ((prev - threshold) * (now - threshold) < 0)
                        lines.Add($"{ccy}: {prev:F2} → {now:F2} (threshold {threshold:F1})");
                }
            }

            var snapshotJson = JsonSerializer.Serialize(currencyStrength.OrderBy(k => k.Key).ToDictionary(k => k.Key, k => k.Value));
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SnapshotKey, cancellationToken);
                if (row != null)
                {
                    row.Value = snapshotJson;
                    row.UpdatedAt = DateTime.UtcNow;
                }
                else
                    db.SystemSettings.Add(new SystemSetting { Key = SnapshotKey, Value = snapshotJson, UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync(cancellationToken);
            }

            if (!alertEnabled || lines.Count == 0)
                return;

            var to = _config["TrailBlazer:SignalAlertEmail"] ?? _config["Email:AlertTo"];
            if (string.IsNullOrWhiteSpace(to))
            {
                _logger.LogDebug("Currency cross alert skipped: TrailBlazer:SignalAlertEmail / Email:AlertTo not set");
                return;
            }

            var host = _config["Email:SmtpHost"];
            var user = _config["Email:Username"];
            var pass = _config["Email:Password"];
            var from = _config["Email:From"] ?? user;
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(from))
            {
                _logger.LogDebug("Currency cross alert skipped: Email SMTP not configured");
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
                var subject = $"Currency strength crossed {threshold:F1} ({lines.Count} currencies)";
                var body = "Combined currency strength (news + fundamentals). Values moved across the neutral threshold.\r\n\r\n" + string.Join("\r\n", lines) + $"\r\n\r\nTime (UTC): {DateTime.UtcNow:O}";
                using var msg = new MailMessage(from, to, subject, body);
                await client.SendMailAsync(msg, cancellationToken);
                _logger.LogInformation("Currency cross alert email sent ({Count} crossings)", lines.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Currency cross alert email failed");
            }
        }

        private static Dictionary<string, double> ParseSnapshot(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
                return d != null
                    ? new Dictionary<string, double>(d, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
