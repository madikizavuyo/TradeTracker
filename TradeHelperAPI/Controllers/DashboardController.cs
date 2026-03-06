using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradeHelper.Data;
using TradeHelper.Services;

namespace TradeHelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            var dc = settings?.DisplayCurrency ?? "USD";
            var ds = settings?.DisplayCurrencySymbol ?? CurrencyService.GetSymbol(dc);

            var trades = await _context.Trades
                .Where(t => t.UserId == userId)
                .ToListAsync();

            var closedTrades = trades.Where(t => t.Status == "Closed").ToList();
            var openTrades = trades.Where(t => t.Status == "Open").ToList();

            var winning = closedTrades.Where(t => t.ProfitLoss.HasValue && t.ProfitLoss.Value > 0).ToList();
            var losing = closedTrades.Where(t => t.ProfitLoss.HasValue && t.ProfitLoss.Value < 0).ToList();

            var totalPnL = closedTrades.Sum(t => t.ProfitLoss ?? 0m);
            var totalPnLDisplay = closedTrades.Sum(t => t.ProfitLossDisplay ?? t.ProfitLoss ?? 0m);

            var winRate = closedTrades.Count > 0
                ? (double)winning.Count / closedTrades.Count * 100
                : 0.0;

            var avgWin = winning.Count > 0 ? winning.Average(t => t.ProfitLoss ?? 0m) : 0m;
            var avgWinDisplay = winning.Count > 0 ? winning.Average(t => t.ProfitLossDisplay ?? t.ProfitLoss ?? 0m) : 0m;
            var avgLoss = losing.Count > 0 ? losing.Average(t => t.ProfitLoss ?? 0m) : 0m;
            var avgLossDisplay = losing.Count > 0 ? losing.Average(t => t.ProfitLossDisplay ?? t.ProfitLoss ?? 0m) : 0m;

            var totalWins = winning.Sum(t => t.ProfitLoss ?? 0m);
            var totalLosses = Math.Abs(losing.Sum(t => t.ProfitLoss ?? 0m));
            var profitFactor = totalLosses > 0 ? (double)(totalWins / totalLosses) : totalWins > 0 ? 999.0 : 0.0;

            var recentTrades = await _context.Trades
                .Where(t => t.UserId == userId)
                .Include(t => t.Strategy)
                .OrderByDescending(t => t.DateTime)
                .Take(10)
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
                    t.Broker,
                    t.Currency,
                    strategyName = t.Strategy != null ? t.Strategy.Name : null
                })
                .ToListAsync();

            var monthlyPerformance = closedTrades
                .GroupBy(t => new { t.DateTime.Year, t.DateTime.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new
                {
                    month = $"{g.Key.Year}-{g.Key.Month:D2}",
                    profitLoss = (double)g.Sum(t => t.ProfitLossDisplay ?? t.ProfitLoss ?? 0m),
                    trades = g.Count(),
                    wins = g.Count(t => t.ProfitLoss.HasValue && t.ProfitLoss.Value > 0)
                })
                .ToList();

            var strategyPerformance = closedTrades
                .GroupBy(t => t.Strategy?.Name ?? "No Strategy")
                .Select(g => new
                {
                    strategy = g.Key,
                    trades = g.Count(),
                    profitLoss = (double)g.Sum(t => t.ProfitLossDisplay ?? t.ProfitLoss ?? 0m),
                    winRate = g.Count() > 0
                        ? (double)g.Count(t => t.ProfitLoss.HasValue && t.ProfitLoss.Value > 0) / g.Count() * 100
                        : 0.0
                })
                .ToList();

            var instrumentPerformance = closedTrades
                .GroupBy(t => t.Instrument)
                .Select(g => new
                {
                    instrument = g.Key,
                    trades = g.Count(),
                    profitLoss = (double)g.Sum(t => t.ProfitLossDisplay ?? t.ProfitLoss ?? 0m),
                    winRate = g.Count() > 0
                        ? (double)g.Count(t => t.ProfitLoss.HasValue && t.ProfitLoss.Value > 0) / g.Count() * 100
                        : 0.0
                })
                .ToList();

            var strategies = await _context.Strategies
                .Where(s => s.UserId == userId && s.IsActive)
                .Select(s => new { s.Id, s.Name })
                .ToListAsync();

            return Ok(new
            {
                totalTrades = trades.Count,
                openTrades = openTrades.Count,
                winningTrades = winning.Count,
                losingTrades = losing.Count,
                totalProfitLoss = (double)totalPnL,
                totalProfitLossDisplay = (double)totalPnLDisplay,
                winRate,
                averageWin = (double)avgWin,
                averageWinDisplay = (double)avgWinDisplay,
                averageLoss = (double)avgLoss,
                averageLossDisplay = (double)avgLossDisplay,
                profitFactor,
                displayCurrency = dc,
                displayCurrencySymbol = ds,
                strategies,
                recentTrades,
                monthlyPerformance,
                strategyPerformance,
                instrumentPerformance
            });
        }
    }
}
