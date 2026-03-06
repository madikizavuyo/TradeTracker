using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TradeHelper.Models
{
    /// <summary>Stores retail sentiment (long/short %) per instrument per date. Enables rollback if TrailBlazerScores are wiped.</summary>
    public class RetailSentimentSnapshot
    {
        [Key]
        public int Id { get; set; }

        public int InstrumentId { get; set; }

        [ForeignKey("InstrumentId")]
        public virtual Instrument? Instrument { get; set; }

        public double LongPct { get; set; }
        public double ShortPct { get; set; }

        public DateTime DateCollected { get; set; } = DateTime.UtcNow;
    }
}
