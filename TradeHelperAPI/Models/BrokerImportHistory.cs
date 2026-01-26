// BrokerImportHistory.cs – Import history tracking
using System;
using System.ComponentModel.DataAnnotations;

namespace TradeHelper.Models
{
    public class BrokerImportHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string BrokerName { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string OriginalFileName { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? FilePath { get; set; }

        public int TradesImported { get; set; } = 0;

        public int TradesSkipped { get; set; } = 0;

        public int TradesFailed { get; set; } = 0;

        [MaxLength(5000)]
        public string? ImportNotes { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending"; // Pending, Processing, InProgress, PartiallyCompleted, Completed, Failed

        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }
    }
}


