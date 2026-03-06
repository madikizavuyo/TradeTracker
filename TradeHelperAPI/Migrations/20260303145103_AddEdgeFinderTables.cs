using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeFinderTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetClass",
                table: "Instruments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "COTReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CommercialLong = table.Column<long>(type: "bigint", nullable: false),
                    CommercialShort = table.Column<long>(type: "bigint", nullable: false),
                    NonCommercialLong = table.Column<long>(type: "bigint", nullable: false),
                    NonCommercialShort = table.Column<long>(type: "bigint", nullable: false),
                    OpenInterest = table.Column<long>(type: "bigint", nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COTReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EconomicHeatmapEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Indicator = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false),
                    PreviousValue = table.Column<double>(type: "float", nullable: false),
                    Impact = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DateCollected = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EconomicHeatmapEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EdgeFinderScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstrumentId = table.Column<int>(type: "int", nullable: false),
                    OverallScore = table.Column<double>(type: "float", nullable: false),
                    Bias = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FundamentalScore = table.Column<double>(type: "float", nullable: false),
                    SentimentScore = table.Column<double>(type: "float", nullable: false),
                    TechnicalScore = table.Column<double>(type: "float", nullable: false),
                    COTScore = table.Column<double>(type: "float", nullable: false),
                    RetailSentimentScore = table.Column<double>(type: "float", nullable: false),
                    EconomicScore = table.Column<double>(type: "float", nullable: false),
                    DataSources = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DateComputed = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeFinderScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EdgeFinderScores_Instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Instruments",
                columns: new[] { "Id", "AssetClass", "Name", "Type" },
                values: new object[,]
                {
                    { 100, "ForexMajor", "EURUSD", "Currency" },
                    { 101, "ForexMajor", "GBPUSD", "Currency" },
                    { 102, "ForexMajor", "USDJPY", "Currency" },
                    { 103, "ForexMajor", "USDCHF", "Currency" },
                    { 104, "ForexMajor", "AUDUSD", "Currency" },
                    { 105, "ForexMajor", "USDCAD", "Currency" },
                    { 106, "ForexMajor", "NZDUSD", "Currency" },
                    { 107, "ForexMajor", "GBPJPY", "Currency" },
                    { 108, "ForexMinor", "EURGBP", "Currency" },
                    { 109, "ForexMinor", "EURJPY", "Currency" },
                    { 110, "ForexMinor", "EURCHF", "Currency" },
                    { 111, "ForexMinor", "EURAUD", "Currency" },
                    { 112, "ForexMinor", "EURCAD", "Currency" },
                    { 113, "ForexMinor", "EURNZD", "Currency" },
                    { 114, "ForexMinor", "GBPCHF", "Currency" },
                    { 115, "ForexMinor", "GBPAUD", "Currency" },
                    { 116, "ForexMinor", "GBPCAD", "Currency" },
                    { 117, "ForexMinor", "GBPNZD", "Currency" },
                    { 118, "ForexMinor", "AUDJPY", "Currency" },
                    { 119, "ForexMinor", "AUDCHF", "Currency" },
                    { 120, "ForexMinor", "AUDCAD", "Currency" },
                    { 121, "ForexMinor", "AUDNZD", "Currency" },
                    { 122, "ForexMinor", "NZDJPY", "Currency" },
                    { 123, "ForexMinor", "NZDCHF", "Currency" },
                    { 124, "ForexMinor", "NZDCAD", "Currency" },
                    { 125, "ForexMinor", "CADJPY", "Currency" },
                    { 126, "ForexMinor", "CADCHF", "Currency" },
                    { 127, "ForexMinor", "CHFJPY", "Currency" },
                    { 128, "ForexMinor", "USDSEK", "Currency" },
                    { 129, "Index", "US500", "Commodity" },
                    { 130, "Index", "US30", "Commodity" },
                    { 131, "Index", "US100", "Commodity" },
                    { 132, "Index", "DE40", "Commodity" },
                    { 133, "Index", "UK100", "Commodity" },
                    { 134, "Index", "JP225", "Commodity" },
                    { 135, "Metal", "XAUUSD", "Commodity" },
                    { 136, "Metal", "XAGUSD", "Commodity" },
                    { 137, "Metal", "XPTUSD", "Commodity" },
                    { 138, "Metal", "XPDUSD", "Commodity" },
                    { 139, "Commodity", "USOIL", "Commodity" },
                    { 140, "Bond", "US10Y", "Commodity" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_COTReports_Symbol_ReportDate",
                table: "COTReports",
                columns: new[] { "Symbol", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EconomicHeatmapEntries_Currency_Indicator",
                table: "EconomicHeatmapEntries",
                columns: new[] { "Currency", "Indicator" });

            migrationBuilder.CreateIndex(
                name: "IX_EdgeFinderScores_InstrumentId_DateComputed",
                table: "EdgeFinderScores",
                columns: new[] { "InstrumentId", "DateComputed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "COTReports");

            migrationBuilder.DropTable(
                name: "EconomicHeatmapEntries");

            migrationBuilder.DropTable(
                name: "EdgeFinderScores");

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 100);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 101);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 102);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 103);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 104);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 105);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 106);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 107);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 108);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 109);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 110);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 111);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 112);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 113);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 114);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 115);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 116);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 117);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 118);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 119);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 120);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 121);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 122);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 123);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 124);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 125);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 126);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 127);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 128);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 129);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 130);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 131);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 132);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 133);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 134);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 135);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 136);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 137);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 138);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 139);

            migrationBuilder.DeleteData(
                table: "Instruments",
                keyColumn: "Id",
                keyValue: 140);

            migrationBuilder.DropColumn(
                name: "AssetClass",
                table: "Instruments");
        }
    }
}
