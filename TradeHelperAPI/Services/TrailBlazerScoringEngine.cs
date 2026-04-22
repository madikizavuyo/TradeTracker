using TradeHelper.Models;

namespace TradeHelper.Services
{
    /// <summary>Context for fundamental scoring: forex uses relative strength (base vs quote); commodities use USD fundamentals (e.g. weak USD = bullish gold).</summary>
    /// <param name="BaseCurrency">Base currency code (e.g. EUR for EURUSD).</param>
    /// <param name="QuoteCurrency">Quote currency code (e.g. USD for EURUSD).</param>
    /// <param name="CurrencyOverrides">Optional manual adjustments per currency (e.g. CAD +0.5 for oil/geopolitical strength). Positive = stronger currency.</param>
    public record FundamentalContext(
        Dictionary<string, double> BaseData,
        Dictionary<string, double> QuoteData,
        bool IsForexPair,
        bool IsUsdDenominatedCommodity,
        string BaseCurrency = "",
        string QuoteCurrency = "",
        IReadOnlyDictionary<string, double>? CurrencyOverrides = null);

    /// <summary>Context for per-instrument weight selection: asset class and data availability.</summary>
    public record ScoringWeightContext(
        string? AssetClass,
        bool HasCOT,
        bool HasRetail,
        bool HasFundamental,
        bool HasTechnical,
        bool HasNewsSentiment);

    public class TrailBlazerScoringEngine
    {
        public TrailBlazerScore CalculateScore(
            int instrumentId,
            FundamentalContext fundamentalContext,
            COTReport? cotReport,
            COTReport? previousCotReport,
            (double longPct, double shortPct) retailSentiment,
            double newsSentimentScore,
            Dictionary<string, double> technicals,
            List<string> dataSources,
            ScoringWeightContext weightContext,
            double? currencyStrengthScore = null,
            double? syntheticCOTScore = null)
        {
            var fundamentalScore = CalculateFundamentalScore(fundamentalContext);
            var institutionalScore = syntheticCOTScore ?? CalculateInstitutionalScore(cotReport, previousCotReport);
            var retailScore = CalculateRetailSentimentScore(retailSentiment);
            var technicalScore = ComputeTechnicalScore(technicals);
            var hasCS = currencyStrengthScore.HasValue;
            var csScore = currencyStrengthScore ?? 0; // Only used when wCS > 0 (hasCS=true)

            var (wF, wCOT, wR, wNews, wT, wCS) = GetWeights(weightContext, hasCS);

            var overall = (fundamentalScore * wF)
                        + (institutionalScore * wCOT)
                        + (retailScore * wR)
                        + (newsSentimentScore * wNews)
                        + (technicalScore * wT)
                        + (hasCS ? (csScore * wCS) : 0);

            overall = Math.Clamp(overall, 1.0, 10.0);

            var bias = overall switch
            {
                <= 3.5 => "Bearish",
                >= 6.5 => "Bullish",
                _ => "Neutral"
            };

            return new TrailBlazerScore
            {
                InstrumentId = instrumentId,
                OverallScore = Math.Round(overall, 2),
                Bias = bias,
                FundamentalScore = Math.Round(fundamentalScore, 2),
                SentimentScore = Math.Round((institutionalScore + retailScore) / 2, 2),
                TechnicalScore = Math.Round(technicalScore, 2),
                COTScore = Math.Round(institutionalScore, 2),
                RetailSentimentScore = Math.Round(retailScore, 2),
                NewsSentimentScore = Math.Round(newsSentimentScore, 2),
                RetailLongPct = Math.Round(retailSentiment.longPct, 2),
                RetailShortPct = Math.Round(retailSentiment.shortPct, 2),
                EconomicScore = Math.Round(fundamentalScore, 2),
                CurrencyStrengthScore = hasCS ? Math.Round(csScore, 2) : null,
                DataSources = System.Text.Json.JsonSerializer.Serialize(dataSources),
                DateComputed = DateTime.UtcNow
            };
        }

        /// <summary>Returns (wF, wCOT, wR, wNews, wT, wCS) weights. wCS = currency strength (blend of macro + news per pair).</summary>
        private static (double wF, double wCOT, double wR, double wNews, double wT, double wCS) GetWeights(ScoringWeightContext ctx, bool hasCurrencyStrength)
        {
            var ac = ctx.AssetClass ?? "";
            var isForex = ac.StartsWith("Forex", StringComparison.OrdinalIgnoreCase);
            const double wCurrencyStrength = 0.15; // 15% when available (forex or USD commodities)

            double wF, wCOT, wR, wNews, wT;

            const double wFundamental = 0.10;

            // Pre-normalization targets: news 26% and CS 15% when all pillars exist; technical/COT trimmed vs prior 18%/10%.
            if (isForex)
            {
                if (ctx.HasCOT && ctx.HasRetail)
                    (wF, wCOT, wR, wNews, wT) = (wFundamental, 0.23, 0.14, 0.26, 0.12);
                else if (!ctx.HasCOT && ctx.HasRetail)
                    (wF, wCOT, wR, wNews, wT) = (wFundamental, 0, 0.14, 0.26, 0.35);
                else if (ctx.HasCOT && !ctx.HasRetail)
                    (wF, wCOT, wR, wNews, wT) = (wFundamental, 0.23, 0, 0.26, 0.26);
                else
                    (wF, wCOT, wR, wNews, wT) = (wFundamental, 0, 0, 0.26, 0.49);
            }
            else
            {
                (wF, wCOT, wR, wNews, wT) = ac.ToUpperInvariant() switch
                {
                    "METAL" => (wFundamental, 0, 0, 0.26, 0.49),
                    "INDEX" => (wFundamental, 0, 0, 0.26, 0.49),
                    "COMMODITY" => (wFundamental, 0, 0, 0.26, 0.49),
                    "BOND" => (wFundamental, 0, 0, 0.26, 0.49),
                    "CRYPTO" => (wFundamental, 0.22, 0, 0.26, 0.27),
                    _ => (wFundamental, 0, 0, 0.26, 0.49)
                };
            }

            var f = ctx.HasFundamental ? wF : 0.0;
            var cot = ctx.HasCOT ? wCOT : 0.0;
            var r = ctx.HasRetail ? wR : 0.0;
            var news = ctx.HasNewsSentiment ? wNews : 0.0;
            var t = ctx.HasTechnical ? wT : 0.0;
            var cs = (hasCurrencyStrength && (isForex || ac.Equals("Metal", StringComparison.OrdinalIgnoreCase) || ac.Equals("Commodity", StringComparison.OrdinalIgnoreCase))) ? wCurrencyStrength : 0.0;

            var total = f + cot + r + news + t + cs;
            if (total <= 0)
                return (0.16, 0.16, 0.16, 0.20, 0.16, 0.16);

            return (f / total, cot / total, r / total, news / total, t / total, cs / total);
        }

        private static double CalculateFundamentalScore(FundamentalContext ctx)
        {
            const double AvgGdpGrowth = 2.0;
            const double GdpBand = 0.5;
            const double FedInflationTarget = 2.0;
            const double CpiBand = 0.5;

            double score = 5.0;
            int factorsWithData = 0;

            if (ctx.IsForexPair)
            {
                // Forex: relative strength (base vs quote). Higher = bullish for base = buy pair.
                // GDP: base stronger = bullish
                if (ctx.BaseData.TryGetValue("GDP", out var bGdp) && ctx.QuoteData.TryGetValue("GDP", out var qGdp) && (bGdp != 0 || qGdp != 0))
                {
                    var diff = bGdp - qGdp;
                    score += diff > 0.5 ? 1.5 : diff < -0.5 ? -1.5 : 0;
                    factorsWithData++;
                }
                // CPI: lower inflation in base = stronger currency
                if (ctx.BaseData.TryGetValue("CPI", out var bCpi) && ctx.QuoteData.TryGetValue("CPI", out var qCpi) && (bCpi != 0 || qCpi != 0))
                {
                    var diff = qCpi - bCpi; // higher quote inflation = base stronger
                    score += diff > 0.5 ? 1.0 : diff < -0.5 ? -1.0 : 0;
                    factorsWithData++;
                }
                // Unemployment: lower in base = stronger
                if (ctx.BaseData.TryGetValue("Unemployment", out var bUnemp) && ctx.QuoteData.TryGetValue("Unemployment", out var qUnemp) && (bUnemp != 0 || qUnemp != 0))
                {
                    var diff = qUnemp - bUnemp;
                    score += diff > 1 ? 1.5 : diff < -1 ? -1.5 : 0;
                    factorsWithData++;
                }
                // Interest rate: higher in base = stronger (carry)
                if (ctx.BaseData.TryGetValue("InterestRate", out var bRate) && ctx.QuoteData.TryGetValue("InterestRate", out var qRate) && (bRate != 0 || qRate != 0))
                {
                    var diff = bRate - qRate;
                    score += diff > 0.5 ? 1.0 : diff < -0.5 ? -1.0 : 0;
                    factorsWithData++;
                }
                // PMI: higher in base = stronger
                if (ctx.BaseData.TryGetValue("PMI", out var bPmi) && ctx.QuoteData.TryGetValue("PMI", out var qPmi) && (bPmi != 0 || qPmi != 0))
                {
                    var diff = bPmi - qPmi;
                    score += diff > 2 ? 1.5 : diff < -2 ? -1.5 : 0;
                    factorsWithData++;
                }
            }
            else if (ctx.IsUsdDenominatedCommodity)
            {
                // Commodity (e.g. XAUUSD): weak USD = bullish for gold. Use quote (USD) data, invert.
                var usd = ctx.QuoteData;
                if (usd.TryGetValue("GDP", out var gdp) && gdp != 0)
                {
                    score += gdp < AvgGdpGrowth - GdpBand ? 1.5 : gdp > AvgGdpGrowth + GdpBand ? -1.5 : 0;
                    factorsWithData++;
                }
                if (usd.TryGetValue("CPI", out var cpi) && cpi != 0)
                {
                    score += cpi > FedInflationTarget + CpiBand ? 1.5 : cpi < FedInflationTarget - CpiBand ? -0.5 : 0;
                    factorsWithData++;
                }
                if (usd.TryGetValue("Unemployment", out var unemp) && unemp != 0)
                {
                    score += unemp > 6 ? 1.0 : unemp < 4 ? -0.5 : 0;
                    factorsWithData++;
                }
                if (usd.TryGetValue("InterestRate", out var rate) && rate != 0)
                {
                    score += rate < 1 ? 1.0 : rate > 4 ? -1.0 : 0;
                    factorsWithData++;
                }
                if (usd.TryGetValue("PMI", out var pmi) && pmi != 0)
                {
                    // OECD Business Confidence: 100=neutral. Weak US (pmi<100) = bullish gold
                    score += pmi < 98 ? 0.5 : pmi > 102 ? -0.5 : 0;
                    factorsWithData++;
                }
            }
            else
            {
                // Fallback (e.g. indices, bonds): use average of base/quote if available. High 10Y yield = bearish for stocks.
                var merged = MergeData(ctx.BaseData, ctx.QuoteData);
                if (merged.TryGetValue("GDP", out var gdp) && gdp != 0) { score += gdp > AvgGdpGrowth + GdpBand ? 1.5 : gdp < AvgGdpGrowth - GdpBand ? -1.5 : 0; factorsWithData++; }
                if (merged.TryGetValue("CPI", out var cpi) && cpi != 0) { score += cpi > FedInflationTarget + CpiBand ? -1.0 : cpi < FedInflationTarget - CpiBand ? 0.5 : 0; factorsWithData++; }
                if (merged.TryGetValue("Unemployment", out var unemp) && unemp != 0) { score += unemp < 4 ? 1.5 : unemp < 6 ? 0.5 : -1.5; factorsWithData++; }
                if (merged.TryGetValue("InterestRate", out var rate) && rate != 0) { score += rate > 3 ? 1.0 : rate > 1 ? 0.5 : -0.5; factorsWithData++; }
                // PMI from heatmap is OECD business confidence (100=neutral), same as metals — not ISM 0–50 scale
                if (merged.TryGetValue("PMI", out var pmi) && pmi != 0)
                {
                    score += pmi > 102 ? 1.5 : pmi > 100 ? 0.5 : pmi < 98 ? -1.5 : pmi < 100 ? -0.5 : 0;
                    factorsWithData++;
                }
                if (merged.TryGetValue("Treasury10Y", out var treasury10Y) && treasury10Y != 0) { score += treasury10Y > 4.5 ? -1.0 : treasury10Y < 3 ? 0.5 : 0; factorsWithData++; }
                if (merged.TryGetValue("PCE", out var pce) && pce != 0) { score += pce > FedInflationTarget + CpiBand ? -0.5 : pce < FedInflationTarget - CpiBand ? 0.5 : 0; factorsWithData++; }
                // JTSJOL on FRED is thousands of openings (~6900 = 6.9M); compare in millions
                if (merged.TryGetValue("JOLTs", out var jolts) && jolts != 0)
                {
                    var joltsMillions = jolts > 200 ? jolts / 1000.0 : jolts;
                    score += joltsMillions > 9 ? 0.5 : joltsMillions < 7 ? -0.5 : 0;
                    factorsWithData++;
                }
                // ICSA is weekly claims level (~219000); thresholds are in same scale as thousands (250k, 350k)
                if (merged.TryGetValue("JoblessClaims", out var claims) && claims != 0)
                {
                    var claimsK = claims > 1000 ? claims / 1000.0 : claims;
                    score += claimsK < 250 ? 0.5 : claimsK > 350 ? -0.5 : 0;
                    factorsWithData++;
                }
            }

            // Apply geopolitical/narrative overrides (e.g. CAD +0.5 for oil exports, war beneficiary)
            if (ctx.CurrencyOverrides != null && ctx.CurrencyOverrides.Count > 0)
            {
                var adj = 0.0;
                if (ctx.IsForexPair)
                {
                    if (ctx.CurrencyOverrides.TryGetValue(ctx.BaseCurrency, out var baseOv)) adj += baseOv;  // Stronger base = bullish
                    if (ctx.CurrencyOverrides.TryGetValue(ctx.QuoteCurrency, out var quoteOv)) adj -= quoteOv; // Stronger quote = bearish
                }
                else if (ctx.IsUsdDenominatedCommodity && ctx.CurrencyOverrides.TryGetValue("USD", out var usdOv))
                    adj -= usdOv; // Stronger USD = bearish for gold/oil (priced in USD)
                score += adj;
            }
            return factorsWithData == 0 ? 5.0 : Math.Clamp(score, 1.0, 10.0);
        }

        private static Dictionary<string, double> MergeData(Dictionary<string, double> a, Dictionary<string, double> b)
        {
            var r = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in a.Keys.Union(b.Keys))
            {
                var va = a.TryGetValue(k, out var v1) ? v1 : 0;
                var vb = b.TryGetValue(k, out var v2) ? v2 : 0;
                if (va != 0 || vb != 0)
                    r[k] = (va != 0 && vb != 0) ? (va + vb) / 2 : (va != 0 ? va : vb);
            }
            return r;
        }

        private static double CalculateInstitutionalScore(COTReport? current, COTReport? previous)
        {
            if (current == null) return 5.0;

            var netNonCommercial = current.NonCommercialLong - current.NonCommercialShort;
            var totalNonCommercial = current.NonCommercialLong + current.NonCommercialShort;

            if (totalNonCommercial == 0) return 5.0;

            // Ratio from -1 to 1, then scale to 1-10
            var ratio = (double)netNonCommercial / totalNonCommercial;
            var score = 5.0 + (ratio * 4.0);

            // Week-over-week momentum bonus
            if (previous != null)
            {
                var prevNet = previous.NonCommercialLong - previous.NonCommercialShort;
                var momentum = netNonCommercial - prevNet;
                if (momentum > 0) score += 0.5;
                else if (momentum < 0) score -= 0.5;
            }

            return Math.Clamp(score, 1.0, 10.0);
        }

        private static double CalculateRetailSentimentScore((double longPct, double shortPct) sentiment)
        {
            // Contrarian: high retail long = bearish signal
            var longPct = sentiment.longPct;

            if (longPct >= 70) return 2.5;   // Very bearish (retail is heavily long)
            if (longPct >= 60) return 4.0;
            if (longPct <= 30) return 8.0;   // Very bullish (retail is heavily short)
            if (longPct <= 40) return 6.5;

            return 5.0; // Balanced
        }

        /// <summary>Computes a 1–10 technical score from RSI, MACD, EMA indicators. Used by TrailBlazer and DailyPrediction.</summary>
        public static double ComputeTechnicalScore(Dictionary<string, double> technicals)
        {
            double score = 5.0;

            // RSI
            if (technicals.TryGetValue("RSI", out var rsi) && rsi != 50)
            {
                score += rsi switch
                {
                    > 70 => -2.0,   // Overbought (bearish)
                    > 60 => -0.5,
                    < 30 => 2.0,    // Oversold (bullish opportunity)
                    < 40 => 0.5,
                    _ => 0
                };
            }

            // MACD crossover
            if (technicals.TryGetValue("MACD", out var macd) && technicals.TryGetValue("MACDSignal", out var signal))
            {
                if (macd > signal) score += 1.0;       // Bullish crossover
                else if (macd < signal) score -= 1.0;   // Bearish crossover
            }

            // EMA trend (50/200 golden/death cross)
            if (technicals.TryGetValue("EMA50", out var ema50) && technicals.TryGetValue("EMA200", out var ema200) && ema50 > 0 && ema200 > 0)
            {
                if (ema50 > ema200) score += 1.5;       // Golden cross - bullish
                else if (ema50 < ema200) score -= 1.5;   // Death cross - bearish
            }

            // 200-day MA bearish signal: price below EMA200 adds penalty
            if (technicals.TryGetValue("Close", out var close) && technicals.TryGetValue("EMA200", out var ema200Price) && close > 0 && ema200Price > 0 && close < ema200Price)
            {
                score -= 1.0;   // Price below EMA200 = bearish
            }

            // Stochastic: oversold (<20) = bullish bounce +0.5, overbought (>80) = bearish -0.5
            if (technicals.TryGetValue("StochasticK", out var stochasticK))
            {
                if (stochasticK < 20) score += 0.5;
                else if (stochasticK > 80) score -= 0.5;
            }

            return Math.Clamp(score, 1.0, 10.0);
        }
    }
}
