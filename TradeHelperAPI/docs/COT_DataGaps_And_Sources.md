# COT Data Gaps and Sources

## Overview

TrailBlazer uses CFTC Commitments of Traders (COT) data for institutional sentiment. COT is matched by **instrument name** (`Instruments.Name` must equal `COTReports.Symbol`).

## Instruments WITH CFTC COT Mapping (in code)

The following symbols are mapped in `TrailBlazerDataService.CftcToSymbol` and can receive COT from CFTC HTML pages:

| Symbol | Asset Class |
|--------|-------------|
| EURUSD, GBPUSD, USDJPY, USDCHF, USDCAD, AUDUSD, NZDUSD | Forex Major |
| USDMXN, USDBRL, USDZAR | Forex Minor |
| EURGBP, EURJPY | Forex Cross |
| EURZAR, GBPZAR, AUDZAR, NZDZAR, CADZAR, CHFZAR, JPYZAR | Forex ZAR |
| BTC, ETH | Crypto |
| XAUUSD, XAGUSD, XPTUSD, XPDUSD | Metals |
| USOIL | Commodity |
| US10Y, US5Y, US30Y | Bonds |
| US500, US100, JP225 | Index |

## Instruments LIKELY MISSING COT

Based on the CFTC mapping and typical instrument lists:

- **SOL** – No CFTC COT for Solana (CFTC only has BTC, ETH)
- **Forex crosses** – EURCHF, GBPJPY, AUDJPY, EURAUD, GBPAUD, etc. (CFTC reports only selected crosses)
- **Other indexes** – If you have DE40, UK100, etc., they may not be in CFTC LOF pages
- **Other commodities** – Natural gas, etc., may require different CFTC report URLs

## How to Find Gaps in Production

Run the SQL script:

```
TradeHelperAPI/Migrations/COT_DataGap_Analysis.sql
```

against your production database. It will list:
1. Instruments with no COT data
2. Instruments with COT (and latest report date)
3. Orphaned COT symbols
4. Summary counts

## Where to Get Additional COT Data

### 1. CFTC (current source)

- **financial_lof.htm** – Forex, some crosses
- **deacmelof.htm** – CME (crypto, indexes)
- **deanymelof.htm** – NYMEX (oil)
- **deacmxlof.htm** – COMEX (metals)
- **deafrexlof.htm** – ICE (some forex)

Add more URLs to `CftcCotUrls` in `TrailBlazerDataService.cs` if CFTC publishes additional reports.

### 2. CFTC JSON API (alternative)

CFTC provides machine-readable data:
- https://www.cftc.gov/MarketReports/CommitmentsofTraders/index.htm
- Look for "Historical Data" or "Data Download" links for CSV/JSON

### 3. Third-party APIs

- **Quandl/Nasdaq Data Link** – COT datasets (e.g. CFTC/xxx)
- **Alpha Vantage** – Some COT-related endpoints
- **TradingView** – COT indicators (not direct API)

### 4. Extending the Symbol Map

To add new instruments, update `CftcToSymbol` in `TrailBlazerDataService.cs` with the exact CFTC contract name as it appears on the HTML page. Use `TryResolveCftcSymbol` prefix/contains logic for variants (e.g. "GOLD 100 OZ" → XAUUSD).

## Debugging "no COT data" Logs

When you see `TrailBlazer: no COT data for {Instrument}` in logs:

1. Check if the instrument name matches a CFTC symbol (see table above).
2. Verify the CFTC URL returns data (fetch manually or check logs for fetch failures).
3. Run `COT_DataGap_Analysis.sql` to confirm the instrument has no row in `COTReports`.
