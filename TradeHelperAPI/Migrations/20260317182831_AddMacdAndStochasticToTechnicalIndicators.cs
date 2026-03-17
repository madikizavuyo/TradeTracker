using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddMacdAndStochasticToTechnicalIndicators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MACD",
                table: "TechnicalIndicators",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MACDSignal",
                table: "TechnicalIndicators",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StochasticK",
                table: "TechnicalIndicators",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MACD",
                table: "TechnicalIndicators");

            migrationBuilder.DropColumn(
                name: "MACDSignal",
                table: "TechnicalIndicators");

            migrationBuilder.DropColumn(
                name: "StochasticK",
                table: "TechnicalIndicators");
        }
    }
}
