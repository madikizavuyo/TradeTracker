// Instrument.cs – Represents a currency or commodity
namespace TradeHelper.Models
{
    public class Instrument
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public required string Type { get; set; } // "Currency" or "Commodity"
    }
}