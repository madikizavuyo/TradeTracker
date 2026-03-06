#nullable disable
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace TradeHelper.Logging
{
    internal sealed class DbLogger : ILogger
    {
        private readonly string _category;
        private readonly ChannelWriter<LogEntry> _writer;

        public DbLogger(string category, ChannelWriter<LogEntry> writer)
        {
            _category = category;
            _writer = writer;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var level = logLevel.ToString();
            var exStr = exception?.ToString();

            var entry = new LogEntry { Timestamp = System.DateTime.UtcNow, Level = level, Category = _category, Message = message, Exception = exStr };
            _writer.TryWrite(entry);
        }
    }
}
