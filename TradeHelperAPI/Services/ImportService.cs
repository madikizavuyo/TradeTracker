// ImportService.cs – Service for parsing and importing trade files
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using OfficeOpenXml;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using TradeHelper.Data;
using TradeHelper.Models;
using Microsoft.EntityFrameworkCore;

namespace TradeHelper.Services
{
    public class ImportService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ImportService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly CurrencyService _currencyService;
        private HashSet<string> _importedInBatch = new();
        
        private string GetDebugLogPath()
        {
            var logDir = Path.Combine(Directory.GetCurrentDirectory(), ".cursor");
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);
            return Path.Combine(logDir, "debug.log");
        }

        public ImportService(ApplicationDbContext context, ILogger<ImportService> logger,
            IServiceProvider serviceProvider, CurrencyService currencyService)
        {
            _context = context;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _currencyService = currencyService;
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // ── Shared column-name aliases for broker Excel files ──────────────────
        private static readonly Dictionary<string, string> _colAliases =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["time"] = "entryTime", ["open time"] = "entryTime",
            ["entry time"] = "entryTime", ["date"] = "entryTime",
            ["open date"] = "entryTime", ["datetime"] = "entryTime",

            ["position"] = "positionId", ["ticket"] = "positionId",
            ["order"] = "positionId", ["deal"] = "positionId", ["#"] = "positionId",

            ["symbol"] = "symbol", ["instrument"] = "symbol",
            ["pair"] = "symbol", ["market"] = "symbol", ["item"] = "symbol",

            ["type"] = "type", ["direction"] = "type", ["side"] = "type",

            ["volume"] = "volume", ["lots"] = "volume",
            ["size"] = "volume", ["lot size"] = "volume", ["quantity"] = "volume",

            ["price"] = "price",
            ["open price"] = "entryPrice", ["entry price"] = "entryPrice",
            ["close price"] = "exitPrice", ["exit price"] = "exitPrice",

            ["s / l"] = "stopLoss", ["stop loss"] = "stopLoss",
            ["sl"] = "stopLoss", ["s/l"] = "stopLoss",

            ["t / p"] = "takeProfit", ["take profit"] = "takeProfit",
            ["tp"] = "takeProfit", ["t/p"] = "takeProfit",

            ["commission"] = "commission", ["comm"] = "commission",
            ["swap"] = "swap", ["rollover"] = "swap", ["financing"] = "swap",

            ["profit"] = "profit", ["p/l"] = "profit", ["pnl"] = "profit",
            ["profit/loss"] = "profit", ["net p/l"] = "profit",
        };

        private static readonly HashSet<string> _sectionMarkers =
            new(StringComparer.OrdinalIgnoreCase)
        {
            "Orders", "Deals", "Open Positions", "Results", "Working Orders",
            "Financing", "Deposits", "Summary", "Balance", "Positions"
        };

        /// <summary>
        /// Build a canonical-name → column-index map from a header row.
        /// Handles duplicates like "Time" and "Price" that appear for both entry and exit.
        /// </summary>
        private static Dictionary<string, int> BuildColumnMap(ExcelWorksheet ws, int headerRow)
        {
            var map = new Dictionary<string, int>();
            bool seenFirstTime = false, seenFirstPrice = false;

            for (int col = 1; col <= ws.Dimension.End.Column; col++)
            {
                var h = ws.Cells[headerRow, col].Value?.ToString()?.Trim();
                if (string.IsNullOrEmpty(h) || !_colAliases.TryGetValue(h, out var canon))
                    continue;

                if (canon == "entryTime")
                {
                    if (!seenFirstTime) { map["entryTime"] = col; seenFirstTime = true; }
                    else if (!map.ContainsKey("exitTime")) map["exitTime"] = col;
                }
                else if (canon == "price")
                {
                    if (!seenFirstPrice) { map["entryPrice"] = col; seenFirstPrice = true; }
                    else if (!map.ContainsKey("exitPrice")) map["exitPrice"] = col;
                }
                else if (!map.ContainsKey(canon))
                {
                    map[canon] = col;
                }
            }
            return map;
        }

        /// <summary>
        /// Scan the first 30 rows for one that looks like a trades/positions header
        /// (must have at least entryTime + symbol + one of type/profit).
        /// </summary>
        private static int FindBrokerTradesHeaderRow(ExcelWorksheet ws)
        {
            for (int row = 1; row <= Math.Min(30, ws.Dimension.End.Row); row++)
            {
                var map = BuildColumnMap(ws, row);
                if (map.ContainsKey("entryTime") && map.ContainsKey("symbol") &&
                    (map.ContainsKey("type") || map.ContainsKey("profit")))
                    return row;
            }
            return -1;
        }

        private static readonly string[] _excelDateFormats =
        {
            "yyyy.MM.dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss",
            "dd/MM/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss",
            "dd/MM/yyyy HH:mm",    "MM/dd/yyyy HH:mm",
            "yyyy.MM.dd",          "yyyy-MM-dd",
            "dd/MM/yyyy",          "MM/dd/yyyy"
        };

        private static bool TryParseExcelDateTime(object? value, out DateTime result)
        {
            result = default;
            if (value == null) return false;
            if (value is DateTime dt) { result = dt; return true; }
            if (value is double d) { result = DateTime.FromOADate(d); return true; }

            var s = value.ToString();
            if (string.IsNullOrWhiteSpace(s)) return false;

            return DateTime.TryParseExact(s, _excelDateFormats,
                       CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
                || DateTime.TryParse(s, CultureInfo.InvariantCulture,
                       DateTimeStyles.None, out result);
        }

        /// <summary>
        /// Checks DB and current in-memory batch to prevent duplicate imports.
        /// Matches on UserId + Instrument + Type + EntryPrice + DateTime (±1 min).
        /// </summary>
        private async Task<bool> IsDuplicateTradeAsync(string userId, Trade trade)
        {
            var batchKey = $"{userId}|{trade.Instrument}|{trade.Type}|{trade.EntryPrice}|{trade.DateTime:yyyyMMddHHmm}";
            if (!_importedInBatch.Add(batchKey))
                return true;

            var oneMinBefore = trade.DateTime.AddMinutes(-1);
            var oneMinAfter = trade.DateTime.AddMinutes(1);
            return await _context.Trades.AnyAsync(t =>
                t.UserId == userId &&
                t.Instrument == trade.Instrument &&
                t.Type == trade.Type &&
                t.EntryPrice == trade.EntryPrice &&
                t.DateTime >= oneMinBefore &&
                t.DateTime <= oneMinAfter);
        }

        public async Task<ImportResult> ImportTradesFromFileAsync(
            Stream fileStream,
            string fileName,
            string userId,
            string? brokerName = null,
            string? currency = null,
            int? strategyId = null)
        {
            _importedInBatch.Clear();

            var result = new ImportResult
            {
                TradesImported = 0,
                TradesSkipped = 0,
                TradesFailed = 0,
                Errors = new List<string>()
            };

            try
            {
                var extension = Path.GetExtension(fileName)?.ToLowerInvariant();

                switch (extension)
                {
                    case ".csv":
                        await ImportFromCsvAsync(fileStream, userId, brokerName, currency, strategyId, result);
                        break;
                    case ".xlsx":
                    case ".xls":
                        await ImportFromExcelAsync(fileStream, userId, brokerName, currency, strategyId, result);
                        break;
                    case ".pdf":
                        await ImportFromPdfAsync(fileStream, userId, brokerName, currency, strategyId, result);
                        break;
                    default:
                        result.Errors.Add($"Unsupported file type: {extension}");
                        return result;
                }

                // ── Apply display-currency conversion to newly added trades ──
                var userSettings = await _context.UserSettings
                    .FirstOrDefaultAsync(s => s.UserId == userId);
                var displayCurrency = userSettings?.DisplayCurrency ?? "USD";
                var displaySymbol = userSettings?.DisplayCurrencySymbol ?? "$";

                var pendingTrades = _context.ChangeTracker
                    .Entries<Trade>()
                    .Where(e => e.State == EntityState.Added)
                    .Select(e => e.Entity)
                    .ToList();

                foreach (var trade in pendingTrades)
                {
                    trade.DisplayCurrency = displayCurrency;
                    trade.DisplayCurrencySymbol = displaySymbol;

                    if (trade.ProfitLoss.HasValue &&
                        !string.Equals(trade.Currency, displayCurrency, StringComparison.OrdinalIgnoreCase))
                    {
                        trade.ProfitLossDisplay = await _currencyService.ConvertAsync(
                            trade.ProfitLoss.Value, trade.Currency, displayCurrency);
                    }

                    result.DetectedCurrency ??= trade.Currency;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing trades from file {FileName}", fileName);
                result.Errors.Add($"Import failed: {ex.Message}");
            }

            return result;
        }

        private async Task ImportFromCsvAsync(
            Stream fileStream,
            string userId,
            string? brokerName,
            string? currency,
            int? strategyId,
            ImportResult result)
        {
            using var reader = new StreamReader(fileStream);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null
            };

            using var csv = new CsvReader(reader, config);
            
            // Try to read header
            if (!await csv.ReadAsync())
                return;

            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? Array.Empty<string>();

            // Common column name mappings
            var columnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "instrument", "instrument" },
                { "symbol", "instrument" },
                { "pair", "instrument" },
                { "entryprice", "entryprice" },
                { "openprice", "entryprice" },
                { "exitprice", "exitprice" },
                { "closeprice", "exitprice" },
                { "stoploss", "stoploss" },
                { "sl", "stoploss" },
                { "takeprofit", "takeprofit" },
                { "tp", "takeprofit" },
                { "profit", "profitloss" },
                { "pnl", "profitloss" },
                { "profitloss", "profitloss" },
                { "date", "datetime" },
                { "opentime", "datetime" },
                { "datetime", "datetime" },
                { "closetime", "exitdatetime" },
                { "exittime", "exitdatetime" },
                { "exitdatetime", "exitdatetime" },
                { "type", "type" },
                { "direction", "type" },
                { "status", "status" },
                { "lotsize", "lotsize" },
                { "volume", "lotsize" },
                { "size", "lotsize" },
                { "notes", "notes" },
                { "comment", "notes" }
            };

            // Find column indices
            var columnIndices = new Dictionary<string, int>();
            for (int i = 0; i < headers.Length; i++)
            {
                var header = headers[i]?.Trim() ?? "";
                foreach (var map in columnMap)
                {
                    if (header.Equals(map.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        columnIndices[map.Value] = i;
                        break;
                    }
                }
            }

            // Read data rows
            while (await csv.ReadAsync())
            {
                try
                {
                    var trade = ParseTradeFromRow(csv, headers, columnIndices, userId, brokerName, currency, strategyId);
                    
                    if (trade != null)
                    {
                        if (await IsDuplicateTradeAsync(userId, trade))
                            result.TradesSkipped++;
                        else
                        {
                            _context.Trades.Add(trade);
                            result.TradesImported++;
                        }
                    }
                    else
                    {
                        result.TradesFailed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse trade from CSV row");
                    result.TradesFailed++;
                }
            }
        }

        private async Task ImportFromExcelAsync(
            Stream fileStream,
            string userId,
            string? brokerName,
            string? currency,
            int? strategyId,
            ImportResult result)
        {
            using var package = new ExcelPackage(fileStream);
            var worksheet = package.Workbook.Worksheets[0];

            if (worksheet.Dimension == null)
                return;

            // Detect broker trade report: known titles or any row with trade-like headers
            var firstCell = worksheet.Cells[1, 1].Value?.ToString()?.Trim() ?? "";
            var knownTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Trade History Report", "Trade Report", "Trading Report",
                "Account Report", "Account Statement", "Statement"
            };

            if (knownTitles.Contains(firstCell))
            {
                _logger.LogInformation("Detected broker trade report: '{Title}'", firstCell);
                await ImportFromBrokerExcelAsync(worksheet, userId, brokerName, currency, strategyId, result);
                return;
            }

            // Fallback: scan for any row that looks like a positions/trades header
            var detectedHeader = FindBrokerTradesHeaderRow(worksheet);
            if (detectedHeader > 0)
            {
                _logger.LogInformation("Detected trade data header at row {Row}", detectedHeader);
                await ImportFromBrokerExcelAsync(worksheet, userId, brokerName, currency, strategyId, result, detectedHeader);
                return;
            }

            var startRow = 1;
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Read header row
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var headerValue = worksheet.Cells[startRow, col].Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(headerValue))
                {
                    headers[headerValue] = col;
                }
            }

            // Column mappings (same as CSV)
            var columnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "instrument", "instrument" },
                { "symbol", "instrument" },
                { "pair", "instrument" },
                { "entryprice", "entryprice" },
                { "openprice", "entryprice" },
                { "exitprice", "exitprice" },
                { "closeprice", "exitprice" },
                { "stoploss", "stoploss" },
                { "sl", "stoploss" },
                { "takeprofit", "takeprofit" },
                { "tp", "takeprofit" },
                { "profit", "profitloss" },
                { "pnl", "profitloss" },
                { "profitloss", "profitloss" },
                { "date", "datetime" },
                { "opentime", "datetime" },
                { "datetime", "datetime" },
                { "closetime", "exitdatetime" },
                { "exittime", "exitdatetime" },
                { "exitdatetime", "exitdatetime" },
                { "type", "type" },
                { "direction", "type" },
                { "status", "status" },
                { "lotsize", "lotsize" },
                { "volume", "lotsize" },
                { "size", "lotsize" },
                { "notes", "notes" },
                { "comment", "notes" }
            };

            var columnIndices = new Dictionary<string, int>();
            foreach (var map in columnMap)
            {
                if (headers.ContainsKey(map.Key))
                {
                    columnIndices[map.Value] = headers[map.Key];
                }
            }

            // Read data rows
            for (int row = startRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                try
                {
                    var trade = ParseTradeFromExcelRow(worksheet, row, columnIndices, userId, brokerName, currency, strategyId);
                    
                    if (trade != null)
                    {
                        if (await IsDuplicateTradeAsync(userId, trade))
                            result.TradesSkipped++;
                        else
                        {
                            _context.Trades.Add(trade);
                            result.TradesImported++;
                        }
                    }
                    else
                    {
                        result.TradesFailed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse trade from Excel row {Row}", row);
                    result.TradesFailed++;
                }
            }
        }

        private Trade? ParseTradeFromRow(
            CsvReader csv,
            string[] headers,
            Dictionary<string, int> columnIndices,
            string userId,
            string? brokerName,
            string? currency,
            int? strategyId)
        {
            try
            {
                var trade = new Trade
                {
                    UserId = userId,
                    StrategyId = strategyId,
                    Broker = brokerName,
                    Currency = currency ?? "USD",
                    Status = "Closed",
                    Type = "Long"
                };

                // Instrument (required)
                if (columnIndices.TryGetValue("instrument", out var instIdx) && instIdx < headers.Length)
                {
                    trade.Instrument = csv[instIdx]?.Trim() ?? "";
                }
                if (string.IsNullOrEmpty(trade.Instrument))
                    return null;

                // Entry Price (required)
                if (columnIndices.TryGetValue("entryprice", out var entryIdx) && entryIdx < headers.Length)
                {
                    if (decimal.TryParse(csv[entryIdx]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var entryPrice))
                        trade.EntryPrice = entryPrice;
                    else
                        return null;
                }
                else
                    return null;

                // Exit Price
                if (columnIndices.TryGetValue("exitprice", out var exitIdx) && exitIdx < headers.Length)
                {
                    if (decimal.TryParse(csv[exitIdx]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var exitPrice))
                        trade.ExitPrice = exitPrice;
                }

                // Stop Loss
                if (columnIndices.TryGetValue("stoploss", out var slIdx) && slIdx < headers.Length)
                {
                    if (decimal.TryParse(csv[slIdx]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var sl))
                        trade.StopLoss = sl;
                }

                // Take Profit
                if (columnIndices.TryGetValue("takeprofit", out var tpIdx) && tpIdx < headers.Length)
                {
                    if (decimal.TryParse(csv[tpIdx]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tp))
                        trade.TakeProfit = tp;
                }

                // Profit/Loss
                if (columnIndices.TryGetValue("profitloss", out var plIdx) && plIdx < headers.Length)
                {
                    if (decimal.TryParse(csv[plIdx]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var pl))
                    {
                        trade.ProfitLoss = pl;
                        trade.ProfitLossDisplay = pl; // Will be converted later if needed
                    }
                }

                // DateTime (required)
                if (columnIndices.TryGetValue("datetime", out var dtIdx) && dtIdx < headers.Length)
                {
                    var dateStr = csv[dtIdx]?.Trim() ?? "";
                    if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                        trade.DateTime = dateTime.ToUniversalTime();
                    else
                        return null;
                }
                else
                    return null;

                // Exit DateTime
                if (columnIndices.TryGetValue("exitdatetime", out var exitDtIdx) && exitDtIdx < headers.Length)
                {
                    var exitDateStr = csv[exitDtIdx]?.Trim() ?? "";
                    if (DateTime.TryParse(exitDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exitDateTime))
                        trade.ExitDateTime = exitDateTime.ToUniversalTime();
                }

                // Type
                if (columnIndices.TryGetValue("type", out var typeIdx) && typeIdx < headers.Length)
                {
                    var typeStr = csv[typeIdx]?.Trim()?.ToUpper() ?? "";
                    if (typeStr.Contains("SHORT") || typeStr == "S")
                        trade.Type = "Short";
                    else
                        trade.Type = "Long";
                }

                // Status
                if (columnIndices.TryGetValue("status", out var statusIdx) && statusIdx < headers.Length)
                {
                    var statusStr = csv[statusIdx]?.Trim()?.ToUpper() ?? "";
                    if (statusStr.Contains("CANCELLED") || statusStr == "CANCEL")
                        trade.Status = "Cancelled";
                    else if (statusStr.Contains("OPEN") || statusStr == "O")
                        trade.Status = "Open";
                    else
                        trade.Status = "Closed";
                }

                // Lot Size
                if (columnIndices.TryGetValue("lotsize", out var lotIdx) && lotIdx < headers.Length)
                {
                    if (decimal.TryParse(csv[lotIdx]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lotSize))
                        trade.LotSize = lotSize;
                }

                // Notes
                if (columnIndices.TryGetValue("notes", out var notesIdx) && notesIdx < headers.Length)
                {
                    trade.Notes = csv[notesIdx]?.Trim();
                }

                return trade;
            }
            catch
            {
                return null;
            }
        }

        private Trade? ParseTradeFromExcelRow(
            ExcelWorksheet worksheet,
            int row,
            Dictionary<string, int> columnIndices,
            string userId,
            string? brokerName,
            string? currency,
            int? strategyId)
        {
            try
            {
                var trade = new Trade
                {
                    UserId = userId,
                    StrategyId = strategyId,
                    Broker = brokerName,
                    Currency = currency ?? "USD",
                    Status = "Closed",
                    Type = "Long"
                };

                // Instrument (required)
                if (columnIndices.TryGetValue("instrument", out var instCol))
                {
                    trade.Instrument = worksheet.Cells[row, instCol].Value?.ToString()?.Trim() ?? "";
                }
                if (string.IsNullOrEmpty(trade.Instrument))
                    return null;

                // Entry Price (required)
                if (columnIndices.TryGetValue("entryprice", out var entryCol))
                {
                    var entryValue = worksheet.Cells[row, entryCol].Value;
                    if (entryValue != null && decimal.TryParse(entryValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var entryPrice))
                        trade.EntryPrice = entryPrice;
                    else
                        return null;
                }
                else
                    return null;

                // Exit Price
                if (columnIndices.TryGetValue("exitprice", out var exitCol))
                {
                    var exitValue = worksheet.Cells[row, exitCol].Value;
                    if (exitValue != null && decimal.TryParse(exitValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var exitPrice))
                        trade.ExitPrice = exitPrice;
                }

                // Stop Loss
                if (columnIndices.TryGetValue("stoploss", out var slCol))
                {
                    var slValue = worksheet.Cells[row, slCol].Value;
                    if (slValue != null && decimal.TryParse(slValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var sl))
                        trade.StopLoss = sl;
                }

                // Take Profit
                if (columnIndices.TryGetValue("takeprofit", out var tpCol))
                {
                    var tpValue = worksheet.Cells[row, tpCol].Value;
                    if (tpValue != null && decimal.TryParse(tpValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tp))
                        trade.TakeProfit = tp;
                }

                // Profit/Loss
                if (columnIndices.TryGetValue("profitloss", out var plCol))
                {
                    var plValue = worksheet.Cells[row, plCol].Value;
                    if (plValue != null && decimal.TryParse(plValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var pl))
                    {
                        trade.ProfitLoss = pl;
                        trade.ProfitLossDisplay = pl;
                    }
                }

                // DateTime (required)
                if (columnIndices.TryGetValue("datetime", out var dtCol))
                {
                    var dtValue = worksheet.Cells[row, dtCol].Value;
                    if (dtValue != null)
                    {
                        DateTime dateTime;
                        if (dtValue is DateTime dt)
                            dateTime = dt;
                        else if (DateTime.TryParse(dtValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDt))
                            dateTime = parsedDt;
                        else
                            return null;
                        trade.DateTime = dateTime.ToUniversalTime();
                    }
                    else
                        return null;
                }
                else
                    return null;

                // Exit DateTime
                if (columnIndices.TryGetValue("exitdatetime", out var exitDtCol))
                {
                    var exitDtValue = worksheet.Cells[row, exitDtCol].Value;
                    if (exitDtValue != null)
                    {
                        DateTime exitDateTime;
                        if (exitDtValue is DateTime edt)
                            exitDateTime = edt;
                        else if (DateTime.TryParse(exitDtValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedExitDt))
                            exitDateTime = parsedExitDt;
                        else
                            exitDateTime = default;
                        if (exitDateTime != default)
                            trade.ExitDateTime = exitDateTime.ToUniversalTime();
                    }
                }

                // Type
                if (columnIndices.TryGetValue("type", out var typeCol))
                {
                    var typeStr = worksheet.Cells[row, typeCol].Value?.ToString()?.ToUpper()?.Trim() ?? "";
                    if (typeStr.Contains("SHORT") || typeStr == "S")
                        trade.Type = "Short";
                    else
                        trade.Type = "Long";
                }

                // Status
                if (columnIndices.TryGetValue("status", out var statusCol))
                {
                    var statusStr = worksheet.Cells[row, statusCol].Value?.ToString()?.ToUpper()?.Trim() ?? "";
                    if (statusStr.Contains("CANCELLED") || statusStr == "CANCEL")
                        trade.Status = "Cancelled";
                    else if (statusStr.Contains("OPEN") || statusStr == "O")
                        trade.Status = "Open";
                    else
                        trade.Status = "Closed";
                }

                // Lot Size
                if (columnIndices.TryGetValue("lotsize", out var lotCol))
                {
                    var lotValue = worksheet.Cells[row, lotCol].Value;
                    if (lotValue != null && decimal.TryParse(lotValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lotSize))
                        trade.LotSize = lotSize;
                }

                // Notes
                if (columnIndices.TryGetValue("notes", out var notesCol))
                {
                    trade.Notes = worksheet.Cells[row, notesCol].Value?.ToString()?.Trim();
                }

                return trade;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generic broker Excel importer – uses dynamic column mapping so it handles
        /// cTrader and any other platform that produces a spreadsheet with recognizable
        /// trade-like headers (Time, Symbol, Type, Price, Profit, etc.).
        /// </summary>
        private async Task ImportFromBrokerExcelAsync(
            ExcelWorksheet worksheet,
            string userId,
            string? brokerName,
            string? currency,
            int? strategyId,
            ImportResult result,
            int? preDetectedHeaderRow = null)
        {
            string detectedCurrency = currency ?? "USD";
            string detectedBroker = brokerName ?? "Unknown";

            // Try to extract account metadata from the first few rows
            for (int row = 1; row <= Math.Min(10, worksheet.Dimension.End.Row); row++)
            {
                var label = worksheet.Cells[row, 1].Value?.ToString()?.Trim();
                if (string.IsNullOrEmpty(label)) continue;

                // Look for currency in any column on that row
                for (int col = 2; col <= Math.Min(8, worksheet.Dimension.End.Column); col++)
                {
                    var val = worksheet.Cells[row, col].Value?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(val)) continue;

                    if ((label.StartsWith("Account", StringComparison.OrdinalIgnoreCase) ||
                         label.StartsWith("Currency", StringComparison.OrdinalIgnoreCase)) && currency == null)
                    {
                        var m = Regex.Match(val, @"\b([A-Z]{3})\b");
                        if (m.Success && m.Groups[1].Value is "USD" or "EUR" or "GBP" or "JPY" or "CHF"
                            or "AUD" or "NZD" or "CAD" or "HKD" or "SGD" or "PLN" or "CZK" or "HUF")
                            detectedCurrency = m.Groups[1].Value;
                    }
                    if ((label.StartsWith("Company", StringComparison.OrdinalIgnoreCase) ||
                         label.StartsWith("Broker", StringComparison.OrdinalIgnoreCase) ||
                         label.StartsWith("Server", StringComparison.OrdinalIgnoreCase)) && brokerName == null)
                    {
                        detectedBroker = val;
                    }
                }
            }

            int headerRow = preDetectedHeaderRow ?? FindBrokerTradesHeaderRow(worksheet);
            if (headerRow == -1)
            {
                result.Errors.Add("Could not find a row with recognizable trade column headers");
                return;
            }

            var cols = BuildColumnMap(worksheet, headerRow);
            if (!cols.ContainsKey("symbol"))
            {
                result.Errors.Add("Required 'Symbol' column not found in the header row");
                return;
            }

            _logger.LogInformation(
                "Broker Excel: header row {Row}, mapped columns [{Cols}], currency={Cur}, broker={Bro}",
                headerRow, string.Join(", ", cols.Select(kv => $"{kv.Key}={kv.Value}")),
                detectedCurrency, detectedBroker);

            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                var firstCell = worksheet.Cells[row, 1].Value?.ToString()?.Trim() ?? "";

                if (_sectionMarkers.Contains(firstCell))
                    break;
                if (string.IsNullOrWhiteSpace(firstCell))
                    continue;

                var symbol = worksheet.Cells[row, cols["symbol"]].Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(symbol))
                    continue;

                try
                {
                    // Entry time (required)
                    if (!cols.ContainsKey("entryTime") ||
                        !TryParseExcelDateTime(worksheet.Cells[row, cols["entryTime"]].Value, out var entryTime))
                        continue;

                    // Type / direction
                    var tradeType = "Long";
                    if (cols.TryGetValue("type", out var typeCol))
                    {
                        var t = worksheet.Cells[row, typeCol].Value?.ToString()?.Trim()?.ToLower();
                        if (t is "sell" or "short" or "s")
                            tradeType = "Short";
                    }

                    // Volume
                    decimal? lotSize = null;
                    if (cols.TryGetValue("volume", out var volCol))
                    {
                        if (decimal.TryParse(worksheet.Cells[row, volCol].Value?.ToString(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                            lotSize = v;
                    }

                    // Entry price (required)
                    if (!cols.ContainsKey("entryPrice") ||
                        !decimal.TryParse(worksheet.Cells[row, cols["entryPrice"]].Value?.ToString(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var entryPrice))
                        continue;

                    // Stop Loss
                    decimal? stopLoss = null;
                    if (cols.TryGetValue("stopLoss", out var slCol))
                    {
                        var v = worksheet.Cells[row, slCol].Value;
                        if (v != null && decimal.TryParse(v.ToString(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var sl))
                            stopLoss = sl;
                    }

                    // Take Profit
                    decimal? takeProfit = null;
                    if (cols.TryGetValue("takeProfit", out var tpCol))
                    {
                        var v = worksheet.Cells[row, tpCol].Value;
                        if (v != null && decimal.TryParse(v.ToString(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var tp))
                            takeProfit = tp;
                    }

                    // Exit time
                    DateTime? exitTime = null;
                    if (cols.TryGetValue("exitTime", out var etCol) &&
                        TryParseExcelDateTime(worksheet.Cells[row, etCol].Value, out var et))
                        exitTime = et;

                    // Exit price
                    decimal? exitPrice = null;
                    if (cols.TryGetValue("exitPrice", out var epCol))
                    {
                        var v = worksheet.Cells[row, epCol].Value;
                        if (v != null && decimal.TryParse(v.ToString(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var ep))
                            exitPrice = ep;
                    }

                    // Commission
                    decimal commission = 0;
                    if (cols.TryGetValue("commission", out var commCol))
                    {
                        var v = worksheet.Cells[row, commCol].Value;
                        if (v != null) decimal.TryParse(v.ToString(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out commission);
                    }

                    // Swap
                    decimal swap = 0;
                    if (cols.TryGetValue("swap", out var swCol))
                    {
                        var v = worksheet.Cells[row, swCol].Value;
                        if (v != null) decimal.TryParse(v.ToString(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out swap);
                    }

                    // Profit (required)
                    if (!cols.ContainsKey("profit") ||
                        !decimal.TryParse(worksheet.Cells[row, cols["profit"]].Value?.ToString(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var profit))
                        continue;

                    if (!exitPrice.HasValue || !exitTime.HasValue)
                        continue;

                    var netPnL = profit + swap + commission;

                    var trade = new Trade
                    {
                        UserId = userId,
                        StrategyId = strategyId,
                        Broker = detectedBroker,
                        Currency = detectedCurrency,
                        Instrument = symbol,
                        EntryPrice = entryPrice,
                        ExitPrice = exitPrice,
                        StopLoss = stopLoss,
                        TakeProfit = takeProfit,
                        ProfitLoss = netPnL,
                        ProfitLossDisplay = netPnL,
                        DateTime = entryTime.ToUniversalTime(),
                        ExitDateTime = exitTime?.ToUniversalTime(),
                        LotSize = lotSize,
                        Status = "Closed",
                        Type = tradeType
                    };

                    if (await IsDuplicateTradeAsync(userId, trade))
                        result.TradesSkipped++;
                    else
                    {
                        _context.Trades.Add(trade);
                        result.TradesImported++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse trade at row {Row}", row);
                    result.TradesFailed++;
                }
            }

            _logger.LogInformation("Broker Excel import: {Imported} imported, {Skipped} skipped, {Failed} failed",
                result.TradesImported, result.TradesSkipped, result.TradesFailed);
        }

        private async Task ImportFromPdfAsync(
            Stream fileStream,
            string userId,
            string? brokerName,
            string? currency,
            int? strategyId,
            ImportResult result)
        {
            try
            {
                // Extract text from PDF
                var pdfText = ExtractTextFromPdf(fileStream);
                if (string.IsNullOrWhiteSpace(pdfText))
                {
                    result.Errors.Add("Could not extract text from PDF. The PDF might be image-based or encrypted.");
                    return;
                }

                _logger.LogInformation("Extracted {Length} characters from PDF", pdfText.Length);

                // Log a sample so we can debug detection issues
                var sample = pdfText.Length > 500 ? pdfText[..500] : pdfText;
                _logger.LogInformation("PDF text sample: {Sample}", sample.Replace("\n", "\\n").Replace("\r", "\\r"));

                // Detect transaction-based PDF (DXtrade or similar platforms)
                // Use case-insensitive regex to handle varied text extraction output
                bool hasSettledPnlKeyword = Regex.IsMatch(pdfText,
                    @"Settled\s*Pn\s*L|Settled\s*P\s*&\s*L|Realized\s*Pn\s*L|Realized\s*P\s*&\s*L",
                    RegexOptions.IgnoreCase);
                bool hasTransactionLines = Regex.IsMatch(pdfText,
                    @"(?:\d+[:\s]\d+\s+)?\d{2}[/.\-]\d{2}[/.\-]\d{2,4}\s+\d{2}:\d{2}\s+(Buy|Sell)",
                    RegexOptions.IgnoreCase);
                bool hasDXtradeSignatures = Regex.IsMatch(pdfText,
                    @"Transaction\s*(ID|Time)|Account\s*statement|Working\s*Orders|Open\s*Positions",
                    RegexOptions.IgnoreCase);

                _logger.LogInformation(
                    "PDF detection: hasSettledPnl={PnL}, hasTransactionLines={Tx}, hasDXtradeSignatures={Sig}",
                    hasSettledPnlKeyword, hasTransactionLines, hasDXtradeSignatures);

                if ((hasSettledPnlKeyword || hasDXtradeSignatures) && hasTransactionLines)
                {
                    _logger.LogInformation("Detected transaction-based PDF statement (DXtrade-like)");
                    await ImportFromDXtradePdfAsync(pdfText, userId, brokerName, currency, strategyId, result);
                    return;
                }

                // Second chance: if we see DXtrade signatures even without matching
                // transaction lines regex (text extraction may differ), try DXtrade parser
                if (hasDXtradeSignatures && hasSettledPnlKeyword)
                {
                    _logger.LogInformation("DXtrade signatures found, attempting DXtrade parser");
                    await ImportFromDXtradePdfAsync(pdfText, userId, brokerName, currency, strategyId, result);
                    if (result.TradesImported > 0)
                        return;
                    _logger.LogWarning("DXtrade parser found no trades, falling through to AI");
                }

                // Use AI to extract closed trades from PDF text
                List<Trade> trades;
                var mlModelService = _serviceProvider.GetService<MLModelService>();
                if (mlModelService != null)
                {
                    _logger.LogInformation("Using AI to extract closed trades from PDF...");
                    trades = await ExtractTradesWithAIAsync(pdfText, userId, brokerName, currency, strategyId, mlModelService);
                    
                    if (trades.Count == 0)
                    {
                        _logger.LogWarning("AI extraction returned no trades, falling back to pattern matching");
                        // Fallback to pattern matching if AI returns nothing
                        trades = ParseTradesFromPdfText(pdfText, userId, brokerName, currency, strategyId);
                    }
                }
                else
                {
                    _logger.LogInformation("AI service not available, using pattern matching for PDF extraction");
                    trades = ParseTradesFromPdfText(pdfText, userId, brokerName, currency, strategyId);
                }

                if (trades.Count == 0)
                {
                    result.Errors.Add("No closed trades found in PDF. The PDF format might not be recognized or may not contain closed trade data.");
                    return;
                }


                // Save trades to database
                _logger.LogInformation("Attempting to save {Count} validated trades to database", trades.Count);
                
                // First, check how many trades currently exist for this user (for debugging)
                var existingTradeCount = await _context.Trades.CountAsync(t => t.UserId == userId);
                _logger.LogInformation("Current trade count in database for user: {Count}", existingTradeCount);
                
                foreach (var trade in trades)
                {
                    try
                    {
                        // Final validation before saving - ensure all required fields are present
                        if (string.IsNullOrWhiteSpace(trade.Instrument))
                        {
                            _logger.LogWarning("Skipping trade with missing instrument");
                            result.TradesFailed++;
                            continue;
                        }
                        
                        if (trade.EntryPrice <= 0)
                        {
                            _logger.LogWarning("Skipping trade with invalid entry price: {EntryPrice}", trade.EntryPrice);
                            result.TradesFailed++;
                            continue;
                        }
                        
                        if (trade.DateTime == default(DateTime))
                        {
                            _logger.LogWarning("Skipping trade with missing or invalid DateTime. Instrument: {Instrument}", trade.Instrument);
                            result.TradesFailed++;
                            continue;
                        }
                        
                        if (!trade.ProfitLoss.HasValue)
                        {
                            _logger.LogWarning("Skipping trade without profitLoss value. Instrument: {Instrument}", trade.Instrument);
                            result.TradesFailed++;
                            continue;
                        }

                        if (await IsDuplicateTradeAsync(userId, trade))
                        {
                            result.TradesSkipped++;
                        }
                        else
                        {
                            _context.Trades.Add(trade);
                            result.TradesImported++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to save trade from PDF. Instrument: {Instrument}, EntryPrice: {EntryPrice}, DateTime: {DateTime}, Error: {Error}", 
                            trade?.Instrument ?? "Unknown", trade?.EntryPrice ?? 0, trade?.DateTime ?? default, ex.Message);
                        result.TradesFailed++;
                    }
                }
                
                _logger.LogInformation("PDF import: {Imported} imported, {Skipped} skipped, {Failed} failed",
                    result.TradesImported, result.TradesSkipped, result.TradesFailed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing PDF file");
                result.Errors.Add($"PDF parsing error: {ex.Message}");
            }
        }

        private async Task<List<Trade>> ExtractTradesWithAIAsync(string pdfText, string userId, string? brokerName, string? currency, int? strategyId, MLModelService mlModelService)
        {
            var trades = new List<Trade>();

            try
            {

                // Get AI-extracted trades as JSON
                var jsonResponse = await mlModelService.ExtractTradesFromPdfTextAsync(pdfText);
                
                _logger.LogInformation("AI returned JSON response length: {Length}", jsonResponse?.Length ?? 0);
                
                // Log the raw response for debugging (truncate if too long)
                if (!string.IsNullOrWhiteSpace(jsonResponse))
                {
                    var responsePreview = jsonResponse.Length > 2000 ? jsonResponse.Substring(0, 2000) + "..." : jsonResponse;
                    _logger.LogWarning("AI JSON response preview (first 2000 chars): {Response}", responsePreview);
                }
                else
                {
                    _logger.LogWarning("AI returned empty or null response");
                }
                
                if (string.IsNullOrWhiteSpace(jsonResponse) || jsonResponse.Trim() == "[]")
                {
                    _logger.LogWarning("AI returned empty or invalid response");
                    return trades;
                }
                
                // Parse JSON response
                System.Text.Json.JsonDocument? doc = null;
                try
                {
                    doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse AI JSON response. Response: {Response}", jsonResponse);
                    return trades;
                }
                
                using (doc)
                {
                    var tradesArray = doc.RootElement;

                    if (tradesArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var tradeCount = tradesArray.GetArrayLength();
                        _logger.LogInformation("AI extracted {Count} trades from JSON. Expected approximately 120 trades with numeric Settled PnL.", tradeCount);
                        
                        // #region agent log
                        try {
                            var logPath = Path.Combine(Directory.GetCurrentDirectory(), ".cursor", "debug.log");
                            var logDir = Path.GetDirectoryName(logPath);
                            if (!Directory.Exists(logDir))
                                Directory.CreateDirectory(logDir);
                            var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:787\",\"message\":\"AI extracted trade count\",\"data\":{{\"tradeCount\":{tradeCount},\"expectedCount\":120}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"A\"}}\n";
                            System.IO.File.AppendAllText(logPath, logEntry);
                            _logger.LogWarning("DEBUG: Wrote to log file: {Path}", logPath);
                        } catch (Exception logEx) {
                            _logger.LogError(logEx, "DEBUG: Failed to write log file: {Error}, Path: {Path}", logEx.Message, Path.Combine(Directory.GetCurrentDirectory(), ".cursor", "debug.log"));
                        }
                        // #endregion
                        
                        if (tradeCount > 150)
                        {
                            _logger.LogWarning("⚠️ CRITICAL WARNING: AI extracted {Count} trades, but expected only ~120. This indicates the AI is incorrectly including trades with \"—\" in Settled PnL. These will be filtered out during validation.", tradeCount);
                        }
                        
                        // Log a sample of the raw JSON to debug what the AI is returning
                        if (tradeCount > 150)
                        {
                            _logger.LogWarning("Sample of first 5 trades from AI response:");
                            var sampleCount = 0;
                            foreach (var sampleTrade in tradesArray.EnumerateArray())
                            {
                                if (sampleCount >= 5) break;
                                if (sampleTrade.TryGetProperty("profitLoss", out var samplePL))
                                {
                                    var plValue = samplePL.ValueKind == System.Text.Json.JsonValueKind.String 
                                        ? samplePL.GetString() 
                                        : samplePL.GetDecimal().ToString();
                                    var instrument = sampleTrade.TryGetProperty("instrument", out var inst) ? inst.GetString() : "N/A";
                                    _logger.LogWarning("  Trade {Index}: Instrument={Instrument}, ProfitLoss={ProfitLoss}", 
                                        sampleCount + 1, instrument, plValue);
                                    
                                    // #region agent log
                                    try {
                                        var logPath = GetDebugLogPath();
                                        var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:822\",\"message\":\"Sample trade profitLoss\",\"data\":{{\"index\":{sampleCount + 1},\"instrument\":\"{instrument}\",\"profitLoss\":\"{plValue}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"A\"}}\n";
                                        System.IO.File.AppendAllText(logPath, logEntry);
                                    } catch {}
                                    // #endregion
                                }
                                sampleCount++;
                            }
                        }
                        
                        int processedCount = 0;
                        int rejectedCount = 0;
                        int zeroProfitLossCount = 0;
                        
                        foreach (var tradeElement in tradesArray.EnumerateArray())
                        {
                            try
                            {
                            processedCount++;
                            
                            var trade = new Trade
                            {
                                UserId = userId,
                                StrategyId = strategyId,
                                Broker = brokerName,
                                Currency = currency ?? "USD",
                                Status = "Closed" // AI should only extract closed trades
                            };

                            // Extract instrument
                            if (tradeElement.TryGetProperty("instrument", out var instrumentProp))
                                trade.Instrument = instrumentProp.GetString() ?? "";

                            // Extract entry price
                            if (tradeElement.TryGetProperty("entryPrice", out var entryPriceProp))
                                trade.EntryPrice = entryPriceProp.GetDecimal();

                            // Extract exit price
                            if (tradeElement.TryGetProperty("exitPrice", out var exitPriceProp))
                                trade.ExitPrice = exitPriceProp.GetDecimal();

                            // Extract date/time - REQUIRED
                            if (tradeElement.TryGetProperty("dateTime", out var dateTimeProp))
                            {
                                var dateStr = dateTimeProp.GetString();
                                if (!string.IsNullOrWhiteSpace(dateStr))
                                {
                                    if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                                    {
                                        trade.DateTime = dateTime.ToUniversalTime();
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Failed to parse dateTime: {DateStr}", dateStr);
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogDebug("Trade missing dateTime property");
                            }

                            // Extract exit date/time
                            if (tradeElement.TryGetProperty("exitDateTime", out var exitDateTimeProp))
                            {
                                var exitDateStr = exitDateTimeProp.GetString();
                                if (DateTime.TryParse(exitDateStr, out var exitDateTime))
                                    trade.ExitDateTime = exitDateTime.ToUniversalTime();
                            }

                            // Extract profit/loss - REQUIRED for closed trades
                            // MUST have a numeric profitLoss value - transactions with "—", "-", empty, or missing PnL are NOT eligible
                            if (!tradeElement.TryGetProperty("profitLoss", out var profitLossProp))
                            {
                                rejectedCount++;
                                _logger.LogWarning("Skipping trade without profitLoss property - not an eligible closed trade. Instrument: {Instrument}", trade.Instrument ?? "N/A");
                                
                                // #region agent log
                                try {
                                    var logPath = GetDebugLogPath();
                                    var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Trade rejected - missing profitLoss\",\"data\":{{\"processedCount\":{processedCount},\"rejectedCount\":{rejectedCount},\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"B\"}}\n";
                                    System.IO.File.AppendAllText(logPath, logEntry);
                                } catch {}
                                // #endregion
                                
                                continue; // Skip trades without P/L
                            }

                            // Check if profitLoss is a valid number (not null, not empty string, not "—")
                            if (profitLossProp.ValueKind == System.Text.Json.JsonValueKind.Null)
                            {
                                rejectedCount++;
                                _logger.LogWarning("Skipping trade with null profitLoss value - not an eligible closed trade. Instrument: {Instrument}", trade.Instrument ?? "N/A");
                                
                                // #region agent log
                                try {
                                    var logPath = GetDebugLogPath();
                                    var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Trade rejected - null profitLoss\",\"data\":{{\"processedCount\":{processedCount},\"rejectedCount\":{rejectedCount},\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"B\"}}\n";
                                    System.IO.File.AppendAllText(logPath, logEntry);
                                } catch {}
                                // #endregion
                                
                                continue;
                            }

                            // Handle string values - reject "—", "-", empty strings
                            if (profitLossProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                var profitLossStr = profitLossProp.GetString();
                                
                                // #region agent log
                                try {
                                    var logPath = GetDebugLogPath();
                                    var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Processing trade with string profitLoss\",\"data\":{{\"processedCount\":{processedCount},\"profitLossStr\":\"{profitLossStr}\",\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"C\"}}\n";
                                    System.IO.File.AppendAllText(logPath, logEntry);
                                } catch {}
                                // #endregion
                                
                                if (string.IsNullOrWhiteSpace(profitLossStr) || 
                                    profitLossStr == "—" || 
                                    profitLossStr == "-" || 
                                    profitLossStr == "–" || // en dash
                                    profitLossStr.Trim() == "—" ||
                                    profitLossStr.Trim() == "-")
                                {
                                    rejectedCount++;
                                    _logger.LogWarning("Skipping trade with empty/missing profitLoss value (\"{Value}\") - not an eligible closed trade. Instrument: {Instrument}", 
                                        profitLossStr, trade.Instrument ?? "N/A");
                                    
                                    // #region agent log
                                    try {
                                        var logPath = GetDebugLogPath();
                                        var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Trade rejected - invalid profitLoss string\",\"data\":{{\"processedCount\":{processedCount},\"rejectedCount\":{rejectedCount},\"profitLossStr\":\"{profitLossStr}\",\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"C\"}}\n";
                                        System.IO.File.AppendAllText(logPath, logEntry);
                                    } catch {}
                                    // #endregion
                                    
                                    continue;
                                }
                                
                                // Try to parse the string as a number
                                if (!decimal.TryParse(profitLossStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedValue))
                                {
                                    rejectedCount++;
                                    _logger.LogWarning("Skipping trade - profitLoss string \"{Value}\" cannot be parsed as a number. Instrument: {Instrument}", 
                                        profitLossStr, trade.Instrument ?? "N/A");
                                    
                                    // #region agent log
                                    try {
                                        var logPath = GetDebugLogPath();
                                        var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Trade rejected - unparseable profitLoss\",\"data\":{{\"processedCount\":{processedCount},\"rejectedCount\":{rejectedCount},\"profitLossStr\":\"{profitLossStr}\",\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"C\"}}\n";
                                        System.IO.File.AppendAllText(logPath, logEntry);
                                    } catch {}
                                    // #endregion
                                    
                                    continue;
                                }
                                
                                trade.ProfitLoss = parsedValue;
                                trade.ProfitLossDisplay = parsedValue;
                                
                                if (parsedValue == 0) zeroProfitLossCount++;
                                
                                _logger.LogInformation("Extracted trade with profitLoss: {ProfitLoss} for instrument: {Instrument}", parsedValue, trade.Instrument ?? "N/A");
                                
                                // #region agent log
                                try {
                                    var logPath = GetDebugLogPath();
                                    var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Trade accepted with string profitLoss\",\"data\":{{\"processedCount\":{processedCount},\"profitLoss\":{parsedValue},\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"D\"}}\n";
                                    System.IO.File.AppendAllText(logPath, logEntry);
                                } catch {}
                                // #endregion
                                
                                // Continue to next validation step
                            }
                            else
                            {
                                // Try to get the decimal value directly
                                decimal profitLossValue;
                                try
                                {
                                    profitLossValue = profitLossProp.GetDecimal();
                                    
                                    // #region agent log
                                    try {
                                        var logPath = GetDebugLogPath();
                                        var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Processing trade with numeric profitLoss\",\"data\":{{\"processedCount\":{processedCount},\"profitLoss\":{profitLossValue},\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"D\"}}\n";
                                        System.IO.File.AppendAllText(logPath, logEntry);
                                    } catch {}
                                    // #endregion
                                    
                                    // CRITICAL: If profitLoss is 0 and we have > 150 trades, this might be a "—" trade incorrectly parsed as 0
                                    // We'll allow 0 for now (break-even trades are valid), but log a warning
                                    if (profitLossValue == 0)
                                    {
                                        zeroProfitLossCount++;
                                        _logger.LogDebug("Trade with profitLoss = 0 (break-even trade or possibly \"—\" incorrectly parsed). Instrument: {Instrument}", trade.Instrument ?? "N/A");
                                    }
                                    
                                    trade.ProfitLoss = profitLossValue;
                                    trade.ProfitLossDisplay = profitLossValue;
                                    _logger.LogInformation("Extracted trade with profitLoss: {ProfitLoss} for instrument: {Instrument}", profitLossValue, trade.Instrument ?? "N/A");
                                }
                                catch (Exception ex)
                                {
                                    rejectedCount++;
                                    _logger.LogWarning("Skipping trade - profitLoss is not a valid number: {Error}. Instrument: {Instrument}", 
                                        ex.Message, trade.Instrument ?? "N/A");
                                    
                                    // #region agent log
                                    try {
                                        var logPath = GetDebugLogPath();
                                        var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Trade rejected - exception parsing profitLoss\",\"data\":{{\"processedCount\":{processedCount},\"rejectedCount\":{rejectedCount},\"error\":\"{ex.Message}\",\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"B\"}}\n";
                                        System.IO.File.AppendAllText(logPath, logEntry);
                                    } catch {}
                                    // #endregion
                                    
                                    continue;
                                }
                            }

                            // Extract type
                            if (tradeElement.TryGetProperty("type", out var typeProp))
                                trade.Type = typeProp.GetString() ?? "Long";

                            // Extract status - must be "Closed" for closed trades
                            if (tradeElement.TryGetProperty("status", out var statusProp))
                            {
                                var status = statusProp.GetString() ?? "Closed";
                                if (status.ToLowerInvariant() != "closed")
                                {
                                    _logger.LogDebug("Skipping trade with status: {Status}", status);
                                    continue; // Skip non-closed trades
                                }
                                trade.Status = "Closed";
                            }
                            else
                            {
                                trade.Status = "Closed"; // Default to Closed for trades with P/L
                            }

                            // Extract lot size
                            if (tradeElement.TryGetProperty("lotSize", out var lotSizeProp))
                                trade.LotSize = lotSizeProp.GetDecimal();

                            // Extract stop loss
                            if (tradeElement.TryGetProperty("stopLoss", out var stopLossProp))
                                trade.StopLoss = stopLossProp.GetDecimal();

                            // Extract take profit
                            if (tradeElement.TryGetProperty("takeProfit", out var takeProfitProp))
                                trade.TakeProfit = takeProfitProp.GetDecimal();

                            // Validate required fields - must have instrument, entry price, dateTime, and profit/loss
                            var validationErrors = new List<string>();
                            
                            // #region agent log
                            try {
                                var logDir = Path.Combine(Directory.GetCurrentDirectory(), ".cursor");
                                if (!Directory.Exists(logDir))
                                    Directory.CreateDirectory(logDir);
                                var logPath = Path.Combine(logDir, "debug.log");
                                var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:1078\",\"message\":\"Starting trade validation\",\"data\":{{\"processedCount\":{processedCount},\"instrument\":\"{trade.Instrument ?? "N/A"}\",\"entryPrice\":{trade.EntryPrice},\"profitLoss\":{(trade.ProfitLoss.HasValue ? trade.ProfitLoss.Value.ToString() : "null")}}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"G\"}}\n";
                                File.AppendAllText(logPath, logEntry);
                            } catch (Exception logEx) {
                                _logger.LogError(logEx, "Failed to write debug log");
                            }
                            // #endregion

                            if (string.IsNullOrWhiteSpace(trade.Instrument))
                                validationErrors.Add("Missing instrument");
                            
                            if (trade.EntryPrice <= 0)
                                validationErrors.Add("Invalid or missing entry price");
                            
                            // CRITICAL: Reject trades with suspiciously high entryPrice values (> 1000000)
                            // These are likely Order IDs incorrectly mapped to entryPrice by the AI
                            // Legitimate entry prices should be reasonable (typically < 1000000)
                            if (trade.EntryPrice > 1000000)
                            {
                                validationErrors.Add($"Suspicious entryPrice value: {trade.EntryPrice} (likely Order ID, not actual price)");
                                _logger.LogWarning("🔴 REJECTING trade with suspicious entryPrice value: {EntryPrice}. This looks like an Order ID, not actual price. Instrument: {Instrument}", 
                                    trade.EntryPrice, trade.Instrument ?? "N/A");
                                
                                // #region agent log
                                try {
                                    var logPath = GetDebugLogPath();
                                    var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:1095\",\"message\":\"Trade rejected - suspicious entryPrice\",\"data\":{{\"processedCount\":{processedCount},\"entryPrice\":{trade.EntryPrice},\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"F\"}}\n";
                                    System.IO.File.AppendAllText(logPath, logEntry);
                                } catch {}
                                // #endregion
                            }
                            
                            if (trade.DateTime == default(DateTime))
                                validationErrors.Add("Missing or invalid dateTime");
                            
                            if (!trade.ProfitLoss.HasValue)
                            {
                                validationErrors.Add("Missing profitLoss value");
                            }
                            else
                            {
                                // Additional validation: Log the profitLoss value to ensure it's a real number
                                _logger.LogInformation("Trade validation - Instrument: {Instrument}, ProfitLoss: {ProfitLoss}", 
                                    trade.Instrument ?? "N/A", trade.ProfitLoss.Value);
                                
                                // CRITICAL: Reject trades with suspiciously high profitLoss values (> 10000)
                                // These are likely Order IDs incorrectly mapped to profitLoss by the AI
                                // Legitimate profit/loss values should be reasonable amounts (typically < 10000)
                                if (Math.Abs(trade.ProfitLoss.Value) > 10000)
                                {
                                    validationErrors.Add($"Suspicious profitLoss value: {trade.ProfitLoss.Value} (likely Order ID, not actual P/L)");
                                    _logger.LogWarning("🔴 REJECTING trade with suspicious profitLoss value: {ProfitLoss}. This looks like an Order ID, not actual profit/loss. Instrument: {Instrument}", 
                                        trade.ProfitLoss.Value, trade.Instrument ?? "N/A");
                                    
                                    // #region agent log
                                    try {
                                        var logPath = GetDebugLogPath();
                                        var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:1112\",\"message\":\"Trade rejected - suspicious profitLoss\",\"data\":{{\"processedCount\":{processedCount},\"profitLoss\":{trade.ProfitLoss.Value},\"instrument\":\"{trade.Instrument ?? "N/A"}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"F\"}}\n";
                                        System.IO.File.AppendAllText(logPath, logEntry);
                                    } catch {}
                                    // #endregion
                                }
                                
                                // Note: We allow profitLoss = 0 (break-even trades) as valid
                                // But trades with "—" should not have been extracted by the AI in the first place
                            }

                            // #region agent log
                            try {
                                var logPath = GetDebugLogPath();
                                var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:1139\",\"message\":\"Checking validation errors\",\"data\":{{\"processedCount\":{processedCount},\"validationErrorCount\":{validationErrors.Count},\"hasErrors\":{validationErrors.Any().ToString().ToLower()},\"errors\":\"{string.Join("; ", validationErrors)}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"G\"}}\n";
                                System.IO.File.AppendAllText(logPath, logEntry);
                            } catch {}
                            // #endregion
                            
                            if (validationErrors.Any())
                            {
                                rejectedCount++;
                                _logger.LogWarning("Skipping trade due to validation errors: {Errors}. Instrument: {Instrument}, EntryPrice: {EntryPrice}, DateTime: {DateTime}, ProfitLoss: {ProfitLoss}", 
                                    string.Join(", ", validationErrors), 
                                    trade.Instrument ?? "N/A", 
                                    trade.EntryPrice, 
                                    trade.DateTime, 
                                    trade.ProfitLoss);
                                
                                // #region agent log
                                try {
                                    var logPath = GetDebugLogPath();
                                    var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:1142\",\"message\":\"Trade rejected - validation errors\",\"data\":{{\"processedCount\":{processedCount},\"rejectedCount\":{rejectedCount},\"errors\":\"{string.Join("; ", validationErrors)}\",\"instrument\":\"{trade.Instrument ?? "N/A"}\",\"entryPrice\":{trade.EntryPrice},\"profitLoss\":{(trade.ProfitLoss.HasValue ? trade.ProfitLoss.Value.ToString() : "null")}}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"E\"}}\n";
                                    System.IO.File.AppendAllText(logPath, logEntry);
                                } catch {}
                                // #endregion
                                
                                continue;
                            }
                            
                            // CRITICAL CHECK: Ensure profitLoss is actually set and is a valid number
                            if (!trade.ProfitLoss.HasValue)
                            {
                                rejectedCount++;
                                _logger.LogWarning("Trade passed initial validation but profitLoss is still null - rejecting. Instrument: {Instrument}", trade.Instrument);
                                
                                // #region agent log
                                try {
                                    var logPath = GetDebugLogPath();
                                    var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Trade rejected - profitLoss null after validation\",\"data\":{{\"processedCount\":{processedCount},\"rejectedCount\":{rejectedCount},\"instrument\":\"{trade.Instrument}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"E\"}}\n";
                                    System.IO.File.AppendAllText(logPath, logEntry);
                                } catch {}
                                // #endregion
                                
                                continue;
                            }

                            // FINAL CHECK: Ensure profitLoss is actually set (not null, not default)
                            if (!trade.ProfitLoss.HasValue)
                            {
                                rejectedCount++;
                                _logger.LogWarning("REJECTING trade - profitLoss is null after all validation. This trade likely had \"—\" in Settled PnL. Instrument: {Instrument}", trade.Instrument);
                                
                                // #region agent log
                                try {
                                    var logPath = GetDebugLogPath();
                                    var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Trade rejected - profitLoss null final check\",\"data\":{{\"processedCount\":{processedCount},\"rejectedCount\":{rejectedCount},\"instrument\":\"{trade.Instrument}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"E\"}}\n";
                                    System.IO.File.AppendAllText(logPath, logEntry);
                                } catch {}
                                // #endregion
                                
                                continue;
                            }
                            
                            // Ensure this is a legitimate closed trade with P/L
                            trades.Add(trade);
                            _logger.LogInformation("✓ VALID trade added to list: {Instrument}, Entry: {EntryPrice}, P/L: {ProfitLoss}, Date: {DateTime}", 
                                trade.Instrument, trade.EntryPrice, trade.ProfitLoss, trade.DateTime);
                            
                            // #region agent log
                            try {
                                var logPath = GetDebugLogPath();
                                var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:1195\",\"message\":\"Trade ACCEPTED and added\",\"data\":{{\"processedCount\":{processedCount},\"instrument\":\"{trade.Instrument ?? "N/A"}\",\"entryPrice\":{trade.EntryPrice},\"profitLoss\":{(trade.ProfitLoss.HasValue ? trade.ProfitLoss.Value.ToString() : "null")}}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"G\"}}\n";
                                System.IO.File.AppendAllText(logPath, logEntry);
                            } catch {}
                            // #endregion
                            
                            // #region agent log
                            try {
                                var logPath = GetDebugLogPath();
                                var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:{0}\",\"message\":\"Trade accepted and added\",\"data\":{{\"processedCount\":{processedCount},\"acceptedCount\":{trades.Count},\"profitLoss\":{trade.ProfitLoss.Value},\"instrument\":\"{trade.Instrument}\"}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"D\"}}\n";
                                System.IO.File.AppendAllText(logPath, logEntry);
                            } catch {}
                            // #endregion
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse AI-extracted trade: {Error}", ex.Message);
                            }
                        }
                        
                        _logger.LogInformation("AI extracted {Count} valid closed trades from PDF after validation", trades.Count);
                        _logger.LogWarning("🔍 DEBUG: Trade processing summary - Processed: {Processed}, Rejected: {Rejected}, Accepted: {Accepted}, Zero P/L Count: {Zero}", processedCount, rejectedCount, trades.Count, zeroProfitLossCount);
                        
                        // #region agent log
                        try {
                            var logPath = @"c:\Workplace\TradeTracker\.cursor\debug.log";
                            var logEntry = $"{{\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"location\":\"ImportService.cs:1179\",\"message\":\"Final trade counts after validation\",\"data\":{{\"totalProcessed\":{processedCount},\"totalRejected\":{rejectedCount},\"totalAccepted\":{trades.Count},\"zeroProfitLossCount\":{zeroProfitLossCount}}},\"sessionId\":\"debug-session\",\"runId\":\"run1\",\"hypothesisId\":\"ALL\"}}\n";
                            System.IO.File.AppendAllText(logPath, logEntry);
                        } catch (Exception logEx) {
                            _logger.LogError(logEx, "Failed to write debug log file");
                        }
                        // #endregion
                    }
                    else
                    {
                        _logger.LogWarning("AI response is not a JSON array. ValueKind: {ValueKind}", tradesArray.ValueKind);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AI trade extraction");
            }

            return trades;
        }

        private string ExtractTextFromPdf(Stream pdfStream)
        {
            try
            {
                pdfStream.Position = 0; // Reset stream position
                var pdfReader = new PdfReader(pdfStream);
                var pdfDocument = new PdfDocument(pdfReader);
                var text = new StringBuilder();

                for (int page = 1; page <= pdfDocument.GetNumberOfPages(); page++)
                {
                    var pageText = PdfTextExtractor.GetTextFromPage(pdfDocument.GetPage(page));
                    text.AppendLine(pageText);
                }

                pdfDocument.Close();
                pdfReader.Close();

                return text.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF");
                throw;
            }
        }

        private List<Trade> ParseTradesFromPdfText(string pdfText, string userId, string? brokerName, string? currency, int? strategyId)
        {
            var trades = new List<Trade>();
            var lines = pdfText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // Common patterns for trade data in PDFs
            var datePattern = @"(\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2}|\d{1,2}\.\d{1,2}\.\d{2,4})";
            var numberPattern = @"-?\d+\.?\d*";

            // Try to find trade rows - look for lines with dates and numbers
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.Length < 10)
                    continue;

                var hasDate = Regex.IsMatch(line, datePattern);
                var hasNumbers = Regex.Matches(line, numberPattern).Count >= 3;

                if (hasDate && hasNumbers)
                {
                    try
                    {
                        var trade = ParseTradeFromPdfLine(line, userId, brokerName, currency, strategyId);
                        if (trade != null)
                        {
                            trades.Add(trade);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to parse line as trade: {Line}", line);
                    }
                }
            }

            // If no trades found with pattern matching, try a more aggressive approach
            if (trades.Count == 0)
            {
                _logger.LogInformation("No trades found with pattern matching. Attempting alternative parsing...");
                
                var tableStartPatterns = new[] { "trade", "position", "order", "transaction", "deal" };
                var inTradeSection = false;

                foreach (var line in lines)
                {
                    var lowerLine = line.ToLowerInvariant();
                    
                    if (tableStartPatterns.Any(pattern => lowerLine.Contains(pattern) && (lowerLine.Contains("date") || lowerLine.Contains("time"))))
                    {
                        inTradeSection = true;
                        continue;
                    }

                    if (inTradeSection)
                    {
                        if (lowerLine.Contains("date") || lowerLine.Contains("time") || lowerLine.Contains("symbol") || lowerLine.Contains("instrument"))
                            continue;

                        var trade = ParseTradeFromPdfLine(line, userId, brokerName, currency, strategyId);
                        if (trade != null)
                        {
                            trades.Add(trade);
                        }
                    }
                }
            }

            return trades;
        }

        private Trade? ParseTradeFromPdfLine(string line, string userId, string? brokerName, string? currency, int? strategyId)
        {
            try
            {
                var datePattern = @"(\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2}|\d{1,2}\.\d{1,2}\.\d{2,4})";
                var dateMatch = Regex.Match(line, datePattern);
                if (!dateMatch.Success)
                    return null;

                DateTime tradeDate;
                if (!DateTime.TryParse(dateMatch.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out tradeDate))
                {
                    var formats = new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "dd.MM.yyyy", "dd-MM-yyyy" };
                    bool parsed = false;
                    foreach (var format in formats)
                    {
                        if (DateTime.TryParseExact(dateMatch.Value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out tradeDate))
                        {
                            parsed = true;
                            break;
                        }
                    }
                    if (!parsed)
                        return null;
                }

                var instrumentPattern = @"\b([A-Z]{2,6})\b";
                var instrumentMatches = Regex.Matches(line, instrumentPattern);
                string? instrument = null;
                foreach (Match match in instrumentMatches)
                {
                    var value = match.Value;
                    if (!new[] { "USD", "EUR", "GBP", "JPY", "AUD", "CAD", "CHF", "NZD", "OPEN", "CLOSE", "BUY", "SELL", "LONG", "SHORT" }.Contains(value))
                    {
                        instrument = value;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(instrument))
                    return null;

                var numberPattern = @"-?\d+\.?\d*";
                var numbers = Regex.Matches(line, numberPattern)
                    .Cast<Match>()
                    .Select(m => m.Value)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();

                if (numbers.Count < 2)
                    return null;

                decimal? entryPrice = null;
                decimal? exitPrice = null;
                decimal? profitLoss = null;

                if (numbers.Count >= 2)
                {
                    if (decimal.TryParse(numbers[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var num1))
                        entryPrice = num1;
                    if (numbers.Count >= 2 && decimal.TryParse(numbers[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var num2))
                        exitPrice = num2;
                    if (numbers.Count >= 3 && decimal.TryParse(numbers[numbers.Count - 1], NumberStyles.Any, CultureInfo.InvariantCulture, out var num3))
                        profitLoss = num3;
                }

                if (!entryPrice.HasValue)
                    return null;

                var type = "Long";
                var lowerLine = line.ToLowerInvariant();
                if (lowerLine.Contains("sell") || lowerLine.Contains("short") || (profitLoss.HasValue && profitLoss.Value < 0 && !lowerLine.Contains("buy")))
                {
                    type = "Short";
                }

                var trade = new Trade
                {
                    UserId = userId,
                    StrategyId = strategyId,
                    Broker = brokerName,
                    Currency = currency ?? "USD",
                    Instrument = instrument,
                    EntryPrice = entryPrice.Value,
                    ExitPrice = exitPrice,
                    ProfitLoss = profitLoss,
                    ProfitLossDisplay = profitLoss,
                    DateTime = tradeDate.ToUniversalTime(),
                    Status = "Closed",
                    Type = type
                };

                return trade;
            }
            catch
            {
                return null;
            }
        }

        private static readonly string[] _pdfDateTimeFormats =
        {
            "dd/MM/yyyy HH:mm", "MM/dd/yyyy HH:mm",
            "dd/MM/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss",
            "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss",
            "dd-MM-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss",
            "yyyy.MM.dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss",
            "yyyy/MM/dd HH:mm:ss"
        };

        // Pattern A: DXtrade – TransactionID  Date  Time  Direction  Size  Symbol  Price  OrderID  <PnL Commission>
        // TransactionID may have colon (6077901:641038) or space (6077901 641038) depending on PDF extractor
        private static readonly Regex _txPatternA = new(
            @"^(\d+[:\s]\d+)\s+(\d{2}[/.\-]\d{2}[/.\-]\d{2,4})\s+(\d{2}:\d{2}(?::\d{2})?)\s+(Buy|Sell)\s+([\d.]+)\s+([A-Za-z][A-Za-z0-9./]+)\s+([\d,]+\.?\d*)\s+(\d+)\s+(.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Pattern B: No TransactionID – Date  Time  Direction  Size  Symbol  Price  <remainder>
        private static readonly Regex _txPatternB = new(
            @"^(\d{2}[/.\-]\d{2}[/.\-]\d{2,4})\s+(\d{2}:\d{2}(?::\d{2})?)\s+(Buy|Sell)\s+([\d.]+)\s+([A-Za-z][A-Za-z0-9./]+)\s+([\d,]+\.?\d*)\s+(.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private async Task ImportFromDXtradePdfAsync(
            string pdfText,
            string userId,
            string? brokerName,
            string? currency,
            int? strategyId,
            ImportResult result)
        {
            if (string.IsNullOrEmpty(currency))
            {
                var currencyMatch = Regex.Match(pdfText, @"Currency\s+([A-Z]{3})");
                currency = currencyMatch.Success ? currencyMatch.Groups[1].Value : "USD";
            }

            var allLines = pdfText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // ── Section filtering: only parse lines inside the "Transactions" section ──
            var transactionLines = new List<string>();
            bool inTransactions = false;
            var sectionHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Financing", "Deposits", "Withdrawals", "Deposits, Withdrawals & Adjustments",
                "Working Orders", "Open Positions", "Summary", "Balance Chart", "Totals"
            };

            foreach (var raw in allLines)
            {
                var trimmed = raw.Trim();

                // Detect Transactions section start (handle variable whitespace from PDF extractors)
                if (!inTransactions)
                {
                    if (Regex.IsMatch(trimmed, @"^Transaction\s*ID", RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(trimmed, @"^Transactions\s*$", RegexOptions.IgnoreCase))
                    {
                        inTransactions = true;
                        continue;
                    }
                    // Also enter transactions mode if we see a transaction-like line with Buy/Sell
                    if (Regex.IsMatch(trimmed, @"\d+[:\s]\d+\s+\d{2}[/.\-]\d{2}[/.\-]\d{2,4}\s+\d{2}:\d{2}\s+(Buy|Sell)",
                        RegexOptions.IgnoreCase))
                    {
                        inTransactions = true;
                        // Don't continue - fall through to add this line
                    }
                    else continue;
                }

                if (sectionHeaders.Any(h => trimmed.StartsWith(h, StringComparison.OrdinalIgnoreCase)))
                    break;
                if (Regex.IsMatch(trimmed, @"^--\s*\d+\s+of\s+\d+\s*--$"))
                    continue;
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;
                transactionLines.Add(trimmed);
            }

            _logger.LogInformation("DXtrade: {Total} total lines, {Section} lines in Transactions section",
                allLines.Length, transactionLines.Count);

            // Log first few transaction lines for debugging
            for (int dbg = 0; dbg < Math.Min(5, transactionLines.Count); dbg++)
                _logger.LogInformation("DXtrade sample line [{Idx}]: {Line}", dbg, transactionLines[dbg]);

            // ── Phase 1: try Pattern A (with TransactionID + OrderID) on all lines ──
            var entries = new List<DXtradeTransaction>();
            var exits = new List<DXtradeTransaction>();
            int patternAMatches = 0;
            int patternAFails = 0;

            foreach (var line in transactionLines)
            {
                var matchA = _txPatternA.Match(line);
                if (!matchA.Success)
                {
                    patternAFails++;
                    if (patternAFails <= 3)
                        _logger.LogWarning("DXtrade pattern A no match: {Line}", line);
                    continue;
                }
                patternAMatches++;
                try
                {
                    var tx = ParsePdfTxPatternA(matchA);
                    if (tx != null)
                        (tx.SettledPnL.HasValue ? exits : entries).Add(tx);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse transaction (pattern A): {Line}", line);
                }
            }

            _logger.LogInformation("DXtrade Pattern A results: {Matches} matched, {Fails} unmatched, {Entries} entries, {Exits} exits",
                patternAMatches, patternAFails, entries.Count, exits.Count);

            // ── Phase 2: only if Pattern A found nothing, fall back to Pattern B ──
            if (entries.Count == 0 && exits.Count == 0)
            {
                _logger.LogInformation("Pattern A found no matches, falling back to Pattern B");
                foreach (var line in transactionLines)
                {
                    var matchB = _txPatternB.Match(line);
                    if (!matchB.Success) continue;
                    try
                    {
                        var tx = ParsePdfTxPatternB(matchB);
                        if (tx != null)
                            (tx.SettledPnL.HasValue ? exits : entries).Add(tx);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse transaction (pattern B): {Line}", line);
                    }
                }
            }

            // PnL sanity check: reject exits where |PnL| looks like an OrderID (> 100k)
            var validExits = new List<DXtradeTransaction>();
            foreach (var ex in exits)
            {
                if (ex.SettledPnL.HasValue && Math.Abs(ex.SettledPnL.Value) > 100_000m)
                {
                    _logger.LogWarning(
                        "Discarding exit with implausible PnL {PnL} for {Symbol} (likely a misread OrderID)",
                        ex.SettledPnL.Value, ex.Symbol);
                    continue;
                }
                validExits.Add(ex);
            }
            exits = validExits;

            _logger.LogInformation("DXtrade: parsed {Entries} entries, {Exits} exits (after validation)",
                entries.Count, exits.Count);

            // Sort entries chronologically for FIFO matching
            entries.Sort((a, b) => a.Time.CompareTo(b.Time));

            var usedEntryIndices = new HashSet<int>();
            var unmatchedExits = new List<DXtradeTransaction>();

            foreach (var exit in exits)
            {
                var oppositeDir = exit.Direction == "Buy" ? "Sell" : "Buy";
                int matchedIdx = -1;

                // Pass 1: exact match (symbol + size + direction + time order)
                for (int i = 0; i < entries.Count; i++)
                {
                    if (usedEntryIndices.Contains(i)) continue;
                    var entry = entries[i];
                    if (entry.Symbol == exit.Symbol &&
                        entry.Size == exit.Size &&
                        entry.Direction == oppositeDir &&
                        entry.Time <= exit.Time)
                    {
                        matchedIdx = i;
                        break;
                    }
                }

                // Pass 2: relaxed match (symbol + direction, ignore size — handles partial closes)
                if (matchedIdx == -1)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (usedEntryIndices.Contains(i)) continue;
                        var entry = entries[i];
                        if (entry.Symbol == exit.Symbol &&
                            entry.Direction == oppositeDir &&
                            entry.Time <= exit.Time)
                        {
                            matchedIdx = i;
                            break;
                        }
                    }
                }

                if (matchedIdx >= 0)
                {
                    usedEntryIndices.Add(matchedIdx);
                    var matched = entries[matchedIdx];
                    var tradeType = matched.Direction == "Buy" ? "Long" : "Short";

                    var trade = new Trade
                    {
                        UserId = userId,
                        StrategyId = strategyId,
                        Broker = brokerName ?? "Unknown",
                        Currency = currency,
                        Instrument = matched.Symbol,
                        EntryPrice = matched.Price,
                        ExitPrice = exit.Price,
                        ProfitLoss = exit.SettledPnL,
                        ProfitLossDisplay = exit.SettledPnL,
                        DateTime = matched.Time.ToUniversalTime(),
                        ExitDateTime = exit.Time.ToUniversalTime(),
                        LotSize = exit.Size,
                        Status = "Closed",
                        Type = tradeType
                    };

                    if (await IsDuplicateTradeAsync(userId, trade))
                        result.TradesSkipped++;
                    else
                    {
                        _context.Trades.Add(trade);
                        result.TradesImported++;
                    }
                }
                else
                {
                    unmatchedExits.Add(exit);
                }
            }

            // Pass 3: create standalone trades for exits whose entries are outside the statement period
            foreach (var exit in unmatchedExits)
            {
                var tradeType = exit.Direction == "Sell" ? "Long" : "Short";

                var trade = new Trade
                {
                    UserId = userId,
                    StrategyId = strategyId,
                    Broker = brokerName ?? "Unknown",
                    Currency = currency,
                    Instrument = exit.Symbol,
                    EntryPrice = exit.Price,
                    ExitPrice = exit.Price,
                    ProfitLoss = exit.SettledPnL,
                    ProfitLossDisplay = exit.SettledPnL,
                    DateTime = exit.Time.ToUniversalTime(),
                    ExitDateTime = exit.Time.ToUniversalTime(),
                    LotSize = exit.Size,
                    Status = "Closed",
                    Type = tradeType
                };

                _logger.LogInformation(
                    "DXtrade: creating standalone trade for {Symbol} {Dir} {Size} at {Time} PnL={PnL} (entry not in statement)",
                    exit.Symbol, exit.Direction, exit.Size, exit.Time, exit.SettledPnL);

                if (await IsDuplicateTradeAsync(userId, trade))
                    result.TradesSkipped++;
                else
                {
                    _context.Trades.Add(trade);
                    result.TradesImported++;
                }
            }

            _logger.LogInformation("DXtrade import: {Imported} imported, {Skipped} skipped, {Unmatched} standalone (entry not in statement)",
                result.TradesImported, result.TradesSkipped, unmatchedExits.Count);
        }

        /// <summary>
        /// Parse a transaction line matching Pattern A (DXtrade: TxID Date Time Dir Size Symbol Price OrderID PnL Comm).
        /// </summary>
        private static DXtradeTransaction? ParsePdfTxPatternA(Match m)
        {
            var remainder = m.Groups[9].Value.Trim();
            var parts = remainder.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            decimal? settledPnL = null;
            decimal? commission = null;

            if (parts.Length >= 1)
            {
                var n = NormalizeDXtradeNumber(parts[0]);
                if (n != null && decimal.TryParse(n, NumberStyles.Any, CultureInfo.InvariantCulture, out var pnl))
                    settledPnL = pnl;
            }
            if (parts.Length >= 2)
            {
                var n = NormalizeDXtradeNumber(parts[1]);
                if (n != null && decimal.TryParse(n, NumberStyles.Any, CultureInfo.InvariantCulture, out var c))
                    commission = c;
            }

            if (!TryParsePdfDateTime(m.Groups[2].Value, m.Groups[3].Value, out var time))
                return null;

            return new DXtradeTransaction
            {
                TransactionId = m.Groups[1].Value,
                Time = time,
                Direction = m.Groups[4].Value,
                Size = decimal.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture),
                Symbol = m.Groups[6].Value,
                Price = decimal.Parse(m.Groups[7].Value.Replace(",", ""), CultureInfo.InvariantCulture),
                OrderId = m.Groups[8].Value,
                SettledPnL = settledPnL,
                Commission = commission
            };
        }

        /// <summary>
        /// Parse a transaction line matching Pattern B (no TxID: Date Time Dir Size Symbol Price Remainder).
        /// </summary>
        private static DXtradeTransaction? ParsePdfTxPatternB(Match m)
        {
            var remainder = m.Groups[7].Value.Trim();
            var parts = remainder.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            decimal? settledPnL = null;
            decimal? commission = null;

            foreach (var part in parts)
            {
                var n = NormalizeDXtradeNumber(part);
                if (n == null) continue;
                if (!decimal.TryParse(n, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    continue;

                if (!settledPnL.HasValue) settledPnL = val;
                else if (!commission.HasValue) commission = val;
                else break;
            }

            if (!TryParsePdfDateTime(m.Groups[1].Value, m.Groups[2].Value, out var time))
                return null;

            return new DXtradeTransaction
            {
                TransactionId = "",
                Time = time,
                Direction = m.Groups[3].Value,
                Size = decimal.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                Symbol = m.Groups[5].Value,
                Price = decimal.Parse(m.Groups[6].Value.Replace(",", ""), CultureInfo.InvariantCulture),
                OrderId = "",
                SettledPnL = settledPnL,
                Commission = commission
            };
        }

        private static bool TryParsePdfDateTime(string datePart, string timePart, out DateTime result)
        {
            var combined = datePart + " " + timePart;
            return DateTime.TryParseExact(combined, _pdfDateTimeFormats,
                       CultureInfo.InvariantCulture, DateTimeStyles.None, out result)
                || DateTime.TryParse(combined, CultureInfo.InvariantCulture,
                       DateTimeStyles.None, out result);
        }

        /// <summary>
        /// Normalizes number strings from PDFs by replacing non-breaking hyphens/dashes.
        /// Returns null for dash-only values (meaning "no value").
        /// </summary>
        private static string? NormalizeDXtradeNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            if (trimmed == "\u2014" || trimmed == "\u2013" || trimmed == "-")
                return null;

            return trimmed
                .Replace("\u2011", "-")
                .Replace("\u2013", "-")
                .Replace("\u2212", "-")
                .Replace(",", "");
        }

        private class DXtradeTransaction
        {
            public string TransactionId { get; set; } = "";
            public DateTime Time { get; set; }
            public string Direction { get; set; } = "";
            public decimal Size { get; set; }
            public string Symbol { get; set; } = "";
            public decimal Price { get; set; }
            public string OrderId { get; set; } = "";
            public decimal? SettledPnL { get; set; }
            public decimal? Commission { get; set; }
        }
    }

    public class ImportResult
    {
        public int TradesImported { get; set; }
        public int TradesSkipped { get; set; }
        public int TradesFailed { get; set; }
        public string? DetectedCurrency { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}

