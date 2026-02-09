using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zerbitzaria.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$azzpzF.PYHypYS9DGAYIauUdUoipmUdciyQOmzNyMMsXwdU5wdTbe");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$xc.smXeoQAItIhi8v.oh8efTbcXepiDZ8vt5cTdl.OLPKij23ZCNa");
        }
    }
}
