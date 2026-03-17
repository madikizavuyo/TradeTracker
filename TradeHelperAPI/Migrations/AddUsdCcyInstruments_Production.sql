-- AddUsdCcyInstruments: Add USDMXN, USDBRL to Instruments (USDZAR exists as id 141)
-- Run after applying 20260317000000_AddUsdCcyInstruments migration

SET IDENTITY_INSERT Instruments ON;

IF NOT EXISTS (SELECT 1 FROM Instruments WHERE Name = 'USDMXN')
    INSERT INTO Instruments (Id, AssetClass, Name, Type) VALUES (153, 'ForexMajor', 'USDMXN', 'Currency');

IF NOT EXISTS (SELECT 1 FROM Instruments WHERE Name = 'USDBRL')
    INSERT INTO Instruments (Id, AssetClass, Name, Type) VALUES (154, 'ForexMinor', 'USDBRL', 'Currency');

SET IDENTITY_INSERT Instruments OFF;

-- Record migration
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260317000000_AddUsdCcyInstruments')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260317000000_AddUsdCcyInstruments', N'9.0.5');
