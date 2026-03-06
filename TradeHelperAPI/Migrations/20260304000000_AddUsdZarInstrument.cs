using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUsdZarInstrument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Instruments",
                columns: new[] { "Id", "AssetClass", "Name", "Type" },
                values: new object[] { 141, "ForexMinor", "USDZAR", "Currency" });

            migrationBuilder.InsertData(
                table: "Instruments",
                columns: new[] { "Id", "AssetClass", "Name", "Type" },
                values: new object[] { 142, "ForexMinor", "USDCNY", "Currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 142);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 141);
        }
    }
}
