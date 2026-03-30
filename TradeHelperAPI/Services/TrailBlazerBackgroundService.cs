using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    public class TrailBlazerBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _provider;
        private readonly IConfiguration _config;
        private readonly TrailBlazerRefreshProgressService _progress;
        private readonly IBreakoutSignalNotifier? _breakoutNotifier;
        private readonly ILogger<TrailBlazerBackgroundService> _logger;

        public TrailBlazerBackgroundService(
            IServiceProvider provider,
            IConfiguration config,
            TrailBlazerRefreshProgressService progress,
            ILogger<TrailBlazerBackgroundService> logger,
            IBreakoutSignalNotifier? breakoutNotifier = null)
        {
            _provider = provider;
            _config = config;
            _progress = progress;
            _logger = logger;
            _breakoutNotifier = breakoutNotifier;
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
            TrailBlazerDataService.SetBraveRefreshContext(true);
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
                TrailBlazerDataService.SetBraveRefreshContext(false);
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

                _progress.SetRunning("currency-strength", "Building currency strength (news + fundamentals)...");
                var newsCacheHours = _config.GetValue("TrailBlazer:CurrencyStrengthNewsCacheHours", 48);
                await BuildAndStoreCurrencyStrengthAsync(db, dataService, scope.ServiceProvider, newsCacheHours);

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

                var currencyOverrides = await LoadCurrencyOverridesAsync(db);
                var currencyStrength = await LoadCombinedCurrencyStrengthAsync(db, heatmapEntries, newsCacheHours);

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
                        var (hasRetail, hasCOT, hasTech, hasNews) = await ProcessInstrumentAsync(db, dataService, scoringEngine, instrument, heatmapEntries, cotBatch, myFxBookSentiment, currencyOverrides, currencyStrength, _breakoutNotifier, _logger);
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
            finally
            {
                try { await dataService.RecordBraveRefreshCompleteIfUsedAsync(); } catch { /* best effort */ }
                TrailBlazerDataService.SetBraveRefreshContext(false);
                TrailBlazerDataService.SetForceBraveForRefresh(false);
            }
        }

        private const string CurrencyStrengthNewsKey = "CurrencyStrengthNewsCache";
        private const string CurrencyStrengthNewsUpdatedKey = "CurrencyStrengthNewsUpdatedAt";

        private static async Task BuildAndStoreCurrencyStrengthAsync(ApplicationDbContext db, TrailBlazerDataService dataService, IServiceProvider sp, int newsCacheHours)
        {
            var newsSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == CurrencyStrengthNewsKey);
            var updatedSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == CurrencyStrengthNewsUpdatedKey);
            if (newsSetting != null && !string.IsNullOrEmpty(newsSetting.Value) && updatedSetting != null
                && DateTime.TryParse(updatedSetting.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cacheUpdated)
                && (DateTime.UtcNow - cacheUpdated).TotalHours < newsCacheHours)
                return;

            var ml = sp.GetService<MLModelService>();
            if (ml == null) return;

            var headlines = await dataService.FetchGlobalEconomicNewsAsync();
            if (headlines.Count == 0) return;

            var newsScores = await ml.GenerateCurrencyStrengthFromNewsAsync(headlines);
            if (newsScores == null || newsScores.Count == 0) return;

            var json = System.Text.Json.JsonSerializer.Serialize(newsScores);
            var now = DateTime.UtcNow;
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == CurrencyStrengthNewsKey);
            if (setting != null)
            {
                setting.Value = json;
                setting.UpdatedAt = now;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting { Key = CurrencyStrengthNewsKey, Value = json, UpdatedAt = now });
            }

            var updatedAtRow = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == CurrencyStrengthNewsUpdatedKey);
            if (updatedAtRow != null)
            {
                updatedAtRow.Value = now.ToString("o");
                updatedAtRow.UpdatedAt = now;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting { Key = CurrencyStrengthNewsUpdatedKey, Value = now.ToString("o"), UpdatedAt = now });
            }
            await db.SaveChangesAsync();
        }

        private static async Task<Dictionary<string, double>> LoadCombinedCurrencyStrengthAsync(ApplicationDbContext db, List<TradeHelper.Models.EconomicHeatmapEntry> entries, int newsCacheHours)
        {
            var fundamentalByCurrency = entries
                .GroupBy(e => e.Currency)
                .ToDictionary(g => g.Key, g =>
                {
                    var pos = g.Count(x => string.Equals(x.Impact, "Positive", StringComparison.OrdinalIgnoreCase));
                    var neg = g.Count(x => string.Equals(x.Impact, "Negative", StringComparison.OrdinalIgnoreCase));
                    var total = g.Count();
                    var raw = total > 0 ? 5.0 + (pos - neg) * 1.5 : 5.0;
                    return Math.Clamp(raw, 1.0, 10.0);
                });

            var newsSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == CurrencyStrengthNewsKey);
            var updatedSetting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == CurrencyStrengthNewsUpdatedKey);
            Dictionary<string, double>? newsScores = null;
            if (newsSetting != null && !string.IsNullOrEmpty(newsSetting.Value) && updatedSetting != null
                && DateTime.TryParse(updatedSetting.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var updated)
                && (DateTime.UtcNow - updated).TotalHours < newsCacheHours)
            {
                try
                {
                    newsScores = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(newsSetting.Value);
                }
                catch { /* ignore */ }
            }

            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var allCurrencies = fundamentalByCurrency.Keys.Union(newsScores != null ? newsScores.Keys : Enumerable.Empty<string>()).Distinct();
            foreach (var ccy in allCurrencies)
            {
                var fund = fundamentalByCurrency.TryGetValue(ccy, out var f) ? f : 5.0;
                var news = newsScores != null && newsScores.TryGetValue(ccy, out var n) ? n : (double?)null;
                result[ccy] = news.HasValue ? Math.Clamp(0.8 * news.Value + 0.2 * fund, 1.0, 10.0) : fund;
            }
            return result;
        }

        private static async Task<Dictionary<string, double>> LoadCurrencyOverridesAsync(ApplicationDbContext db)
        {
            const string prefix = "CurrencyOverride_";
            var settings = await db.SystemSettings
                .Where(s => s.Key.StartsWith(prefix))
                .ToListAsync();
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in settings)
            {
                var ccy = s.Key.Length > prefix.Length ? s.Key[prefix.Length..] : "";
                if (string.IsNullOrEmpty(ccy)) continue;
                var val = s.Value?.Trim().TrimStart('+') ?? "";
                if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var num))
                    result[ccy] = Math.Clamp(num, -2.0, 2.0);
            }
            return result;
        }

        /// <summary>True if persisted score used web news (legacy tag Brave/Finnhub or current Yahoo/Finnhub/Brave).</summary>
        private static bool NewsDataSourceInScore(string? dataSources)
        {
            if (string.IsNullOrEmpty(dataSources)) return false;
            return dataSources.Contains("Yahoo/Finnhub/Brave", StringComparison.OrdinalIgnoreCase)
                   || dataSources.Contains("Brave/Finnhub", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<(bool hasRetail, bool hasCOT, bool hasTech, bool hasNews)> ProcessInstrumentAsync(
            ApplicationDbContext db,
            TrailBlazerDataService dataService,
            TrailBlazerScoringEngine scoringEngine,
            Instrument instrument,
            List<TradeHelper.Models.EconomicHeatmapEntry> heatmapEntries,
            Dictionary<string, COTReport> cotBatch,
            IReadOnlyDictionary<string, (double longPct, double shortPct)> myFxBookSentiment,
            IReadOnlyDictionary<string, double> currencyOverrides,
            IReadOnlyDictionary<string, double> currencyStrength,
            IBreakoutSignalNotifier? breakoutNotifier,
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

            // Synthetic COT for Forex Crosses (e.g. GBPJPY) with no direct COT: base score - quote score from USD pairs
            double? syntheticCOTScore = null;
            if (cotReport == null && IsForexCross(instrument))
            {
                syntheticCOTScore = await GetSyntheticCOTScoreForForexCrossAsync(db, instrument.Name, logger);
                if (syntheticCOTScore.HasValue)
                    dataSources.Add("CFTC");
            }

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
            var fundamentalContext = BuildFundamentalContext(instrument, heatmapEntries, currencyOverrides.Count > 0 ? currencyOverrides : null);
            var hasFundamental = fundamentalContext.BaseData.Values.Any(v => v != 0) || fundamentalContext.QuoteData.Values.Any(v => v != 0);
            if (hasFundamental)
                dataSources.Add("FRED");

            var (newsSentimentScore, hasNewsSentiment, newsItems) = await dataService.FetchNewsSentimentScoreWithItemsAsync(instrument.Name, instrument.AssetClass, db);
            var effectiveNewsScore = newsSentimentScore;
            if (hasNewsSentiment)
            {
                dataSources.Add("Yahoo/Finnhub/Brave");
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
                if (latestScore != null && NewsDataSourceInScore(latestScore.DataSources))
                {
                    effectiveNewsScore = latestScore.NewsSentimentScore;
                    dataSources.Add("Yahoo/Finnhub/Brave");
                }
            }

            var hasRetail = dataSources.Contains("myfxbook");
            var hasNews = dataSources.Contains("Yahoo/Finnhub/Brave");
            var hasCOT = cotReport != null || syntheticCOTScore.HasValue;
            var weightContext = new ScoringWeightContext(
                instrument.AssetClass,
                hasCOT,
                hasRetail,
                hasFundamental,
                hasTechnical,
                hasNews);

            double? currencyStrengthScore = null;
            if (currencyStrength.Count > 0)
            {
                if (instrument.Type == "Currency" && instrument.Name.Length >= 6 && instrument.Name.All(char.IsLetter))
                {
                    var baseCcy = instrument.Name[..3];
                    var quoteCcy = instrument.Name[3..];
                    if (currencyStrength.TryGetValue(baseCcy, out var bStr) && currencyStrength.TryGetValue(quoteCcy, out var qStr))
                    {
                        currencyStrengthScore = Math.Clamp((bStr - qStr) * 0.5 + 5.0, 1.0, 10.0);
                        dataSources.Add("CurrencyStrength");
                    }
                }
                else if (((instrument.AssetClass ?? "").Equals("Metal", StringComparison.OrdinalIgnoreCase) || (instrument.AssetClass ?? "").Equals("Commodity", StringComparison.OrdinalIgnoreCase)) && (instrument.Name == "XAUUSD" || instrument.Name == "XAGUSD" || instrument.Name == "USOIL" || instrument.Name == "XPTUSD" || instrument.Name == "XPDUSD"))
                {
                    if (currencyStrength.TryGetValue("USD", out var usdStr))
                    {
                        currencyStrengthScore = Math.Clamp(10.0 - usdStr, 1.0, 10.0);
                        dataSources.Add("CurrencyStrength");
                    }
                }
            }

            var technicalsForScoring = hasTechnical ? technicalsFromDb! : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["RSI"] = 50, ["MACD"] = 0, ["MACDSignal"] = 0, ["EMA50"] = 0, ["EMA200"] = 0, ["StochasticK"] = 50 };
            var score = scoringEngine.CalculateScore(
                instrument.Id,
                fundamentalContext,
                cotReport,
                previousCot,
                retailSentiment,
                effectiveNewsScore,
                technicalsForScoring,
                dataSources,
                weightContext,
                currencyStrengthScore,
                syntheticCOTScore
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
            if (cotReport == null && !syntheticCOTScore.HasValue && latestScore != null)
            {
                score.COTScore = latestScore.COTScore;
                logger.LogDebug("TrailBlazer: {Instrument} COT score preserved from prior ({Score:F1})", instrument.Name, score.COTScore);
            }

            await dataService.ApplyBoxBreakoutTradeSetupAsync(score, instrument.Name);
            if (breakoutNotifier != null)
                await breakoutNotifier.TryNotifyStrongSignalAsync(score, instrument.Name);

            db.TrailBlazerScores.Add(score);
            var hasTechnicalSource = dataSources.Contains("YahooFinance") || dataSources.Contains("TwelveData") || dataSources.Contains("MarketStack") || dataSources.Contains("iTick") || dataSources.Contains("EODHD") || dataSources.Contains("FMP") || dataSources.Contains("NasdaqDataLink");
            logger.LogDebug("TrailBlazer: score saved for {Instrument} (sources: {Sources})", instrument.Name, string.Join(", ", dataSources));
            return (dataSources.Contains("myfxbook"), hasCOT, hasTechnicalSource, hasNews);
        }

        /// <summary>Builds fundamental context for scoring. Forex = base vs quote (relative strength). Commodity = USD data (weak USD = bullish gold).</summary>
        private static FundamentalContext BuildFundamentalContext(Instrument instrument, List<TradeHelper.Models.EconomicHeatmapEntry> heatmapEntries, IReadOnlyDictionary<string, double>? currencyOverrides = null)
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
                baseCcy = "USD";
                quoteCcy = "USD";
            }
            else
            {
                baseCcy = quoteCcy = "USD";
            }

            foreach (var ind in new[] { "GDP", "CPI", "Unemployment", "InterestRate", "PMI", "Treasury10Y", "PCE", "JOLTs", "JoblessClaims" })
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

            return new FundamentalContext(baseData, quoteData, isForex, isUsdCommodity, baseCcy, quoteCcy, currencyOverrides);
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

        /// <summary>True if instrument is a Forex cross (e.g. GBPJPY) with no USD in the pair.</summary>
        private static bool IsForexCross(Instrument instrument)
        {
            if (instrument.Type != "Currency" || instrument.Name.Length < 6 || !instrument.Name.All(char.IsLetter))
                return false;
            var baseCcy = instrument.Name[..3];
            var quoteCcy = instrument.Name[3..];
            return !string.Equals(baseCcy, "USD", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(quoteCcy, "USD", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>For Forex crosses (e.g. GBPJPY) with no direct COT: synthetic = base COT - quote COT from USD pairs.
        /// Base strength from XXXUSD (e.g. GBPUSD); quote strength from USDYYY (e.g. USDJPY high = bearish JPY, so quote strength = 10 - USDJPY_COT).</summary>
        private static async Task<double?> GetSyntheticCOTScoreForForexCrossAsync(ApplicationDbContext db, string symbol, ILogger logger)
        {
            var baseCcy = symbol[..3];
            var quoteCcy = symbol[3..];
            var basePair = baseCcy + "USD";
            var quotePair = "USD" + quoteCcy;

            var baseInstrumentId = await db.Instruments.Where(i => i.Name == basePair).Select(i => (int?)i.Id).FirstOrDefaultAsync();
            var quoteInstrumentId = await db.Instruments.Where(i => i.Name == quotePair).Select(i => (int?)i.Id).FirstOrDefaultAsync();
            if (!baseInstrumentId.HasValue || !quoteInstrumentId.HasValue)
            {
                logger.LogDebug("TrailBlazer: synthetic COT for {Symbol} skipped (missing instruments {Base} or {Quote})", symbol, basePair, quotePair);
                return null;
            }

            var baseScore = await db.TrailBlazerScores
                .Where(s => s.InstrumentId == baseInstrumentId.Value)
                .OrderByDescending(s => s.DateComputed)
                .Select(s => (double?)s.COTScore)
                .FirstOrDefaultAsync();
            var quotePairScore = await db.TrailBlazerScores
                .Where(s => s.InstrumentId == quoteInstrumentId.Value)
                .OrderByDescending(s => s.DateComputed)
                .Select(s => (double?)s.COTScore)
                .FirstOrDefaultAsync();

            if (!baseScore.HasValue || !quotePairScore.HasValue)
            {
                logger.LogDebug("TrailBlazer: synthetic COT for {Symbol} skipped (missing {Base} or {Quote} scores)", symbol, basePair, quotePair);
                return null;
            }

            // Base strength = basePair COT (e.g. GBPUSD high = bullish GBP). Quote strength: USDJPY high = bullish USD = bearish JPY, so quote strength = 10 - quotePairCOT.
            var baseStrength = baseScore.Value;
            var quoteStrength = 10.0 - quotePairScore.Value;
            var synthetic = baseStrength - quoteStrength;
            var normalized = Math.Clamp(5.0 + synthetic / 2.0, 1.0, 10.0);
            logger.LogDebug("TrailBlazer: synthetic COT for {Symbol}: {Base}={BaseScore:F1}, {Quote} strength={QuoteStr:F1} (from 10-{QuotePair}={QuotePairScore:F1}), synthetic={Syn:F1} -> {Norm:F1}",
                symbol, basePair, baseStrength, quoteCcy, quoteStrength, quotePair, quotePairScore.Value, synthetic, normalized);
            return Math.Round(normalized, 2);
        }
    }
}
