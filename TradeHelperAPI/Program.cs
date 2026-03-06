using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TradeHelper.Data;
using TradeHelper.Logging;
using TradeHelper.Models;
using TradeHelper.Services;

var builder = WebApplication.CreateBuilder(args);

// Database logging: persist ILogger output to ApplicationLogs table
builder.Services.AddSingleton<ILoggerProvider>(sp => new DbLoggerProvider(sp, sp.GetRequiredService<IConfiguration>()));

// Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// MVC + Razor Pages
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "YourSuperSecretKeyForJWTTokenGenerationThatIsAtLeast32CharactersLong!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TradeHelperAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TradeHelperClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173", 
                "http://127.0.0.1:5173",
                "http://localhost:5174",
                "http://127.0.0.1:5174")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // Required for JWT token authentication
    });
});

// File Upload Configuration
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100_000_000; // 100MB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

// Memory cache for AI analysis
builder.Services.AddMemoryCache();

// Services
builder.Services.AddHttpClient<WebScraperService>();
builder.Services.AddHttpClient<MLModelService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddHttpClient<CurrencyService>();
builder.Services.AddHostedService<DailyPredictionService>();

// TrailBlazer Services
builder.Services.AddSingleton<TrailBlazerRefreshProgressService>();
builder.Services.AddHttpClient<TwelveDataService>();
builder.Services.AddHttpClient<MarketStackService>();
builder.Services.AddHttpClient<iTickService>();
builder.Services.AddHttpClient<EodhdService>();
builder.Services.AddHttpClient<FmpService>();
builder.Services.AddHttpClient<NasdaqDataLinkService>();
builder.Services.AddHttpClient<TrailBlazerDataService>();
builder.Services.AddHttpClient<OecdDataService>();
builder.Services.AddHttpClient<WorldBankDataService>();
builder.Services.AddScoped<ApiRateLimitService>();
            builder.Services.AddHttpClient<AlphaVantageService>();
builder.Services.AddScoped<TrailBlazerScoringEngine>();
builder.Services.AddHostedService<TrailBlazerBackgroundService>();
builder.Services.AddHostedService<LogCleanupService>();

// Configure Kestrel for large file uploads
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 100_000_000; // 100MB
});

var app = builder.Build();

// Middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// CORS must come before UseAuthentication() and handle preflight requests
app.UseCors();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers(); // Map API controllers
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Seed roles and admin user before starting the app
using (var scope = app.Services.CreateScope())
{
    await StartupTasks.SeedRolesAndAdminAsync(scope.ServiceProvider);
}

// Standalone: load technical data from Twelve Data only (e.g. dotnet run -- load-technical)
if (args.Contains("load-technical") || args.Contains("--load-technical") || args.Contains("load-twelve-data") || args.Contains("--load-twelve-data"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var twelveDataService = scope.ServiceProvider.GetRequiredService<TwelveDataService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Loading technical data from Twelve Data only (rate limit ~8 req/min)...");
        var count = await twelveDataService.LoadTechnicalDataToDatabaseAsync(db, limit: 8);
        logger.LogInformation("Loaded technical data for {Count} instruments.", count);
    }
    return;
}

// Standalone: delete retail data not from myfxbook/load-myfxbook (e.g. dotnet run -- cleanup-retail)
if (args.Contains("cleanup-retail") || args.Contains("--cleanup-retail"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var allScores = await db.TrailBlazerScores.ToListAsync();
    var toDelete = allScores.Where(s =>
    {
        var ds = s.DataSources ?? "";
        return !ds.Contains("myfxbook", StringComparison.OrdinalIgnoreCase)
            && !ds.Contains("load-myfxbook", StringComparison.OrdinalIgnoreCase);
    }).ToList();
    db.TrailBlazerScores.RemoveRange(toDelete);
    var snapshotsDeleted = await db.RetailSentimentSnapshots.ExecuteDeleteAsync();
    await db.SaveChangesAsync();
    logger.LogInformation("Cleanup: deleted {ScoreCount} TrailBlazerScores (non-myfxbook retail) and {SnapshotCount} RetailSentimentSnapshots.", toDelete.Count, snapshotsDeleted);
    return;
}

// Standalone: run full TrailBlazer refresh (COT, FRED, TwelveData, Brave/Finnhub, MyFXBook) — populates all score components
if (args.Contains("refresh-trailblazer") || args.Contains("--refresh-trailblazer"))
{
    using (var scope = app.Services.CreateScope())
    {
        var bgService = app.Services.GetServices<IHostedService>().OfType<TrailBlazerBackgroundService>().FirstOrDefault();
        if (bgService == null)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError("TrailBlazerBackgroundService not found");
            Environment.Exit(1);
        }
        var logger2 = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger2.LogInformation("Running full TrailBlazer refresh (COT, FRED, Technical, News, Retail)...");
        await bgService.RunRefreshCycleAsync();
        logger2.LogInformation("TrailBlazer refresh completed.");
    }
    return;
}

// Standalone: load MyFXBook data to DB without starting the web app (e.g. dotnet run -- load-myfxbook)
if (args.Contains("load-myfxbook") || args.Contains("--load-myfxbook"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dataService = scope.ServiceProvider.GetRequiredService<TrailBlazerDataService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var session = Environment.GetEnvironmentVariable("MYFXBOOK_SESSION");
        logger.LogInformation("Loading MyFXBook data to database... (Session: {HasSession})", session != null ? "provided" : "login");
        var batch = await dataService.FetchMyFxBookSentimentBatchWithSessionAsync(session);
        var instruments = await db.Instruments.ToDictionaryAsync(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["JPN225"] = "JP225", ["GER30"] = "DE40", ["NAS100"] = "US100" };
        var updated = 0;
        foreach (var kv in batch)
        {
            var key = kv.Key;
            var lookupKey = aliasMap.TryGetValue(key, out var alias) ? alias : key;
            if (!instruments.TryGetValue(lookupKey, out var instrument)) continue;
            var (longPct, shortPct) = kv.Value;
            if (Math.Abs(longPct - 50) < 1 && Math.Abs(shortPct - 50) < 1) continue; // Skip 50/50 — do not overwrite good data with default
            var retailScore = longPct >= 70 ? 2.5 : longPct >= 60 ? 4.0 : longPct <= 30 ? 8.0 : longPct <= 40 ? 6.5 : 5.0;
            var dataSources = System.Text.Json.JsonSerializer.Serialize(new[] { "myfxbook", "load-myfxbook" });
            var existing = await db.TrailBlazerScores.Where(s => s.InstrumentId == instrument.Id).OrderByDescending(s => s.DateComputed).FirstOrDefaultAsync();
            if (existing != null)
            {
                existing.RetailLongPct = Math.Round(longPct, 2);
                existing.RetailShortPct = Math.Round(shortPct, 2);
                existing.RetailSentimentScore = Math.Round(retailScore, 2);
                existing.DataSources = dataSources;
                existing.DateComputed = DateTime.UtcNow;
            }
            else
            {
                db.TrailBlazerScores.Add(new TrailBlazerScore
                {
                    InstrumentId = instrument.Id,
                    OverallScore = 5.0,
                    Bias = "Neutral",
                    FundamentalScore = 5.0,
                    SentimentScore = retailScore,
                    TechnicalScore = 5.0,
                    COTScore = 5.0,
                    RetailSentimentScore = Math.Round(retailScore, 2),
                    RetailLongPct = Math.Round(longPct, 2),
                    RetailShortPct = Math.Round(shortPct, 2),
                    EconomicScore = 5.0,
                    DataSources = dataSources,
                    DateComputed = DateTime.UtcNow
                });
            }
            updated++;
        }
        await db.SaveChangesAsync();
        logger.LogInformation("Loaded MyFXBook retail sentiment for {Updated} instruments.", updated);
    }
    return;
}

app.Run();