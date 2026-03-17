# Deploying Trade Tracker to SmarterASP.NET (All-in-One)

This guide covers hosting the .NET 9 API and React frontend together on SmarterASP.NET using **Self-Contained** deployment (the .NET 9 runtime is bundled—no "Runtime" or "Not Found" issues).

## 1. Prepare the React Frontend

### Set the API URL
The file `TradeTrackerFrontEnd/.env.production` is already configured with:
```
VITE_API_BASE_URL=/api
```
(Relative path since frontend and API share the same domain.)

### Build the Frontend
```bash
cd TradeTrackerFrontEnd
npm install
npx vite build
```

### Copy Build Output
Copy **everything** from `TradeTrackerFrontEnd/dist/` into `TradeHelperAPI/wwwroot/`.

## 2. Configure the .NET Backend

`Program.cs` is already configured for all-in-one deployment:
- `UseDefaultFiles()` – serves `index.html` for `/`
- `UseStaticFiles()` – serves CSS/JS from `wwwroot`
- `MapControllers()` – API routes under `/api/*`
- `MapFallbackToFile("index.html")` – React client-side routing

### Project Settings (already in TradeHelperAPI.csproj)
- **ServerGarbageCollection=false** – Workstation GC for shared hosting (avoids high memory usage)
- **RuntimeIdentifier=win-x64** – 64-bit Windows (matches 64-bit app pool; use win-x86 if pool has "Enable 32-Bit" = True)
- **SelfContained=true** – Bundles the .NET 9 runtime so the server doesn't need it installed

### Publish Locally
1. In Visual Studio: Right-click **TradeHelperAPI** → **Publish**
2. Target: **Folder**
3. **Deployment Mode: Self-Contained**
4. **Target Runtime: win-x64** (64-bit; required if app pool is 64-bit—fixes "500.32 Failed to load .NET Core host")

Or via command line:
```bash
dotnet publish -c Release -o ./publish
```

## 3. Publish Folder Structure (verify before upload)

Your publish folder must contain:
- **Many .dll files** – the bundled .NET 9 engine
- **TradeHelperAPI.exe** – main entry point
- **appsettings.json** and **appsettings.Production.json** – with your connection string
- **wwwroot/** – **must** contain `index.html` and `assets/` (React build output)

## 4. SmarterASP.NET Upload

### Database
1. Control Panel → **Databases** → **MSSQL** → **Add Database**
2. Run migrations:
   - **Development:** `dotnet ef database update --project TradeHelperAPI` (uses appsettings.json)
   - **Production:** `dotnet ef database update --project TradeHelperAPI --connection "Server=sql6033.site4now.net;Database=db_ac6619_veeg2fresh;User Id=db_ac6619_veeg2fresh_admin;Password=YOUR_PASSWORD;TrustServerCertificate=True"`
   - **If Production fails** (migration history mismatch): Run `TradeHelperAPI/Migrations/ApplyFixEconomicHeatmapEntries_Production.sql` in SmarterASP's database query tool.

### FTP Upload (recommended if Web Deploy fails)
1. **Delete everything** in `/site/wwwroot/` on the server (FileZilla)
2. Upload the **contents** of your local Publish folder into `/site/wwwroot/`
3. Control Panel → **Restart Site**

### Web Deploy (if Validate Connection succeeds)
- Use the `veeg2fresh-WebDeploy` publish profile
- If "Validate Connection" fails, check your SmarterASP password in the profile—do not try to open `msdeploy.axd` in a browser

### Environment
- Set `ASPNETCORE_ENVIRONMENT=Production` in Application Settings (or rely on `appsettings.Production.json`)

## 5. Keep Background Jobs Running

Shared hosts often stop the app after ~20 minutes of inactivity.

- **Premium plan:** App Pool → Idle Timeout = 0, Start Mode = AlwaysRunning
- **Other plans:** Use [UptimeRobot](https://uptimerobot.com) to ping your site every 5 minutes

## Troubleshooting 500 Internal Server Error

If you see **500** with the generic IIS message ("There is a problem with the resource you are looking for"):

### 1. Recycle the App Pool after deploy
- **Control Panel** → **Hosting Manager** → **Application Pools**
- Find your site's pool → **Recycle** (or **Restart Site**)
- Web Deploy may not restart the process; the old exe might still be running

### 2. Enable and check stdout logs (critical)
In your site root (`/site/wwwroot/`), locate `web.config`:

1. **Create a `logs` folder** if it does not exist (the project now deploys one).
2. Ensure the `aspNetCore` element has:
   ```xml
   stdoutLogEnabled="true"
   stdoutLogFile=".\logs\stdout"
   ```
3. Via **FTP**, open the `logs/` folder and look for `stdout_*.log` – contains startup exceptions and stack traces.
4. **For more verbose errors**, edit `web.config` and temporarily set:
   ```xml
   <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" />
   ```
   (Note: this uses `appsettings.Development.json` – ensure it has a valid connection string for your DB, or the app may fail for a different reason.)

### 3. Verify Site Type and bitness
- **Site Type** must be **ASP.NET Core** or **No Managed Code**
- App pool bitness must match: `win-x64` for 64-bit pool, `win-x86` if **Enable 32-Bit Applications** = True

### 4. Database connection
- Ensure `appsettings.Production.json` has the correct connection string
- Test the connection string locally: `dotnet ef database update --connection "..."`

### 5. JWT key
- `appsettings.json` (or Production) must have `Jwt:Key` (min 32 chars)
- Missing key throws at startup: "Jwt:Key must be configured"

## Troubleshooting 401 Unauthorized (HTML Error Page)

If you see **401** on `/api/auth/check`, `/vite.svg`, or static files, and the response body is **HTML** (not JSON), the request is being blocked by **IIS/SmarterASP before it reaches the app**. Fix in the Control Panel:

### 1. Site Type (most common cause)
- **Hosting Manager** → **Website Domain Manager** → **Site Type**
- Select the correct type for ASP.NET Core (e.g. "ASP.NET Core" or "No Managed Code")
- Wrong type (e.g. "ASP.NET 4.x") can cause 401.2

### 2. Authentication
- **Manage Website** → **Authentication** or **IIS Settings**
- Ensure **Anonymous Authentication** is **enabled**
- Disable **Basic Authentication** and **Windows Authentication** if enabled

### 3. Site Guard / Application Firewall
- **Manage Website** → **Site Guard**
- If enabled, try turning **OFF** temporarily to test
- Or toggle **ON then OFF** to reset

### 4. IP Protection
- Disable if it blocks your IP or requires login

### 5. Check App Logs
- After deploy, check `logs/stdout*.log` via FTP
- If empty or missing, the app may not be starting (requests never reach it)

## Troubleshooting App Pool Crashes ("Process terminated unexpectedly", "ISAPI unhealthy")

If the app pool keeps recycling with exit code 0xffffffff or "ISAPI reported unhealthy":

1. **Background services** – The app now delays DailyPredictionService (5 min) and TrailBlazerBackgroundService (3 min) so the app can serve HTTP requests first. If crashes persist, consider disabling them via config.

2. **Startup timeout** – `web.config` has `startupTimeLimit="300"` (5 min). If DB connection is slow, the app has more time to start.

3. **Seed failure** – Startup seeding is wrapped in try-catch; DB failures no longer crash the app.

4. **Memory** – With 800 MB limit, ensure `ServerGarbageCollection=false` (Workstation GC) is set. Check `logs/stdout*.log` for OOM or exceptions.

## Verification

- **Frontend:** `https://your-site.com`
- **API:** `https://your-site.com/api/auth/login` (and other endpoints)
- **Logs:** Check the `Logs/` folder via FTP if you see 500 errors
