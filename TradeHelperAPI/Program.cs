using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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

// Database logging: persist ILogger output to ApplicationLogs table (enabled in Production for refresh failure diagnosis)
builder.Services.AddSingleton<ILoggerProvider>(sp => new DbLoggerProvider(sp, sp.GetRequiredService<IConfiguration>()));

// Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity (MaxLengthForKeys=128 for SmarterASP.NET 900-byte index limit)
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.Stores.MaxLengthForKeys = 128;
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
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey))
    throw new InvalidOperationException("Jwt:Key must be configured. See SECRETS.md.");
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

// CORS - load allowed origins from config for production
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://127.0.0.1:5173", "http://localhost:5174", "http://127.0.0.1:5174" };
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
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

// DataProtection: persist keys to app directory (SmarterASP shared hosting - no registry/user profile)
var keysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

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
builder.Services.AddHttpClient<TrailBlazerDataService>()
    .AddTypedClient((http, sp) => new TrailBlazerDataService(
        http,
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<TrailBlazerDataService>>(),
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ApiRateLimitService>(),
        sp.GetService<TwelveDataService>(),
        sp.GetService<MarketStackService>(),
        sp.GetService<iTickService>(),
        sp.GetService<EodhdService>(),
        sp.GetService<FmpService>(),
        sp.GetService<NasdaqDataLinkService>(),
        sp.GetService<IMemoryCache>()));
builder.Services.AddHttpClient<OecdDataService>();
builder.Services.AddHttpClient<WorldBankDataService>();
builder.Services.AddScoped<ApiRateLimitService>();
builder.Services.AddSingleton<LoginRateLimitService>();
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
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("An error occurred. Please try again later.");
        });
    });
    app.UseHsts();
}

// Skip HTTPS redirect on HTTP-only hosting (SmarterASP/ntempurl)
if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseDefaultFiles(); // Finds index.html for /
app.UseStaticFiles();  // Serves CSS/JS from wwwroot

// CORS must come before UseAuthentication() and handle preflight requests
app.UseCors();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers(); // API routes under /api/*
app.MapFallbackToFile("index.html"); // React SPA routing (all-in-one deployment)

// Seed roles and admin user before starting the app (don't crash if DB is slow/unavailable)
try
{
    using (var scope = app.Services.CreateScope())
    {
        await StartupTasks.SeedRolesAndAdminAsync(scope.ServiceProvider);
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Seed roles/admin failed (app will continue)");
}

// Standalone: apply FixEconomicHeatmapEntriesIdentity migration (when ef database update fails due to history mismatch)
if (args.Contains("run-fix-migration") || args.Contains("--run-fix-migration"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var sql = @"
        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EconomicHeatmapEntries')
        AND NOT EXISTS (
            SELECT 1 FROM sys.identity_columns ic
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            JOIN sys.tables t ON c.object_id = t.object_id
            WHERE t.name = 'EconomicHeatmapEntries' AND c.name = 'Id'
        )
        BEGIN
            CREATE TABLE [dbo].[EconomicHeatmapEntries_new] (
                [Id] int NOT NULL IDENTITY(1,1),
                [Currency] nvarchar(10) NOT NULL,
                [Indicator] nvarchar(50) NOT NULL,
                [Value] float NOT NULL,
                [PreviousValue] float NOT NULL,
                [Impact] nvarchar(20) NOT NULL,
                [DateCollected] datetime2 NOT NULL,
                CONSTRAINT [PK_EconomicHeatmapEntries_new] PRIMARY KEY ([Id])
            );
            CREATE INDEX [IX_EconomicHeatmapEntries_new_Currency_Indicator] ON [dbo].[EconomicHeatmapEntries_new] ([Currency], [Indicator]);
            SET IDENTITY_INSERT [dbo].[EconomicHeatmapEntries_new] ON;
            INSERT INTO [dbo].[EconomicHeatmapEntries_new] ([Id], [Currency], [Indicator], [Value], [PreviousValue], [Impact], [DateCollected])
            SELECT [Id], [Currency], [Indicator], [Value], [PreviousValue], [Impact], [DateCollected]
            FROM [dbo].[EconomicHeatmapEntries];
            SET IDENTITY_INSERT [dbo].[EconomicHeatmapEntries_new] OFF;
            DROP TABLE [dbo].[EconomicHeatmapEntries];
            EXEC sp_rename 'dbo.EconomicHeatmapEntries_new', 'EconomicHeatmapEntries';
            EXEC sp_rename 'PK_EconomicHeatmapEntries_new', 'PK_EconomicHeatmapEntries';
            EXEC sp_rename 'dbo.EconomicHeatmapEntries.IX_EconomicHeatmapEntries_new_Currency_Indicator', 'IX_EconomicHeatmapEntries_Currency_Indicator', 'INDEX';
        END
        IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260307212133_FixEconomicHeatmapEntriesIdentity')
        BEGIN
            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES (N'20260307212133_FixEconomicHeatmapEntriesIdentity', N'9.0.5');
        END";
    await db.Database.ExecuteSqlRawAsync(sql);
    logger.LogInformation("FixEconomicHeatmapEntriesIdentity applied successfully.");
    return;
}

// Standalone: fix TechnicalIndicators.Id to be IDENTITY (production: dotnet run -- run-fix-technical-indicators)
if (args.Contains("run-fix-technical-indicators") || args.Contains("--run-fix-technical-indicators"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var sql = @"
        IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TechnicalIndicators')
        AND NOT EXISTS (
            SELECT 1 FROM sys.identity_columns ic
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            JOIN sys.tables t ON c.object_id = t.object_id
            WHERE t.name = 'TechnicalIndicators' AND c.name = 'Id'
        )
        BEGIN
            CREATE TABLE [dbo].[TechnicalIndicators_new] (
                [Id] int NOT NULL IDENTITY(1,1),
                [InstrumentId] int NOT NULL,
                [Date] datetime2 NOT NULL,
                [RSI] float NULL,
                [SMA14] float NULL,
                [SMA50] float NULL,
                [EMA50] float NULL,
                [EMA200] float NULL,
                [DateCollected] datetime2 NOT NULL,
                [Source] nvarchar(50) NULL,
                CONSTRAINT [PK_TechnicalIndicators_new] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_TechnicalIndicators_new_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([Id]) ON DELETE CASCADE
            );
            CREATE INDEX [IX_TechnicalIndicators_new_InstrumentId_Date] ON [dbo].[TechnicalIndicators_new] ([InstrumentId], [Date]);
            INSERT INTO [dbo].[TechnicalIndicators_new] ([InstrumentId], [Date], [RSI], [SMA14], [SMA50], [EMA50], [EMA200], [DateCollected], [Source])
            SELECT [InstrumentId], [Date], [RSI], [SMA14], [SMA50], [EMA50], [EMA200], [DateCollected], [Source]
            FROM [dbo].[TechnicalIndicators];
            DROP TABLE [dbo].[TechnicalIndicators];
            EXEC sp_rename 'dbo.TechnicalIndicators_new', 'TechnicalIndicators';
            EXEC sp_rename 'PK_TechnicalIndicators_new', 'PK_TechnicalIndicators';
            EXEC sp_rename 'FK_TechnicalIndicators_new_Instruments_InstrumentId', 'FK_TechnicalIndicators_Instruments_InstrumentId', 'OBJECT';
            EXEC sp_rename 'dbo.TechnicalIndicators.IX_TechnicalIndicators_new_InstrumentId_Date', 'IX_TechnicalIndicators_InstrumentId_Date', 'INDEX';
        END";
    await db.Database.ExecuteSqlRawAsync(sql);
    logger.LogInformation("FixTechnicalIndicatorsIdentity applied successfully.");
    return;
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

// Standalone: remove API block (e.g. dotnet run -- clear-api-block Brave)
if (args.Contains("clear-api-block") || args.Contains("--clear-api-block"))
{
    var apiName = args.SkipWhile(a => a != "clear-api-block" && a != "--clear-api-block").Skip(1).FirstOrDefault() ?? "Brave";
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var key = "ApiBlock_" + apiName;
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null)
        {
            db.SystemSettings.Remove(setting);
            await db.SaveChangesAsync();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Removed API block for {Api}.", apiName);
        }
    }
    return;
}

// Standalone: inspect production DB for refresh failures and API blocks (e.g. dotnet run -- inspect-logs --environment Production)
if (args.Contains("inspect-logs") || args.Contains("--inspect-logs"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("=== Inspecting production database for refresh failures ===\n");

        // 1. API blocks (Finnhub, Brave, etc.) — these prevent news from downloading
        var apiBlocks = await db.SystemSettings
            .Where(s => s.Key.StartsWith("ApiBlock_"))
            .OrderBy(s => s.Key)
            .ToListAsync();
        if (apiBlocks.Any())
        {
            logger.LogWarning("API BLOCKS (credit/rate limit — news and other data may not download):");
            foreach (var b in apiBlocks)
            {
                var until = DateTime.TryParse(b.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                    ? dt : (DateTime?)null;
                var apiName = b.Key.Replace("ApiBlock_", "");
                var status = until.HasValue && until.Value > DateTime.UtcNow
                    ? $"BLOCKED until {until.Value:yyyy-MM-dd HH:mm} UTC"
                    : "expired (will retry)";
                logger.LogWarning("  {Api}: {Status}", apiName, status);
            }
        }
        else
        {
            logger.LogInformation("No API blocks found (Finnhub, Brave, etc. should work).");
        }

        // 2. ApplicationLogs — persisted when DbLoggerProvider is enabled (Production + Development)
        var todayStart = DateTime.UtcNow.Date;
        var logs = await db.ApplicationLogs
            .Where(l => l.Timestamp >= todayStart.AddDays(-1))
            .OrderByDescending(l => l.Timestamp)
            .Take(200)
            .ToListAsync();
        if (logs.Any())
        {
            logger.LogInformation("\nRecent ApplicationLogs (last 24h):");
            var failures = logs.Where(l => l.Level == "Error" || l.Level == "Warning").ToList();
            foreach (var l in failures.Take(20))
            {
                var msg = l.Message.Replace("\n", " ");
                logger.LogInformation("  [{Level}] {Time:HH:mm} {Cat}: {Msg}", l.Level, l.Timestamp, l.Category, msg.Length > 100 ? msg[..100] + "..." : msg);
                if (!string.IsNullOrEmpty(l.Exception))
                {
                    var ex = l.Exception.Replace("\r\n", " | ");
                    logger.LogInformation("    Exception: {Ex}", ex.Length > 500 ? ex[..500] + "..." : ex);
                }
            }
            if (failures.Count > 20)
                logger.LogInformation("  ... and {Count} more", failures.Count - 20);
            if (failures.Count == 0)
                logger.LogInformation("  (no errors/warnings in last 24h)");
        }
        else
        {
            logger.LogInformation("\nApplicationLogs: empty.");
        }

        // 3. NewsArticles — verify if news is being persisted
        var newsCutoff = DateTime.UtcNow.AddDays(-3);
        var newsBySymbol = await db.NewsArticles
            .Where(n => n.DateCollected >= newsCutoff)
            .GroupBy(n => n.Symbol)
            .Select(g => new { Symbol = g.Key, Count = g.Count(), Latest = g.Max(n => n.DateCollected) })
            .OrderByDescending(x => x.Latest)
            .Take(15)
            .ToListAsync();
        logger.LogInformation("\nNewsArticles (last 3 days):");
        if (newsBySymbol.Any())
        {
            foreach (var n in newsBySymbol)
                logger.LogInformation("  {Symbol}: {Count} articles, latest {Date:yyyy-MM-dd HH:mm}", n.Symbol, n.Count, n.Latest);
        }
        else
        {
            logger.LogWarning("  No news articles in last 3 days — news may not be downloading or saving.");
        }

        // 4. Instruments — verify BTC, ETH, SOL, ZAR pairs exist (from AddCryptoAndZarInstruments migration)
        var keyInstruments = await db.Instruments
            .Where(i => new[] { "BTC", "ETH", "SOL", "USDZAR", "EURZAR", "GBPZAR" }.Contains(i.Name))
            .Select(i => new { i.Name, i.AssetClass })
            .ToListAsync();
        logger.LogInformation("\nKey instruments (BTC/ETH/SOL/ZAR):");
        if (keyInstruments.Any())
            foreach (var i in keyInstruments) logger.LogInformation("  {Name} ({AssetClass})", i.Name, i.AssetClass ?? "—");
        else
            logger.LogWarning("  BTC, ETH, SOL, ZAR pairs NOT FOUND — run BaselineProductionForAddCryptoAndZar.sql then dotnet ef database update");

        // 5. COT reports — latest ReportDate per symbol
        var cotLatest = await db.COTReports
            .GroupBy(c => c.Symbol)
            .Select(g => new { Symbol = g.Key, ReportDate = g.Max(c => c.ReportDate), Count = g.Count() })
            .OrderByDescending(x => x.ReportDate)
            .Take(15)
            .ToListAsync();
        logger.LogInformation("\nCOT reports (latest per symbol):");
        if (cotLatest.Any())
            foreach (var c in cotLatest) logger.LogInformation("  {Symbol}: {Date:yyyy-MM-dd} ({Count} total)", c.Symbol, c.ReportDate, c.Count);
        else
            logger.LogWarning("  No COT reports in database.");

        // 6. TrailBlazer scores for BTC/ETH/SOL/ZAR — do they appear in Asset Scanner?
        var newInstrumentScores = await (from i in db.Instruments
            where new[] { "BTC", "ETH", "SOL", "EURZAR", "GBPZAR", "AUDZAR" }.Contains(i.Name)
            join s in db.TrailBlazerScores on i.Id equals s.InstrumentId into scores
            from sc in scores.DefaultIfEmpty()
            group sc by new { i.Name } into g
            select new { Name = g.Key.Name, Latest = g.Where(x => x != null).Select(x => x!.DateComputed).DefaultIfEmpty().Max(), Count = g.Count(x => x != null) })
            .ToListAsync();
        logger.LogInformation("\nTrailBlazer scores for new instruments (Asset Scanner / News):");
        if (newInstrumentScores.Any())
        {
            foreach (var s in newInstrumentScores)
            {
                var dateStr = s.Latest == default ? "none" : s.Latest.ToString("yyyy-MM-dd HH:mm");
                logger.LogInformation("  {Name}: latest {Date} ({Count} scores)", s.Name, dateStr, s.Count);
            }
            var missing = newInstrumentScores.Where(x => x.Count == 0).Select(x => x.Name).ToList();
            if (missing.Any())
                logger.LogWarning("  Missing scores for: {Missing} — refresh has not processed them yet.", string.Join(", ", missing));
        }
        else
            logger.LogWarning("  No new instruments found — run AddCryptoAndZarInstruments migration.");

        // 7. Latest TrailBlazer scores — check if news sentiment is present
        var latestScores = await db.TrailBlazerScores
            .Include(s => s.Instrument)
            .OrderByDescending(s => s.DateComputed)
            .Take(10)
            .ToListAsync();
        if (latestScores.Any())
        {
            logger.LogInformation("\nLatest TrailBlazer scores (news/data sources):");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in latestScores)
            {
                var name = s.Instrument?.Name ?? "?";
                if (seen.Contains(name)) continue;
                seen.Add(name);
                var hasNews = (s.DataSources ?? "").Contains("Brave", StringComparison.OrdinalIgnoreCase)
                    || (s.DataSources ?? "").Contains("Finnhub", StringComparison.OrdinalIgnoreCase);
                logger.LogInformation("  {Instrument}: News={HasNews}, DataSources={Ds}, DateComputed={Date:yyyy-MM-dd HH:mm}",
                    name, hasNews ? "yes" : "no", s.DataSources ?? "—", s.DateComputed);
            }
        }
    }
    return;
}

// Standalone: compare instruments vs COT data — find missing COT for instruments that have CFTC data (e.g. dotnet run -- inspect-cot --environment Production)
if (args.Contains("inspect-cot") || args.Contains("--inspect-cot"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dataService = scope.ServiceProvider.GetRequiredService<TrailBlazerDataService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("=== Inspecting COT coverage for production instruments ===\n");

        var instruments = await db.Instruments.Select(i => i.Name).ToListAsync();
        var cotInDb = (await db.COTReports.GroupBy(c => c.Symbol).Select(g => g.Key).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        logger.LogInformation("COT in database: {Count} symbols — {Symbols}", cotInDb.Count, string.Join(", ", cotInDb.OrderBy(x => x)));

        logger.LogInformation("Fetching COT from CFTC (all URLs)...");
        var cotFromCftc = await dataService.FetchCOTReportBatchAsync();
        var cotSymbolsFromCftc = cotFromCftc.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        logger.LogInformation("COT parsed from CFTC: {Count} symbols — {Symbols}", cotSymbolsFromCftc.Count, string.Join(", ", cotSymbolsFromCftc.OrderBy(x => x)));

        var missingInDb = instruments.Where(i => !cotInDb.Contains(i)).OrderBy(x => x).ToList();
        var availableFromCftc = missingInDb.Where(i => cotSymbolsFromCftc.Contains(i)).OrderBy(x => x).ToList();
        var notAvailableFromCftc = missingInDb.Where(i => !cotSymbolsFromCftc.Contains(i)).OrderBy(x => x).ToList();

        logger.LogInformation("\nInstruments with NO COT in DB: {Count}", missingInDb.Count);
        if (availableFromCftc.Any())
        {
            logger.LogWarning("  AVAILABLE from CFTC (add mapping or fix parser): {Symbols}", string.Join(", ", availableFromCftc));
        }
        if (notAvailableFromCftc.Any())
        {
            logger.LogInformation("  Not in CFTC (indices, SOL, etc.): {Symbols}", string.Join(", ", notAvailableFromCftc));
        }
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