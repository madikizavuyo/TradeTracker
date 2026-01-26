// ApplicationDbContext.cs – EF Core context for Trade Helper
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
        public DbSet<Trade> Trades { get; set; }
        public DbSet<Strategy> Strategies { get; set; }
        public DbSet<TradeImage> TradeImages { get; set; }
        public DbSet<BrokerImportHistory> BrokerImportHistories { get; set; }
    }
}