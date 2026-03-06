namespace TradeHelper.Models
{
    /// <summary>
    /// Stores ILogger output (LogInformation, LogWarning, etc.) for persistence and querying.
    /// </summary>
    public class ApplicationLog
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;  // Information, Warning, Error, Debug
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
    }
}
