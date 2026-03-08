using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCryptoAndZarInstruments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Crypto
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 143, "Crypto", "BTC", "Commodity" });
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 144, "Crypto", "ETH", "Commodity" });
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 145, "Crypto", "SOL", "Commodity" });
            // ZAR pairs (USDZAR already exists as 141)
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 146, "ForexMinor", "EURZAR", "Currency" });
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 147, "ForexMinor", "GBPZAR", "Currency" });
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 148, "ForexMinor", "AUDZAR", "Currency" });
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 149, "ForexMinor", "NZDZAR", "Currency" });
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 150, "ForexMinor", "CADZAR", "Currency" });
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 151, "ForexMinor", "CHFZAR", "Currency" });
            migrationBuilder.InsertData(table: "Instruments", columns: new[] { "Id", "AssetClass", "Name", "Type" }, values: new object[] { 152, "ForexMinor", "JPYZAR", "Currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            for (int id = 152; id >= 143; id--)
                migrationBuilder.DeleteData(table: "Instruments", keyColumn: "Id", keyValue: id);
        }
    }
}
