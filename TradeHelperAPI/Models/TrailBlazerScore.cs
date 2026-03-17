using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TradeHelper.Models
{
    public class TrailBlazerScore
    {
        [Key]
        public int Id { get; set; }

        public int InstrumentId { get; set; }

        [ForeignKey("InstrumentId")]
        public virtual Instrument? Instrument { get; set; }

        public double OverallScore { get; set; }

        [MaxLength(20)]
        public string Bias { get; set; } = "Neutral";

        public double FundamentalScore { get; set; }
        public double SentimentScore { get; set; }
        public double TechnicalScore { get; set; }
        public double COTScore { get; set; }
        public double RetailSentimentScore { get; set; }
        public double NewsSentimentScore { get; set; } = 5;
        public double RetailLongPct { get; set; } = 50;
        public double RetailShortPct { get; set; } = 50;
        public double EconomicScore { get; set; }

        /// <summary>Currency strength component (80% news analysis + 20% fundamentals). Null when N/A (not included in calculations).</summary>
        public double? CurrencyStrengthScore { get; set; }

        [MaxLength(2000)]
        public string? DataSources { get; set; }

        public DateTime DateComputed { get; set; } = DateTime.UtcNow;

        /// <summary>When technical data was last successfully pulled (from TechnicalIndicators.DateCollected).</summary>
        public DateTime? TechnicalDataDateCollected { get; set; }
    }
}
