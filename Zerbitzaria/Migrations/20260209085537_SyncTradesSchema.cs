using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zerbitzaria.Migrations
{
    /// <inheritdoc />
    public partial class SyncTradesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Markets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
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
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    Leverage = table.Column<int>(type: "INTEGER", nullable: false),
                    Margin = table.Column<decimal>(type: "TEXT", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsOpen = table.Column<bool>(type: "INTEGER", nullable: false),
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
                    Symbol = table.Column<string>(type: "TEXT", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    Pnl = table.Column<decimal>(type: "TEXT", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Margin = table.Column<decimal>(type: "TEXT", nullable: false),
                    Leverage = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsOpen = table.Column<bool>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Markets",
                columns: new[] { "Id", "Change", "IsUp", "Price", "Symbol" },
                values: new object[,]
                {
                    { 1, 2.1000000000000001, true, 42123.45m, "BTC" },
                    { 2, 1.8, true, 3210.12m, "ETH" },
                    { 3, -0.5, false, 98.45m, "SOL" },
                    { 4, 0.90000000000000002, true, 0.78m, "XRP" }
                });

            migrationBuilder.InsertData(
                table: "Positions",
                columns: new[] { "Id", "EntryPrice", "IsOpen", "Leverage", "Margin", "Quantity", "Side", "Symbol", "UserId" },
                values: new object[] { 1, 0m, true, 63, 12m, 0m, "LONG", "DOGE", 1 });

            migrationBuilder.InsertData(
                table: "Trades",
                columns: new[] { "Id", "EntryPrice", "IsOpen", "Leverage", "Margin", "Pnl", "Quantity", "Side", "Symbol", "Timestamp", "UserId" },
                values: new object[] { 1, 42123.45m, false, 1, 100m, 1771827.25m, 100m, "LONG", "BTCUSD", new DateTime(2026, 1, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), 1 });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Balance", "PasswordHash", "Username" },
                values: new object[] { 1, 100000m, "$2a$11$xc.smXeoQAItIhi8v.oh8efTbcXepiDZ8vt5cTdl.OLPKij23ZCNa", "admin" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Markets");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
