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

## Security notes

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
