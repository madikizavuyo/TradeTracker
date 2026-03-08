-- Run this on Production when dotnet ef database update fails with
-- "There is already an object named 'AspNetRoles' in the database."
--
-- The production DB has the schema but __EFMigrationsHistory is empty or incomplete.
-- This script baselines the migration history so EF will only apply AddCryptoAndZarInstruments.
--
-- 1. Execute this script against production DB (SmarterASP SQL Manager or sqlcmd)
-- 2. Then run: $env:ASPNETCORE_ENVIRONMENT='Production'; dotnet ef database update --context ApplicationDbContext

IF OBJECT_ID(N'[__EFMigrationsHistory]', 'U') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END
GO

-- Insert each migration if not already present
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20250519211556_InitialCreate')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20250519211556_InitialCreate', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251214192159_AddTradeAndStrategyModels')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20251214192159_AddTradeAndStrategyModels', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260303123805_AddUserSettings')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260303123805_AddUserSettings', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260303145103_AddEdgeFinderTables')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260303145103_AddEdgeFinderTables', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260303165828_AddRetailSentimentPct')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260303165828_AddRetailSentimentPct', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260304000000_AddUsdZarInstrument')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260304000000_AddUsdZarInstrument', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260304000051_AddNewsSentimentScore')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260304000051_AddNewsSentimentScore', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260305164403_AddTechnicalIndicators')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260305164403_AddTechnicalIndicators', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260305185947_AddDataPersistenceTables')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260305185947_AddDataPersistenceTables', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260305211849_AddApplicationLogs')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260305211849_AddApplicationLogs', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260305220000_AddSystemSettings')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260305220000_AddSystemSettings', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260306195852_AddTechnicalSourceAndTechnicalDataDateCollected')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260306195852_AddTechnicalSourceAndTechnicalDataDateCollected', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260307143235_LimitIdentityKeysTo128Chars')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260307143235_LimitIdentityKeysTo128Chars', N'9.0.5');
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260307212133_FixEconomicHeatmapEntriesIdentity')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260307212133_FixEconomicHeatmapEntriesIdentity', N'9.0.5');
