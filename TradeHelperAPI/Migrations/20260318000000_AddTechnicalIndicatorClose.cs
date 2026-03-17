using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicalIndicatorClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Close",
                table: "TechnicalIndicators",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Close",
                table: "TechnicalIndicators");
        }
    }
}
