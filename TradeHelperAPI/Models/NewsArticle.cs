using System.ComponentModel.DataAnnotations;

namespace TradeHelper.Models
{
    /// <summary>Stores news articles fetched for instruments. Used for display and rollback.</summary>
    public class NewsArticle
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string Headline { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Summary { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Source { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Url { get; set; } = string.Empty;

        [MaxLength(500)]
        public string ImageUrl { get; set; } = string.Empty;

        public DateTime PublishedAt { get; set; }
        public DateTime DateCollected { get; set; } = DateTime.UtcNow;
    }
}
