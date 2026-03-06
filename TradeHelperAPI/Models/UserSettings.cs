using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TradeHelper.Models
{
    public class UserSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = "";

        [Required]
        [MaxLength(10)]
        public string DisplayCurrency { get; set; } = "USD";

        [Required]
        [MaxLength(10)]
        public string DisplayCurrencySymbol { get; set; } = "$";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
