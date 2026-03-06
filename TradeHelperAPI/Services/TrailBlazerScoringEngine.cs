using TradeHelper.Models;

namespace TradeHelper.Services
{
    /// <summary>Context for fundamental scoring: forex uses relative strength (base vs quote); commodities use USD fundamentals (e.g. weak USD = bullish gold).</summary>
    public record FundamentalContext(
        Dictionary<string, double> BaseData,
        Dictionary<string, double> QuoteData,
        bool IsForexPair,
        bool IsUsdDenominatedCommodity);

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
            ScoringWeightContext weightContext)
        {
            var fundamentalScore = CalculateFundamentalScore(fundamentalContext);
            var institutionalScore = CalculateInstitutionalScore(cotReport, previousCotReport);
            var retailScore = CalculateRetailSentimentScore(retailSentiment);
            var technicalScore = CalculateTechnicalScore(technicals);

            var (wF, wCOT, wR, wNews, wT) = GetWeights(weightContext);

            var overall = (fundamentalScore * wF)
                        + (institutionalScore * wCOT)
                        + (retailScore * wR)
                        + (newsSentimentScore * wNews)
                        + (technicalScore * wT);

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
                DataSources = System.Text.Json.JsonSerializer.Serialize(dataSources),
                DateComputed = DateTime.UtcNow
            };
        }

        /// <summary>Returns (wF, wCOT, wR, wNews, wT) weights based on asset class and data availability.</summary>
        private static (double wF, double wCOT, double wR, double wNews, double wT) GetWeights(ScoringWeightContext ctx)
        {
            var ac = ctx.AssetClass ?? "";
            var isForex = ac.StartsWith("Forex", StringComparison.OrdinalIgnoreCase);

            double wF, wCOT, wR, wNews, wT;

            if (isForex)
            {
                // Forex: use predefined profiles (10% news when available)
                if (ctx.HasCOT && ctx.HasRetail)
                    (wF, wCOT, wR, wNews, wT) = (0.27, 0.22, 0.13, 0.10, 0.28);
                else if (!ctx.HasCOT && ctx.HasRetail)
                    (wF, wCOT, wR, wNews, wT) = (0.34, 0, 0.13, 0.10, 0.43);
                else if (ctx.HasCOT && !ctx.HasRetail)
                    (wF, wCOT, wR, wNews, wT) = (0.31, 0.22, 0, 0.10, 0.37);
                else
                    (wF, wCOT, wR, wNews, wT) = (0.40, 0, 0, 0.10, 0.50);
            }
            else
            {
                // Non-forex: fixed profiles (10% news when available)
                (wF, wCOT, wR, wNews, wT) = ac.ToUpperInvariant() switch
                {
                    "METAL" => (0.22, 0, 0, 0.10, 0.68),
                    "INDEX" => (0.27, 0, 0, 0.10, 0.63),
                    "COMMODITY" => (0.31, 0, 0, 0.10, 0.59),
                    "BOND" => (0.49, 0, 0, 0.10, 0.41),
                    _ => (0.31, 0, 0, 0.10, 0.59)
                };
            }

            // Zero out weights for sources with no data, then redistribute proportionally
            var f = ctx.HasFundamental ? wF : 0.0;
            var cot = ctx.HasCOT ? wCOT : 0.0;
            var r = ctx.HasRetail ? wR : 0.0;
            var news = ctx.HasNewsSentiment ? wNews : 0.0;
            var t = ctx.HasTechnical ? wT : 0.0;

            var total = f + cot + r + news + t;
            if (total <= 0)
                return (0.20, 0.20, 0.20, 0.20, 0.20); // fallback when all missing

            return (f / total, cot / total, r / total, news / total, t / total);
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
                // Fallback (e.g. indices, bonds): use average of base/quote if available
                var merged = MergeData(ctx.BaseData, ctx.QuoteData);
                if (merged.TryGetValue("GDP", out var gdp) && gdp != 0) { score += gdp > AvgGdpGrowth + GdpBand ? 1.5 : gdp < AvgGdpGrowth - GdpBand ? -1.5 : 0; factorsWithData++; }
                if (merged.TryGetValue("CPI", out var cpi) && cpi != 0) { score += cpi > FedInflationTarget + CpiBand ? -1.0 : cpi < FedInflationTarget - CpiBand ? 0.5 : 0; factorsWithData++; }
                if (merged.TryGetValue("Unemployment", out var unemp) && unemp != 0) { score += unemp < 4 ? 1.5 : unemp < 6 ? 0.5 : -1.5; factorsWithData++; }
                if (merged.TryGetValue("InterestRate", out var rate) && rate != 0) { score += rate > 3 ? 1.0 : rate > 1 ? 0.5 : -0.5; factorsWithData++; }
                if (merged.TryGetValue("PMI", out var pmi) && pmi != 0) { score += pmi > 55 ? 1.5 : pmi > 50 ? 0.5 : -1.5; factorsWithData++; }
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

        private static double CalculateTechnicalScore(Dictionary<string, double> technicals)
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

            return Math.Clamp(score, 1.0, 10.0);
        }
    }
}
