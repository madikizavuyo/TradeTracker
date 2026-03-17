using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class MakeCurrencyStrengthScoreNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add column if missing (e.g. AddCurrencyStrengthScore not applied), else alter to nullable
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id WHERE t.name = 'EdgeFinderScores' AND c.name = 'CurrencyStrengthScore')
                    ALTER TABLE [EdgeFinderScores] ADD [CurrencyStrengthScore] float NULL;
                ELSE
                BEGIN
                    DECLARE @var sysname;
                    SELECT @var = [d].[name] FROM [sys].[default_constraints] [d]
                    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
                    WHERE [d].[parent_object_id] = OBJECT_ID(N'[EdgeFinderScores]') AND [c].[name] = N'CurrencyStrengthScore';
                    IF @var IS NOT NULL EXEC(N'ALTER TABLE [EdgeFinderScores] DROP CONSTRAINT [' + @var + ']');
                    ALTER TABLE [EdgeFinderScores] ALTER COLUMN [CurrencyStrengthScore] float NULL;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "CurrencyStrengthScore",
                table: "EdgeFinderScores",
                type: "float",
                nullable: false,
                defaultValue: 5.0,
                oldClrType: typeof(double),
                oldType: "float",
                oldNullable: true);
        }
    }
}
