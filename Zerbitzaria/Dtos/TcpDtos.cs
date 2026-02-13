namespace Zerbitzaria.Dtos
{
    public record LoginResponseDto(string Username, decimal Balance, int UserId);
    public record RegisterResponseDto(string Message);
    public record ErrorResponseDto(string Error, string Message);
    public record MarketDto(string Symbol, decimal Price, double Change, bool IsUp);

    public record UserProfileDto(string Username, decimal Balance, int Id);

    public record TradeDto(int Id, string Symbol, string Side, decimal Pnl, decimal EntryPrice, decimal Margin, int Leverage, decimal Quantity, bool IsOpen, System.DateTime Timestamp, int UserId);

    public record PositionDto(int Id, string Symbol, string Side, int Leverage, decimal Margin, decimal EntryPrice, decimal Quantity, bool IsOpen, int UserId, int? TradeId);

    public record DashboardDto(decimal Balance, System.Collections.Generic.List<MarketDto> Markets, System.Collections.Generic.List<PositionDto> Positions);

    public record CloseTradeResultDto(TradeDto Trade, decimal CurrentPrice, decimal Pnl);

    public record GenericMessageDto(string Message);
}