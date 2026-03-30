# Secrets and local configuration

**Do not commit real API keys or passwords to the repository.**

## Required for app to start

| Setting | Purpose |
|---------|---------|
| `Jwt:Key` | JWT signing key (min 32 chars). Dev default in `appsettings.Development.json`. |
| `ConnectionStrings:DefaultConnection` | SQL Server connection. Dev uses LocalDB in `appsettings.Development.json`. |

## Optional – for full features

Add these to `appsettings.Development.json` or environment variables:

| Setting | Purpose |
|---------|---------|
| `Google:ApiKey` | Gemini AI analysis, predictions. Get at https://aistudio.google.com/apikey |
| `TrailBlazer:BraveApiKey` | News/search. https://brave.com/search/api/ |
| `TrailBlazer:FredApiKey` | Economic data. https://fred.stlouisfed.org/docs/api/api_key.html |
| `TrailBlazer:FinnhubApiKey` | News. https://finnhub.io/ |
| `TrailBlazer:TwelveDataApiKey` | Technical data. https://twelvedata.com/ |
| `TrailBlazer:AlphaVantageApiKey` | Technical fallback. https://www.alphavantage.co/ |
| `TrailBlazer:MarketStackApiKey` | Technical fallback. https://marketstack.com/ |
| `TrailBlazer:iTickApiKey` | Technical fallback. https://itick.io/ |
| `TrailBlazer:EodhdApiKey` | Technical fallback. https://eodhd.com/ |
| `TrailBlazer:FmpApiKey` | Technical fallback. https://site.financialmodelingprep.com/ |
| `TrailBlazer:NasdaqDataLinkApiKey` | Technical fallback. https://data.nasdaq.com/ |
| `TrailBlazer:MyFXBookEmail` / `MyFXBookPassword` | Retail sentiment. https://www.myfxbook.com/ |
| `TrailBlazer:MyFXBookSession` | Optional. Use session ID directly instead of login (e.g. from browser cookie). Takes precedence over email/password. |
| `TrailBlazer:ExchangeRateApiKey` | Currency conversion. https://www.exchangerate-api.com/ |
| `TrailBlazer:YahooFinanceEnabled` | Default `true`. Yahoo Finance (unofficial chart/quote/news) as primary for technicals, forex quotes, and news when mappable; no API key. Set `false` to disable. |
| `TrailBlazer:SignalAlertEmail` | Optional. If set and `Email:SmtpHost` / `Email:From` are configured, sends at most one email per instrument per 24h when a **STRONG_BUY** or **STRONG_SELL** box-breakout + scanner signal fires. |
| `TrailBlazer:BoxBreakoutLookback` / `BoxBreakoutMaxRangePct` | Consolidation window (days) and max range % of mid-price to qualify as a “tight box” before breakout. |

## How to configure

1. **`appsettings.Development.json`** (recommended for local dev)  
   Override only what you need:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=TradeHelperDB;Integrated Security=True;TrustServerCertificate=True"
     },
     "Jwt": { "Key": "your-32-char-secret-key-here" },
     "TrailBlazer": {
       "BraveApiKey": "your-key",
       "FredApiKey": "your-key"
     }
   }
   ```

2. **Environment variables**  
   Use double underscore: `TrailBlazer__BraveApiKey`, `Google__ApiKey`, etc.

3. **User Secrets**  
   ```bash
   dotnet user-secrets set "Jwt:Key" "your-secret"
   dotnet user-secrets set "TrailBlazer:BraveApiKey" "your-key"
   ```

After any leak, **rotate the exposed keys** in the provider dashboards.

## API cost controls (Gemini / Brave)

| Setting | Default | Purpose |
|---------|---------|---------|
| `Google:GeminiCooldownHours` | 48 | After any successful Gemini call, further Gemini HTTP requests are skipped until this many hours pass. Stored in `SystemSettings` as `GeminiLastCallUtc`. |
| `TrailBlazer:BraveCooldownHours` | 48 | Same pattern for Brave: `BraveLastCallUtc`. |
| `TrailBlazer:BraveOutlookCacheMinutes` | 2880 (48h) | In-memory cache for per-symbol outlook Brave searches. |
| `TrailBlazer:CurrencyStrengthNewsCacheHours` | 48 | How long Gemini-derived currency strength from news stays valid in DB before refresh may refetch news and call Gemini again. |

To **force** an immediate run (e.g. after changing prompts), delete the corresponding row from `SystemSettings` for `GeminiLastCallUtc` and/or `BraveLastCallUtc`, or set `Value` to an old ISO timestamp.

## Security notes

- **`appsettings.json` in the repo** has **no real API keys or passwords** (placeholders and LocalDB). For local development, create **`appsettings.Local.json`** in `TradeHelperAPI/` with your connection string, JWT key, TrailBlazer keys, SMTP, etc. That file is **gitignored** and is merged in after `appsettings.json` (see `Program.cs`).
- `appsettings.Development.json` is in `.gitignore` – never force-add it.
- For stronger security, use **User Secrets** instead of appsettings.Development.json:
  ```powershell
  cd TradeHelperAPI
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Data Source=..."
  dotnet user-secrets set "Jwt:Key" "your-key"
  dotnet user-secrets set "TrailBlazer:BraveApiKey" "your-key"
  # etc.
  ```
  User Secrets override appsettings and are stored outside the project.
