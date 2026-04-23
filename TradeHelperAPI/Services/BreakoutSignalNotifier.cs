using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    public interface IBreakoutSignalNotifier
    {
        /// <summary>Queue a strong signal for inclusion in the next consolidated email. Honours the 24h per-(instrument, signal) dedupe: already-alerted signals are silently skipped. Safe to call multiple times per refresh cycle.</summary>
        Task QueueStrongSignalAsync(TrailBlazerScore score, string instrumentName, CancellationToken cancellationToken = default);

        /// <summary>Send ONE consolidated email containing every signal queued since the last flush, then reset the queue and mark those (instrument, signal) keys as alerted in SystemSettings. No-op when nothing was queued.</summary>
        Task FlushConsolidatedAsync(CancellationToken cancellationToken = default);

        /// <summary>Legacy convenience: queue + flush in a single call. Prefer Queue+Flush when processing many instruments in a refresh cycle.</summary>
        Task TryNotifyStrongSignalAsync(TrailBlazerScore score, string instrumentName, CancellationToken cancellationToken = default);
    }

    public class BreakoutSignalNotifier : IBreakoutSignalNotifier
    {
        private const string SettingPrefix = "BreakoutAlertSent_";

        private readonly IConfiguration _config;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IBrevoEmailService _email;
        private readonly ILogger<BreakoutSignalNotifier> _logger;

        private readonly ConcurrentQueue<QueuedSignal> _queue = new();

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

        public async Task QueueStrongSignalAsync(TrailBlazerScore score, string instrumentName, CancellationToken cancellationToken = default)
        {
            var sig = (score.TradeSetupSignal ?? "").Trim();
            var confluenceCount = CountAlignedConfluences(sig, score.TradeSetupDetail);
            if (!IsAlertWorthy(sig) || confluenceCount < 1)
            {
                if (IsAlertWorthy(sig) && confluenceCount < 1)
                    _logger.LogDebug("Breakout alert skipped (<1 confluence): {Instrument} {Signal} ({ConfluenceCount})", instrumentName, sig, confluenceCount);
                return;
            }

            var key = BuildDedupeKey(instrumentName, sig);
            var now = DateTime.UtcNow;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (existing != null
                && DateTime.TryParse(existing.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last)
                && (now - last).TotalHours < 24)
            {
                _logger.LogDebug("Breakout alert skipped (24h dedupe): {Instrument} {Signal}", instrumentName, sig);
                return;
            }

            _queue.Enqueue(new QueuedSignal
            {
                Instrument = instrumentName,
                Signal = sig,
                OverallScore = score.OverallScore,
                TechnicalScore = score.TechnicalScore,
                FundamentalScore = score.FundamentalScore,
                Bias = score.Bias ?? "",
                Detail = score.TradeSetupDetail ?? "",
                DateComputed = score.DateComputed,
                DedupeKey = key
            });
        }

        public async Task FlushConsolidatedAsync(CancellationToken cancellationToken = default)
        {
            var batch = DrainQueue();
            if (batch.Count == 0)
            {
                _logger.LogDebug("Consolidated breakout email skipped: no signals queued");
                return;
            }

            List<string> recipients;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
                _logger.LogWarning("Consolidated breakout email: {Count} signals queued but no recipients with an email address; dedupe NOT updated", batch.Count);
                return;
            }

            var ordered = batch
                .OrderBy(b => TierRank(b.Signal))
                .ThenByDescending(b => b.OverallScore)
                .ToList();

            var (subject, text, html) = BuildConsolidatedEmail(ordered);
            var sent = await _email.SendAsync(subject, text, recipients, cancellationToken, html);
            if (!sent)
            {
                _logger.LogWarning("Consolidated breakout email delivery failed; {Count} signals will be retried on next refresh", batch.Count);
                return;
            }

            var now = DateTime.UtcNow;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                foreach (var item in batch)
                {
                    var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == item.DedupeKey, cancellationToken);
                    if (existing != null)
                    {
                        existing.Value = now.ToString("o");
                        existing.UpdatedAt = now;
                    }
                    else
                    {
                        db.SystemSettings.Add(new SystemSetting { Key = item.DedupeKey, Value = now.ToString("o"), UpdatedAt = now });
                    }
                }
                await db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("Consolidated breakout email sent: {Count} signals to {RecipientCount} recipients ({Summary})",
                batch.Count, recipients.Count, string.Join(", ", batch.Select(b => $"{b.Instrument}:{b.Signal}")));
        }

        public async Task TryNotifyStrongSignalAsync(TrailBlazerScore score, string instrumentName, CancellationToken cancellationToken = default)
        {
            await QueueStrongSignalAsync(score, instrumentName, cancellationToken);
            await FlushConsolidatedAsync(cancellationToken);
        }

        private List<QueuedSignal> DrainQueue()
        {
            var list = new List<QueuedSignal>();
            while (_queue.TryDequeue(out var item))
                list.Add(item);
            return list;
        }

        private bool IsAlertWorthy(string signal)
        {
            var emailReversalBuy = _config.GetValue("TrailBlazer:EmailReversalBuyAlerts", true);
            return string.Equals(signal, "STRONG_BUY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "STRONG_SELL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "STRONG_REVERSAL_BUY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "STRONG_REVERSAL_SELL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "BUY NOW", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "SELL NOW", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "GOLDEN_ZONE_BUY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "GOLDEN_ZONE_SELL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "DOUBLE_CONFLUENCE_BUY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, "DOUBLE_CONFLUENCE_SELL", StringComparison.OrdinalIgnoreCase)
                || (emailReversalBuy && (
                        string.Equals(signal, "REVERSAL_BUY", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(signal, "REVERSAL_SELL", StringComparison.OrdinalIgnoreCase)));
        }

        private static int CountAlignedConfluences(string signal, string? detail)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return 0;

            if (string.Equals(signal, "BUY NOW", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(signal, "SELL NOW", StringComparison.OrdinalIgnoreCase))
                return 3; // Fib touch + horizontal level + trendline confluence zone.

            if (string.Equals(signal, "DOUBLE_CONFLUENCE_BUY", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(signal, "DOUBLE_CONFLUENCE_SELL", StringComparison.OrdinalIgnoreCase))
                return 2; // Horizontal + trendline.

            if (string.Equals(signal, "GOLDEN_ZONE_BUY", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(signal, "GOLDEN_ZONE_SELL", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(detail))
                    return 0;

                var hasHorizontal = detail.Contains("support", StringComparison.OrdinalIgnoreCase)
                    || detail.Contains("resistance", StringComparison.OrdinalIgnoreCase)
                    || detail.Contains("horizontal", StringComparison.OrdinalIgnoreCase);
                var hasTrendline = detail.Contains("trendline", StringComparison.OrdinalIgnoreCase);
                return (hasHorizontal || hasTrendline) ? 2 : 0; // Fib golden zone + at least one confluence.
            }

            var count = 0;
            if (!string.IsNullOrWhiteSpace(detail))
            {
                if (detail.Contains("Fib", StringComparison.OrdinalIgnoreCase))
                    count++;
                if (detail.Contains("support", StringComparison.OrdinalIgnoreCase) ||
                    detail.Contains("resistance", StringComparison.OrdinalIgnoreCase) ||
                    detail.Contains("horizontal", StringComparison.OrdinalIgnoreCase))
                    count++;
                if (detail.Contains("trendline", StringComparison.OrdinalIgnoreCase))
                    count++;
                if (detail.Contains("confluence", StringComparison.OrdinalIgnoreCase))
                    count = Math.Max(count, 2);
            }

            return count;
        }

        private static string BuildDedupeKey(string instrumentName, string signal)
            => $"{SettingPrefix}{instrumentName.ToUpperInvariant()}_{signal.ToUpperInvariant().Replace(" ", "_")}";

        /// <summary>Lower rank = higher priority (sorted first).</summary>
        private static int TierRank(string signal) => signal.ToUpperInvariant() switch
        {
            "BUY NOW" => 0,
            "SELL NOW" => 0,
            "STRONG_REVERSAL_BUY" => 1,
            "STRONG_REVERSAL_SELL" => 1,
            "STRONG_BUY" => 2,
            "STRONG_SELL" => 2,
            "REVERSAL_BUY" => 3,
            "REVERSAL_SELL" => 3,
            _ => 9
        };

        private (string subject, string text, string html) BuildConsolidatedEmail(List<QueuedSignal> ordered)
        {
            var buyNow = ordered.Where(b => string.Equals(b.Signal, "BUY NOW", StringComparison.OrdinalIgnoreCase)).ToList();
            var sellNow = ordered.Where(b => string.Equals(b.Signal, "SELL NOW", StringComparison.OrdinalIgnoreCase)).ToList();
            var other = ordered.Except(buyNow).Except(sellNow).ToList();

            string subject;
            if (buyNow.Count > 0 && sellNow.Count > 0)
                subject = $"TrailBlazer: {buyNow.Count} BUY NOW / {sellNow.Count} SELL NOW + {other.Count} more";
            else if (buyNow.Count > 0)
                subject = $"TrailBlazer: {buyNow.Count} BUY NOW signal{(buyNow.Count == 1 ? "" : "s")}" + (other.Count > 0 ? $" + {other.Count} more" : "");
            else if (sellNow.Count > 0)
                subject = $"TrailBlazer: {sellNow.Count} SELL NOW signal{(sellNow.Count == 1 ? "" : "s")}" + (other.Count > 0 ? $" + {other.Count} more" : "");
            else
                subject = $"TrailBlazer: {ordered.Count} signal{(ordered.Count == 1 ? "" : "s")} this refresh";

            var text = BuildTextBody(buyNow, sellNow, other);
            var html = BuildHtmlBody(buyNow, sellNow, other);
            return (subject, text, html);
        }

        private static string BuildTextBody(List<QueuedSignal> buyNow, List<QueuedSignal> sellNow, List<QueuedSignal> other)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"TrailBlazer Asset Scanner refresh: {buyNow.Count + sellNow.Count + other.Count} signal(s).");
            sb.AppendLine($"Time (UTC): {DateTime.UtcNow:O}");
            sb.AppendLine();

            if (buyNow.Count > 0)
            {
                sb.AppendLine("=== BUY NOW (all confluences aligned) ===");
                foreach (var s in buyNow) AppendTextRow(sb, s);
                sb.AppendLine();
            }
            if (sellNow.Count > 0)
            {
                sb.AppendLine("=== SELL NOW (all confluences aligned) ===");
                foreach (var s in sellNow) AppendTextRow(sb, s);
                sb.AppendLine();
            }
            if (other.Count > 0)
            {
                sb.AppendLine("=== Other high-conviction signals ===");
                foreach (var s in other) AppendTextRow(sb, s);
            }
            return sb.ToString();
        }

        private static void AppendTextRow(StringBuilder sb, QueuedSignal s)
        {
            sb.AppendLine($"- [{s.Signal}] {s.Instrument} | Score {s.OverallScore:F1} | Bias {s.Bias} | Tech {s.TechnicalScore:F1} | Fund {s.FundamentalScore:F1}");
            if (!string.IsNullOrWhiteSpace(s.Detail))
                sb.AppendLine($"    {s.Detail}");
        }

        private static string BuildHtmlBody(List<QueuedSignal> buyNow, List<QueuedSignal> sellNow, List<QueuedSignal> other)
        {
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#111;max-width:760px;margin:0 auto;padding:16px;\">");
            sb.Append($"<h2 style=\"margin:0 0 8px 0;\">TrailBlazer Asset Scanner refresh</h2>");
            sb.Append($"<p style=\"color:#555;margin:0 0 16px 0;font-size:13px;\">{buyNow.Count + sellNow.Count + other.Count} signal(s) &middot; {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>");

            if (buyNow.Count > 0)
                sb.Append(RenderSection("BUY NOW — all confluences aligned", "#15803d", "#dcfce7", buyNow));
            if (sellNow.Count > 0)
                sb.Append(RenderSection("SELL NOW — all confluences aligned", "#b91c1c", "#fee2e2", sellNow));
            if (other.Count > 0)
                sb.Append(RenderSection("Other high-conviction signals", "#1f2937", "#f3f4f6", other));

            sb.Append("<p style=\"color:#777;font-size:12px;margin-top:16px;\">Signals are deduped for 24h per instrument+signal. Open the scanner for live analytics.</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        private static string RenderSection(string title, string headerColor, string bg, List<QueuedSignal> items)
        {
            var sb = new StringBuilder();
            sb.Append($"<h3 style=\"color:{headerColor};margin:16px 0 8px 0;\">{System.Net.WebUtility.HtmlEncode(title)}</h3>");
            sb.Append("<table cellpadding=\"8\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%;border:1px solid #e5e7eb;font-size:13px;\">");
            sb.Append($"<thead><tr style=\"background:{bg};\">")
              .Append("<th align=\"left\">Signal</th>")
              .Append("<th align=\"left\">Instrument</th>")
              .Append("<th align=\"right\">Score</th>")
              .Append("<th align=\"left\">Bias</th>")
              .Append("<th align=\"right\">Tech</th>")
              .Append("<th align=\"right\">Fund</th>")
              .Append("<th align=\"left\">Detail</th>")
              .Append("</tr></thead><tbody>");
            foreach (var s in items)
            {
                sb.Append("<tr style=\"border-top:1px solid #e5e7eb;\">")
                  .Append($"<td><strong>{System.Net.WebUtility.HtmlEncode(s.Signal)}</strong></td>")
                  .Append($"<td>{System.Net.WebUtility.HtmlEncode(s.Instrument)}</td>")
                  .Append($"<td align=\"right\">{s.OverallScore:F1}</td>")
                  .Append($"<td>{System.Net.WebUtility.HtmlEncode(s.Bias)}</td>")
                  .Append($"<td align=\"right\">{s.TechnicalScore:F1}</td>")
                  .Append($"<td align=\"right\">{s.FundamentalScore:F1}</td>")
                  .Append($"<td style=\"color:#374151;\">{System.Net.WebUtility.HtmlEncode(s.Detail)}</td>")
                  .Append("</tr>");
            }
            sb.Append("</tbody></table>");
            return sb.ToString();
        }

        private sealed class QueuedSignal
        {
            public string Instrument { get; init; } = "";
            public string Signal { get; init; } = "";
            public double OverallScore { get; init; }
            public double TechnicalScore { get; init; }
            public double FundamentalScore { get; init; }
            public string Bias { get; init; } = "";
            public string Detail { get; init; } = "";
            public DateTime DateComputed { get; init; }
            public string DedupeKey { get; init; } = "";
        }
    }
}
