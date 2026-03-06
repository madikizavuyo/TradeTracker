namespace TradeHelper.Models
{
    /// <summary>Stores technical indicator values from Alpha Vantage (RSI, SMA, etc.) per instrument per date.</summary>
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
        public DateTime DateCollected { get; set; }

        public Instrument? Instrument { get; set; }
    }
}
