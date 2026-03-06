using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradeHelper.Data;

namespace TradeHelper.Controllers
{
    internal record TradeRow(string Instrument, decimal? ProfitLoss);

    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AIController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AIController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("insights")]
        public async Task<IActionResult> GetInsights(
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

            var rows = await query
                .Select(t => new { t.Instrument, t.ProfitLoss })
                .ToListAsync();

            var trades = rows.Select(r => new TradeRow(r.Instrument, r.ProfitLoss)).ToList();
            var insights = BuildInsightsFromTrades(trades);
            return Ok(insights);
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(new { available = true, message = "AI insights are available" });
        }

        private static object BuildInsightsFromTrades(List<TradeRow> trades)
        {
            if (trades.Count == 0)
            {
                return new
                {
                    overallPerformance = "No closed trades found in the selected period. Add more trades and try again.",
                    strengths = Array.Empty<string>(),
                    weaknesses = new[] { "Insufficient trade data for analysis" },
                    recommendations = new[] { "Record more closed trades to get meaningful AI insights" },
                    bestInstruments = Array.Empty<string>(),
                    worstInstruments = Array.Empty<string>(),
                    optimalTimeframes = Array.Empty<string>(),
                    emotionalPatterns = "Unable to analyze patterns without trade data.",
                    nextSteps = new[] { "Add trades via the Import or Trades page", "Ensure trades have status 'Closed'" },
                    confidence = 0.0
                };
            }

            var withProfit = trades.Where(t => t.ProfitLoss.HasValue).ToList();
            var winning = withProfit.Where(t => t.ProfitLoss!.Value > 0).ToList();
            var winRate = withProfit.Count > 0 ? (double)winning.Count / withProfit.Count : 0;
            var totalProfit = withProfit.Sum(t => t.ProfitLoss!.Value);

            var byInstrument = withProfit
                .GroupBy(t => t.Instrument)
                .Select(g => new { Instrument = g.Key, PnL = g.Sum(t => t.ProfitLoss!.Value) })
                .OrderByDescending(x => x.PnL)
                .ToList();

            var bestInstruments = byInstrument.Where(x => x.PnL > 0).Take(5).Select(x => x.Instrument).ToArray();
            var worstInstruments = byInstrument.Where(x => x.PnL < 0).Take(5).Select(x => x.Instrument).ToArray();

            var strengths = new List<string>();
            var weaknesses = new List<string>();
            var recommendations = new List<string>();

            if (winRate >= 0.55) strengths.Add($"Strong win rate of {(winRate * 100):F0}%");
            else if (winRate < 0.45) weaknesses.Add($"Win rate below 50% ({(winRate * 100):F0}%) - consider reviewing entry criteria");

            if (totalProfit > 0) strengths.Add($"Profitability in the period: {totalProfit:F2}");
            else if (totalProfit < 0) weaknesses.Add($"Net loss in the period: {totalProfit:F2}");

            if (withProfit.Count >= 20) strengths.Add("Good sample size for analysis");
            else if (withProfit.Count < 10) recommendations.Add("Record more trades for statistically significant insights");

            if (bestInstruments.Length > 0) recommendations.Add($"Focus on {string.Join(", ", bestInstruments)} - your best performers");
            if (worstInstruments.Length > 0) recommendations.Add($"Review {string.Join(", ", worstInstruments)} - consider reducing exposure or improving strategy");

            var confidence = Math.Min(0.95, 0.2 + (withProfit.Count / 100.0) + (winRate * 0.3));
            var overallPerformance = totalProfit >= 0
                ? $"You closed {withProfit.Count} trades with a {(winRate * 100):F0}% win rate and net profit of {totalProfit:F2}. Performance is positive in the selected period."
                : $"You closed {withProfit.Count} trades with a {(winRate * 100):F0}% win rate and net loss of {totalProfit:F2}. Consider reviewing your risk management and entry criteria.";

            return new
            {
                overallPerformance,
                strengths = (strengths.Count > 0 ? strengths : new List<string> { "Building trade history" }).ToArray(),
                weaknesses = (weaknesses.Count > 0 ? weaknesses : new List<string> { "None identified" }).ToArray(),
                recommendations = (recommendations.Count > 0 ? recommendations : new List<string> { "Keep recording trades for ongoing analysis" }).ToArray(),
                bestInstruments,
                worstInstruments,
                optimalTimeframes = Array.Empty<string>(),
                emotionalPatterns = "Pattern analysis requires more trade data. Consider tracking your emotional state before/after trades.",
                nextSteps = new[]
                {
                    "Continue recording trades consistently",
                    "Review losing trades for patterns",
                    "Consider journaling for emotional pattern analysis"
                },
                confidence
            };
        }
    }
}
