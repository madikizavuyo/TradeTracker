using System.ComponentModel.DataAnnotations;

namespace TradeHelper.Models
{
    /// <summary>Stores news articles fetched for instruments. Used for display and rollback.</summary>
    public class NewsArticle
    {
        public const int MaxSymbolLength = 20;
        public const int MaxHeadlineLength = 500;
        public const int MaxSummaryLength = 2000;
        public const int MaxSourceLength = 200;
        /// <summary>Google News RSS and others can exceed 1k+ characters; match DB.</summary>
        public const int MaxUrlLength = 4000;
        public const int MaxImageUrlLength = 500;

        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(MaxSymbolLength)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        [MaxLength(MaxHeadlineLength)]
        public string Headline { get; set; } = string.Empty;

        [MaxLength(MaxSummaryLength)]
        public string Summary { get; set; } = string.Empty;

        [MaxLength(MaxSourceLength)]
        public string Source { get; set; } = string.Empty;

        [MaxLength(MaxUrlLength)]
        public string Url { get; set; } = string.Empty;

        [MaxLength(MaxImageUrlLength)]
        public string ImageUrl { get; set; } = string.Empty;

        public DateTime PublishedAt { get; set; }
        public DateTime DateCollected { get; set; } = DateTime.UtcNow;

        /// <summary>Per-field trim for SQL storage (Google News URLs are often 1–3k+ chars).</summary>
        public static string TruncateTo(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
