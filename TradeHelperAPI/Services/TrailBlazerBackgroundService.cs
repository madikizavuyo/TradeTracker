using Microsoft.EntityFrameworkCore;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    public class TrailBlazerBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _provider;
        private readonly IConfiguration _config;
        private readonly TrailBlazerRefreshProgressService _progress;
        private readonly ILogger<TrailBlazerBackgroundService> _logger;

        public TrailBlazerBackgroundService(
            IServiceProvider provider,
            IConfiguration config,
            TrailBlazerRefreshProgressService progress,
            ILogger<TrailBlazerBackgroundService> logger)
        {
            _provider = provider;
            _config = config;
            _progress = progress;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait 3 min after startup so app can serve HTTP requests first (avoids "unhealthy" recycle on shared hosting)
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunRefreshCycleAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TrailBlazer refresh cycle failed");
                }

                var intervalHours = _config.GetValue("TrailBlazer:RefreshIntervalHours", 12);
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
        }

        public async Task RunRefreshCycleAsync()
        {
            _logger.LogInformation("TrailBlazer refresh cycle starting...");
            _progress.SetRunning("heatmap", "Fetching economic heatmap (FRED)...");

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dataService = scope.ServiceProvider.GetRequiredService<TrailBlazerDataService>();
            var scoringEngine = scope.ServiceProvider.GetRequiredService<TrailBlazerScoringEngine>();
            var worldBankService = scope.ServiceProvider.GetService<WorldBankDataService>();

            var instruments = await db.Instruments.ToListAsync();
            if (instruments.Count == 0)
            {
                _logger.LogWarning("No instruments found in database");
                _progress.SetIdle();
                return;
            }

            try
            {
                var heatmapEntries = await dataService.BuildEconomicHeatmapAsync();
                var heatmapFromDb = false;
                if (!heatmapEntries.Any(e => e.Value != 0))
                {
                    var fromDb = await db.EconomicHeatmapEntries.ToListAsync();
                    heatmapEntries = fromDb
                        .GroupBy(e => new { e.Currency, e.Indicator })
                        .Select(g => g.OrderByDescending(e => e.DateCollected).First())
                        .ToList();
                    heatmapFromDb = true;
                    _logger.LogInformation("TrailBlazer: FRED empty/blocked, using {Count} heatmap entries from DB", heatmapEntries.Count);
                }
                _logger.LogInformation("TrailBlazer: Heatmap built with {Count} entries", heatmapEntries.Count);

                _progress.SetRunning("heatmap", "Applying World Bank fallback...");
                if (worldBankService != null)
                {
                    var currencies = heatmapEntries.Select(e => e.Currency).Distinct().ToList();
                    var wbData = await worldBankService.FetchForCurrenciesAsync(currencies);
                    foreach (var wb in wbData)
                    {
                        foreach (var entry in heatmapEntries.Where(e => string.Equals(e.Currency, wb.Currency, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (entry.Indicator == "GDP" && entry.Value == 0 && wb.GdpGrowthAnnualPct.HasValue)
                            {
                                entry.Value = wb.GdpGrowthAnnualPct.Value;
                                entry.Impact = entry.Value >= 1.5 && entry.Value <= 2.5 ? "Neutral" : entry.Value > 2.5 ? "Positive" : "Negative";
                                _logger.LogDebug("World Bank GDP fallback for {Currency}: {Value}%", wb.Currency, entry.Value);
                            }
                            else if (entry.Indicator == "CPI" && entry.Value == 0 && wb.InflationAnnualPct.HasValue)
                            {
                                entry.Value = wb.InflationAnnualPct.Value;
                                entry.Impact = entry.Value >= 1.5 && entry.Value <= 2.5 ? "Neutral" : entry.Value > 2.5 ? "Negative" : "Positive";
                                _logger.LogDebug("World Bank CPI fallback for {Currency}: {Value}%", wb.Currency, entry.Value);
                            }
                        }
                    }
                }

                _progress.SetRunning("cot", "Fetching COT reports from CFTC...");
                var cotBatch = await dataService.FetchCOTReportBatchAsync();
                _logger.LogInformation("TrailBlazer: COT fetched {Count} symbols", cotBatch.Count);

                _progress.SetRunning("myfxbook", "Fetching retail sentiment from MyFXBook...");
                var myFxBookSentiment = await dataService.FetchMyFxBookSentimentBatchAsync();
                if (myFxBookSentiment.Count == 0)
                    _logger.LogWarning("TrailBlazer: MyFXBook returned 0 symbols — retail will be preserved or 50/50. Check credentials, session, or API.");
                else
                    _logger.LogInformation("TrailBlazer: MyFXBook fetched {Count} symbols for retail sentiment", myFxBookSentiment.Count);

                // Append heatmap (keep history for rollback); only add when from FRED (not DB fallback)
                if (!heatmapFromDb)
                    db.EconomicHeatmapEntries.AddRange(heatmapEntries);
                await SaveChangesWithFullErrorLoggingAsync(db, "heatmap/COT");

                _progress.SetRunning("instruments", "Processing instruments...", 0, instruments.Count);
                var idx = 0;
                var statsRetail = 0;
                var statsCOT = 0;
                var statsTechnical = 0;
                var statsNews = 0;
                var statsNoRetail = 0;
                foreach (var instrument in instruments)
                {
                    try
                    {
                        var (hasRetail, hasCOT, hasTech, hasNews) = await ProcessInstrumentAsync(db, dataService, scoringEngine, instrument, heatmapEntries, cotBatch, myFxBookSentiment, _logger);
                        if (hasRetail) statsRetail++;
                        else statsNoRetail++;
                        if (hasCOT) statsCOT++;
                        if (hasTech) statsTechnical++;
                        if (hasNews) statsNews++;
                        _logger.LogDebug("Processed {Instrument}", instrument.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process {Instrument}", instrument.Name);
                    }
                    idx++;
                    _progress.SetRunning("instruments", $"Scoring {instrument.Name} ({idx}/{instruments.Count})", idx, instruments.Count);
                    await Task.Delay(500);
                }

                await SaveChangesWithFullErrorLoggingAsync(db, "scores/news/COT/heatmap batch");

                db.UserLogs.Add(new UserLog
                {
                    Email = "system@trailblazer",
                    Action = $"TrailBlazer refresh completed for {instruments.Count} instruments",
                    Timestamp = DateTime.UtcNow
                });
                await SaveChangesWithFullErrorLoggingAsync(db, "UserLog completion");

                _progress.SetCompleted(instruments.Count);
                _logger.LogInformation(
                    "TrailBlazer refresh completed: {Count} instruments | Retail: {Retail} | COT: {COT} | Technical: {Tech} | News: {News} | No retail: {NoRetail}",
                    instruments.Count, statsRetail, statsCOT, statsTechnical, statsNews, statsNoRetail);
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullExceptionMessage(ex);
                _logger.LogError(ex, "TrailBlazer refresh failed: {FullMessage}", fullMsg);
                _progress.SetFailed(fullMsg.Length > 200 ? fullMsg[..200] + "..." : fullMsg);
            }
        }

        private static async Task<(bool hasRetail, bool hasCOT, bool hasTech, bool hasNews)> ProcessInstrumentAsync(
            ApplicationDbContext db,
            TrailBlazerDataService dataService,
            TrailBlazerScoringEngine scoringEngine,
            Instrument instrument,
            List<TradeHelper.Models.EconomicHeatmapEntry> heatmapEntries,
            Dictionary<string, COTReport> cotBatch,
            IReadOnlyDictionary<string, (double longPct, double shortPct)> myFxBookSentiment,
            ILogger logger)
        {
            var dataSources = new List<string>();

            // COT data (from CFTC). If API blocked/empty, use latest from DB. Do not post to DB when we have no data.
            cotBatch.TryGetValue(instrument.Name, out var cotReport);
            if (cotReport == null)
            {
                cotReport = await db.COTReports
                    .Where(c => c.Symbol == instrument.Name)
                    .OrderByDescending(c => c.ReportDate)
                    .FirstOrDefaultAsync();
                if (cotReport == null)
                    logger.LogDebug("TrailBlazer: no COT data for {Instrument}; not posting to database", instrument.Name);
            }
            else
            {
                db.COTReports.Add(cotReport);
                logger.LogDebug("TrailBlazer: COT data saved for {Instrument} (ReportDate={Date})", instrument.Name, cotReport.ReportDate);
            }
            if (cotReport != null)
                dataSources.Add("CFTC");

            // Previous COT for momentum comparison
            var previousCot = await db.COTReports
                .Where(c => c.Symbol == instrument.Name)
                .OrderByDescending(c => c.ReportDate)
                .Skip(1)
                .FirstOrDefaultAsync();

            // Retail sentiment: MyFXBook community outlook. Never overwrite with 50/50 or empty — preserve existing if incoming is invalid.
            var sentimentResult = await dataService.FetchForexRetailSentimentAsync(instrument.Name, myFxBookSentiment);
            var retailSentiment = sentimentResult ?? (50.0, 50.0);
            var latestScore = await db.TrailBlazerScores
                .Where(s => s.InstrumentId == instrument.Id)
                .OrderByDescending(s => s.DateComputed)
                .FirstOrDefaultAsync();

            var incomingRetailIsEmpty = !sentimentResult.HasValue || (Math.Abs(retailSentiment.longPct - 50) < 1 && Math.Abs(retailSentiment.shortPct - 50) < 1);
            if (incomingRetailIsEmpty)
            {
                var existingIsValid = latestScore != null && (Math.Abs(latestScore.RetailLongPct - 50) >= 1 || Math.Abs(latestScore.RetailShortPct - 50) >= 1);
                if (existingIsValid)
                {
                    retailSentiment = (latestScore!.RetailLongPct, latestScore.RetailShortPct);
                    dataSources.Add("myfxbook"); // preserved from prior load
                    logger.LogDebug("TrailBlazer: {Instrument} retail preserved ({Long:F0}/{Short:F0})", instrument.Name, retailSentiment.longPct, retailSentiment.shortPct);
                }
                else
                {
                    logger.LogDebug("TrailBlazer: {Instrument} has no retail data (MyFXBook empty or 50/50, no existing to preserve)", instrument.Name);
                }
            }
            else if (sentimentResult.HasValue)
            {
                dataSources.Add("myfxbook");
            }

            // Only write to DB when we got viable data from endpoint; never when using preserved/prior data
            if (dataSources.Contains("myfxbook") && sentimentResult.HasValue && !incomingRetailIsEmpty)
            {
                db.RetailSentimentSnapshots.Add(new RetailSentimentSnapshot
                {
                    InstrumentId = instrument.Id,
                    LongPct = retailSentiment.longPct,
                    ShortPct = retailSentiment.shortPct,
                    DateCollected = DateTime.UtcNow
                });
                logger.LogDebug("TrailBlazer: retail sentiment snapshot saved for {Instrument} ({Long:F0}/{Short:F0})", instrument.Name, retailSentiment.longPct, retailSentiment.shortPct);
            }

            // Technical indicators: fetch from providers and save to DB only when we get data. Never default to 5.
            await dataService.FetchAndStoreTechnicalIndicatorsAsync(db, instrument.Id, instrument.Name);

            // Scoring uses ONLY data from TechnicalIndicators table (never in-memory defaults)
            var (technicalsFromDb, technicalSource, technicalDateCollected) = await TrailBlazerDataService.LoadTechnicalFromDbForScoringAsync(db, instrument.Id);
            var hasTechnical = technicalsFromDb != null;
            if (hasTechnical && technicalSource != null)
                dataSources.Add(technicalSource);

            // Fundamental context: forex = relative strength (base vs quote); commodity = USD fundamentals (weak USD = bullish gold)
            var fundamentalContext = BuildFundamentalContext(instrument, heatmapEntries);
            var hasFundamental = fundamentalContext.BaseData.Values.Any(v => v != 0) || fundamentalContext.QuoteData.Values.Any(v => v != 0);
            if (hasFundamental)
                dataSources.Add("FRED");

            var (newsSentimentScore, hasNewsSentiment, newsItems) = await dataService.FetchNewsSentimentScoreWithItemsAsync(instrument.Name, instrument.AssetClass, db);
            var effectiveNewsScore = newsSentimentScore;
            if (hasNewsSentiment)
            {
                dataSources.Add("Brave/Finnhub");
                if (newsItems.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    foreach (var item in newsItems)
                    {
                        db.NewsArticles.Add(new TradeHelper.Models.NewsArticle
                        {
                            Symbol = instrument.Name,
                            Headline = item.Headline,
                            Summary = item.Summary ?? "",
                            Source = item.Source ?? "",
                            Url = item.Url ?? "",
                            ImageUrl = item.ImageUrl ?? "",
                            PublishedAt = item.PublishedAt,
                            DateCollected = now
                        });
                    }
                    logger.LogDebug("TrailBlazer: {Count} news articles saved for {Instrument}", newsItems.Count, instrument.Name);
                }
                else
                    logger.LogDebug("TrailBlazer: news sentiment for {Instrument} but no articles to save (not posting empty news to database)", instrument.Name);
            }
            else
            {
                logger.LogDebug("TrailBlazer: no news sentiment for {Instrument}; not posting news to database", instrument.Name);
                if (latestScore != null && (latestScore.DataSources ?? "").Contains("Brave/Finnhub", StringComparison.OrdinalIgnoreCase))
                {
                    effectiveNewsScore = latestScore.NewsSentimentScore;
                    dataSources.Add("Brave/Finnhub");
                }
            }

            var hasRetail = dataSources.Contains("myfxbook");
            var hasNews = dataSources.Contains("Brave/Finnhub");
            var weightContext = new ScoringWeightContext(
                instrument.AssetClass,
                cotReport != null,
                hasRetail,
                hasFundamental,
                hasTechnical,
                hasNews);

            // Calculate score (technicals from DB only; empty dict when no data)
            var technicalsForScoring = hasTechnical ? technicalsFromDb! : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["RSI"] = 50, ["MACD"] = 0, ["MACDSignal"] = 0, ["EMA50"] = 0, ["EMA200"] = 0 };
            var score = scoringEngine.CalculateScore(
                instrument.Id,
                fundamentalContext,
                cotReport,
                previousCot,
                retailSentiment,
                effectiveNewsScore,
                technicalsForScoring,
                dataSources,
                weightContext
            );

            // When no technical data in DB: preserve from prior; never default to 5
            if (!hasTechnical && latestScore != null)
            {
                score.TechnicalScore = latestScore.TechnicalScore;
                score.TechnicalDataDateCollected = latestScore.TechnicalDataDateCollected;
                logger.LogDebug("TrailBlazer: {Instrument} technical score preserved from prior ({Score:F1})", instrument.Name, score.TechnicalScore);
            }
            else if (hasTechnical && technicalDateCollected.HasValue)
            {
                score.TechnicalDataDateCollected = technicalDateCollected;
            }
            if (!hasFundamental && latestScore != null)
            {
                score.FundamentalScore = latestScore.FundamentalScore;
                score.EconomicScore = latestScore.EconomicScore;
                logger.LogDebug("TrailBlazer: {Instrument} fundamental score preserved from prior ({Score:F1})", instrument.Name, score.FundamentalScore);
            }
            if (cotReport == null && latestScore != null)
            {
                score.COTScore = latestScore.COTScore;
                logger.LogDebug("TrailBlazer: {Instrument} COT score preserved from prior ({Score:F1})", instrument.Name, score.COTScore);
            }

            db.TrailBlazerScores.Add(score);
            var hasTechnicalSource = dataSources.Contains("TwelveData") || dataSources.Contains("MarketStack") || dataSources.Contains("iTick") || dataSources.Contains("EODHD") || dataSources.Contains("FMP") || dataSources.Contains("NasdaqDataLink");
            logger.LogDebug("TrailBlazer: score saved for {Instrument} (sources: {Sources})", instrument.Name, string.Join(", ", dataSources));
            return (dataSources.Contains("myfxbook"), cotReport != null, hasTechnicalSource, hasNews);
        }

        /// <summary>Builds fundamental context for scoring. Forex = base vs quote (relative strength). Commodity = USD data (weak USD = bullish gold).</summary>
        private static FundamentalContext BuildFundamentalContext(Instrument instrument, List<TradeHelper.Models.EconomicHeatmapEntry> heatmapEntries)
        {
            var baseData = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var quoteData = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            string baseCcy, quoteCcy;

            if (instrument.Type == "Currency" && instrument.Name.Length >= 6 && instrument.Name.All(char.IsLetter))
            {
                baseCcy = instrument.Name[..3];
                quoteCcy = instrument.Name[3..];
            }
            else if (instrument.Type == "Commodity")
            {
                // XAUUSD, XAGUSD, USOIL: priced in USD; we use USD fundamentals (weak USD = bullish)
                baseCcy = "USD";
                quoteCcy = "USD";
            }
            else
            {
                baseCcy = quoteCcy = "USD";
            }

            foreach (var ind in new[] { "GDP", "CPI", "Unemployment", "InterestRate", "PMI" })
            {
                var b = heatmapEntries.FirstOrDefault(e => string.Equals(e.Currency, baseCcy, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Indicator, ind, StringComparison.OrdinalIgnoreCase));
                var q = heatmapEntries.FirstOrDefault(e => string.Equals(e.Currency, quoteCcy, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Indicator, ind, StringComparison.OrdinalIgnoreCase));
                if (b != null && b.Value != 0) baseData[ind] = b.Value;
                if (q != null && q.Value != 0) quoteData[ind] = q.Value;
            }

            var isForex = instrument.Type == "Currency" && instrument.Name.Length >= 6;
            var isUsdCommodity = instrument.Type == "Commodity" && (
                instrument.Name == "XAUUSD" || instrument.Name == "XAGUSD" || instrument.Name == "XPTUSD" || instrument.Name == "XPDUSD" || instrument.Name == "USOIL" ||
                instrument.Name == "BTC" || instrument.Name == "ETH" || instrument.Name == "SOL");

            return new FundamentalContext(baseData, quoteData, isForex, isUsdCommodity);
        }

        private async Task SaveChangesWithFullErrorLoggingAsync(ApplicationDbContext db, string context)
        {
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var fullMsg = GetFullExceptionMessage(ex);
                _logger.LogError(ex, "SaveChanges failed ({Context}): {FullMessage}", context, fullMsg);
                throw;
            }
        }

        private static string GetFullExceptionMessage(Exception ex)
        {
            var parts = new List<string>();
            for (var e = ex; e != null; e = e.InnerException)
                parts.Add($"{e.GetType().Name}: {e.Message}");
            var msg = string.Join(" | Inner: ", parts);
            if (ex is Microsoft.EntityFrameworkCore.DbUpdateException && ex.InnerException != null)
                msg += " | " + ex.InnerException.Message;
            return msg;
        }
    }
}
