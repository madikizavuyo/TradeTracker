-- Fix RetailSentimentSnapshots.Id: ensure IDENTITY so EF can insert without explicit Id
-- Run against production when DbUpdateException: Cannot insert NULL into column 'Id' occurs

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'RetailSentimentSnapshots')
AND NOT EXISTS (
    SELECT 1 FROM sys.identity_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'RetailSentimentSnapshots' AND c.name = 'Id'
)
BEGIN
    CREATE TABLE [dbo].[RetailSentimentSnapshots_new] (
        [Id] int NOT NULL IDENTITY(1,1),
        [InstrumentId] int NOT NULL,
        [LongPct] float NOT NULL,
        [ShortPct] float NOT NULL,
        [DateCollected] datetime2 NOT NULL,
        CONSTRAINT [PK_RetailSentimentSnapshots_new] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RetailSentimentSnapshots_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments]([Id]) ON DELETE CASCADE
    );
    CREATE INDEX [IX_RetailSentimentSnapshots_InstrumentId_DateCollected] ON [dbo].[RetailSentimentSnapshots_new] ([InstrumentId], [DateCollected]);
    INSERT INTO [dbo].[RetailSentimentSnapshots_new] ([InstrumentId], [LongPct], [ShortPct], [DateCollected])
    SELECT [InstrumentId], [LongPct], [ShortPct], [DateCollected]
    FROM [dbo].[RetailSentimentSnapshots];
    DROP TABLE [dbo].[RetailSentimentSnapshots];
    EXEC sp_rename 'dbo.RetailSentimentSnapshots_new', 'RetailSentimentSnapshots';
    EXEC sp_rename 'PK_RetailSentimentSnapshots_new', 'PK_RetailSentimentSnapshots';
END
