using System;
using System.ComponentModel.DataAnnotations;

namespace TradePro.Models
{
    public class Trade
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty; // LONG/SHORT
        public decimal Pnl { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal Margin { get; set; }
        public int Leverage { get; set; }
        public decimal Quantity { get; set; }
        public bool IsOpen { get; set; } = true;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int UserId { get; set; }
    }
}