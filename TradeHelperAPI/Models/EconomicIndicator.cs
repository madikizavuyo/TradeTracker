namespace EconomicDataFetcher.Models
{
    public class EconomicIndicator
    {
        public required string IndicatorName { get; set; }
        public required string Value { get; set; }
        public required string Source { get; set; }
        public required string Country { get; set; }
        public DateTime DateCollected { get; set; }
    }
}