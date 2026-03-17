using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyStrengthScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CurrencyStrengthScore",
                table: "EdgeFinderScores",
                type: "float",
                nullable: false,
                defaultValue: 5.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyStrengthScore",
                table: "EdgeFinderScores");
        }
    }
}
