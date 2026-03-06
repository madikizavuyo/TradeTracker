-- Run this script if SystemSettings table is missing (e.g. migration 20260305220000 was not applied).
-- Creates the SystemSettings table used by ApiRateLimitService and TrailBlazerDataService (MyFXBook session).
IF OBJECT_ID(N'[dbo].[SystemSettings]', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SystemSettings] (
        [Id] int NOT NULL IDENTITY,
        [Key] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [dbo].[SystemSettings] ([Key]);
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260305220000_AddSystemSettings', N'9.0.5');
END
GO
