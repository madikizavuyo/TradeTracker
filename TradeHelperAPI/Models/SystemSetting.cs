namespace TradeHelper.Models
{
    /// <summary>Key-value store for system-wide settings (e.g. MyFXBook session).</summary>
    public class SystemSetting
    {
        public int Id { get; set; }
        public required string Key { get; set; }
        public string? Value { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
