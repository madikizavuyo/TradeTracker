using System.ComponentModel.DataAnnotations;

namespace TradeHelper.Models
{
    /// <summary>CFTC Commitment of Traders data scraped from https://www.cftc.gov/dea/options/financial_lof.htm</summary>
    public class COTReport
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Symbol { get; set; } = string.Empty;

        public long CommercialLong { get; set; }
        public long CommercialShort { get; set; }
        public long NonCommercialLong { get; set; }
        public long NonCommercialShort { get; set; }
        public long OpenInterest { get; set; }

        public DateTime ReportDate { get; set; }
    }
}
