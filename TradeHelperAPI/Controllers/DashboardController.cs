using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TradeHelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        [HttpGet]
        public IActionResult Index()
        {
            // Return empty dashboard data structure
            var dashboardData = new
            {
                totalTrades = 0,
                openTrades = 0,
                winningTrades = 0,
                losingTrades = 0,
                totalProfitLoss = 0.0,
                totalProfitLossDisplay = 0.0,
                winRate = 0.0,
                averageWin = 0.0,
                averageWinDisplay = 0.0,
                averageLoss = 0.0,
                averageLossDisplay = 0.0,
                profitFactor = 0.0,
                displayCurrency = "USD",
                displayCurrencySymbol = "$",
                strategies = new object[0],
                recentTrades = new object[0],
                monthlyPerformance = new object[0],
                strategyPerformance = new object[0],
                instrumentPerformance = new object[0]
            };

            return Ok(dashboardData);
        }
    }
}

