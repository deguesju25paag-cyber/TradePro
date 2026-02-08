namespace Zerbitzaria.Dtos
{
    public record OpenTradeDto(string Symbol, string Side, decimal Margin, int Leverage, decimal? EntryPrice);
}
