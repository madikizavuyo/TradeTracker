using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeSetupSignalToEdgeFinderScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TradeSetupSignal",
                table: "EdgeFinderScores",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeSetupDetail",
                table: "EdgeFinderScores",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TradeSetupSignal",
                table: "EdgeFinderScores");

            migrationBuilder.DropColumn(
                name: "TradeSetupDetail",
                table: "EdgeFinderScores");
        }
    }
}
