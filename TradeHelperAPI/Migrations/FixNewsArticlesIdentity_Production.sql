-- Run this on Production (SmarterASP) to fix the NewsArticles.Id column.
-- Error: "Cannot insert the value NULL into column 'Id', table 'NewsArticles'; column does not allow nulls"
--
-- The NewsArticles table was created without IDENTITY on Id. This script recreates
-- the table with IDENTITY, preserving all data.
--
-- Execute against production DB via SmarterASP SQL Manager or sqlcmd.

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'NewsArticles')
AND NOT EXISTS (
    SELECT 1 FROM sys.identity_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'NewsArticles' AND c.name = 'Id'
)
BEGIN
    -- Create new table with IDENTITY on Id
    CREATE TABLE [dbo].[NewsArticles_new] (
        [Id] int NOT NULL IDENTITY(1,1),
        [Symbol] nvarchar(20) NOT NULL,
        [Headline] nvarchar(500) NOT NULL,
        [Summary] nvarchar(2000) NOT NULL,
        [Source] nvarchar(200) NOT NULL,
        [Url] nvarchar(1000) NOT NULL,
        [ImageUrl] nvarchar(500) NOT NULL,
        [PublishedAt] datetime2 NOT NULL,
        [DateCollected] datetime2 NOT NULL,
        CONSTRAINT [PK_NewsArticles_new] PRIMARY KEY ([Id])
    );

    CREATE INDEX [IX_NewsArticles_new_Symbol_DateCollected] ON [dbo].[NewsArticles_new] ([Symbol], [DateCollected]);

    -- Copy data (Id will be auto-generated)
    INSERT INTO [dbo].[NewsArticles_new] ([Symbol], [Headline], [Summary], [Source], [Url], [ImageUrl], [PublishedAt], [DateCollected])
    SELECT [Symbol], [Headline], [Summary], [Source], [Url], [ImageUrl], [PublishedAt], [DateCollected]
    FROM [dbo].[NewsArticles];

    DROP TABLE [dbo].[NewsArticles];
    EXEC sp_rename 'dbo.NewsArticles_new', 'NewsArticles';
    EXEC sp_rename 'PK_NewsArticles_new', 'PK_NewsArticles';
    EXEC sp_rename 'dbo.NewsArticles.IX_NewsArticles_new_Symbol_DateCollected', 'IX_NewsArticles_Symbol_DateCollected', 'INDEX';
END
GO
