// TradeImage.cs – Trade screenshot/image model
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TradeHelper.Models
{
    public class TradeImage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TradeId { get; set; }

        [Required]
        [MaxLength(20)]
        public string Type { get; set; } = "Entry"; // Entry, Exit

        [Required]
        [MaxLength(500)]
        public string OriginalFileName { get; set; } = string.Empty;

        public byte[]? ImageData { get; set; }

        public long FileSizeBytes { get; set; }

        [MaxLength(100)]
        public string MimeType { get; set; } = "image/jpeg";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("TradeId")]
        public virtual Trade? Trade { get; set; }
    }
}


