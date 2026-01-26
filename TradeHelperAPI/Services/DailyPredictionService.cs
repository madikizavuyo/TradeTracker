// DailyPredictionService.cs – Background service to collect data and store prediction
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradeHelper.Data;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    public class DailyPredictionService : BackgroundService
    {
        private readonly IServiceProvider _provider;

        public DailyPredictionService(IServiceProvider provider)
        {
            _provider = provider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunPredictionCycleAsync();
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }

        public async Task RunPredictionCycleAsync()
        {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var scraper = scope.ServiceProvider.GetRequiredService<WebScraperService>();
            var ml = scope.ServiceProvider.GetRequiredService<MLModelService>();

            var instruments = db.Instruments.ToList();

            foreach (var instrument in instruments)
            {
                var country = instrument.Type == "Currency" ? "United States" : "World";
                var sentiment = await scraper.GetRetailSentimentAsync(instrument.Name);
                var technical = await scraper.GetTechnicalScoreAsync(instrument.Name);
                var economic = await scraper.GetEconomicDataAsync(country);

                var indicators = new IndicatorData
                {
                    InstrumentId = instrument.Id,
                    COTScore = sentiment,
                    RetailPositionScore = 100 - sentiment,
                    TrendScore = technical,
                    SeasonalityScore = 5.0,
                    GDP = economic.GDP,
                    CPI = economic.CPI,
                    ManufacturingPMI = economic.ManufacturingPMI,
                    ServicesPMI = economic.ServicesPMI,
                    EmploymentChange = economic.EmploymentChange,
                    UnemploymentRate = economic.UnemploymentRate,
                    InterestRate = economic.InterestRate,
                    DateCollected = DateTime.UtcNow
                };

                db.IndicatorData.Add(indicators);
                await db.SaveChangesAsync();

                var score = await ml.PredictBiasAsync(indicators);
                db.UserLogs.Add(new UserLog
                {
                    Email = "system@tradehelper.ai",
                    Action = $"Predicted {instrument.Name} bias score: {score:0.00}",
                    Timestamp = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }
    }
}