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
        /// <summary>MACD line (EMA12 - EMA26). Used for crossover scoring.</summary>
        public double? MACD { get; set; }
        /// <summary>MACD signal line (EMA9 of MACD). Used for crossover scoring.</summary>
        public double? MACDSignal { get; set; }
        /// <summary>Stochastic %K (0-100). Oversold &lt;20, overbought &gt;80.</summary>
        public double? StochasticK { get; set; }
        /// <summary>Latest close price for price vs EMA200 comparison. Used for bearish signal when price &lt; EMA200.</summary>
        public double? Close { get; set; }
        /// <summary>When a successful data pull happened.</summary>
        public DateTime DateCollected { get; set; }
        /// <summary>Provider that supplied the data (TwelveData, MarketStack, iTick, etc.).</summary>
        [MaxLength(50)]
        public string? Source { get; set; }

        public Instrument? Instrument { get; set; }
    }
}
