using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradeHelper.Data;
using TradeHelper.Models;
using TradeHelper.Services;

namespace TradeHelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TradesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly CurrencyService _currencyService;

        public TradesController(ApplicationDbContext context, CurrencyService currencyService)
        {
            _context = context;
            _currencyService = currencyService;
        }

        [HttpGet("Index")]
        public async Task<IActionResult> Index(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? search = null,
            [FromQuery] string? instrument = null,
            [FromQuery] int? strategyId = null,
            [FromQuery] string? status = null,
            [FromQuery] string? startDate = null,
            [FromQuery] string? endDate = null,
            [FromQuery] string? sortBy = "date",
            [FromQuery] string? sortOrder = "desc")
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Build query
            var query = _context.Trades
                .Where(t => t.UserId == userId)
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(search))
            {
                var term = search.Trim();
                query = query.Where(t =>
                    t.Instrument.Contains(term) ||
                    (t.Notes != null && t.Notes.Contains(term)));
            }

            if (!string.IsNullOrEmpty(instrument))
            {
                query = query.Where(t => t.Instrument == instrument);
            }

            if (strategyId.HasValue)
            {
                query = query.Where(t => t.StrategyId == strategyId);
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(t => t.Status == status);
            }

            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var start))
            {
                query = query.Where(t => t.DateTime >= start.ToUniversalTime());
            }

            if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var end))
            {
                var endOfDay = end.Date.AddDays(1).AddTicks(-1).ToUniversalTime();
                query = query.Where(t => t.DateTime <= endOfDay);
            }

            // Get total count before pagination
            var totalCount = await query.CountAsync();

            // Get user's display currency for fallback
            var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            var displaySymbol = settings?.DisplayCurrencySymbol ?? "$";
            var displayCurrency = settings?.DisplayCurrency ?? "USD";

            // Apply sorting
            switch (sortBy.ToLower())
            {
                case "date":
                    query = sortOrder.ToLower() == "asc" 
                        ? query.OrderBy(t => t.DateTime)
                        : query.OrderByDescending(t => t.DateTime);
                    break;
                case "instrument":
                    query = sortOrder.ToLower() == "asc"
                        ? query.OrderBy(t => t.Instrument)
                        : query.OrderByDescending(t => t.Instrument);
                    break;
                case "profitloss":
                    query = sortOrder.ToLower() == "asc"
                        ? query.OrderBy(t => t.ProfitLoss ?? 0)
                        : query.OrderByDescending(t => t.ProfitLoss ?? 0);
                    break;
                default:
                    query = query.OrderByDescending(t => t.DateTime);
                    break;
            }

            // Apply pagination
            var trades = await query
                .Include(t => t.Strategy)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new
                {
                    t.Id,
                    t.Instrument,
                    t.EntryPrice,
                    t.ExitPrice,
                    t.ProfitLoss,
                    t.ProfitLossDisplay,
                    t.DisplayCurrency,
                    t.DisplayCurrencySymbol,
                    t.DateTime,
                    t.ExitDateTime,
                    t.Status,
                    t.Type,
                    t.LotSize,
                    t.StopLoss,
                    t.TakeProfit,
                    t.Broker,
                    t.Currency,
                    t.StrategyId,
                    strategyName = t.Strategy != null ? t.Strategy.Name : null
                })
                .ToListAsync();

            // Ensure display currency/symbol from user settings; convert when trade currency differs
            var items = new List<object>();
            foreach (var t in trades)
            {
                var tradeDisplayCurrency = t.DisplayCurrency ?? displayCurrency;
                var tradeDisplaySymbol = t.DisplayCurrencySymbol ?? displaySymbol;
                decimal? profitLossDisplay = t.ProfitLossDisplay;

                if (t.ProfitLoss.HasValue && tradeDisplayCurrency != displayCurrency)
                {
                    profitLossDisplay = await _currencyService.ConvertAsync(
                        t.ProfitLoss.Value, t.Currency, displayCurrency);
                    tradeDisplayCurrency = displayCurrency;
                    tradeDisplaySymbol = displaySymbol;
                }
                else if (!profitLossDisplay.HasValue && t.ProfitLoss.HasValue)
                {
                    profitLossDisplay = await _currencyService.ConvertAsync(
                        t.ProfitLoss.Value, t.Currency, displayCurrency);
                    tradeDisplaySymbol = displaySymbol;
                }

                items.Add(new
                {
                    t.Id,
                    t.Instrument,
                    t.EntryPrice,
                    t.ExitPrice,
                    t.ProfitLoss,
                    profitLossDisplay,
                    displayCurrency = tradeDisplayCurrency,
                    displayCurrencySymbol = tradeDisplaySymbol,
                    t.DateTime,
                    t.ExitDateTime,
                    t.Status,
                    t.Type,
                    t.LotSize,
                    t.StopLoss,
                    t.TakeProfit,
                    t.Broker,
                    t.Currency,
                    t.StrategyId,
                    t.strategyName
                });
            }

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var response = new
            {
                items,
                totalCount = totalCount,
                pageNumber = pageNumber,
                pageSize = pageSize,
                totalPages = totalPages,
                hasPreviousPage = pageNumber > 1,
                hasNextPage = pageNumber < totalPages
            };

            return Ok(response);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Details(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var trade = await _context.Trades
                .Include(t => t.TradeImages)
                .Include(t => t.Strategy)
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (trade == null)
                return NotFound(new { message = "Trade not found" });

            return Ok(new
            {
                id = trade.Id,
                userId = trade.UserId,
                strategyId = trade.StrategyId,
                instrument = trade.Instrument,
                entryPrice = trade.EntryPrice,
                exitPrice = trade.ExitPrice,
                profitLoss = trade.ProfitLoss,
                profitLossDisplay = trade.ProfitLossDisplay,
                stopLoss = trade.StopLoss,
                takeProfit = trade.TakeProfit,
                dateTime = trade.DateTime,
                exitDateTime = trade.ExitDateTime,
                notes = trade.Notes,
                status = trade.Status,
                type = trade.Type,
                lotSize = trade.LotSize,
                broker = trade.Broker,
                currency = trade.Currency,
                displayCurrency = trade.DisplayCurrency,
                displayCurrencySymbol = trade.DisplayCurrencySymbol,
                createdAt = trade.CreatedAt,
                updatedAt = trade.UpdatedAt,
                strategyName = trade.Strategy?.Name,
                tradeImages = (trade.TradeImages ?? new List<TradeHelper.Models.TradeImage>()).Select(ti => new
                {
                    id = ti.Id,
                    tradeId = ti.TradeId,
                    type = ti.Type,
                    originalFileName = ti.OriginalFileName,
                    fileSizeBytes = ti.FileSizeBytes,
                    mimeType = ti.MimeType
                }).ToList()
            });
        }

        [HttpPost]
        public IActionResult Create([FromBody] object trade)
        {
            return BadRequest(new { message = "Trade creation not yet implemented" });
        }

        [HttpPut("{id}")]
        public IActionResult Edit(int id, [FromBody] object trade)
        {
            return BadRequest(new { message = "Trade update not yet implemented" });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var trade = await _context.Trades
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

            if (trade == null)
                return NotFound(new { message = "Trade not found" });

            _context.Trades.Remove(trade);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("all")]
        public async Task<IActionResult> DeleteAll()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var trades = await _context.Trades
                .Where(t => t.UserId == userId)
                .ToListAsync();

            _context.Trades.RemoveRange(trades);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Deleted {trades.Count} trades" });
        }
    }
}

