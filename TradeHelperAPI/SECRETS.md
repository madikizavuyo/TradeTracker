# Secrets and local configuration

**Do not commit real API keys or passwords to the repository.**

This project uses several API keys and secrets. Configure them in one of these ways:

1. **Recommended for local development:** `appsettings.Development.json`  
   This file is in `.gitignore`. Create it in the same folder as `appsettings.json` and override only the values you need, for example:

   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Data Source=YOUR_SERVER;..."
     },
     "Google": { "ApiKey": "your-google-api-key" },
     "TrailBlazer": {
       "BraveApiKey": "your-brave-api-key",
       "FredApiKey": "your-fred-api-key"
     }
   }
   ```

2. **Environment variables**  
   You can set environment variables; ASP.NET Core will bind them (e.g. `Google__ApiKey`, `TrailBlazer__BraveApiKey`).

3. **User Secrets (development)**  
   From the `TradeHelperAPI` folder run:
   ```bash
   dotnet user-secrets set "Google:ApiKey" "your-key"
   dotnet user-secrets set "TrailBlazer:BraveApiKey" "your-key"
   ```

After any leak, **rotate the exposed keys** in the respective provider dashboards (Google Cloud Console, Brave Search, etc.); keys that were in git history are considered compromised.
