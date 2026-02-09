using System.ComponentModel.DataAnnotations;

namespace TradePro.Models
{
    public class Position
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty; // LONG/SHORT
        public int Leverage { get; set; }
        public decimal Margin { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal Quantity { get; set; }
        public bool IsOpen { get; set; } = true;
        public int UserId { get; set; }

        // optional link to server trade id
        public int? TradeId { get; set; }
    }
}