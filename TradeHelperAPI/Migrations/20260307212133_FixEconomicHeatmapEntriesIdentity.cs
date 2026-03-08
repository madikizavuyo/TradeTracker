using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeHelperAPI.Migrations
{
    /// <inheritdoc />
    public partial class FixEconomicHeatmapEntriesIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix: EconomicHeatmapEntries.Id must have IDENTITY for EF inserts to work.
            // If the table was created without IDENTITY (e.g. manual DB setup), recreate it.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EconomicHeatmapEntries')
                AND NOT EXISTS (
                    SELECT 1 FROM sys.identity_columns ic
                    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                    JOIN sys.tables t ON c.object_id = t.object_id
                    WHERE t.name = 'EconomicHeatmapEntries' AND c.name = 'Id'
                )
                BEGIN
                    CREATE TABLE [dbo].[EconomicHeatmapEntries_new] (
                        [Id] int NOT NULL IDENTITY(1,1),
                        [Currency] nvarchar(10) NOT NULL,
                        [Indicator] nvarchar(50) NOT NULL,
                        [Value] float NOT NULL,
                        [PreviousValue] float NOT NULL,
                        [Impact] nvarchar(20) NOT NULL,
                        [DateCollected] datetime2 NOT NULL,
                        CONSTRAINT [PK_EconomicHeatmapEntries_new] PRIMARY KEY ([Id])
                    );
                    CREATE INDEX [IX_EconomicHeatmapEntries_new_Currency_Indicator] ON [dbo].[EconomicHeatmapEntries_new] ([Currency], [Indicator]);
                    SET IDENTITY_INSERT [dbo].[EconomicHeatmapEntries_new] ON;
                    INSERT INTO [dbo].[EconomicHeatmapEntries_new] ([Id], [Currency], [Indicator], [Value], [PreviousValue], [Impact], [DateCollected])
                    SELECT [Id], [Currency], [Indicator], [Value], [PreviousValue], [Impact], [DateCollected]
                    FROM [dbo].[EconomicHeatmapEntries];
                    SET IDENTITY_INSERT [dbo].[EconomicHeatmapEntries_new] OFF;
                    DROP TABLE [dbo].[EconomicHeatmapEntries];
                    EXEC sp_rename 'dbo.EconomicHeatmapEntries_new', 'EconomicHeatmapEntries';
                    EXEC sp_rename 'PK_EconomicHeatmapEntries_new', 'PK_EconomicHeatmapEntries';
                    EXEC sp_rename 'dbo.EconomicHeatmapEntries.IX_EconomicHeatmapEntries_new_Currency_Indicator', 'IX_EconomicHeatmapEntries_Currency_Indicator', 'INDEX';
                END
                ELSE IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EconomicHeatmapEntries')
                BEGIN
                    CREATE TABLE [dbo].[EconomicHeatmapEntries] (
                        [Id] int NOT NULL IDENTITY(1,1),
                        [Currency] nvarchar(10) NOT NULL,
                        [Indicator] nvarchar(50) NOT NULL,
                        [Value] float NOT NULL,
                        [PreviousValue] float NOT NULL,
                        [Impact] nvarchar(20) NOT NULL,
                        [DateCollected] datetime2 NOT NULL,
                        CONSTRAINT [PK_EconomicHeatmapEntries] PRIMARY KEY ([Id])
                    );
                    CREATE INDEX [IX_EconomicHeatmapEntries_Currency_Indicator] ON [dbo].[EconomicHeatmapEntries] ([Currency], [Indicator]);
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No down - original table structure is restored by recreating
        }
    }
}
