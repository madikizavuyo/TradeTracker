using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TradesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TradesController(ApplicationDbContext context)
        {
            _context = context;
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
                query = query.Where(t => 
                    t.Instrument.Contains(search));
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
                query = query.Where(t => t.DateTime <= end.ToUniversalTime());
            }

            // Get total count before pagination
            var totalCount = await query.CountAsync();

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
                    t.DateTime,
                    t.ExitDateTime,
                    t.Status,
                    t.Type,
                    t.LotSize,
                    t.StopLoss,
                    t.TakeProfit,
                    t.Broker,
                    t.Currency,
                    t.StrategyId
                })
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var response = new
            {
                items = trades,
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
        public IActionResult Details(int id)
        {
            return NotFound(new { message = "Trade not found" });
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

