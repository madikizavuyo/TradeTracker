-- AddMacdAndStochasticToTechnicalIndicators: Add MACD, MACDSignal, StochasticK to TechnicalIndicators
-- Run to apply technical scoring improvements (MACD crossover, Stochastic oversold/overbought)

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TechnicalIndicators') AND name = 'MACD')
    ALTER TABLE TechnicalIndicators ADD MACD float NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TechnicalIndicators') AND name = 'MACDSignal')
    ALTER TABLE TechnicalIndicators ADD MACDSignal float NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TechnicalIndicators') AND name = 'StochasticK')
    ALTER TABLE TechnicalIndicators ADD StochasticK float NULL;

-- Record migration
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260317182831_AddMacdAndStochasticToTechnicalIndicators')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260317182831_AddMacdAndStochasticToTechnicalIndicators', N'9.0.5');
