// Trade.cs – Trading transaction model
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TradeHelper.Models
{
    public class Trade
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        public int? StrategyId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Instrument { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18,5)")]
        public decimal EntryPrice { get; set; }

        [Column(TypeName = "decimal(18,5)")]
        public decimal? ExitPrice { get; set; }

        [Column(TypeName = "decimal(18,5)")]
        public decimal? StopLoss { get; set; }

        [Column(TypeName = "decimal(18,5)")]
        public decimal? TakeProfit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ProfitLoss { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ProfitLossDisplay { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? RiskReward { get; set; }

        [Required]
        public DateTime DateTime { get; set; }

        public DateTime? ExitDateTime { get; set; }

        [MaxLength(5000)]
        public string? Notes { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Open"; // Open, Closed, Cancelled

        [Required]
        [MaxLength(10)]
        public string Type { get; set; } = "Long"; // Long, Short

        [Column(TypeName = "decimal(18,2)")]
        public decimal? LotSize { get; set; }

        [MaxLength(100)]
        public string? Broker { get; set; }

        [Required]
        [MaxLength(10)]
        public string Currency { get; set; } = "USD";

        [MaxLength(10)]
        public string? DisplayCurrency { get; set; }

        [MaxLength(10)]
        public string? DisplayCurrencySymbol { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("StrategyId")]
        public virtual Strategy? Strategy { get; set; }

        public virtual ICollection<TradeImage>? TradeImages { get; set; }
    }
}


