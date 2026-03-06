#nullable disable
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Logging
{
    /// <summary>
    /// Logging provider that persists ILogger output to the ApplicationLogs table.
    /// Uses a background processor to batch writes and avoid blocking.
    /// </summary>
    public class DbLoggerProvider : ILoggerProvider
    {
        private readonly IServiceProvider _provider;
        private readonly IConfiguration _config;
        private readonly ConcurrentDictionary<string, DbLogger> _loggers = new();
        private readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        private readonly Task _processor;
        private readonly CancellationTokenSource _cts = new();

        public DbLoggerProvider(IServiceProvider provider, IConfiguration config)
        {
            _provider = provider;
            _config = config;
            _processor = ProcessLogQueueAsync(_cts.Token);
        }

        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(categoryName, name => new DbLogger(name, _channel.Writer));

        public void Dispose()
        {
            _channel.Writer.Complete();
            _cts.Cancel();
            _processor.GetAwaiter().GetResult();
            _cts.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task ProcessLogQueueAsync(CancellationToken ct)
        {
            var batch = new List<LogEntry>(64);
            var batchInterval = TimeSpan.FromMilliseconds(500);
            var lastFlush = DateTime.UtcNow;

            await foreach (var entry in _channel.Reader.ReadAllAsync(ct))
            {
                batch.Add(entry);

                if (batch.Count >= 50 || DateTime.UtcNow - lastFlush >= batchInterval)
                {
                    await FlushBatchAsync(batch);
                    batch.Clear();
                    lastFlush = DateTime.UtcNow;
                }
            }

            if (batch.Count > 0)
                await FlushBatchAsync(batch);
        }

        private async Task FlushBatchAsync(List<LogEntry> batch)
        {
            if (batch.Count == 0) return;

            try
            {
                using var scope = _provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var minLevel = _config.GetValue("Logging:Database:MinLevel", "Debug");
                var minLevelOrdinal = GetLevelOrdinal(minLevel);

                var entities = batch
                    .Where(e => GetLevelOrdinal(e.Level) >= minLevelOrdinal)
                    .Select(e => new ApplicationLog
                    {
                        Timestamp = e.Timestamp,
                        Level = e.Level,
                        Category = e.Category,
                        Message = Truncate(e.Message, 4000),
                        Exception = e.Exception != null ? Truncate(e.Exception, 2000) : (string)null
                    })
                    .ToList();

                if (entities.Count > 0)
                {
                    db.ApplicationLogs.AddRange(entities);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                // Avoid recursive logging; swallow to prevent crash
            }
        }

        private static int GetLevelOrdinal(string level) => level switch
        {
            "Trace" => 0,
            "Debug" => 1,
            "Information" => 2,
            "Warning" => 3,
            "Error" => 4,
            "Critical" => 5,
            _ => 1
        };

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Length <= max ? s : s.Substring(0, max);
    }

    internal sealed class LogEntry
    {
        public DateTime Timestamp { get; init; }
        public string Level { get; init; } = "";
        public string Category { get; init; } = "";
        public string Message { get; init; } = "";
        public string Exception { get; init; }
    }
}
