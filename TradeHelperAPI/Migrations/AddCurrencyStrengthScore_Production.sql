-- Run this on Production when deploying without EF migrations.
-- Adds CurrencyStrengthScore column to EdgeFinderScores (TrailBlazer scoring).
-- Nullable: N/A when not applicable (forex/USD commodities); not included in calculations.
--
-- Execute against production DB via SmarterASP SQL Manager or sqlcmd.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'EdgeFinderScores' AND c.name = 'CurrencyStrengthScore'
)
BEGIN
    ALTER TABLE [dbo].[EdgeFinderScores]
    ADD [CurrencyStrengthScore] float NULL;

    IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260315000000_AddCurrencyStrengthScore')
        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260315000000_AddCurrencyStrengthScore', N'9.0.5');
END
GO
