# TradeTracker Backend API Documentation
## For React Frontend Development

This document provides comprehensive API endpoint documentation for the TradeTracker backend. Use this as a reference when building a separate React frontend application.

---

## Table of Contents
1. [Base Configuration](#base-configuration)
2. [Authentication & Authorization](#authentication--authorization)
3. [Account Endpoints](#account-endpoints)
4. [Dashboard Endpoints](#dashboard-endpoints)
5. [Trades Endpoints](#trades-endpoints)
6. [Strategies Endpoints](#strategies-endpoints)
7. [Import Endpoints](#import-endpoints)
8. [MetaTrader5 Endpoints](#metatrader5-endpoints)
9. [ML Trading Endpoints](#ml-trading-endpoints)
10. [Reports Endpoints](#reports-endpoints)
11. [Settings Endpoints](#settings-endpoints)
12. [AI Insights Endpoints](#ai-insights-endpoints)
13. [Data Models](#data-models)

---

## Base Configuration

### Backend URL
```
Base URL: https://your-backend-url.com
or
Development: https://localhost:7xxx (check launchSettings.json)
```

### Authentication
The application uses **ASP.NET Core Identity with Cookie Authentication**. You'll need to:
- Send credentials via POST to login endpoint
- Include cookies in subsequent requests
- Handle CSRF tokens with `[ValidateAntiForgeryToken]` protected endpoints

### Headers Required
```javascript
{
  'Content-Type': 'application/json',
  'X-Requested-With': 'XMLHttpRequest', // For AJAX detection
  'RequestVerificationToken': 'token-value' // For CSRF protection
}
```

---

## Authentication & Authorization

### 1. Register New User
**Endpoint:** `POST /Account/Register`  
**Auth Required:** No  
**Description:** Create a new user account

**Request Body:**
```json
{
  "email": "user@example.com",
  "password": "SecurePassword123!",
  "confirmPassword": "SecurePassword123!",
  "firstName": "John",
  "lastName": "Doe"
}
```

**Response:** 
- **Success:** Redirect to `/Dashboard/Index`
- **Failure:** 400 with validation errors

---

### 2. Login
**Endpoint:** `POST /Account/Login`  
**Auth Required:** No  
**Description:** Authenticate user and create session

**Request Body:**
```json
{
  "email": "user@example.com",
  "password": "SecurePassword123!",
  "rememberMe": true
}
```

**Response:**
- **Success:** 200 + Sets authentication cookie, redirects to dashboard or returnUrl
- **Failure:** 400 with error message

---

### 3. Logout
**Endpoint:** `POST /Account/Logout`  
**Auth Required:** Yes  
**Description:** Sign out user and clear session

**Response:**
- **Success:** Redirect to `/Home/Index`

---

### 4. External Login (Google OAuth)
**Endpoint:** `POST /Account/ExternalLogin`  
**Auth Required:** No  
**Description:** Initiate OAuth login with external provider

**Request Body:**
```json
{
  "provider": "Google",
  "returnUrl": "/Dashboard/Index"
}
```

**Response:** Challenge redirect to OAuth provider

---

### 5. External Login Callback
**Endpoint:** `GET /Account/ExternalLoginCallback`  
**Auth Required:** No  
**Description:** Handle OAuth callback

**Query Parameters:**
- `returnUrl` (optional)
- `remoteError` (optional)

---

### 6. External Login Confirmation
**Endpoint:** `POST /Account/ExternalLoginConfirmation`  
**Auth Required:** No  
**Description:** Complete external login account creation

**Request Body:**
```json
{
  "email": "user@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "provider": "Google"
}
```

---

## Dashboard Endpoints

### 1. Get Dashboard Data
**Endpoint:** `GET /Dashboard/Index`  
**Auth Required:** Yes  
**Description:** Get comprehensive dashboard overview

**Response:**
```json
{
  "totalTrades": 150,
  "openTrades": 5,
  "winningTrades": 90,
  "losingTrades": 55,
  "totalProfitLoss": 12500.50,
  "totalProfitLossDisplay": 231259.25,
  "winRate": 60.0,
  "averageWin": 200.00,
  "averageWinDisplay": 3700.00,
  "averageLoss": -150.00,
  "averageLossDisplay": -2775.00,
  "profitFactor": 1.33,
  "displayCurrency": "ZAR",
  "displayCurrencySymbol": "R",
  "strategies": [...],
  "recentTrades": [...],
  "monthlyPerformance": [...],
  "strategyPerformance": [...],
  "instrumentPerformance": [...]
}
```

---

## Trades Endpoints

### 1. Get All Trades (with Filtering)
**Endpoint:** `GET /Trades/Index`  
**Auth Required:** Yes  
**Description:** Get all user trades with optional filters

**Query Parameters:**
- `search` (string, optional): Search in instrument or notes
- `instrument` (string, optional): Filter by specific instrument
- `strategyId` (int, optional): Filter by strategy
- `status` (string, optional): "Open" | "Closed" | "Cancelled"
- `startDate` (DateTime, optional): Filter by date range start
- `endDate` (DateTime, optional): Filter by date range end
- `sortBy` (string, optional): "date" | "instrument" | "profit" | "entryprice" | "exitprice"
- `sortOrder` (string, optional): "asc" | "desc"

**Response:**
```json
{
  "trades": [
    {
      "id": 1,
      "instrument": "EURUSD",
      "entryPrice": 1.0850,
      "exitPrice": 1.0920,
      "stopLoss": 1.0800,
      "takeProfit": 1.0950,
      "profitLoss": 700.00,
      "profitLossDisplay": 12950.00,
      "riskReward": 1.4,
      "dateTime": "2024-01-15T10:30:00Z",
      "exitDateTime": "2024-01-15T15:45:00Z",
      "notes": "Strong uptrend",
      "status": "Closed",
      "type": "Long",
      "lotSize": 0.1,
      "broker": "XM",
      "currency": "USD",
      "displayCurrency": "ZAR",
      "displayCurrencySymbol": "R",
      "strategyName": "Trend Following"
    }
  ],
  "strategies": [...],
  "instruments": ["EURUSD", "GBPUSD", ...],
  "displayCurrency": "ZAR",
  "displayCurrencySymbol": "R"
}
```

---

### 2. Get Trade Details
**Endpoint:** `GET /Trades/Details/{id}`  
**Auth Required:** Yes  
**Description:** Get detailed information for a specific trade

**Response:** Single trade object (see structure above) with additional `tradeImages` array

---

### 3. Create New Trade
**Endpoint:** `POST /Trades/Create`  
**Auth Required:** Yes  
**Description:** Create a new trade entry

**Request Body (FormData):**
```json
{
  "strategyId": 1,
  "instrument": "EURUSD",
  "entryPrice": 1.0850,
  "exitPrice": 1.0920,
  "stopLoss": 1.0800,
  "takeProfit": 1.0950,
  "dateTime": "2024-01-15T10:30:00Z",
  "exitDateTime": "2024-01-15T15:45:00Z",
  "notes": "Strong uptrend",
  "status": "Closed",
  "type": "Long",
  "lotSize": 0.1,
  "broker": "XM",
  "entryImages": [File, File],
  "exitImages": [File]
}
```

**Response:**
- **Success:** Redirect to `/Trades/Index`
- **Failure:** 400 with validation errors

---

### 4. Update Trade
**Endpoint:** `POST /Trades/Edit/{id}`  
**Auth Required:** Yes  
**Description:** Update an existing trade

**Request Body:** Same as Create (with `id` included)

**Response:**
- **Success:** Redirect to `/Trades/Index`
- **Failure:** 400 with validation errors

---

### 5. Delete Trade
**Endpoint:** `POST /Trades/Delete/{id}`  
**Auth Required:** Yes  
**Description:** Delete a trade (includes associated images)

**Response:** Redirect to `/Trades/Index`

---

### 6. Get Trade Image
**Endpoint:** `GET /Trades/Image/{imageId}`  
**Auth Required:** Yes  
**Description:** Retrieve a trade image

**Response:** Image file (binary)

---

### 7. Clean Database
**Endpoint:** `POST /Trades/CleanDatabase`  
**Auth Required:** Yes  
**Description:** Delete all trades and import history for current user

**Response:**
```json
{
  "message": "Successfully deleted X trades and Y import history records."
}
```

---

## Strategies Endpoints

### 1. Get All Strategies
**Endpoint:** `GET /Strategies/Index`  
**Auth Required:** Yes  
**Description:** Get all trading strategies with performance metrics

**Response:**
```json
[
  {
    "id": 1,
    "name": "Trend Following",
    "description": "Follow major market trends",
    "isActive": true,
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-15T10:00:00Z",
    "totalTrades": 45,
    "winningTrades": 30,
    "losingTrades": 15,
    "totalProfitLoss": 5600.00,
    "winRate": 66.67,
    "averageWin": 250.00,
    "averageLoss": -120.00,
    "profitFactor": 2.08,
    "displayCurrency": "ZAR",
    "displayCurrencySymbol": "R"
  }
]
```

---

### 2. Get Strategy Details
**Endpoint:** `GET /Strategies/Details/{id}`  
**Auth Required:** Yes  
**Description:** Get detailed strategy information

**Response:**
```json
{
  "strategy": {
    "id": 1,
    "name": "Trend Following",
    "description": "Follow major market trends",
    "isActive": true,
    "trades": [...]
  },
  "totalTrades": 45,
  "winningTrades": 30,
  "winRate": 66.67,
  "totalProfitLoss": 5600.00,
  "averageRiskReward": 1.8
}
```

---

### 3. Create Strategy
**Endpoint:** `POST /Strategies/Create`  
**Auth Required:** Yes  
**Description:** Create a new trading strategy

**Request Body:**
```json
{
  "name": "Breakout Strategy",
  "description": "Trade breakouts from consolidation",
  "isActive": true
}
```

**Response:**
- **Success:** Redirect to `/Strategies/Index`
- **Failure:** 400 with validation errors

---

### 4. Update Strategy
**Endpoint:** `POST /Strategies/Edit/{id}`  
**Auth Required:** Yes  
**Description:** Update an existing strategy

**Request Body:**
```json
{
  "id": 1,
  "name": "Updated Strategy Name",
  "description": "Updated description",
  "isActive": false
}
```

**Response:**
- **Success:** Redirect to `/Strategies/Index`
- **Failure:** 400 with validation errors

---

### 5. Delete Strategy
**Endpoint:** `POST /Strategies/Delete/{id}`  
**Auth Required:** Yes  
**Description:** Delete a strategy

**Response:** Redirect to `/Strategies/Index`

---

## Import Endpoints

### 1. Get Import History
**Endpoint:** `GET /Import/Index`  
**Auth Required:** Yes  
**Description:** Get recent import history (last 10)

**Response:**
```json
[
  {
    "id": 1,
    "brokerName": "XM",
    "originalFileName": "trades_jan_2024.xlsx",
    "filePath": "uploads/imports/1_trades_jan_2024.xlsx",
    "tradesImported": 45,
    "tradesSkipped": 3,
    "tradesFailed": 1,
    "importNotes": "Import completed successfully",
    "status": "Completed",
    "importedAt": "2024-01-15T10:00:00Z",
    "completedAt": "2024-01-15T10:05:00Z"
  }
]
```

---

### 2. Upload Import File
**Endpoint:** `POST /Import/Upload`  
**Auth Required:** Yes  
**Description:** Import trades from CSV or Excel file

**Request Body (FormData):**
```json
{
  "brokerName": "XM",
  "file": File,
  "instrumentColumn": "Symbol",
  "entryPriceColumn": "OpenPrice",
  "exitPriceColumn": "ClosePrice",
  "stopLossColumn": "SL",
  "dateTimeColumn": "OpenTime",
  "exitDateTimeColumn": "CloseTime",
  "notesColumn": "Comment",
  "statusColumn": "Status",
  "typeColumn": "Type",
  "lotSizeColumn": "Volume"
}
```

**Response:**
```json
{
  "message": "Import completed! X trades imported, Y skipped, Z failed."
}
```

---

## MetaTrader5 Endpoints

### 1. Get MT5 Import History
**Endpoint:** `GET /MetaTrader5/Index`  
**Auth Required:** Yes  
**Description:** Get MT5 import history

**Response:** Array of import history objects

---

### 2. Test MT5 Connection
**Endpoint:** `POST /MetaTrader5/Connect`  
**Auth Required:** Yes  
**Description:** Test connection to MetaTrader 5

**Request Body:**
```json
{
  "accountNumber": "12345678",
  "password": "mt5password",
  "server": "XM-Global"
}
```

**Response:**
- **Success:** Connection successful message + account info
- **Failure:** Error message

---

### 3. Import Trades from MT5
**Endpoint:** `POST /MetaTrader5/ImportTrades`  
**Auth Required:** Yes  
**Description:** Import trades from MT5 account via API

**Request Body:**
```json
{
  "accountNumber": "12345678",
  "password": "mt5password",
  "server": "XM-Global",
  "fromDate": "2024-01-01T00:00:00Z",
  "toDate": "2024-01-31T23:59:59Z"
}
```

**Response:**
```json
{
  "message": "Import completed: X trades imported, Y skipped."
}
```

---

### 4. Upload MT5 File
**Endpoint:** `POST /MetaTrader5/UploadFile`  
**Auth Required:** Yes  
**Description:** Upload and process MT5 statement file (CSV, Excel, or PDF with AI)

**Request Body (FormData):**
```json
{
  "file": File,
  "currency": "USD",
  "strategyId": 1,
  "useAIProcessing": false
}
```

**Response (JSON for AJAX):**
```json
{
  "success": true,
  "message": "Import completed: X trades imported, Y skipped",
  "tradesImported": 45,
  "tradesSkipped": 3
}
```

---

### 5. Get Account Info
**Endpoint:** `GET /MetaTrader5/AccountInfo`  
**Auth Required:** Yes  
**Description:** Get MT5 account information

**Query Parameters:**
- `accountNumber` (string)
- `server` (string)

**Response:**
```json
{
  "accountNumber": "12345678",
  "server": "XM-Global",
  "balance": 10000.00,
  "equity": 10500.00,
  "margin": 2000.00,
  "freeMargin": 8500.00,
  "currency": "USD"
}
```

---

### 6. Clear Database
**Endpoint:** `POST /MetaTrader5/ClearDatabase`  
**Auth Required:** Yes  
**Description:** Clear all user data (trades, strategies, import history)

**Response:**
```json
{
  "message": "Database cleared successfully! Removed X trades, Y strategies, and Z import records."
}
```

---

## ML Trading Endpoints

### 1. Get ML Import History
**Endpoint:** `GET /MLTrading/Index`  
**Auth Required:** Yes  
**Description:** Get ML-based import history

**Response:** Array of import history objects

---

### 2. Upload File for ML Processing
**Endpoint:** `POST /MLTrading/UploadFile`  
**Auth Required:** Yes  
**Description:** Upload any file (PDF, image, etc.) for AI-based trade extraction

**Request Body (FormData):**
```json
{
  "file": File,
  "currency": "USD",
  "selectedStrategyId": 1
}
```

**Response:**
```json
{
  "message": "ML extraction completed: X trades extracted, Y duplicates skipped."
}
```

---

## Reports Endpoints

### 1. Get Reports Overview
**Endpoint:** `GET /Reports/Index`  
**Auth Required:** Yes  
**Description:** Get comprehensive trading reports

**Response:** Same structure as Dashboard data

---

### 2. Get Charts View
**Endpoint:** `GET /Reports/Charts`  
**Auth Required:** Yes  
**Description:** Get data for charts visualization

**Response:**
```json
{
  "totalTrades": 150,
  "winRate": 60.0,
  "totalProfitLoss": 12500.50,
  "strategies": [...],
  "monthlyPerformance": [...],
  "strategyPerformance": [...],
  "instrumentPerformance": [...]
}
```

---

### 3. Get Performance Report
**Endpoint:** `GET /Reports/Performance`  
**Auth Required:** Yes  
**Description:** Get detailed performance metrics

**Query Parameters:**
- `startDate` (DateTime, optional)
- `endDate` (DateTime, optional)
- `strategyId` (int, optional)

**Response:**
```json
{
  "trades": [...],
  "totalTrades": 45,
  "winningTrades": 30,
  "losingTrades": 15,
  "totalProfitLoss": 5600.00,
  "winRate": 66.67,
  "averageWin": 250.00,
  "averageLoss": -120.00,
  "profitFactor": 2.08,
  "maxDrawdown": 800.00,
  "sharpeRatio": 1.45,
  "averageRiskReward": 1.8
}
```

---

### 4. Get Performance Data (API)
**Endpoint:** `GET /Reports/GetPerformanceData`  
**Auth Required:** Yes  
**Description:** Get performance data in JSON format

**Query Parameters:** Same as Performance Report

**Response:** JSON performance data

---

### 5. Get Chart Data (API)
**Endpoint:** `GET /Reports/GetChartData`  
**Auth Required:** Yes  
**Description:** Get specific chart data

**Query Parameters:**
- `chartType` (string): "equity" | "monthly" | "strategy" | "instrument" | "winrate" | "profitloss"
- `startDate` (DateTime, optional)
- `endDate` (DateTime, optional)
- `strategyId` (int, optional)

**Response:**
```json
{
  "data": [
    {
      "x": "2024-01-15",
      "y": 5600.00
    }
  ]
}
```

---

### 6. Debug Endpoint
**Endpoint:** `GET /Reports/Debug`  
**Auth Required:** Yes  
**Description:** Get debug information about user's trades

**Response:**
```json
{
  "userId": "user-id-123",
  "totalTrades": 150,
  "closedTrades": 145,
  "openTrades": 5,
  "winningTrades": 90,
  "losingTrades": 55,
  "trades": [...]
}
```

---

## Settings Endpoints

### 1. Get Settings
**Endpoint:** `GET /Settings/Index`  
**Auth Required:** Yes  
**Description:** Get user settings

**Response:**
```json
{
  "displayCurrency": "ZAR",
  "availableCurrencies": [
    {
      "code": "USD",
      "name": "US Dollar",
      "symbol": "$"
    },
    {
      "code": "ZAR",
      "name": "South African Rand",
      "symbol": "R"
    }
  ]
}
```

---

### 2. Update Currency
**Endpoint:** `POST /Settings/UpdateCurrency`  
**Auth Required:** Yes  
**Description:** Update user's display currency

**Request Body:**
```json
{
  "displayCurrency": "USD"
}
```

**Response:**
```json
{
  "message": "Display currency updated to USD"
}
```

---

### 3. Test Currency Conversion
**Endpoint:** `GET /Settings/TestCurrency`  
**Auth Required:** Yes  
**Description:** Test currency conversion

**Query Parameters:**
- `fromCurrency` (string): Source currency code
- `toCurrency` (string): Target currency code
- `amount` (decimal): Amount to convert

**Response:**
```json
{
  "success": true,
  "originalAmount": "$100.00",
  "convertedAmount": "R1,850.00",
  "exchangeRate": 18.50
}
```

---

## AI Insights Endpoints

### 1. Get AI Insights
**Endpoint:** `POST /api/AI/insights`  
**Auth Required:** Yes (Recommended)  
**Description:** Get AI-generated trading insights

**Request Body:**
```json
{
  "accountInfo": {
    "accountBalance": 10000.00,
    "equity": 10500.00,
    "margin": 2000.00,
    "freeMargin": 8500.00,
    "marginLevel": 525.0,
    "currency": "USD",
    "leverage": 100,
    "totalTrades": 150,
    "totalProfit": 5600.00
  },
  "openPositions": [
    {
      "symbol": "EURUSD",
      "type": "BUY",
      "volume": 0.1,
      "openPrice": 1.0850,
      "currentPrice": 1.0920,
      "profit": 70.00,
      "openTime": "2024-01-15T10:00:00Z",
      "comment": "Trend trade"
    }
  ],
  "recentTrades": [
    {
      "symbol": "GBPUSD",
      "type": "SELL",
      "volume": 0.1,
      "openPrice": 1.2650,
      "closePrice": 1.2600,
      "profit": 50.00,
      "openTime": "2024-01-14T08:00:00Z",
      "closeTime": "2024-01-14T16:00:00Z",
      "durationHours": 8.0,
      "comment": "Range trade"
    }
  ],
  "marketData": {
    "currentPrices": {
      "EURUSD": 1.0920,
      "GBPUSD": 1.2600
    },
    "dailyHighs": {
      "EURUSD": 1.0950,
      "GBPUSD": 1.2680
    },
    "dailyLows": {
      "EURUSD": 1.0820,
      "GBPUSD": 1.2580
    },
    "volatilityIndicators": {
      "EURUSD": 0.85,
      "GBPUSD": 1.20
    },
    "marketSentiment": "Bullish",
    "lastUpdated": "2024-01-15T12:00:00Z"
  }
}
```

**Response:**
```json
{
  "success": true,
  "insights": "Based on your trading data...",
  "recommendations": [
    "Consider reducing position size",
    "Strong uptrend in EURUSD",
    "Monitor support at 1.0800"
  ],
  "riskAssessment": {
    "level": "Moderate",
    "suggestions": [...]
  },
  "performanceAnalysis": {
    "strengths": [...],
    "weaknesses": [...]
  },
  "isMockData": false,
  "timestamp": "2024-01-15T12:00:00Z"
}
```

---

### 2. Get Current Session Insights
**Endpoint:** `GET /api/AI/insights/current`  
**Auth Required:** Yes (Recommended)  
**Description:** Get AI insights for current trading session (uses sample data)

**Response:** Same as Get AI Insights

---

### 3. Check AI Service Status
**Endpoint:** `GET /api/AI/status`  
**Auth Required:** No  
**Description:** Check if AI service is available

**Response:**
```json
{
  "available": true,
  "timestamp": "2024-01-15T12:00:00Z",
  "service": "DeepSeek AI"
}
```

---

## Data Models

### Trade Model
```typescript
interface Trade {
  id: number;
  userId: string;
  strategyId?: number;
  instrument: string;
  entryPrice: number;
  exitPrice?: number;
  stopLoss?: number;
  takeProfit?: number;
  profitLoss?: number;
  profitLossDisplay?: number;
  riskReward?: number;
  dateTime: string; // ISO 8601
  exitDateTime?: string; // ISO 8601
  notes?: string;
  status: 'Open' | 'Closed' | 'Cancelled';
  type: 'Long' | 'Short';
  lotSize?: number;
  broker?: string;
  currency: string;
  displayCurrency?: string;
  displayCurrencySymbol?: string;
  createdAt: string; // ISO 8601
  updatedAt?: string; // ISO 8601
  strategyName?: string;
  tradeImages?: TradeImage[];
}
```

### Strategy Model
```typescript
interface Strategy {
  id: number;
  name: string;
  description?: string;
  userId: string;
  createdAt: string; // ISO 8601
  updatedAt?: string; // ISO 8601
  isActive: boolean;
  trades?: Trade[];
}
```

### Import History Model
```typescript
interface BrokerImportHistory {
  id: number;
  userId: string;
  brokerName: string;
  originalFileName: string;
  filePath: string;
  tradesImported: number;
  tradesSkipped: number;
  tradesFailed: number;
  importNotes?: string;
  status: 'Pending' | 'Processing' | 'InProgress' | 'PartiallyCompleted' | 'Completed' | 'Failed';
  importedAt: string; // ISO 8601
  completedAt?: string; // ISO 8601
}
```

### Trade Image Model
```typescript
interface TradeImage {
  id: number;
  tradeId: number;
  type: 'Entry' | 'Exit';
  originalFileName: string;
  imageData: Uint8Array; // Binary data
  fileSizeBytes: number;
  mimeType: string;
}
```

### User Model
```typescript
interface ApplicationUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  displayCurrency?: string;
  socialAuthProvider?: string;
  createdAt: string; // ISO 8601
  lastLoginAt?: string; // ISO 8601
}
```

---

## Error Handling

### Standard Error Response
```json
{
  "error": "Error message description",
  "statusCode": 400,
  "details": [
    "Validation error 1",
    "Validation error 2"
  ]
}
```

### HTTP Status Codes
- **200 OK**: Successful GET/POST
- **201 Created**: Resource created successfully
- **400 Bad Request**: Validation errors
- **401 Unauthorized**: Not authenticated
- **403 Forbidden**: Authenticated but not authorized
- **404 Not Found**: Resource not found
- **500 Internal Server Error**: Server error

---

## CSRF Protection

For POST, PUT, DELETE requests, include anti-forgery token:

```javascript
// Get token from meta tag or cookie
const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

// Include in headers
headers: {
  'RequestVerificationToken': token
}
```

---

## CORS Configuration

Ensure your React app's origin is added to backend CORS policy:

```csharp
// In Program.cs or Startup.cs
builder.Services.AddCors(options => {
    options.AddPolicy("ReactApp", policy => {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
```

---

## Rate Limiting & Best Practices

1. **Pagination**: Implement pagination for large datasets (trades, strategies)
2. **Caching**: Cache frequently accessed data (strategies, user settings)
3. **Debouncing**: Debounce search/filter inputs
4. **Error Boundaries**: Implement React error boundaries
5. **Loading States**: Show loading indicators for async operations
6. **Optimistic Updates**: Update UI optimistically, rollback on error
7. **File Uploads**: Use FormData, show upload progress
8. **WebSockets/SignalR**: Consider for real-time trade updates (UploadProgressHub available)

---

## WebSocket/SignalR Hub

### Upload Progress Hub
**Endpoint:** `/uploadProgressHub`  
**Description:** Real-time upload progress updates

**Client Methods:**
```typescript
// Connect to hub
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/uploadProgressHub")
  .build();

// Listen for progress updates
connection.on("ReceiveProgress", (progress) => {
  console.log(`Upload progress: ${progress}%`);
});

await connection.start();
```

---

## Configuration Values

### Supported Currencies
- USD (US Dollar) - $
- ZAR (South African Rand) - R
- EUR (Euro) - €
- GBP (British Pound Sterling) - £
- JPY (Japanese Yen) - ¥
- AUD (Australian Dollar) - A$
- CAD (Canadian Dollar) - C$
- CHF (Swiss Franc) - CHF

### Trade Statuses
- Open
- Closed
- Cancelled

### Trade Types
- Long
- Short

### Image Types
- Entry
- Exit

### Import Statuses
- Pending
- Processing
- InProgress
- PartiallyCompleted
- Completed
- Failed

---

## Example React Integration

### Authentication Service
```typescript
class AuthService {
  async login(email: string, password: string) {
    const response = await fetch('/Account/Login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, rememberMe: true }),
      credentials: 'include'
    });
    return response.ok;
  }

  async logout() {
    await fetch('/Account/Logout', {
      method: 'POST',
      credentials: 'include'
    });
  }
}
```

### Trades Service
```typescript
class TradesService {
  async getTrades(filters?: TradeFilters) {
    const params = new URLSearchParams(filters as any);
    const response = await fetch(`/Trades/Index?${params}`, {
      credentials: 'include'
    });
    return response.json();
  }

  async createTrade(trade: TradeCreateDto) {
    const formData = new FormData();
    Object.keys(trade).forEach(key => {
      formData.append(key, trade[key]);
    });

    const response = await fetch('/Trades/Create', {
      method: 'POST',
      body: formData,
      credentials: 'include'
    });
    return response.ok;
  }
}
```

---

## Additional Notes

1. **File Size Limits**: 
   - Single image: 5MB max
   - Total per trade: 50MB max

2. **Date Formats**: All dates are in ISO 8601 format (UTC)

3. **Currency Conversion**: Automatic conversion to user's display currency

4. **Duplicate Prevention**: System automatically detects and skips duplicate trades during import

5. **AI Processing**: PDF and image files can be processed using DeepSeek AI for automatic trade extraction

6. **Session Management**: Cookie-based authentication with configurable timeout

---

## Support & Questions

For additional information or clarification on any endpoint, refer to the controller source code:
- `Controllers/AccountController.cs`
- `Controllers/TradesController.cs`
- `Controllers/StrategiesController.cs`
- `Controllers/DashboardController.cs`
- `Controllers/ImportController.cs`
- `Controllers/MetaTrader5Controller.cs`
- `Controllers/MLTradingController.cs`
- `Controllers/ReportsController.cs`
- `Controllers/SettingsController.cs`
- `Controllers/AIController.cs`

---

**Last Updated:** October 2025  
**Backend Version:** ASP.NET Core 8.0  
**Database:** SQL Server with Entity Framework Core


