using System.ComponentModel.DataAnnotations;

namespace Zerbitzaria.Models
{
    public class Market
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public double Change { get; set; }
        public bool IsUp { get; set; }

        public Market() { }
        public Market(string symbol, decimal price, double change, bool isUp)
        {
            Symbol = symbol;
            Price = price;
            Change = change;
            IsUp = isUp;
        }
    }
}