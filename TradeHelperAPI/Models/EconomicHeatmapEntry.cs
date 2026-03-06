using System.ComponentModel.DataAnnotations;

namespace TradeHelper.Models
{
    public class EconomicHeatmapEntry
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Currency { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Indicator { get; set; } = string.Empty;

        public double Value { get; set; }
        public double PreviousValue { get; set; }

        [MaxLength(20)]
        public string Impact { get; set; } = "Neutral";

        public DateTime DateCollected { get; set; } = DateTime.UtcNow;
    }
}
