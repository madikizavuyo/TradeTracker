# Backend Requirements Checklist

## 🎯 Essential Backend Requirements for Frontend Integration

This checklist covers everything the backend needs to have for the TradeTracker frontend to work properly.

---

## ✅ 1. CORS Configuration (CRITICAL)

### Required Settings:
```csharp
// In Program.cs
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
            "http://localhost:5173",    // Vite dev server (REQUIRED)
            "http://localhost:3000",
            "http://localhost:5174"
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();            // IMPORTANT for JWT
    });
});

// Enable CORS middleware
app.UseCors();
```

### Test CORS:
```bash
# In browser console at http://localhost:5173
fetch('http://localhost:5014/api/AI/status')
  .then(r => r.json())
  .then(console.log)
```

**Expected:** Should work without CORS errors  
**If failing:** CORS not configured or origin not allowed

---

## ✅ 2. Authentication Endpoints

### POST `/api/Auth/register`
**Request:**
```json
{
  "email": "test@example.com",
  "password": "Test123",
  "confirmPassword": "Test123",
  "firstName": "Test",
  "lastName": "User"
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "userId": "abc123",
    "email": "test@example.com",
    "firstName": "Test",
    "lastName": "User",
    "displayCurrency": null
  }
}
```

### POST `/api/Auth/login`
**Request:**
```json
{
  "email": "test@example.com",
  "password": "Test123",
  "rememberMe": true
}
```

**Response:** Same as register

### POST `/api/Auth/refresh`
**No body required** (uses JWT from Authorization header)

**Response:**
```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs..."
  }
}
```

### POST `/api/Auth/logout`
**No body required**

---

## ✅ 3. Dashboard Endpoint

### GET `/Dashboard/Index`
**Headers:** `Authorization: Bearer {token}`

**Response:**
```json
{
  "totalTrades": 150,
  "winningTrades": 95,
  "losingTrades": 55,
  "openTrades": 3,
  "winRate": 63.3,
  "totalProfitLoss": 5000.00,
  "totalProfitLossDisplay": 5000.00,
  "displayCurrency": "USD",
  "displayCurrencySymbol": "$",
  "profitFactor": 1.85,
  "averageWin": 200.00,
  "averageWinDisplay": 200.00,
  "averageLoss": 150.00,
  "averageLossDisplay": 150.00,
  "recentTrades": [
    {
      "id": 1,
      "instrument": "EURUSD",
      "type": "Long",
      "entryPrice": 1.0850,
      "exitPrice": 1.0920,
      "profitLoss": 100.00,
      "profitLossDisplay": 100.00,
      "dateTime": "2024-10-27T12:00:00Z",
      "status": "Closed"
    }
  ]
}
```

**Note:** Frontend expects numbers like `winRate` and `profitFactor` to be numbers, not null!

---

## ✅ 4. Trades Endpoints

### GET `/Trades/Index`
**Query Parameters:**
- `pageNumber` (default: 1)
- `pageSize` (default: 50)
- `sortBy` (optional: "date", "instrument", "profit")
- `sortOrder` (optional: "asc", "desc")
- `search` (optional: search term)
- `instrument` (optional: filter by instrument)
- `strategyId` (optional: filter by strategy)
- `status` (optional: "Open", "Closed", "Cancelled")
- `startDate` (optional: ISO 8601 date)
- `endDate` (optional: ISO 8601 date)

**Response:**
```json
{
  "success": true,
  "data": {
    "items": [...],
    "totalCount": 150,
    "pageNumber": 1,
    "pageSize": 50,
    "totalPages": 3,
    "hasPreviousPage": false,
    "hasNextPage": true
  }
}
```

### GET `/Trades/Details/{id}`
### POST `/Trades/Create` (with FormData for images)
### POST `/Trades/Edit/{id}` (with FormData)
### POST `/Trades/Delete/{id}`

---

## ✅ 5. Strategies Endpoints

### GET `/Strategies/Index`
**Response:**
```json
[
  {
    "id": 1,
    "name": "Trend Following",
    "description": "...",
    "isActive": true,
    "totalTrades": 50,
    "winRate": 65.0,
    "profitFactor": 2.1,
    "totalProfitLoss": 2500.00,
    "totalProfitLossDisplay": 2500.00,
    "displayCurrencySymbol": "$"
  }
]
```

### GET `/Strategies/Details/{id}`
### POST `/Strategies/Create`
### POST `/Strategies/Edit/{id}`
### POST `/Strategies/Delete/{id}`

---

## ✅ 6. Reports Endpoints

### GET `/api/Reports/performance`
**Query Parameters:**
- `startDate` (optional)
- `endDate` (optional)
- `strategyId` (optional)

**Response:**
```json
{
  "success": true,
  "data": {
    "totalTrades": 100,
    "winRate": 60.0,
    "profitFactor": 1.8,
    "totalProfitLoss": 3000.00,
    "displayCurrencySymbol": "$",
    "averageWin": 150.00,
    "averageLoss": 100.00
  }
}
```

### GET `/api/Reports/charts`
Returns available chart types

### GET `/api/Reports/charts/{chartType}`
Returns chart data (equity curve, monthly performance, etc.)

---

## ✅ 7. Settings Endpoints

### GET `/api/Settings`
**Response:**
```json
{
  "success": true,
  "data": {
    "userId": "abc123",
    "displayCurrency": "USD",
    "displayCurrencySymbol": "$",
    "email": "test@example.com",
    "firstName": "Test",
    "lastName": "User"
  }
}
```

### PUT `/api/Settings/currency`
**Request:**
```json
{
  "currency": "EUR"
}
```

### GET `/api/Settings/currencies`
**Response:**
```json
{
  "success": true,
  "data": [
    { "code": "USD", "name": "US Dollar", "symbol": "$" },
    { "code": "EUR", "name": "Euro", "symbol": "€" },
    { "code": "GBP", "name": "British Pound", "symbol": "£" },
    { "code": "JPY", "name": "Japanese Yen", "symbol": "¥" },
    { "code": "AUD", "name": "Australian Dollar", "symbol": "A$" },
    { "code": "CAD", "name": "Canadian Dollar", "symbol": "C$" },
    { "code": "CHF", "name": "Swiss Franc", "symbol": "CHF" },
    { "code": "ZAR", "name": "South African Rand", "symbol": "R" }
  ]
}
```

### GET `/api/Settings/currency/test`
For testing currency conversion

---

## ✅ 8. Import Endpoints

### GET `/api/Import/history`
Returns import history

### POST `/api/Import/upload`
**Content-Type:** `multipart/form-data`
**Body:** File upload (CSV/Excel)

**Response:**
```json
{
  "success": true,
  "message": "Import completed: 45 trades imported, 3 skipped",
  "data": {
    "tradesImported": 45,
    "tradesSkipped": 3,
    "fileName": "trades.csv",
    "fileSize": 15360,
    "processingMethod": "Standard CSV Parser"
  }
}
```

---

## ✅ 9. MetaTrader5 Endpoints

### GET `/api/MetaTrader5/history`
### POST `/api/MetaTrader5/connect`
### POST `/api/MetaTrader5/import`
### POST `/api/MetaTrader5/upload` (with AI processing)
### GET `/api/MetaTrader5/account-info`
### DELETE `/api/MetaTrader5/clear-data`

---

## ✅ 10. ML Trading Endpoints

### GET `/api/MLTrading/history`
### POST `/api/MLTrading/upload` (AI file processing)
### GET `/api/MLTrading/strategies`

---

## ✅ 11. Error Response Format

All error responses should follow this format:

```json
{
  "success": false,
  "message": "Human-readable error message",
  "errors": [
    "Detailed error 1",
    "Detailed error 2"
  ],
  "statusCode": 400,
  "timestamp": "2024-10-27T12:00:00Z"
}
```

### HTTP Status Codes to Use:
- `200` - Success
- `201` - Created
- `400` - Bad Request (validation errors)
- `401` - Unauthorized (missing/invalid token)
- `403` - Forbidden (insufficient permissions)
- `404` - Not Found
- `423` - Locked (account locked)
- `500` - Internal Server Error

---

## ✅ 12. Password Validation Rules

Must match frontend requirements:
```csharp
options.Password.RequireDigit = true;           // Must have 0-9
options.Password.RequireLowercase = true;       // Must have a-z
options.Password.RequireNonAlphanumeric = false; // NO special chars required
options.Password.RequireUppercase = true;       // Must have A-Z
options.Password.RequiredLength = 6;            // Minimum 6 chars
```

---

## ✅ 13. JWT Configuration

### Token Generation:
```csharp
// Should include these claims
new Claim(ClaimTypes.NameIdentifier, userId),
new Claim(ClaimTypes.Email, email),
new Claim("firstName", firstName),
new Claim("lastName", lastName)
```

### Token Expiry:
- Recommended: 24 hours (1440 minutes)
- Frontend refreshes proactively 30 minutes before expiry

### Authorization Header:
Backend must accept: `Authorization: Bearer {token}`

---

## ✅ 14. File Upload Constraints

### Maximum File Size:
```csharp
[RequestSizeLimit(100_000_000)] // 100MB
```

### Supported File Types:
- CSV: `.csv`
- Excel: `.xlsx`, `.xls`
- PDF: `.pdf` (for AI processing)
- Images: `.jpg`, `.jpeg`, `.png` (for AI processing)

---

## ✅ 15. Database Constraints

### User:
- Email must be unique (case-insensitive)
- Username = Email

### Trades:
- Instrument: Required, max 50 chars
- EntryPrice: Required, decimal(18,5)
- ProfitLoss: decimal(18,2)
- DateTime: Required, UTC

### Strategies:
- Name: Required, max 100 chars
- Description: Optional, max 5000 chars

---

## 🧪 Testing the Backend

### Test 1: Health Check
```bash
curl http://localhost:5014/api/AI/status
```

**Expected:**
```json
{
  "success": true,
  "data": {
    "available": true,
    "timestamp": "2024-10-27T...",
    "service": "DeepSeek AI"
  }
}
```

### Test 2: Register User
```bash
curl -X POST http://localhost:5014/api/Auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com",
    "password": "Test123",
    "confirmPassword": "Test123",
    "firstName": "Test",
    "lastName": "User"
  }'
```

**Expected:** JWT token in response

### Test 3: Login
```bash
curl -X POST http://localhost:5014/api/Auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com",
    "password": "Test123",
    "rememberMe": true
  }'
```

**Expected:** JWT token in response

### Test 4: Dashboard (with token)
```bash
curl http://localhost:5014/Dashboard/Index \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Expected:** Dashboard data with numbers (not nulls)

### Test 5: CORS Test
Open browser console at `http://localhost:5173` and run:
```javascript
fetch('http://localhost:5014/api/AI/status')
  .then(r => r.json())
  .then(console.log)
  .catch(console.error)
```

**Expected:** No CORS errors, data returned

---

## 🚨 Common Issues & Solutions

### Issue 1: CORS Errors
**Symptom:** `Access to XMLHttpRequest blocked by CORS policy`

**Fix:**
```csharp
// Ensure CORS is configured BEFORE app.UseAuthentication()
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
```

### Issue 2: 401 Unauthorized
**Symptom:** All authenticated requests return 401

**Causes:**
- JWT token not being read from Authorization header
- Token expired
- Token signature invalid

**Fix:**
```csharp
// Ensure JWT authentication is configured
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"],
            ValidAudience = configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(configuration["Jwt:Key"])
            )
        };
    });
```

### Issue 3: Dashboard Shows Zeros
**Symptom:** Dashboard loads but shows 0 for all metrics

**Causes:**
- No trades in database
- Dashboard endpoint returning null for numeric fields

**Fix:**
- Add sample trades to database
- Ensure dashboard calculates winRate and profitFactor even when no trades

### Issue 4: File Upload Fails
**Symptom:** File upload returns 413 Payload Too Large

**Fix:**
```csharp
// In Program.cs
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100_000_000; // 100MB
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 100_000_000;
});
```

### Issue 5: Database Connection
**Symptom:** 500 errors on all endpoints

**Fix:**
- Check connection string in appsettings.json
- Run migrations: `dotnet ef database update`
- Check database server is running

---

## ✅ Quick Verification Commands

### Check Backend is Running:
```bash
curl http://localhost:5014/api/AI/status
```

### Check CORS:
```bash
curl -i -H "Origin: http://localhost:5173" http://localhost:5014/api/AI/status
```

Should see: `Access-Control-Allow-Origin: http://localhost:5173`

### Check Auth Endpoints:
```bash
# Register
curl -X POST http://localhost:5014/api/Auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"test2@test.com","password":"Test123","confirmPassword":"Test123","firstName":"T","lastName":"U"}'

# Login  
curl -X POST http://localhost:5014/api/Auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test2@test.com","password":"Test123","rememberMe":true}'
```

---

## 📊 Minimum Working Backend

For the frontend to work, you MUST have at least:

1. ✅ CORS enabled for `http://localhost:5173`
2. ✅ `/api/Auth/register` endpoint
3. ✅ `/api/Auth/login` endpoint
4. ✅ `/api/Auth/refresh` endpoint
5. ✅ JWT token generation with userId, email, firstName, lastName
6. ✅ `/Dashboard/Index` endpoint returning valid data structure

**Optional but Recommended:**
- All other endpoints (Trades, Strategies, Reports, etc.)
- File upload endpoints
- AI/ML integration
- Database with migrations applied

---

## 🎯 Next Steps

1. **Test each endpoint** using the curl commands above
2. **Check browser console** for CORS errors when using frontend
3. **Verify JWT tokens** are being generated and accepted
4. **Add sample data** to database for testing
5. **Monitor backend logs** for errors

---

**Document Version:** 1.0  
**Last Updated:** October 2025  
**Frontend Version:** 3.2  
**Tested with Backend:** TradeTracker API v1.0








