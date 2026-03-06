using System.ComponentModel.DataAnnotations;

namespace TradeHelper.Models
{
    /// <summary>Stores technical indicator values (RSI, SMA, EMA) per instrument per date. DateCollected = when a successful pull happened.</summary>
    public class TechnicalIndicator
    {
        public int Id { get; set; }
        public int InstrumentId { get; set; }
        public DateTime Date { get; set; }
        public double? RSI { get; set; }
        public double? SMA14 { get; set; }
        public double? SMA50 { get; set; }
        public double? EMA50 { get; set; }
        public double? EMA200 { get; set; }
        /// <summary>When a successful data pull happened.</summary>
        public DateTime DateCollected { get; set; }
        /// <summary>Provider that supplied the data (TwelveData, MarketStack, iTick, etc.).</summary>
        [MaxLength(50)]
        public string? Source { get; set; }

        public Instrument? Instrument { get; set; }
    }
}
