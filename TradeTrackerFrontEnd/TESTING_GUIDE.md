# TradeTracker Frontend Testing Guide

**Backend API:** `http://localhost:5014`  
**Frontend Dev Server:** `http://localhost:5173`  
**Status:** ✅ Ready for testing

---

## 🚀 Quick Start Testing

### 1. Verify Backend is Running

Open a new terminal and test:

```bash
# Test backend health
curl http://localhost:5014/api/AI/status
```

**Expected Response:**
```json
{
  "success": true,
  "data": {
    "available": true,
    "service": "DeepSeek AI",
    "timestamp": "2024-10-27T..."
  }
}
```

**If this fails:**
- Backend is not running on port 5014
- Check backend terminal for errors
- Verify backend configuration

---

### 2. Test CORS Configuration

Open browser console at `http://localhost:5173` and run:

```javascript
fetch('http://localhost:5014/api/AI/status')
  .then(r => r.json())
  .then(data => console.log('✅ CORS working!', data))
  .catch(err => console.error('❌ CORS error:', err));
```

**Expected:** `✅ CORS working!` with JSON data

**If CORS error:**
```
Access to fetch at 'http://localhost:5014/...' from origin 'http://localhost:5173' 
has been blocked by CORS policy
```

**Fix in Backend:** Add to CORS policy:
```csharp
policy.WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials();
```

---

## 📝 Complete Testing Workflow

### Step 1: Registration

1. Open `http://localhost:5173`
2. Click **"Get Started"** button
3. Fill in registration form:
   - **Email:** `test@example.com`
   - **Password:** `Test123` (must have: 6+ chars, uppercase, lowercase, digit)
   - **Confirm Password:** `Test123`
   - **First Name:** `Test`
   - **Last Name:** `User`
4. Click **"Create Account"**

**Expected Behavior:**
- ✅ Form submits to backend
- ✅ Receives JWT token
- ✅ Stores token in localStorage
- ✅ **Automatically redirects to /dashboard**
- ✅ Shows dashboard with user's name in sidebar

**If Error:**
- Check browser console for error details
- Check Network tab → `/api/Auth/register` request/response
- Verify backend is returning JWT token in response

---

### Step 2: Dashboard (Real Data Test)

**On dashboard load, verify:**

1. **Statistics Cards Display Real Numbers:**
   - Total Trades count
   - Win Rate percentage
   - Total P&L with currency symbol
   - Profit Factor

2. **No Mock Data Indicators:**
   - ❌ Should NOT see "42 trades" (that was mock data)
   - ❌ Should NOT see "R100,284.00" (that was mock data)
   - ✅ Should see actual data from YOUR backend

3. **Charts Display:**
   - Equity curve (if you have trades in backend)
   - Monthly performance bars
   - Or empty states if no trades yet

4. **Recent Trades Table:**
   - Shows last 10 trades from backend
   - Or "No recent trades" if database is empty

**Test Error Handling:**
1. Stop backend server
2. Refresh dashboard page
3. **Expected:** Error message appears: "Failed to load dashboard data. Please try again."
4. **Expected:** Retry button is shown
5. Restart backend
6. Click "Retry" button
7. **Expected:** Dashboard loads successfully with data

---

### Step 3: Trades Page

1. Click **"Trades"** in sidebar
2. Navigate to `/trades`

**Verify:**
- ✅ List of trades from backend
- ✅ Search box works (filters by instrument)
- ✅ Status filter works (Open/Closed/Cancelled)
- ✅ Trade cards show:
  - Instrument name
  - Entry/Exit prices
  - P&L with color coding (green = profit, red = loss)
  - Status badge
  - Type badge (Long/Short)
  - Date

**Test with Empty Database:**
- If no trades in backend: Shows "No trades found"
- Click "Add Trade" button → Goes to `/trades/new`

**Test Error Handling:**
- Stop backend → Should show error message with Retry button
- No crashes, no mock data

---

### Step 4: Strategies Page

1. Click **"Strategies"** in sidebar
2. Navigate to `/strategies`

**Verify:**
- ✅ List of strategies from backend
- ✅ Each strategy shows:
  - Name, Description
  - Active/Inactive badge
  - Total trades count
  - Win rate
  - Total P&L
  - Average Win/Loss
  - Profit Factor
- ✅ "View Details" and "Edit" buttons

**Test with Empty Database:**
- If no strategies: Shows target icon with "No strategies yet"
- Shows "Add Strategy" button

---

### Step 5: Settings Page

1. Click **"Settings"** in sidebar
2. Navigate to `/settings`

**Verify:**
- ✅ User information displays:
  - Email
  - First Name
  - Last Name
  - Account creation date
- ✅ Display Currency selector shows current currency
- ✅ Available currencies list (8 currencies):
  - USD ($)
  - ZAR (R)
  - EUR (€)
  - GBP (£)
  - JPY (¥)
  - AUD (A$)
  - CAD (C$)
  - CHF (CHF)

**Test Currency Change:**
1. Select different currency (e.g., EUR)
2. Click "Update Currency"
3. **Expected:** Success message
4. Go back to Dashboard
5. **Expected:** All amounts now show in EUR (€)

**Test Currency Conversion:**
1. Enter amounts in conversion tester
2. From: USD, To: ZAR, Amount: 100
3. Click "Test Conversion"
4. **Expected:** Shows converted amount (e.g., R1,850.00)

---

### Step 6: Import Page

1. Click **"Import"** in sidebar
2. Navigate to `/import`

**Verify:**
- ✅ Import history table (if any previous imports)
- ✅ File upload dropzone
- ✅ "Drop CSV or Excel file here" message

**Test File Upload:**
1. Create a test CSV file:
   ```csv
   Instrument,EntryPrice,ExitPrice,DateTime,Type,ProfitLoss
   EURUSD,1.0850,1.0920,2024-10-27 10:00:00,Long,70.00
   GBPUSD,1.2650,1.2600,2024-10-27 11:00:00,Short,-50.00
   ```
2. Drag file to dropzone
3. Fill broker name, currency
4. Click "Import Trades"
5. **Expected:** Upload progress
6. **Expected:** Success message with count
7. Check Trades page → Should see imported trades

---

### Step 7: MT5 Integration Page

1. Click **"MT5 Integration"** in sidebar
2. Navigate to `/mt5`

**Verify Tabbed Interface:**

**Tab 1: Direct Connection**
- ✅ Account Number input
- ✅ Password input (type=password)
- ✅ Server input
- ✅ "Test Connection" button
- ✅ "Import Trades" button

**Tab 2: File Upload**
- ✅ File upload dropzone
- ✅ Currency selector
- ✅ Strategy selector
- ✅ "Use AI Processing" toggle
- ✅ "Upload" button

**Test with File:**
1. Create MT5 statement (CSV/PDF)
2. Upload with AI processing enabled
3. **Expected:** Processing message
4. **Expected:** Trades extracted count

---

### Step 8: AI Extraction (ML Trading) Page

1. Click **"AI Extraction"** in sidebar
2. Navigate to `/ml-trading`

**Verify:**
- ✅ Import history table
- ✅ File upload dropzone
- ✅ "Drop any file here for AI extraction"

**Test AI Extraction:**
1. Create a trading statement (PDF, image, CSV, etc.)
2. Drag to dropzone
3. Select currency
4. Click "Extract Trades with AI"
5. **Expected:** AI processing message
6. **Expected:** Success with trades count

---

### Step 9: Reports Page

1. Click **"Reports"** in sidebar
2. Navigate to `/reports`

**Verify:**
- ✅ Performance statistics
- ✅ Equity curve chart (if trades exist)
- ✅ Strategy performance chart
- ✅ Date range filters
- ✅ Export buttons (CSV, Excel, PDF)

---

### Step 10: Logout & Login

**Test Logout:**
1. Click user's name in sidebar
2. Click "Sign Out" button
3. **Expected:** Redirects to `/login`
4. **Expected:** localStorage cleared
5. **Expected:** Cannot access `/dashboard` (redirects to `/login`)

**Test Login:**
1. Enter credentials
2. Click "Sign In"
3. **Expected:** Redirects to `/dashboard`
4. **Expected:** All data loads correctly

---

## 🧪 Edge Case Testing

### Test 1: Backend Down (Critical)

1. **Stop backend server**
2. Open `http://localhost:5173` in browser
3. Try to register/login
4. **Expected:** Error message (not mock data)

5. Navigate to Dashboard (if already logged in)
6. **Expected:** Error: "Failed to load dashboard data. Please try again."
7. **Expected:** Retry button shown
8. **NOT Expected:** ❌ No fake data (42 trades, R100,284.00, etc.)

9. **Start backend server**
10. Click "Retry" button
11. **Expected:** Data loads successfully

**This is the MOST IMPORTANT test to verify no mock data is used!**

---

### Test 2: Empty Database

1. Clear all trades from backend database
2. Load Dashboard
3. **Expected:** Shows zeros (0 trades, 0.0% win rate, $0.00 P&L)
4. Load Trades page
5. **Expected:** "No trades found"
6. Load Strategies page
7. **Expected:** "No strategies yet"

**NOT Expected:** ❌ Mock data with 42 trades, etc.

---

### Test 3: Network Errors

1. Disconnect from internet
2. Try to load pages
3. **Expected:** Error messages (not crashes)
4. Reconnect
5. Click Retry buttons
6. **Expected:** Data loads

---

### Test 4: Invalid Token

1. Open browser DevTools → Application → Local Storage
2. Find `token` key
3. Change value to invalid string
4. Refresh page
5. **Expected:** 401 error → Token refresh attempt → Fails → Redirects to login

---

### Test 5: Token Expiry

**Proactive Refresh Test:**
1. Login
2. Wait 5 minutes
3. **Expected:** Token gets refreshed automatically in background
4. Check Network tab → Should see `/api/Auth/refresh` call
5. **Expected:** New token stored
6. **Expected:** No logout, no interruption

---

## ✅ Success Criteria

### Core Functionality (MUST PASS)

- ✅ Registration works → Redirects to dashboard
- ✅ Login works → Redirects to dashboard
- ✅ Dashboard shows REAL data from backend
- ✅ Trades page shows REAL trades from backend
- ✅ Strategies page shows REAL strategies from backend
- ✅ Settings page loads user info from backend
- ✅ Currency change updates display currency
- ✅ File uploads work (Import, MT5, ML)
- ✅ Logout clears token → Redirects to login
- ✅ Protected routes block unauthenticated users

### Error Handling (MUST PASS)

- ✅ Backend down → Shows error message (NOT mock data)
- ✅ Retry button works
- ✅ Network errors handled gracefully
- ✅ Invalid credentials → Shows error
- ✅ 401 errors → Token refresh → Or logout
- ✅ No crashes on missing data

### Data Integrity (CRITICAL)

- ❌ **ZERO mock data** shown anywhere
- ❌ **NO fallback** to mock data on errors
- ✅ All data comes from backend API
- ✅ Empty states shown when no data
- ✅ Error states shown on API failures

---

## 🚨 Common Issues & Solutions

### Issue 1: "Cannot connect to backend"

**Symptoms:**
- Registration fails
- Login fails
- Dashboard shows error

**Check:**
1. Is backend running? → `curl http://localhost:5014/api/AI/status`
2. Is backend on correct port? → Check backend logs
3. Is CORS configured? → Check backend CORS settings

**Fix:**
- Start backend server
- Configure CORS for `http://localhost:5173`

---

### Issue 2: "Dashboard shows zeros"

**This is CORRECT behavior if:**
- Backend database is empty
- No trades have been created yet

**To verify it's working:**
1. Add a trade via Import or TradeForm
2. Refresh Dashboard
3. Should see updated statistics

**This is WRONG if:**
- Backend has trades but Dashboard shows zeros
- Check Network tab → `/Dashboard/Index` response
- Verify response format matches expected structure

---

### Issue 3: "Still seeing mock data (42 trades, R100,284)"

**This should NOT happen!** If you see this:
1. Check which page shows mock data
2. Hard refresh browser (Ctrl+Shift+R)
3. Clear browser cache
4. Check the file was actually updated

**Verification:**
```bash
# From project root
grep -n "getMockDashboardData\|getMockTrades\|getMockStrategies" src/pages/*.tsx
```

**Expected output:** Nothing found

---

### Issue 4: "Login successful but stays on login page"

**Cause:** Token not being stored or isAuthenticated not updating

**Debug:**
1. Open DevTools → Network tab
2. Submit login form
3. Check `/api/Auth/login` response
4. Verify response includes `token` field
5. Check Application → Local Storage
6. Verify `token` is stored

**If token is stored but still on login page:**
- Check AuthContext `isAuthenticated` logic
- Verify useEffect in Login.tsx is triggering navigation

---

### Issue 5: "Charts not showing"

**If you see empty chart areas:**
1. Check if backend has data
2. Check browser console for errors
3. Verify recharts is installed: `npm list recharts`

**Expected:** Charts show when data exists, empty state when no data

---

## 📋 Testing Checklist

Print this and check off as you test:

### Authentication
- [ ] Registration works
- [ ] Login works  
- [ ] Logout works
- [ ] Token refresh works
- [ ] Protected routes work

### Pages Load Real Data
- [ ] Dashboard shows backend data
- [ ] Trades shows backend data
- [ ] Strategies shows backend data
- [ ] Reports shows backend data
- [ ] Settings shows backend data

### Error Handling
- [ ] Backend down → Shows error (not mock data)
- [ ] Retry button works
- [ ] No crashes on errors
- [ ] User-friendly error messages

### Features Work
- [ ] Search trades
- [ ] Filter by status
- [ ] Currency change
- [ ] File uploads (Import)
- [ ] MT5 integration
- [ ] AI extraction
- [ ] Export trades

### Data Integrity
- [ ] ZERO mock data anywhere
- [ ] All data from backend
- [ ] Empty states correct
- [ ] Error states correct

---

## 🎯 Final Verification Command

Run this to absolutely confirm no mock data exists:

```bash
# Search entire src directory
grep -r "mock\|getMock\|MOCK" src/
```

**Expected output:** 
```
(no matches or only in comments)
```

**If you find mock data:** Report the file/line immediately!

---

## ✅ Test Complete!

**If all tests pass:**
- ✅ Frontend is production-ready
- ✅ No mock data exists
- ✅ Error handling is robust
- ✅ Integration with backend is complete

**Report any failures with:**
- Page/component name
- Steps to reproduce
- Expected vs actual behavior
- Browser console errors
- Network tab screenshots

---

**Testing Guide Version:** 1.0  
**Last Updated:** October 27, 2025  
**Status:** Ready for testing








