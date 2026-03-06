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
    public class ReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
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
                .Include(t => t.Strategy)
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

            var recentTrades = trades
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
                .ToList();

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

        [HttpGet("performance")]
        public async Task<IActionResult> Performance(
            [FromQuery] string? startDate = null,
            [FromQuery] string? endDate = null,
            [FromQuery] int? strategyId = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var query = _context.Trades
                .Where(t => t.UserId == userId && t.Status == "Closed")
                .AsQueryable();

            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var start))
                query = query.Where(t => t.DateTime >= start.ToUniversalTime());

            if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var end))
                query = query.Where(t => t.DateTime <= end.ToUniversalTime());

            if (strategyId.HasValue)
                query = query.Where(t => t.StrategyId == strategyId);

            var closedTrades = await query.ToListAsync();

            var winning = closedTrades.Where(t => t.ProfitLoss.HasValue && t.ProfitLoss.Value > 0).ToList();
            var losing = closedTrades.Where(t => t.ProfitLoss.HasValue && t.ProfitLoss.Value < 0).ToList();

            var totalPnL = closedTrades.Sum(t => t.ProfitLoss ?? 0m);
            var winRate = closedTrades.Count > 0
                ? (double)winning.Count / closedTrades.Count * 100
                : 0.0;

            var avgWin = winning.Count > 0 ? (double)winning.Average(t => t.ProfitLoss ?? 0m) : 0.0;
            var avgLoss = losing.Count > 0 ? (double)losing.Average(t => t.ProfitLoss ?? 0m) : 0.0;

            var totalWins = winning.Sum(t => t.ProfitLoss ?? 0m);
            var totalLosses = Math.Abs(losing.Sum(t => t.ProfitLoss ?? 0m));
            var profitFactor = totalLosses > 0 ? (double)(totalWins / totalLosses) : totalWins > 0 ? 999.0 : 0.0;

            var avgRR = closedTrades.Where(t => t.RiskReward.HasValue).Select(t => (double)t.RiskReward!.Value).ToList();

            var equityCurve = closedTrades.OrderBy(t => t.DateTime).ToList();
            double runningPnL = 0;
            double maxPnL = 0;
            double maxDrawdown = 0;
            foreach (var t in equityCurve)
            {
                runningPnL += (double)(t.ProfitLoss ?? 0m);
                if (runningPnL > maxPnL) maxPnL = runningPnL;
                var dd = maxPnL - runningPnL;
                if (dd > maxDrawdown) maxDrawdown = dd;
            }

            return Ok(new
            {
                totalTrades = closedTrades.Count,
                winningTrades = winning.Count,
                losingTrades = losing.Count,
                totalProfitLoss = (double)totalPnL,
                winRate,
                averageWin = avgWin,
                averageLoss = avgLoss,
                profitFactor,
                maxDrawdown,
                sharpeRatio = 0.0,
                averageRiskReward = avgRR.Count > 0 ? avgRR.Average() : 0.0
            });
        }

        [HttpGet("charts")]
        public IActionResult Charts()
        {
            return Ok(new { chartTypes = new[] { "equity", "monthly", "strategy", "instrument", "winrate", "profitloss" } });
        }

        [HttpGet("charts/{chartType}")]
        public async Task<IActionResult> GetChartData(
            string chartType,
            [FromQuery] string? startDate = null,
            [FromQuery] string? endDate = null,
            [FromQuery] int? strategyId = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var query = _context.Trades
                .Where(t => t.UserId == userId && t.Status == "Closed")
                .AsQueryable();

            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var start))
                query = query.Where(t => t.DateTime >= start.ToUniversalTime());
            if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var end))
                query = query.Where(t => t.DateTime <= end.ToUniversalTime());
            if (strategyId.HasValue)
                query = query.Where(t => t.StrategyId == strategyId);

            var trades = await query.OrderBy(t => t.DateTime).ToListAsync();

            object data = chartType.ToLower() switch
            {
                "equity" => BuildEquityData(trades),
                "monthly" => BuildMonthlyData(trades),
                "instrument" => BuildInstrumentData(trades),
                _ => Array.Empty<object>()
            };

            return Ok(new { data });
        }

        private static object BuildEquityData(List<TradeHelper.Models.Trade> trades)
        {
            double cumulative = 0;
            return trades.Select(t =>
            {
                cumulative += (double)(t.ProfitLossDisplay ?? t.ProfitLoss ?? 0m);
                return new { date = t.DateTime.ToString("yyyy-MM-dd"), profitLoss = cumulative };
            }).ToList();
        }

        private static object BuildMonthlyData(List<TradeHelper.Models.Trade> trades)
        {
            return trades
                .GroupBy(t => new { t.DateTime.Year, t.DateTime.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new
                {
                    month = $"{g.Key.Year}-{g.Key.Month:D2}",
                    profitLoss = (double)g.Sum(t => t.ProfitLossDisplay ?? t.ProfitLoss ?? 0m),
                    trades = g.Count()
                }).ToList();
        }

        private static object BuildInstrumentData(List<TradeHelper.Models.Trade> trades)
        {
            return trades
                .GroupBy(t => t.Instrument)
                .Select(g => new
                {
                    instrument = g.Key,
                    profitLoss = (double)g.Sum(t => t.ProfitLossDisplay ?? t.ProfitLoss ?? 0m),
                    trades = g.Count()
                }).ToList();
        }
    }
}
