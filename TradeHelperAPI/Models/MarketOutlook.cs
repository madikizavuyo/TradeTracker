using System.ComponentModel.DataAnnotations;

namespace TradeHelper.Models
{
    /// <summary>Stores market outlook/forecast snippets fetched for instruments. Used for display and rollback.</summary>
    public class MarketOutlook
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Source { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Url { get; set; } = string.Empty;

        public DateTime DateCollected { get; set; } = DateTime.UtcNow;
    }
}
