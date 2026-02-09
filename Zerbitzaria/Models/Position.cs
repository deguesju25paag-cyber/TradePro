using System.ComponentModel.DataAnnotations;

namespace Zerbitzaria.Models
{
    public class Position
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public int Leverage { get; set; }
        public decimal Margin { get; set; }
        // Entry price at the time the position was opened
        public decimal EntryPrice { get; set; }
        // Quantity/size can be derived as Margin * Leverage, but store optionally
        public decimal Quantity { get; set; }
        // Whether the position is still open
        public bool IsOpen { get; set; } = true;
        public int UserId { get; set; }

        // Link back to the Trade entity when created
        public int? TradeId { get; set; }
    }
}