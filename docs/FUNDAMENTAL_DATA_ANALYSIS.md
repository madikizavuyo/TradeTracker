# Fundamental Data Analysis for TrailBlazer Scoring

## Executive Summary

Analysis of data sources for GDP, CPI, Unemployment, Interest Rate, and PMI across 11 currencies (USD, EUR, GBP, JPY, AUD, NZD, CAD, CHF, SEK, ZAR, CNY). Several critical gaps and one major bug were identified and fixed.

---

## Current Coverage

| Currency | GDP | CPI | Unemployment | Interest Rate | PMI |
|----------|-----|-----|---------------|---------------|-----|
| USD | ✓ GDPC1 | ✓ CPIAUCSL | ✓ UNRATE | ✓ FEDFUNDS | ✓ BSCICP03USM665S |
| EUR | ✓ EUNNGDP | ✓ CP0000EZ19M086NEST | ✓ LRHUTTTTEZM156S | ✓ ECBDFR | ✓ BSCICP03EZM665S |
| GBP | ✓ NAEXKP01GBA657S | ✓ GBRCPIALLMINMEI | ✓ LRHUTTTTGBM156S | ✓ BOERUKM | ✓ BSCICP03GBM665S |
| JPY | ✓ JPNRGDPEXP | ✓ JPNCPIALLMINMEI | ✓ LRHUTTTTJPM156S | ✓ IRSTCB01JPM156N | ✓ BSCICP03JPM665S |
| AUD | ✓ AUSGDPNQDSMEI | ✓ AUSCPIALLQINMEI | ✓ LRHUTTTTAUM156S | ✓ IRSTCI01AUM156N | ✓ BSCICP03AUM665S |
| NZD | ✓ NZLGDPNQDSMEI | ✓ NZLCPIALLQINMEI | ✓ LRHUTTTTNZA156N | ✓ IRSTCI01NZM156N | — |
| CAD | ✓ NAEXKP01CAQ189S | ✓ CANCPIALLMINMEI | ✓ LRHUTTTTCAM156S | ✓ IRSTCB01CAM156N | — |
| CHF | ✓ NAEXKP01CHQ189S | ✓ CHECPIALLMINMEI | ✓ LRHUTTTTCHM156S | ✓ IRSTCI01CHM156N | ✓ BSCICP03CHM665S |
| SEK | ✓ CLVMNACSCAB1GQSE | ✓ CP0000SEM086NEST | ✓ LRHUTTTTSEM156S | — | — |
| **ZAR** | ✓ ZAFGDPRQPSMEI | ✓ ZAFCPIALLMINMEI | ✓ LRUN64TTZAQ156S | ✓ IRSTCB01ZAM156N | ✓ BSCICP03ZAM665S |
| **CNY** | ✓ CHNGDPRAPSMEI | ✓ CHNCPIALLMINMEI | — | ✓ IRSTCB01CNM156N | ✓ BSCICP03CNM665S |

---

## Critical Issues

### 1. MANEMP is NOT PMI (Major Bug)

**Current:** `MANEMP` = All Employees, Manufacturing (thousands of persons)  
**Values:** ~12,000 (e.g. 12,692 in Dec 2025)  
**Scoring logic:** `pmi > 50` → always true → always "Positive"

**Fix:** Replace with OECD Business Confidence: `BSCICP03USM665S`  
- Normalized to 100 (>100 = expansion, <100 = contraction)  
- Comparable to PMI interpretation

### 2. Missing Interest Rates (6 currencies)

| Currency | FRED Series | Notes |
|----------|-------------|-------|
| GBP | BOERUKM | Bank of England Policy Rate |
| JPY | IRSTCB01JPM156N | BoJ policy rate (check if current) |
| AUD | IRSTCI01AUM156N | RBA interbank rate |
| NZD | IRSTCI01NZM156N | RBNZ interbank rate |
| CAD | IRSTCB01CAM156N | BoC rate (may end Dec 2023) |
| CHF | IRSTCI01CHM156N | SNB interbank rate |

### 3. Missing PMI / Business Confidence

FRED does not host ISM PMI. Use OECD Business Confidence (BSCICP03) as proxy:
- US: BSCICP03USM665S
- Euro: BSCICP03EZM665S
- UK: BSCICP03GBM665S
- Japan: BSCICP03JPM665S
- Australia: BSCICP03AUM665S
- Canada: (removed — series deprecated)
- Switzerland: (removed — series deprecated)

NZD, CAD, CHF, and Sweden may not have BSCICP03; omit if unavailable.

### 4. Sweden (SEK) – USDSEK pair

| Indicator | FRED Series |
|-----------|-------------|
| GDP | CLVMNACSCAB1GQSE (Real GDP, quarterly) |
| CPI | CP0000SEM086NEST (HICP index, monthly) |
| Unemployment | LRHUTTTTSEM156S |

### 5. South Africa (ZAR) – USDZAR pair

| Indicator | FRED Series | Notes |
|-----------|-------------|-------|
| GDP | ZAFGDPRQPSMEI | Already YoY growth rate (use raw fetch) |
| CPI | ZAFCPIALLMINMEI | Index, compute YoY |
| Unemployment | LRUN64TTZAQ156S | Quarterly, 15–64 age group |
| Interest Rate | IRSTCB01ZAM156N | SARB policy rate |
| PMI | BSCICP03ZAM665S | OECD Business Confidence |

### 6. China (CNY) – USDCNY pair

| Indicator | FRED Series | Notes |
|-----------|-------------|-------|
| GDP | CHNGDPRAPSMEI | Already YoY growth rate (use raw fetch) |
| CPI | CHNCPIALLMINMEI | Index, compute YoY |
| Unemployment | — | Limited FRED coverage |
| Interest Rate | IRSTCB01CNM156N | PBoC policy rate |
| PMI | BSCICP03CNM665S | OECD Business Confidence |

---

## Recommended FRED Series (Complete)

```
USD: GDP=GDPC1, CPI=CPIAUCSL, Unemployment=UNRATE, InterestRate=FEDFUNDS, PMI=BSCICP03USM665S
EUR: GDP=EUNNGDP, CPI=CP0000EZ19M086NEST, Unemployment=LRHUTTTTEZM156S, InterestRate=ECBDFR, PMI=BSCICP03EZM665S
GBP: GDP=NAEXKP01GBA657S, CPI=GBRCPIALLMINMEI, Unemployment=LRHUTTTTGBM156S, InterestRate=BOERUKM, PMI=BSCICP03GBM665S
JPY: GDP=JPNRGDPEXP, CPI=JPNCPIALLMINMEI, Unemployment=LRHUTTTTJPM156S, InterestRate=IRSTCB01JPM156N, PMI=BSCICP03JPM665S
AUD: GDP=AUSGDPNQDSMEI, CPI=AUSCPIALLQINMEI, Unemployment=LRHUTTTTAUM156S, InterestRate=IRSTCI01AUM156N, PMI=BSCICP03AUM665S
NZD: GDP=NZLGDPNQDSMEI, CPI=NZLCPIALLQINMEI, Unemployment=LRHUTTTTNZA156N, InterestRate=IRSTCI01NZM156N
CAD: GDP=NAEXKP01CAQ189S, CPI=CANCPIALLMINMEI, Unemployment=LRHUTTTTCAM156S, InterestRate=IRSTCB01CAM156N
CHF: GDP=NAEXKP01CHA657S, CPI=CHECPIALLMINMEI, Unemployment=LRHUTTTTCHQ156S, InterestRate=IRSTCI01CHM156N
SEK: GDP=CLVMNACSCAB1GQSE, CPI=CP0000SEM086NEST, Unemployment=LRHUTTTTSEM156S
ZAR: GDP=ZAFGDPRQPSMEI*, CPI=ZAFCPIALLMINMEI, Unemployment=LRUN64TTZAQ156S, InterestRate=IRSTCB01ZAM156N, PMI=BSCICP03ZAM665S
CNY: GDP=CHNGDPRAPSMEI*, CPI=CHNCPIALLMINMEI, InterestRate=IRSTCB01CNM156N, PMI=BSCICP03CNM665S
```

\* ZAFGDPRQPSMEI, CHNGDPRAPSMEI are already YoY growth rates; use raw fetch, not YoY calculation.

**Note:** OECD Business Confidence uses 100 as neutral (not 50 like PMI). Adjust scoring: >100 = expansion, <100 = contraction.

---

## Alternative Data Sources (if FRED gaps persist)

| Source | Coverage | API | Notes |
|--------|----------|-----|-------|
| **OECD.Stat** | All OECD countries | REST API | GDP, CPI, unemployment, PMI |
| **Trading Economics** | 196 countries | Paid API | Comprehensive, real-time |
| **Alpha Vantage** | Economic indicators | Free tier | Limited calls |
| **World Bank Data360** | 266 countries | Free API (no key) | GDP growth, inflation (annual) — **implemented** |

### World Bank Data360 (Implemented)

- **API:** https://data360api.worldbank.org | [Open API spec](https://raw.githubusercontent.com/worldbank/open-api-specs/refs/heads/main/Data360%20Open_API.json)
- **Endpoints:** `GET /api/WorldBank/currency/{currency}`, `GET /api/WorldBank/gdp/{iso3}`, `GET /api/WorldBank/inflation/{iso3}`, `GET /api/WorldBank/test`
- **Indicators:** `WB_WDI_NY_GDP_MKTP_KD_ZG` (GDP growth annual %), `WB_WDI_FP_CPI_TOTL_ZG` (inflation annual %)
- **Coverage:** 266 countries via ISO3 codes (USA, GBR, JPN, EMU for Euro area, etc.)
