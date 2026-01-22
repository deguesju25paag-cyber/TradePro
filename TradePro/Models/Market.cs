using System.ComponentModel.DataAnnotations;

namespace TradePro.Models
{
    public class Market
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; }
        public decimal Price { get; set; }
        public double Change { get; set; }
        public bool IsUp { get; set; }

        public string ChangeString => Change.ToString("0.##") + "%";

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
