using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeHelper.Data;

namespace TradeHelper.Controllers
{
    /// <summary>
    /// Admin-only endpoints for diagnostics and error log inspection.
    /// </summary>
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Returns error and warning logs from ApplicationLogs for the last 5 days, with pagination.
        /// Used for admin investigation of refresh failures and other issues.
        /// </summary>
        [HttpGet("error-logs")]
        public async Task<IActionResult> GetErrorLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? level = null)
        {
            var cutoff = DateTime.UtcNow.AddDays(-5);
            var query = _context.ApplicationLogs
                .Where(l => l.Timestamp >= cutoff)
                .Where(l => l.Level == "Error" || l.Level == "Warning");

            if (!string.IsNullOrWhiteSpace(level))
            {
                var lvl = level.Trim();
                if (lvl.Equals("Error", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(l => l.Level == "Error");
                else if (lvl.Equals("Warning", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(l => l.Level == "Warning");
            }

            var totalCount = await query.CountAsync();
            var effectivePageSize = Math.Min(Math.Max(pageSize, 10), 100);
            var items = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .Select(l => new
                {
                    l.Id,
                    l.Timestamp,
                    l.Level,
                    l.Category,
                    l.Message,
                    l.Exception
                })
                .ToListAsync();

            return Ok(new
            {
                items,
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)effectivePageSize)
            });
        }
    }
}
