-- Run this on Production when the column already exists as NOT NULL.
-- Makes CurrencyStrengthScore nullable so N/A is stored as NULL (not included in calculations).
--
-- Execute against production DB via SmarterASP SQL Manager or sqlcmd.

IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'EdgeFinderScores' AND c.name = 'CurrencyStrengthScore'
)
BEGIN
    -- Drop default constraint if it exists
    DECLARE @ConstraintName nvarchar(200);
    SELECT @ConstraintName = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'EdgeFinderScores' AND c.name = 'CurrencyStrengthScore';

    IF @ConstraintName IS NOT NULL
        EXEC('ALTER TABLE [dbo].[EdgeFinderScores] DROP CONSTRAINT [' + @ConstraintName + ']');

    ALTER TABLE [dbo].[EdgeFinderScores]
    ALTER COLUMN [CurrencyStrengthScore] float NULL;

    IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260316000000_MakeCurrencyStrengthScoreNullable')
        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260316000000_MakeCurrencyStrengthScoreNullable', N'9.0.5');
END
GO
