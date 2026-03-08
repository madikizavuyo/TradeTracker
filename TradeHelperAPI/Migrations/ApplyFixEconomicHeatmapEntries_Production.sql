-- Run this on Production (SmarterASP) if dotnet ef database update fails due to migration history mismatch.
-- Fixes EconomicHeatmapEntries.Id to have IDENTITY, then records the migration.

-- 1. Fix EconomicHeatmapEntries table (only if Id lacks IDENTITY)
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

-- 2. Record migration (skip if already applied)
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260307212133_FixEconomicHeatmapEntriesIdentity')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260307212133_FixEconomicHeatmapEntriesIdentity', N'9.0.5');
END
