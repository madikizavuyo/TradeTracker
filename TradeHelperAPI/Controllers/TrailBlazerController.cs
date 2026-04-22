using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TradeHelper.Data;
using TradeHelper.Models;
using TradeHelper.Services;

namespace TradeHelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TrailBlazerController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IServiceProvider _serviceProvider;
        private readonly TrailBlazerDataService _dataService;
        private readonly TrailBlazerRefreshProgressService _progress;
        private readonly OecdDataService _oecdDataService;
        private readonly MLModelService _mlModelService;
        private readonly WorldBankDataService? _worldBankService;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TrailBlazerController> _logger;

        public TrailBlazerController(ApplicationDbContext context, IServiceProvider serviceProvider, TrailBlazerDataService dataService, TrailBlazerRefreshProgressService progress, OecdDataService oecdDataService, MLModelService mlModelService, IWebHostEnvironment env, IConfiguration configuration, ILogger<TrailBlazerController> logger, WorldBankDataService? worldBankService = null)
        {
            _context = context;
            _serviceProvider = serviceProvider;
            _dataService = dataService;
            _progress = progress;
            _oecdDataService = oecdDataService;
            _mlModelService = mlModelService;
            _worldBankService = worldBankService;
            _env = env;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("scores")]
        public async Task<IActionResult> GetScores()
        {
            var latestDate = await _context.TrailBlazerScores
                .MaxAsync(s => (DateTime?)s.DateComputed);

            if (latestDate == null)
                return Ok(Array.Empty<object>());

            var cutoff = latestDate.Value.AddHours(-1);

            var raw = await _context.TrailBlazerScores
                .Include(s => s.Instrument)
                .Where(s => s.DateComputed >= cutoff)
                .OrderByDescending(s => s.DateComputed)
                .ToListAsync();

            var scores = raw
                .GroupBy(s => s.InstrumentId)
                .Select(g => g.First())
                .OrderByDescending(s => s.OverallScore)
                .Select(s => new
                {
                    s.Id,
                    s.InstrumentId,
                    instrumentName = s.Instrument?.Name ?? "",
                    assetClass = s.Instrument?.AssetClass ?? "",
                    s.OverallScore,
                    s.Bias,
                    s.FundamentalScore,
                    s.SentimentScore,
                    s.TechnicalScore,
                    s.COTScore,
                    s.RetailSentimentScore,
                    s.NewsSentimentScore,
                    s.EconomicScore,
                    s.CurrencyStrengthScore,
                    s.DataSources,
                    s.DateComputed,
                    s.TechnicalDataDateCollected,
                    tradeSetupSignal = s.TradeSetupSignal,
                    tradeSetupDetail = s.TradeSetupDetail
                })
                .ToList();

            return Ok(scores);
        }

        [HttpGet("scores/{instrumentId}")]
        public async Task<IActionResult> GetScoreDetail(int instrumentId)
        {
            var score = await _context.TrailBlazerScores
                .Include(s => s.Instrument)
                .Where(s => s.InstrumentId == instrumentId)
                .OrderByDescending(s => s.DateComputed)
                .FirstOrDefaultAsync();

            if (score == null) return NotFound();

            return Ok(new
            {
                score.Id,
                score.InstrumentId,
                instrumentName = score.Instrument?.Name ?? "",
                assetClass = score.Instrument?.AssetClass ?? "",
                score.OverallScore,
                score.Bias,
                score.FundamentalScore,
                score.SentimentScore,
                score.TechnicalScore,
                score.COTScore,
                score.RetailSentimentScore,
                score.NewsSentimentScore,
                score.EconomicScore,
                score.DataSources,
                score.DateComputed,
                score.TechnicalDataDateCollected,
                tradeSetupSignal = score.TradeSetupSignal,
                tradeSetupDetail = score.TradeSetupDetail
            });
        }

        /// <summary>Test Gemini API connectivity. Returns OK or error details. Dev only.</summary>
        [AllowAnonymous]
        [HttpGet("analysis/test")]
        public async Task<IActionResult> TestAnalysisApi()
        {
            if (!_env.IsDevelopment()) return NotFound();
            try
            {
                var ctx = new InstrumentAnalysisContext(
                    "EURUSD", "ForexMajor", 6.2, "Bullish", 6.0, 5.5, 5.0, 55, 45, 5.0, 6.5,
                    "FRED,CFTC,myfxbook,TAAPI",
                    new List<HeatmapEntryForAnalysis> { new("USD", "GDP", 2.1, 2.0, "Positive") },
                    null);
                var result = await _mlModelService.GenerateInstrumentAnalysisAsync(ctx);
                return Ok(new { success = !string.IsNullOrEmpty(result), length = result?.Length ?? 0 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TestAnalysisApi failed");
                return StatusCode(500, new { message = ex.Message, type = ex.GetType().Name });
            }
        }

        /// <summary>AI-generated analysis of an instrument's TrailBlazer score and underlying data.</summary>
        [HttpGet("analysis/{instrumentId}")]
        public async Task<IActionResult> GetAnalysis(int instrumentId)
        {
            try
            {
                var score = await _context.TrailBlazerScores
                    .Include(s => s.Instrument)
                    .Where(s => s.InstrumentId == instrumentId)
                    .OrderByDescending(s => s.DateComputed)
                    .FirstOrDefaultAsync();

                if (score == null || score.Instrument == null)
                    return NotFound();

                var instrument = score.Instrument;
                string baseCcy, quoteCcy;
                if (instrument.Type == "Currency" && instrument.Name.Length >= 6 && instrument.Name.All(char.IsLetter))
                {
                    baseCcy = instrument.Name[..3];
                    quoteCcy = instrument.Name[3..];
                }
                else
                {
                    baseCcy = quoteCcy = "USD";
                }

                var currencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseCcy, quoteCcy };
                var heatmapEntries = await _context.EconomicHeatmapEntries
                    .Where(e => currencies.Contains(e.Currency))
                    .OrderBy(e => e.Currency)
                    .ThenBy(e => e.Indicator)
                    .Select(e => new HeatmapEntryForAnalysis(e.Currency, e.Indicator, e.Value, e.PreviousValue, e.Impact))
                    .ToListAsync();

                var cotReport = await _context.COTReports
                    .Where(c => c.Symbol == instrument.Name)
                    .OrderByDescending(c => c.ReportDate)
                    .FirstOrDefaultAsync();

                var outlookSnippets = await _dataService.FetchInstrumentOutlookAsync(instrument.Name, instrument.AssetClass);
                var webSnippets = outlookSnippets
                    .Select(s => new WebSnippetForAnalysis(s.Title, s.Description ?? "", s.Source))
                    .ToList();

                var ctx = new InstrumentAnalysisContext(
                    instrument.Name,
                    instrument.AssetClass ?? "",
                    score.OverallScore,
                    score.Bias,
                    score.FundamentalScore,
                    score.COTScore,
                    score.RetailSentimentScore,
                    score.RetailLongPct,
                    score.RetailShortPct,
                    score.NewsSentimentScore,
                    score.TechnicalScore,
                    score.DataSources,
                    heatmapEntries,
                    cotReport,
                    webSnippets.Count > 0 ? webSnippets : null);

                var analysis = await _mlModelService.GenerateInstrumentAnalysisAsync(ctx, instrumentId, score.DateComputed);
                if (string.IsNullOrEmpty(analysis))
                    return StatusCode(500, new { message = "AI analysis failed. Ensure Google:ApiKey is set in appsettings.json and the Generative Language API is enabled. Get a key at https://aistudio.google.com/apikey — check server logs for details." });

                return Ok(new { analysis });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TrailBlazer analysis failed for instrument {InstrumentId}", instrumentId);
                return StatusCode(500, new { message = $"AI analysis error: {ex.Message}" });
            }
        }

        [HttpGet("scores/history/{instrumentId}")]
        public async Task<IActionResult> GetScoreHistory(int instrumentId)
        {
            var history = await _context.TrailBlazerScores
                .Where(s => s.InstrumentId == instrumentId)
                .OrderBy(s => s.DateComputed)
                .Select(s => new
                {
                    s.OverallScore,
                    s.Bias,
                    s.FundamentalScore,
                    s.SentimentScore,
                    s.TechnicalScore,
                    s.DateComputed
                })
                .Take(90)
                .ToListAsync();

            return Ok(history);
        }

        /// <summary>Returns instruments that changed Bias recently, with when the change happened.</summary>
        [HttpGet("bias-changes")]
        public async Task<IActionResult> GetBiasChanges([FromQuery] int? lastHours = 48, [FromQuery] int? limit = 20)
        {
            var cutoff = DateTime.UtcNow.AddHours(-(lastHours ?? 48));
            var scoresInWindow = await _context.TrailBlazerScores
                .Include(s => s.Instrument)
                .Where(s => s.DateComputed >= cutoff)
                .OrderBy(s => s.InstrumentId)
                .ThenBy(s => s.DateComputed)
                .Select(s => new { s.InstrumentId, s.Instrument!.Name, s.Bias, s.OverallScore, s.DateComputed })
                .ToListAsync();

            var instrumentIds = scoresInWindow.Select(s => s.InstrumentId).Distinct().ToList();
            var prevScores = instrumentIds.Count > 0
                ? await _context.TrailBlazerScores
                    .Where(s => instrumentIds.Contains(s.InstrumentId) && s.DateComputed < cutoff)
                    .GroupBy(s => s.InstrumentId)
                    .Select(g => new { InstrumentId = g.Key, Bias = g.OrderByDescending(x => x.DateComputed).First().Bias })
                    .ToDictionaryAsync(x => x.InstrumentId, x => x.Bias)
                : new Dictionary<int, string>();

            var changes = new List<(int InstrumentId, string Name, string PreviousBias, string NewBias, double OverallScore, DateTime ChangedAt)>();
            var byInstrument = scoresInWindow.GroupBy(s => s.InstrumentId);
            foreach (var grp in byInstrument)
            {
                var ordered = grp.OrderBy(s => s.DateComputed).ToList();
                var prevBias = prevScores.TryGetValue(grp.Key, out var pb) ? pb : null;
                foreach (var curr in ordered)
                {
                    if (prevBias != null && prevBias != curr.Bias)
                    {
                        changes.Add((curr.InstrumentId, curr.Name, prevBias, curr.Bias, curr.OverallScore, curr.DateComputed));
                    }
                    prevBias = curr.Bias;
                }
            }

            var result = changes
                .OrderByDescending(c => c.ChangedAt)
                .Take(limit ?? 20)
                .Select(c => new { instrumentId = c.InstrumentId, instrumentName = c.Name, previousBias = c.PreviousBias, newBias = c.NewBias, overallScore = c.OverallScore, changedAt = c.ChangedAt })
                .ToList();

            return Ok(result);
        }

        /// <summary>Returns geopolitical/narrative currency overrides (e.g. CAD +0.5 for oil strength). Admin can set via POST.</summary>
        [Authorize(Roles = "Admin")]
        [HttpGet("currency-overrides")]
        public async Task<IActionResult> GetCurrencyOverrides()
        {
            const int prefixLen = 17; // "CurrencyOverride_".Length
            var raw = await _context.SystemSettings
                .Where(s => s.Key.StartsWith("CurrencyOverride_"))
                .Select(s => new { s.Key, s.Value })
                .ToListAsync();
            var settings = raw.Select(s => new { currency = s.Key.Substring(prefixLen), value = s.Value }).ToList();
            return Ok(settings);
        }

        /// <summary>Sets a currency override for scoring (e.g. CAD +0.5 when strong due to oil/geopolitics). Value: -2 to +2.</summary>
        [Authorize(Roles = "Admin")]
        [HttpPost("currency-overrides")]
        public async Task<IActionResult> SetCurrencyOverride([FromBody] CurrencyOverrideRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Currency))
                return BadRequest(new { message = "Currency is required (e.g. CAD, USD)" });
            var ccy = req.Currency.Trim().ToUpperInvariant();
            if (ccy.Length != 3)
                return BadRequest(new { message = "Currency must be 3 letters (e.g. CAD)" });
            var val = Math.Clamp(req.Value, -2.0, 2.0);
            var key = $"CurrencyOverride_{ccy}";
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            var now = DateTime.UtcNow;
            if (setting != null)
            {
                setting.Value = val.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                setting.UpdatedAt = now;
            }
            else
            {
                _context.SystemSettings.Add(new SystemSetting { Key = key, Value = val.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), UpdatedAt = now });
            }
            await _context.SaveChangesAsync();
            return Ok(new { currency = ccy, value = val, message = $"Override set. Next TrailBlazer refresh will apply {ccy} {(val >= 0 ? "+" : "")}{val:F2} to fundamental scoring." });
        }

        /// <summary>Returns currency strength scores. 80% from global economic news analysis (Gemini), 20% from fundamentals (GDP, CPI, etc). Higher = stronger.</summary>
        [HttpGet("currency-strength")]
        public async Task<IActionResult> GetCurrencyStrength()
        {
            var latestDate = await _context.EconomicHeatmapEntries
                .MaxAsync(e => (DateTime?)e.DateCollected);

            var batchCutoff = latestDate.HasValue ? latestDate.Value.AddMinutes(-10) : DateTime.MinValue;
            var entries = latestDate.HasValue
                ? await _context.EconomicHeatmapEntries.Where(e => e.DateCollected >= batchCutoff).ToListAsync()
                : new List<TradeHelper.Models.EconomicHeatmapEntry>();

            var fundamentalByCurrency = entries
                .GroupBy(e => e.Currency)
                .ToDictionary(g => g.Key, g =>
                {
                    var pos = g.Count(x => string.Equals(x.Impact, "Positive", StringComparison.OrdinalIgnoreCase));
                    var neg = g.Count(x => string.Equals(x.Impact, "Negative", StringComparison.OrdinalIgnoreCase));
                    var total = g.Count();
                    var raw = total > 0 ? 5.0 + (pos - neg) * 1.5 : 5.0;
                    return (strength: Math.Clamp(raw, 1.0, 10.0), pos, neg, total);
                });

            Dictionary<string, double>? newsScores = null;
            var newsCacheHours = _configuration.GetValue("TrailBlazer:CurrencyStrengthNewsCacheHours", 48);
            var newsSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "CurrencyStrengthNewsCache");
            var updatedSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "CurrencyStrengthNewsUpdatedAt");
            if (newsSetting != null && !string.IsNullOrEmpty(newsSetting.Value) && updatedSetting != null
                && DateTime.TryParse(updatedSetting.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var updated)
                && (DateTime.UtcNow - updated).TotalHours < newsCacheHours)
            {
                try
                {
                    newsScores = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(newsSetting.Value);
                }
                catch { /* ignore parse errors */ }
            }

            var allCurrencies = fundamentalByCurrency.Keys.Union(newsScores != null ? newsScores.Keys : Enumerable.Empty<string>()).Distinct().ToList();
            var byCurrency = allCurrencies.Select(ccy =>
            {
                var (fundStr, pos, neg, total) = fundamentalByCurrency.TryGetValue(ccy, out var f) ? f : (5.0, 0, 0, 0);
                var newsStr = newsScores != null && newsScores.TryGetValue(ccy, out var n) ? n : (double?)null;
                var strength = newsStr.HasValue
                    ? Math.Clamp(0.8 * newsStr.Value + 0.2 * fundStr, 1.0, 10.0)
                    : fundStr;
                return new { currency = ccy, strength = Math.Round(strength, 1), positiveCount = pos, negativeCount = neg, totalIndicators = total, hasNewsAnalysis = newsStr.HasValue };
            })
                .OrderByDescending(x => x.strength)
                .ToList();

            return Ok(byCurrency);
        }

        [HttpGet("heatmap")]
        public async Task<IActionResult> GetHeatmap()
        {
            var latestDate = await _context.EconomicHeatmapEntries
                .MaxAsync(e => (DateTime?)e.DateCollected);

            if (latestDate == null)
                return Ok(Array.Empty<object>());

            // Return latest batch only (entries from same collection run; window tolerates staggered writes)
            var batchCutoff = latestDate.Value.AddMinutes(-30);
            var entries = await _context.EconomicHeatmapEntries
                .Where(e => e.DateCollected >= batchCutoff)
                .Select(e => new
                {
                    e.Currency,
                    e.Indicator,
                    e.Value,
                    e.PreviousValue,
                    e.Impact,
                    e.DateCollected
                })
                .ToListAsync();

            return Ok(entries);
        }

        /// <summary>Rolling correlation (30/60 days) between USOIL and US500/US30. Uses FMP historical prices.</summary>
        [HttpGet("correlation/oil-index")]
        public async Task<IActionResult> GetOilIndexCorrelation([FromQuery] int? days30, [FromQuery] int? days60)
        {
            var window30 = days30 ?? 30;
            var window60 = days60 ?? 60;
            var result = await _dataService.ComputeOilIndexCorrelationAsync(window30, window60);
            return Ok(result);
        }

        /// <summary>Returns refresh-related errors and warnings from ApplicationLogs. Use for diagnosing TrailBlazer refresh failures.</summary>
        [HttpGet("logs/refresh-errors")]
        public async Task<IActionResult> GetRefreshLogs([FromQuery] int? hoursBack, [FromQuery] int? limit)
        {
            var cutoff = DateTime.UtcNow.AddHours(-(hoursBack ?? 168));
            var take = Math.Min(limit ?? 100, 200);
            var logs = await _context.ApplicationLogs
                .Where(l => l.Timestamp >= cutoff && l.Level != "Debug")
                .Where(l =>
                    l.Category.Contains("TrailBlazer") || l.Category.Contains("TwelveData") || l.Category.Contains("FmpService") ||
                    l.Category.Contains("MarketStack") || l.Category.Contains("Eodhd") || l.Category.Contains("iTick") ||
                    l.Category.Contains("ApiRateLimit") || l.Category.Contains("MLModel") ||
                    l.Message.Contains("refresh") || l.Message.Contains("blocked") || l.Message.Contains("429") ||
                    l.Message.Contains("failed") || l.Message.Contains("rate limit"))
                .OrderByDescending(l => l.Timestamp)
                .Take(take)
                .Select(l => new { l.Id, l.Timestamp, l.Level, l.Category, l.Message, l.Exception })
                .ToListAsync();
            return Ok(logs);
        }

        /// <summary>Relative strength: assets ranked by % price change over 5 and 20 days.</summary>
        [HttpGet("relative-strength")]
        public async Task<IActionResult> GetRelativeStrength([FromQuery] int? days5, [FromQuery] int? days20)
        {
            var ranking = await _dataService.GetRelativeStrengthRankingAsync(days5 ?? 5, days20 ?? 20);
            var result = ranking.Select(r => new { symbol = r.symbol, pctChange5d = Math.Round(r.pctChange5d, 2), pctChange20d = Math.Round(r.pctChange20d, 2) }).ToList();
            return Ok(result);
        }

        /// <summary>Test CFTC parser without saving. Returns count of parsed reports. Dev only.</summary>
        [AllowAnonymous]
        [HttpGet("cot/test-parse")]
        public async Task<IActionResult> TestCOTParse()
        {
            if (!_env.IsDevelopment()) return NotFound();
            var batch = await _dataService.FetchCOTReportBatchAsync();
            return Ok(new { count = batch.Count, symbols = batch.Keys.ToList() });
        }

        /// <summary>Test MyFXBook sentiment API. Returns parsed count and sample data. Dev only.</summary>
        [AllowAnonymous]
        [HttpGet("sentiment/test-myfxbook")]
        public async Task<IActionResult> TestMyFxBookSentiment()
        {
            if (!_env.IsDevelopment()) return NotFound();
            var (result, diagnostic) = await _dataService.FetchMyFxBookSentimentBatchWithDiagnosticAsync();
            var sample = result.Take(10).ToDictionary(k => k.Key, v => new { longPct = v.Value.longPct, shortPct = v.Value.shortPct });
            return Ok(new { count = result.Count, symbols = result.Keys.ToList(), sample, diagnostic });
        }

        /// <summary>Test OECD SDMX API – unemployment data. Returns parsed values by currency. Dev only.</summary>
        [AllowAnonymous]
        [HttpGet("oecd/test-unemployment")]
        public async Task<IActionResult> TestOecdUnemployment()
        {
            if (!_env.IsDevelopment()) return NotFound();
            var currencies = new[] { "USD", "GBP", "ZAR", "EUR" };
            var batch = await _oecdDataService.FetchUnemploymentBatchAsync(currencies);
            var single = await _oecdDataService.FetchUnemploymentRateAsync("USD");
            return Ok(new { batch, singleUsd = single, note = "OECD SDMX API: https://sdmx.oecd.org" });
        }

        /// <summary>Debug: returns raw text snippet from CFTC page to diagnose parsing. Dev only.</summary>
        [AllowAnonymous]
        [HttpGet("cot/debug-text")]
        public async Task<IActionResult> DebugCOTText()
        {
            if (!_env.IsDevelopment()) return NotFound();
            try
            {
                using var client = new HttpClient();
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.cftc.gov/dea/options/financial_lof.htm");
                req.Headers.TryAddWithoutValidation("User-Agent", "TradeTracker/1.0");
                var response = await client.SendAsync(req);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);
                var text = doc.DocumentNode.InnerText;
                var idx = text.IndexOf("CANADIAN DOLLAR", StringComparison.Ordinal);
                var snippet = idx >= 0 ? text.Substring(idx, Math.Min(800, text.Length - idx)) : text.Substring(0, Math.Min(1500, text.Length));
                return Ok(new { length = text.Length, snippet, hasCanadianDollar = idx >= 0 });
            }
            catch (Exception ex)
            {
                return Ok(new { error = ex.Message });
            }
        }

        /// <summary>Scrape COT data from CFTC and overwrite all existing COT data in the database. Sole source: https://www.cftc.gov/dea/options/financial_lof.htm. Admin only.</summary>
        [Authorize(Roles = "Admin")]
        [HttpGet("cot/scrape")]
        public async Task<IActionResult> ScrapeCOT()
        {
            var batch = await _dataService.FetchCOTReportBatchAsync();
            var reports = batch.Values.ToList();
            if (reports.Count == 0)
                return Ok(new { message = "No COT data parsed from CFTC" });

            var existing = await _context.COTReports.ToListAsync();
            _context.COTReports.RemoveRange(existing);
            await _context.COTReports.AddRangeAsync(reports);
            await _context.SaveChangesAsync();
            return Ok(new { message = $"Scraped and saved {reports.Count} COT reports", count = reports.Count, reportDate = reports.First().ReportDate });
        }

        [HttpGet("cot")]
        public async Task<IActionResult> GetCOT()
        {
            try
            {
                var rawCot = await _context.COTReports
                    .OrderByDescending(c => c.ReportDate)
                    .ToListAsync();

                var latestReports = rawCot
                    .GroupBy(c => c.Symbol)
                    .Select(g => g.First())
                    .Select(r => new
                    {
                        symbol = r.Symbol,
                        commercialLong = r.CommercialLong,
                        commercialShort = r.CommercialShort,
                        nonCommercialLong = r.NonCommercialLong,
                        nonCommercialShort = r.NonCommercialShort,
                        openInterest = r.OpenInterest,
                        netNonCommercial = r.NonCommercialLong - r.NonCommercialShort,
                        reportDate = r.ReportDate.ToString("o")
                    })
                    .ToList();

                return Ok(latestReports);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetCOT failed");
                return Ok(Array.Empty<object>());
            }
        }

        /// <summary>Import retail sentiment from myfxbook-retail-sentiment.json file (workspace root). Saves to database. Admin only.</summary>
        [Authorize(Roles = "Admin")]
        [HttpPost("sentiment/import-from-file")]
        public async Task<IActionResult> ImportSentimentFromFile()
        {
            var filePath = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "myfxbook-retail-sentiment.json"));
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { message = $"File not found: {filePath}" });

            using var stream = System.IO.File.OpenRead(filePath);
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("symbols", out var symbols) || symbols.ValueKind != JsonValueKind.Array)
                return BadRequest(new { message = "Invalid JSON: missing symbols array" });

            var instruments = await _context.Instruments.ToDictionaryAsync(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);
            var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["JPN225"] = "JP225", ["GER30"] = "DE40", ["NAS100"] = "US100"
            };
            var updated = 0;

            foreach (var item in symbols.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : item.TryGetProperty("symbol", out var s) ? s.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;
                var shortPct = item.TryGetProperty("shortPercentage", out var sp) && sp.TryGetDouble(out var spVal) ? spVal : -1.0;
                var longPct = item.TryGetProperty("longPercentage", out var lp) && lp.TryGetDouble(out var lpVal) ? lpVal : (shortPct >= 0 ? 100.0 - shortPct : -1.0);
                if (shortPct < 0 || shortPct > 100 || longPct < 0 || longPct > 100) continue;
                if (Math.Abs(longPct - 50) < 1 && Math.Abs(shortPct - 50) < 1) continue; // Skip 50/50 — do not overwrite good data with default

                var key = name.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
                if (key.Length < 4) continue;
                var lookupKey = aliasMap.TryGetValue(key, out var alias) ? alias : key;
                if (!instruments.TryGetValue(lookupKey, out var instrument)) continue;
                var retailScore = CalculateRetailSentimentScore(longPct);
                var dataSources = JsonSerializer.Serialize(new[] { "myfxbook", "import" });

                var existing = await _context.TrailBlazerScores
                    .Where(x => x.InstrumentId == instrument.Id)
                    .OrderByDescending(x => x.DateComputed)
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    existing.RetailLongPct = Math.Round(longPct, 2);
                    existing.RetailShortPct = Math.Round(shortPct, 2);
                    existing.RetailSentimentScore = Math.Round(retailScore, 2);
                    existing.DataSources = dataSources;
                    existing.DateComputed = DateTime.UtcNow;
                }
                else
                {
                    _context.TrailBlazerScores.Add(new TradeHelper.Models.TrailBlazerScore
                    {
                        InstrumentId = instrument.Id,
                        OverallScore = 5.0,
                        Bias = "Neutral",
                        FundamentalScore = 5.0,
                        SentimentScore = retailScore,
                        TechnicalScore = 5.0,
                        COTScore = 5.0,
                        RetailSentimentScore = Math.Round(retailScore, 2),
                        RetailLongPct = Math.Round(longPct, 2),
                        RetailShortPct = Math.Round(shortPct, 2),
                        EconomicScore = 5.0,
                        DataSources = dataSources,
                        DateComputed = DateTime.UtcNow
                    });
                }
                updated++;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"Imported retail sentiment for {updated} instruments", updated });
        }

        /// <summary>Manually fetch market sentiment from MyFXBook community outlook. Saves to database and returns live data. Admin only.</summary>
        [Authorize(Roles = "Admin")]
        [HttpGet("sentiment/scrape")]
        public async Task<IActionResult> ScrapeSentiment()
        {
            var result = await _dataService.GetManualSentimentDataAsync();
            var instruments = await _context.Instruments.Where(i => i.Type == "Currency").ToDictionaryAsync(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);

            foreach (var item in result.Combined)
            {
                var symbol = item.Symbol;
                var longPct = item.LongPct;
                var shortPct = item.ShortPct;
                var source = item.Source;

                if (!instruments.TryGetValue(symbol, out var instrument)) continue;
                if (Math.Abs(longPct - 50) < 1 && Math.Abs(shortPct - 50) < 1) continue; // Skip 50/50 — do not overwrite good data with default

                var retailScore = CalculateRetailSentimentScore(longPct);
                var dataSources = System.Text.Json.JsonSerializer.Serialize(new[] { "scrape", source });

                var existing = await _context.TrailBlazerScores
                    .Where(s => s.InstrumentId == instrument.Id)
                    .OrderByDescending(s => s.DateComputed)
                    .FirstOrDefaultAsync();

                if (existing != null && (DateTime.UtcNow - existing.DateComputed).TotalHours < 24)
                {
                    existing.RetailLongPct = Math.Round(longPct, 2);
                    existing.RetailShortPct = Math.Round(shortPct, 2);
                    existing.RetailSentimentScore = Math.Round(retailScore, 2);
                    existing.DataSources = dataSources;
                    existing.DateComputed = DateTime.UtcNow;
                }
                else
                {
                    _context.TrailBlazerScores.Add(new TradeHelper.Models.TrailBlazerScore
                    {
                        InstrumentId = instrument.Id,
                        OverallScore = 5.0,
                        Bias = "Neutral",
                        FundamentalScore = 5.0,
                        SentimentScore = retailScore,
                        TechnicalScore = 5.0,
                        COTScore = 5.0,
                        RetailSentimentScore = Math.Round(retailScore, 2),
                        RetailLongPct = Math.Round(longPct, 2),
                        RetailShortPct = Math.Round(shortPct, 2),
                        EconomicScore = 5.0,
                        DataSources = dataSources,
                        DateComputed = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(result);
        }

        private static double CalculateRetailSentimentScore(double longPct)
        {
            if (longPct >= 70) return 2.5;
            if (longPct >= 60) return 4.0;
            if (longPct <= 30) return 8.0;
            if (longPct <= 40) return 6.5;
            return 5.0;
        }

        [HttpGet("sentiment")]
        public async Task<IActionResult> GetSentiment()
        {
            var latestDate = await _context.TrailBlazerScores
                .MaxAsync(s => (DateTime?)s.DateComputed);

            if (latestDate == null)
                return Ok(Array.Empty<object>());

            var cutoff = latestDate.Value.AddHours(-1);

            var rawSentiment = await _context.TrailBlazerScores
                .Include(s => s.Instrument)
                .Where(s => s.DateComputed >= cutoff && s.Instrument != null && s.Instrument.Type == "Currency")
                .OrderByDescending(s => s.DateComputed)
                .ToListAsync();

            var sentiment = rawSentiment
                .GroupBy(s => s.InstrumentId)
                .Select(g => g.First())
                .Select(s => new
                {
                    symbol = s.Instrument!.Name,
                    s.RetailSentimentScore,
                    longPct = s.RetailLongPct,
                    shortPct = s.RetailShortPct
                })
                .ToList();

            return Ok(sentiment);
        }

        /// <summary>Returns news for an instrument. Reads from DB first (last 24h); if empty, fetches, stores, and returns.</summary>
        [HttpGet("news/{symbol}")]
        public async Task<IActionResult> GetNews(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { message = "Symbol is required" });

            var normalized = symbol.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var fromDb = await _context.NewsArticles
                .Where(n => n.Symbol == normalized && n.DateCollected >= cutoff)
                .OrderByDescending(n => n.PublishedAt)
                .Take(5)
                .Select(n => new { headline = n.Headline, summary = n.Summary, source = n.Source, url = n.Url, imageUrl = n.ImageUrl, publishedAt = n.PublishedAt })
                .ToListAsync();

            if (fromDb.Count > 0)
                return Ok(fromDb);

            var instrument = await _context.Instruments.FirstOrDefaultAsync(i => i.Name == normalized || i.Name == symbol);
            var fetched = await _dataService.FetchNewsForSymbolAsync(normalized, instrument?.AssetClass);
            var now = DateTime.UtcNow;
            foreach (var item in fetched)
            {
                _context.NewsArticles.Add(new NewsArticle
                {
                    Symbol = NewsArticle.TruncateTo(normalized, NewsArticle.MaxSymbolLength),
                    Headline = NewsArticle.TruncateTo(item.Headline, NewsArticle.MaxHeadlineLength),
                    Summary = NewsArticle.TruncateTo(item.Summary, NewsArticle.MaxSummaryLength),
                    Source = NewsArticle.TruncateTo(item.Source, NewsArticle.MaxSourceLength),
                    Url = NewsArticle.TruncateTo(item.Url, NewsArticle.MaxUrlLength),
                    ImageUrl = NewsArticle.TruncateTo(item.ImageUrl, NewsArticle.MaxImageUrlLength),
                    PublishedAt = item.PublishedAt,
                    DateCollected = now
                });
            }
            await _context.SaveChangesAsync();
            return Ok(fetched.Take(5));
        }

        /// <summary>Returns market outlook for an instrument. Reads from DB first (last 6h); if empty, fetches, stores, and returns.</summary>
        [HttpGet("outlook/{symbol}")]
        public async Task<IActionResult> GetOutlook(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { message = "Symbol is required" });

            var normalized = symbol.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
            var cutoff = DateTime.UtcNow.AddHours(-6);
            var fromDb = await _context.MarketOutlooks
                .Where(m => m.Symbol == normalized && m.DateCollected >= cutoff)
                .Select(m => new { title = m.Title, description = m.Description, source = m.Source, url = m.Url })
                .ToListAsync();

            if (fromDb.Count > 0)
                return Ok(fromDb);

            var instrument = await _context.Instruments.FirstOrDefaultAsync(i => i.Name == normalized || i.Name == symbol);
            var fetched = await _dataService.FetchInstrumentOutlookAsync(normalized, instrument?.AssetClass);
            var now = DateTime.UtcNow;
            foreach (var item in fetched)
            {
                _context.MarketOutlooks.Add(new TradeHelper.Models.MarketOutlook
                {
                    Symbol = normalized,
                    Title = item.Title,
                    Description = item.Description ?? "",
                    Source = item.Source ?? "",
                    Url = item.Url,
                    DateCollected = now
                });
            }
            await _context.SaveChangesAsync();
            return Ok(fetched);
        }

        /// <summary>Returns latest technical indicators from DB for an instrument (RSI, SMA14, SMA50, EMA50, EMA200).</summary>
        [HttpGet("technical/{symbol}")]
        public async Task<IActionResult> GetTechnicalIndicators(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { message = "Symbol is required" });

            var normalized = symbol.Replace("/", "").Replace("_", "").Replace(" ", "").ToUpperInvariant();
            var instrument = await _context.Instruments.FirstOrDefaultAsync(i => i.Name == normalized || i.Name == symbol);
            if (instrument == null)
                return NotFound(new { message = "Instrument not found" });

            var latest = await _context.TechnicalIndicators
                .Where(t => t.InstrumentId == instrument.Id)
                .OrderByDescending(t => t.DateCollected)
                .FirstOrDefaultAsync();

            if (latest == null)
                return Ok(new { rsi = (double?)null, sma14 = (double?)null, sma50 = (double?)null, ema50 = (double?)null, ema200 = (double?)null, dateCollected = (DateTime?)null });

            return Ok(new
            {
                rsi = latest.RSI,
                sma14 = latest.SMA14,
                sma50 = latest.SMA50,
                ema50 = latest.EMA50,
                ema200 = latest.EMA200,
                dateCollected = latest.DateCollected
            });
        }

        [HttpGet("top-setups")]
        public async Task<IActionResult> GetTopSetups()
        {
            var latestDate = await _context.TrailBlazerScores
                .MaxAsync(s => (DateTime?)s.DateComputed);

            if (latestDate == null)
                return Ok(new { bullish = Array.Empty<object>(), bearish = Array.Empty<object>() });

            var cutoff = latestDate.Value.AddHours(-1);

            var rawSetups = await _context.TrailBlazerScores
                .Include(s => s.Instrument)
                .Where(s => s.DateComputed >= cutoff)
                .OrderByDescending(s => s.DateComputed)
                .ToListAsync();

            var allScores = rawSetups
                .GroupBy(s => s.InstrumentId)
                .Select(g => g.First())
                .OrderByDescending(s => s.OverallScore)
                .Select(s => new
                {
                    s.InstrumentId,
                    instrumentName = s.Instrument?.Name ?? "",
                    assetClass = s.Instrument?.AssetClass ?? "",
                    s.OverallScore,
                    s.Bias,
                    s.FundamentalScore,
                    s.SentimentScore,
                    s.TechnicalScore,
                    s.DateComputed
                })
                .ToList();

            var bullish = allScores.Where(s => s.Bias == "Bullish").Take(5).ToList();
            var bearish = allScores.Where(s => s.Bias == "Bearish").OrderBy(s => s.OverallScore).Take(5).ToList();

            return Ok(new { bullish, bearish });
        }

        /// <summary>Returns the timestamp of the last successful data refresh (max DateComputed from TrailBlazerScores).</summary>
        [HttpGet("refresh/last")]
        public async Task<IActionResult> GetLastRefresh()
        {
            var last = await _context.TrailBlazerScores.MaxAsync(s => (DateTime?)s.DateComputed);
            return Ok(new { lastSuccessfulRefresh = last });
        }

        /// <summary>Poll refresh progress. Returns status, step, message, percent. Poll every 2–3 seconds while refreshing.</summary>
        [HttpGet("refresh/status")]
        public IActionResult GetRefreshStatus()
        {
            var p = _progress.GetProgress();
            return Ok(new
            {
                status = p.Status,
                step = p.Step,
                message = p.Message,
                current = p.Current,
                total = p.Total,
                percent = p.Percent,
                completedAt = p.CompletedAt,
                error = p.Error
            });
        }

        /// <summary>Diagnostic for a specific instrument's Asset Scanner signal: full analyzer intermediates (fib levels, swing H/L, touch flags, S/R, trendline) + email notifier state (last alert, recipients, SMTP config) + recent Breakout alert logs. Admin only.</summary>
        [Authorize(Roles = "Admin")]
        [HttpGet("signal-diagnostic/{symbol}")]
        public async Task<IActionResult> SignalDiagnostic(string symbol)
        {
            var upper = (symbol ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(upper))
                return BadRequest(new { message = "symbol required" });

            var instrument = await _context.Instruments.FirstOrDefaultAsync(i => i.Name == upper);
            if (instrument == null)
                return NotFound(new { message = $"Instrument {upper} not found" });

            var latestScore = await _context.TrailBlazerScores
                .Where(s => s.InstrumentId == instrument.Id)
                .OrderByDescending(s => s.DateComputed)
                .FirstOrDefaultAsync();

            if (latestScore == null)
                return NotFound(new { message = $"No TrailBlazerScore for {upper} yet" });

            var diag = await _dataService.DiagnoseSignalAsync(upper, latestScore.OverallScore);

            var alertPrefix = $"BreakoutAlertSent_{upper}_";
            var alertRows = await _context.SystemSettings
                .Where(s => s.Key.StartsWith(alertPrefix))
                .ToListAsync();
            var alertHistory = alertRows.Select(r =>
            {
                DateTime? when = DateTime.TryParse(r.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
                return new
                {
                    signal = r.Key.Substring(alertPrefix.Length).Replace("_", " "),
                    lastSentUtc = when,
                    hoursSince = when.HasValue ? (DateTime.UtcNow - when.Value).TotalHours : (double?)null
                };
            }).OrderByDescending(x => x.lastSentUtc).ToList();

            var recipientCount = await _context.Users.AsNoTracking()
                .Where(u => u.Email != null && u.Email != "")
                .Select(u => u.Email!)
                .Distinct()
                .CountAsync();

            var fromEmail = _configuration["Email:From"];
            var smtpHost = _configuration["Email:SmtpHost"];
            var smtpConfigured = !string.IsNullOrWhiteSpace(fromEmail) && !string.IsNullOrWhiteSpace(smtpHost);

            var since = DateTime.UtcNow.AddHours(-72);
            var logs = await _context.ApplicationLogs
                .Where(l => l.Timestamp >= since)
                .Where(l => l.Message.Contains(upper) || l.Category.Contains("BreakoutSignalNotifier") || l.Category.Contains("BrevoEmailService"))
                .OrderByDescending(l => l.Timestamp)
                .Take(40)
                .Select(l => new { l.Timestamp, l.Level, l.Category, l.Message, l.Exception })
                .ToListAsync();

            return Ok(new
            {
                instrument = upper,
                latestScore = new
                {
                    latestScore.OverallScore,
                    latestScore.Bias,
                    latestScore.TradeSetupSignal,
                    latestScore.TradeSetupDetail,
                    latestScore.DateComputed,
                    latestScore.DataSources
                },
                analyzerDiagnostic = diag,
                emailStatus = new
                {
                    fromEmail,
                    smtpHost,
                    smtpConfigured,
                    fromName = _configuration["Email:FromName"],
                    usersWithEmail = recipientCount,
                    fallbackAlertTo = _configuration["TrailBlazer:SignalAlertEmail"] ?? _configuration["Email:AlertTo"],
                    alertReversalBuyEnabled = _configuration.GetValue("TrailBlazer:EmailReversalBuyAlerts", true)
                },
                alertHistory,
                recentLogs = logs
            });
        }

        /// <summary>Manually trigger TrailBlazer refresh. useBrave=true forces Brave for currency strength on demand; default false avoids Brave token use. Cooldown after a Brave run is TrailBlazer:BraveCooldownHours (default 48).</summary>
        [Authorize(Roles = "Admin")]
        [HttpPost("refresh")]
        public Task<IActionResult> Refresh([FromQuery] bool useBrave = false)
        {
            try
            {
                var bgService = _serviceProvider.GetServices<IHostedService>()
                    .OfType<TrailBlazerBackgroundService>()
                    .FirstOrDefault();

                if (bgService != null)
                {
                    TrailBlazerDataService.SetForceBraveForRefresh(useBrave);
                    _logger.LogInformation("TrailBlazer: Manual refresh triggered via API (useBrave={UseBrave})", useBrave);
                    _ = Task.Run(() => bgService.RunRefreshCycleAsync());
                    return Task.FromResult<IActionResult>(Ok(new { message = "TrailBlazer refresh started", useBrave }));
                }

                _logger.LogError("TrailBlazer: Refresh failed - background service not found");
                return Task.FromResult<IActionResult>(StatusCode(500, new { message = "Background service not found" }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TrailBlazer: Manual refresh failed");
                return Task.FromResult<IActionResult>(StatusCode(500, new { message = $"Refresh failed: {ex.Message}" }));
            }
        }
    }

    public class CurrencyOverrideRequest
    {
        public string Currency { get; set; } = "";
        public double Value { get; set; }
    }
}
