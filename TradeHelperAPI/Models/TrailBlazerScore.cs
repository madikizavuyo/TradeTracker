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

        [MaxLength(2000)]
        public string? DataSources { get; set; }

        public DateTime DateComputed { get; set; } = DateTime.UtcNow;
    }
}
