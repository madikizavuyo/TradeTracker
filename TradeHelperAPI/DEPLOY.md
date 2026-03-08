# Deployment Guide — SmarterASP (FTP)

## 1. Publish folder path

**Primary publish output:**
```
c:\Workplace\TradeTracker\TradeHelperAPI\publish-out
```

Alternative (Release build default):
```
c:\Workplace\TradeTracker\TradeHelperAPI\bin\Release\net9.0\win-x64\publish-out
```
*DeployFtp.ps1 uses `publish-out` at project root by default.*

---

## 2. Pre-deploy steps

### A. Build frontend (if changed)
```powershell
cd c:\Workplace\TradeTracker\TradeTrackerFrontEnd
npm run build
```
Vite outputs to `dist/`. Copy contents to API wwwroot:
```powershell
Copy-Item -Path "dist\*" -Destination "..\TradeHelperAPI\wwwroot\" -Recurse -Force
```

### B. Publish API
```powershell
cd c:\Workplace\TradeTracker\TradeHelperAPI
dotnet publish -c Release -o publish-out
```

---

## 3. Files to transfer (FTP)

**Transfer everything in `publish-out`** except:

| Excluded | Reason |
|----------|--------|
| `wwwroot/Identity/*` | Locked when site runs; stop site first for full deploy |
| `publish-test/*` | Nested junk from bad publishes |
| `publish-out/*` (nested) | Nested junk |
| `*.user`, `*.pubxml` | Publish profile files |

### Main content transferred

| Category | Path / files |
|----------|--------------|
| **Executable** | `TradeHelperAPI.exe`, `TradeHelperAPI.dll`, `TradeHelperAPI.pdb` |
| **Config** | `web.config`, `appsettings.json`, `appsettings.Production.json`, `appsettings.Development.json` |
| **Runtime** | `*.dll`, `*.exe` (ASP.NET Core, dependencies) |
| **Static** | `wwwroot/index.html`, `wwwroot/assets/*`, `wwwroot/vite.svg` |
| **Logs** | `logs/.gitkeep` (folder for stdout) |

---

## 4. Deploy command

```powershell
cd c:\Workplace\TradeTracker\TradeHelperAPI
.\DeployFtp.ps1
```

**Options:**
- `-ForceFull` — upload all files (no incremental)
- `-SourcePath "path"` — use a different source folder
- `-SkipDelete` — overwrite only (no remote deletes)

**Before deploy:** Stop the site in SmarterASP Control Panel to avoid 550 errors on locked files.

**After deploy:** Restart the site in SmarterASP Control Panel.

---

## 5. FTP target

- **Host:** win6053.site4now.net
- **Path:** /site1
- **Credentials:** From `Properties\PublishProfiles\veeg2fresh-FTP.pubxml.user`
