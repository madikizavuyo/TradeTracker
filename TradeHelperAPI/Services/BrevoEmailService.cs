using System.Net;
using System.Net.Mail;

namespace TradeHelper.Services
{
    public interface IBrevoEmailService
    {
        /// <summary>Sends a transactional email via Brevo SMTP relay.</summary>
        Task<bool> SendAsync(
            string subject,
            string textBody,
            IReadOnlyCollection<string> recipients,
            CancellationToken cancellationToken = default,
            string? htmlBody = null);
    }

    public sealed class BrevoEmailService : IBrevoEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<BrevoEmailService> _logger;

        public BrevoEmailService(IConfiguration config, ILogger<BrevoEmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<bool> SendAsync(
            string subject,
            string textBody,
            IReadOnlyCollection<string> recipients,
            CancellationToken cancellationToken = default,
            string? htmlBody = null)
        {
            if (recipients == null || recipients.Count == 0)
            {
                _logger.LogDebug("Brevo SMTP send skipped: no recipients");
                return false;
            }

            var fromEmail = _config["Email:From"];
            var fromName = _config["Email:FromName"] ?? "TrailBlazer";
            var host = _config["Email:SmtpHost"];
            var user = _config["Email:Username"];
            var pass = _config["Email:Password"];
            if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(host))
            {
                _logger.LogWarning("Brevo SMTP send skipped: Email:From or Email:SmtpHost not configured");
                return false;
            }

            var deduped = recipients
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (deduped.Count == 0) return false;

            var port = _config.GetValue("Email:Port", 587);
            var enableSsl = _config.GetValue("Email:EnableSsl", true);

            try
            {
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    Credentials = !string.IsNullOrEmpty(user) ? new NetworkCredential(user, pass) : null
                };
                using var msg = new MailMessage
                {
                    From = new MailAddress(fromEmail, fromName),
                    Subject = subject,
                    Body = htmlBody ?? textBody,
                    IsBodyHtml = htmlBody != null
                };
                msg.To.Add(new MailAddress(fromEmail, fromName));
                foreach (var r in deduped)
                {
                    if (string.Equals(r, fromEmail, StringComparison.OrdinalIgnoreCase)) continue;
                    msg.Bcc.Add(new MailAddress(r));
                }

                await client.SendMailAsync(msg, cancellationToken);
                _logger.LogInformation("Brevo SMTP send OK ({Recipients} recipients via {Host}:{Port})", deduped.Count, host, port);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Brevo SMTP send failed ({Host}:{Port})", host, port);
                return false;
            }
        }
    }
}
