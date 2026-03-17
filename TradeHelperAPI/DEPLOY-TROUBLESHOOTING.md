# SmarterASP Deployment Troubleshooting

## HTTP 500.19 – Config section locked (0x80070021)

**Error:** "This configuration section cannot be used at this path" on `<authentication>`.

**Cause:** SmarterASP locks `<system.webServer><security><authentication>` at the parent level. Your web.config must not include that section.

**Fix:** Replace `site1/web.config` on the server with this minimal version (no authentication block):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath=".\TradeHelperAPI.exe"
            arguments=""
            stdoutLogEnabled="true"
            stdoutLogFile=".\logs\stdout"
            hostingModel="outofprocess"
            startupTimeLimit="300">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

**Remove** any `<security>` or `<authentication>` block that contains `<anonymousAuthentication>` or `<windowsAuthentication>`.

---

## 404 Troubleshooting

**Root 404 but /api/health works?** → wwwroot (React app) wasn't deployed. Stop site, redeploy, start site.

If http://veeg2fresh-001-site1.ntempurl.com/ returns **404**, check the following:

## 1. Site Status
- Log in to **SmarterASP Control Panel**
- Go to **Websites** → your site
- Ensure the site is **Started** (not Stopped)
- Ensure the **Application Pool** is running

## 2. Correct URL
- **ntempurl.com** is a temporary URL and may change
- In Control Panel, find your site's **actual URL** (might be under Domain/Bindings)
- Try both `http://` and `https://` if you have SSL

## 3. Redeploy (site was down or wwwroot missing)
1. **Stop** the site in Control Panel
2. Run: `powershell -ExecutionPolicy Bypass -File .\DeployFtp.ps1 -ForceFull`
3. **Start** the site
4. Wait 1–2 minutes for the app to start

## 4. Check Logs
- In Control Panel, open **Log Files** or **Application Logs**
- Look for `stdout` logs in `site1/logs/` (if enabled)
- Check for 500.30 or other startup errors

## 5. Test if the app is running
- Try **http://veeg2fresh-001-site1.ntempurl.com/api/health**
- If this returns JSON (not 404), the app is running and the issue is with the root/SPA
- If this also returns 404, the app is not running or the URL is wrong

## 6. Verify files on server
- Use **FTP** to connect and confirm:
  - `site1/TradeHelperAPI.exe` exists
  - `site1/web.config` exists
  - `site1/wwwroot/index.html` exists
- If wwwroot is empty, redeploy with the site stopped
