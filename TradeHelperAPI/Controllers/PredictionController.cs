using Microsoft.AspNetCore.Mvc;
using TradeHelper.Data;
using TradeHelper.Services;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using System.Threading.Tasks;

namespace TradeHelper.Controllers
{
    [Authorize]
    [Route("api/predict")]
    public class PredictionController : Controller
    {
        private readonly DailyPredictionService _service;
        private readonly ApplicationDbContext _context;
        private readonly MLModelService _ml;

        public PredictionController(DailyPredictionService service, ApplicationDbContext context, MLModelService ml)
        {
            _service = service;
            _context = context;
            _ml = ml;
        }

        [HttpPost("run")]
        public async Task<IActionResult> RunNow()
        {
            await _service.RunPredictionCycleAsync();
            return Ok("Prediction cycle completed manually.");
        }

        [HttpGet("history")]
        public IActionResult GetPredictionHistory()
        {
            var history = _context.IndicatorData
                .OrderByDescending(x => x.DateCollected)
                .Select(x => new {
                    Date = x.DateCollected.ToString("yyyy-MM-dd"),
                    Instrument = _context.Instruments.FirstOrDefault(i => i.Id == x.InstrumentId) != null 
                        ? _context.Instruments.FirstOrDefault(i => i.Id == x.InstrumentId)!.Name 
                        : "Unknown",
                    Score = _ml.PredictBias(x)
                })
                .Take(100)
                .ToList();

            return Ok(history);
        }
    }
}