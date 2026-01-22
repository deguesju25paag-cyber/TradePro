using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace TradePro.Data.Migrations
{
    [DbContext(typeof(TradePro.Data.ApplicationDbContext))]
    [Migration("20260101000000_InitialCreate")]
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            // Seed admin user with bcrypt-hashed password 'admin'
            var pwd = BCrypt.Net.BCrypt.HashPassword("admin");

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Username", "PasswordHash", "Balance" },
                values: new object[] { 1, "admin", pwd, 100000m });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
