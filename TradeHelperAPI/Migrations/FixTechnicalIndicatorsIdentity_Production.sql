-- Run this on Production (SmarterASP) to fix the TechnicalIndicators.Id column.
-- Error: "Cannot insert the value NULL into column 'Id', table 'TechnicalIndicators'; column does not allow nulls"
--
-- The TechnicalIndicators table was created without IDENTITY on Id. This script recreates
-- the table with IDENTITY, preserving all data.
--
-- Execute against production DB via SmarterASP SQL Manager or sqlcmd.

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TechnicalIndicators')
AND NOT EXISTS (
    SELECT 1 FROM sys.identity_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = 'TechnicalIndicators' AND c.name = 'Id'
)
BEGIN
    -- Create new table with IDENTITY on Id
    CREATE TABLE [dbo].[TechnicalIndicators_new] (
        [Id] int NOT NULL IDENTITY(1,1),
        [InstrumentId] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [RSI] float NULL,
        [SMA14] float NULL,
        [SMA50] float NULL,
        [EMA50] float NULL,
        [EMA200] float NULL,
        [DateCollected] datetime2 NOT NULL,
        [Source] nvarchar(50) NULL,
        CONSTRAINT [PK_TechnicalIndicators_new] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TechnicalIndicators_new_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_TechnicalIndicators_new_InstrumentId_Date] ON [dbo].[TechnicalIndicators_new] ([InstrumentId], [Date]);

    -- Copy data (Id will be auto-generated)
    INSERT INTO [dbo].[TechnicalIndicators_new] ([InstrumentId], [Date], [RSI], [SMA14], [SMA50], [EMA50], [EMA200], [DateCollected], [Source])
    SELECT [InstrumentId], [Date], [RSI], [SMA14], [SMA50], [EMA50], [EMA200], [DateCollected], [Source]
    FROM [dbo].[TechnicalIndicators];

    DROP TABLE [dbo].[TechnicalIndicators];
    EXEC sp_rename 'dbo.TechnicalIndicators_new', 'TechnicalIndicators';
    EXEC sp_rename 'PK_TechnicalIndicators_new', 'PK_TechnicalIndicators';
    EXEC sp_rename 'FK_TechnicalIndicators_new_Instruments_InstrumentId', 'FK_TechnicalIndicators_Instruments_InstrumentId', 'OBJECT';
    EXEC sp_rename 'dbo.TechnicalIndicators.IX_TechnicalIndicators_new_InstrumentId_Date', 'IX_TechnicalIndicators_InstrumentId_Date', 'INDEX';
END
GO
