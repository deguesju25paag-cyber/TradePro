using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace TradePro.Data.Migrations
{
    [DbContext(typeof(TradePro.Data.ApplicationDbContext))]
    [Migration("20260102000000_AddMarketPositionTrade")]
    public partial class AddMarketPositionTrade : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Markets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: true),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    Change = table.Column<double>(type: "REAL", nullable: false),
                    IsUp = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Markets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: true),
                    Side = table.Column<string>(type: "TEXT", nullable: true),
                    Leverage = table.Column<int>(type: "INTEGER", nullable: false),
                    Margin = table.Column<decimal>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: true),
                    Side = table.Column<string>(type: "TEXT", nullable: true),
                    Pnl = table.Column<decimal>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            // seed demo data
            migrationBuilder.InsertData(
                table: "Markets",
                columns: new[] { "Id", "Symbol", "Price", "Change", "IsUp" },
                values: new object[] { 1, "BTC", 42123.45m, 2.1, true });

            migrationBuilder.InsertData(
                table: "Markets",
                columns: new[] { "Id", "Symbol", "Price", "Change", "IsUp" },
                values: new object[] { 2, "ETH", 3210.12m, 1.8, true });

            migrationBuilder.InsertData(
                table: "Markets",
                columns: new[] { "Id", "Symbol", "Price", "Change", "IsUp" },
                values: new object[] { 3, "SOL", 98.45m, -0.5, false });

            migrationBuilder.InsertData(
                table: "Markets",
                columns: new[] { "Id", "Symbol", "Price", "Change", "IsUp" },
                values: new object[] { 4, "XRP", 0.78m, 0.9, true });

            migrationBuilder.InsertData(
                table: "Positions",
                columns: new[] { "Id", "Symbol", "Side", "Leverage", "Margin", "UserId" },
                values: new object[] { 1, "DOGE", "LONG", 63, 12m, 1 });

            migrationBuilder.InsertData(
                table: "Trades",
                columns: new[] { "Id", "Symbol", "Side", "Pnl", "Timestamp", "UserId" },
                values: new object[] { 1, "BTCUSD", "LONG", 1771827.25m, new DateTime(2026, 1, 2), 1 });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Markets");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "Trades");
        }
    }
}
