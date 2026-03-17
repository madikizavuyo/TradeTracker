using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUsdCcyInstruments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // USD pairs that have COT data but were missing from Instruments (USDZAR exists as id 141)
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 153, "ForexMajor", "USDMXN", "Currency" });
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 154, "ForexMinor", "USDBRL", "Currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "Instruments", keyColumn: "Id", keyValue: 153);
            migrationBuilder.DeleteData(table: "Instruments", keyColumn: "Id", keyValue: 154);
        }
    }
}
