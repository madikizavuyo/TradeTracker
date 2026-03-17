-- Fix UserLogs.Id: ensure IDENTITY so EF can insert without explicit Id
-- Run against production when DbUpdateException: Cannot insert NULL into column 'Id' occurs

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'UserLogs')
AND NOT EXISTS (
    SELECT 1 FROM sys.identity_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'UserLogs' AND c.name = 'Id'
)
BEGIN
    CREATE TABLE [dbo].[UserLogs_new] (
        [Id] int NOT NULL IDENTITY(1,1),
        [Email] nvarchar(max) NOT NULL,
        [Action] nvarchar(max) NOT NULL,
        [Timestamp] datetime2 NOT NULL,
        CONSTRAINT [PK_UserLogs_new] PRIMARY KEY ([Id])
    );
    INSERT INTO [dbo].[UserLogs_new] ([Email], [Action], [Timestamp])
    SELECT [Email], [Action], [Timestamp]
    FROM [dbo].[UserLogs];
    DROP TABLE [dbo].[UserLogs];
    EXEC sp_rename 'dbo.UserLogs_new', 'UserLogs';
    EXEC sp_rename 'PK_UserLogs_new', 'PK_UserLogs';
END
