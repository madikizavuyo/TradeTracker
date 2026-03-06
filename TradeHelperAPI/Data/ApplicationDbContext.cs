using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TradeHelper.Models;

namespace TradeHelper.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<IndicatorData> IndicatorData { get; set; }
        public DbSet<Instrument> Instruments { get; set; }
        public DbSet<UserLog> UserLogs { get; set; }
        public DbSet<ApplicationLog> ApplicationLogs { get; set; }
        public DbSet<Trade> Trades { get; set; }
        public DbSet<Strategy> Strategies { get; set; }
        public DbSet<TradeImage> TradeImages { get; set; }
        public DbSet<BrokerImportHistory> BrokerImportHistories { get; set; }
        public DbSet<UserSettings> UserSettings { get; set; }
        public DbSet<TrailBlazerScore> TrailBlazerScores { get; set; }
        public DbSet<COTReport> COTReports { get; set; }
        public DbSet<EconomicHeatmapEntry> EconomicHeatmapEntries { get; set; }
        public DbSet<TechnicalIndicator> TechnicalIndicators { get; set; }
        public DbSet<NewsArticle> NewsArticles { get; set; }
        public DbSet<MarketOutlook> MarketOutlooks { get; set; }
        public DbSet<RetailSentimentSnapshot> RetailSentimentSnapshots { get; set; }
        public DbSet<SystemSetting> SystemSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<TrailBlazerScore>()
                .ToTable("EdgeFinderScores")
                .HasIndex(e => new { e.InstrumentId, e.DateComputed });


            builder.Entity<COTReport>()
                .HasIndex(c => new { c.Symbol, c.ReportDate });

            builder.Entity<EconomicHeatmapEntry>()
                .HasIndex(e => new { e.Currency, e.Indicator });

            builder.Entity<TechnicalIndicator>()
                .HasIndex(t => new { t.InstrumentId, t.Date });

            builder.Entity<NewsArticle>()
                .HasIndex(n => new { n.Symbol, n.DateCollected });

            builder.Entity<MarketOutlook>()
                .HasIndex(m => new { m.Symbol, m.DateCollected });

            builder.Entity<RetailSentimentSnapshot>()
                .HasIndex(r => new { r.InstrumentId, r.DateCollected });

            builder.Entity<ApplicationLog>()
                .HasIndex(a => a.Timestamp);

            builder.Entity<SystemSetting>()
                .HasIndex(s => s.Key)
                .IsUnique();

            SeedInstruments(builder);
        }

        private static void SeedInstruments(ModelBuilder builder)
        {
            int id = 100;
            var instruments = new List<Instrument>();

            // Forex Majors (8)
            string[] majors = ["EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD", "NZDUSD", "GBPJPY"];
            foreach (var m in majors)
                instruments.Add(new Instrument { Id = id++, Name = m, Type = "Currency", AssetClass = "ForexMajor" });

            // Forex Minors (21) – USDZAR added separately below (id 141)
            string[] minors = [
                "EURGBP", "EURJPY", "EURCHF", "EURAUD", "EURCAD", "EURNZD",
                "GBPCHF", "GBPAUD", "GBPCAD", "GBPNZD",
                "AUDJPY", "AUDCHF", "AUDCAD", "AUDNZD",
                "NZDJPY", "NZDCHF", "NZDCAD",
                "CADJPY", "CADCHF",
                "CHFJPY",
                "USDSEK"
            ];
            foreach (var m in minors)
                instruments.Add(new Instrument { Id = id++, Name = m, Type = "Currency", AssetClass = "ForexMinor" });

            // Indices (6)
            string[] indices = ["US500", "US30", "US100", "DE40", "UK100", "JP225"];
            foreach (var i in indices)
                instruments.Add(new Instrument { Id = id++, Name = i, Type = "Commodity", AssetClass = "Index" });

            // Metals (4)
            string[] metals = ["XAUUSD", "XAGUSD", "XPTUSD", "XPDUSD"];
            foreach (var m in metals)
                instruments.Add(new Instrument { Id = id++, Name = m, Type = "Commodity", AssetClass = "Metal" });

            // Energy (1)
            instruments.Add(new Instrument { Id = id++, Name = "USOIL", Type = "Commodity", AssetClass = "Commodity" });

            // Bonds (1)
            instruments.Add(new Instrument { Id = id++, Name = "US10Y", Type = "Commodity", AssetClass = "Bond" });

            // USDZAR (ZAR), USDCNY (Chinese Yuan) – per docs/FUNDAMENTAL_DATA_ANALYSIS.md
            instruments.Add(new Instrument { Id = 141, Name = "USDZAR", Type = "Currency", AssetClass = "ForexMinor" });
            instruments.Add(new Instrument { Id = 142, Name = "USDCNY", Type = "Currency", AssetClass = "ForexMinor" });

            builder.Entity<Instrument>().HasData(instruments);
        }
    }
}