namespace TradeHelper.Models
{
    public class Instrument
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public required string Type { get; set; } // "Currency" or "Commodity"
        public string? AssetClass { get; set; } // "ForexMajor", "ForexMinor", "Index", "Commodity", "Metal", "Bond"
    }
}