using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zerbitzaria.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionTradeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TradeId",
                table: "Positions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Positions",
                keyColumn: "Id",
                keyValue: 1,
                column: "TradeId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$bkpIc/TVZbycadhy9dXipeuSDHHR4nTtTStjCCvycpInOABf2pGT6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TradeId",
                table: "Positions");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$azzpzF.PYHypYS9DGAYIauUdUoipmUdciyQOmzNyMMsXwdU5wdTbe");
        }
    }
}
