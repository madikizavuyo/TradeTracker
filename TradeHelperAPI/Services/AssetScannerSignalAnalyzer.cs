namespace TradeHelper.Services
{
    /// <summary>
    /// Score-led Asset Scanner setup logic.
    /// <para>
    /// Direction from overall score (default thresholds: &gt;6 = BUY, &lt;4 = SELL, 4–6 = WATCH).
    /// </para>
    /// Signal tiers (strongest → weakest):
    /// <list type="bullet">
    ///   <item><description>BUY NOW / SELL NOW — direction + Fib (50% or 61.8%) touched recently + continuation confluence (horizontal support/resistance OR ascending/descending trendline).</description></item>
    ///   <item><description>STRONG_REVERSAL_BUY / STRONG_REVERSAL_SELL — 61.8% Fib of last completed leg touched within the last N bars.</description></item>
    ///   <item><description>REVERSAL_BUY / REVERSAL_SELL — 50% Fib of last completed leg touched within the last N bars.</description></item>
    ///   <item><description>RESISTANCE_BUY / RESISTANCE_SELL — bounce off trend support / rejection at trend resistance (horizontal continuation).</description></item>
    ///   <item><description>TRENDLINE_BUY / TRENDLINE_SELL — bounce off an ascending trendline through higher-lows (BUY) or rejection at a descending trendline through lower-highs (SELL).</description></item>
    ///   <item><description>BUY / SELL — directional only from score, awaiting Fib / continuation alignment.</description></item>
    /// </list>
    /// </summary>
    public static class AssetScannerSignalAnalyzer
    {
        /// <summary>
        /// Returns the signal key and a short human-readable detail explaining why the signal was raised.
        /// </summary>
        /// <param name="barsNewestFirst">Daily bars ordered newest-first.</param>
        /// <param name="overallScore">Scanner 0–10 score.</param>
        /// <param name="swingLookback">Bars used to find the last swing high/low for the Fib leg.</param>
        /// <param name="continuationLookback">Bars used to identify trend support/resistance for continuation bounces.</param>
        /// <param name="fibTolerancePct">Percent distance considered a "touch" of a Fib level.</param>
        /// <param name="continuationTolerancePct">Percent distance considered a "touch" of the continuation support/resistance.</param>
        /// <param name="fibLookbackBars">Number of most-recent bars a Fib touch is allowed to have occurred in.</param>
        /// <param name="buyThreshold">Score above which direction is BUY (default 6.0 — "above 6 is Buy").</param>
        /// <param name="sellThreshold">Score below which direction is SELL (default 4.0 — "under 4 is Sell").</param>
        /// <param name="trendlinePivot">Bars on either side of a local min/max needed to qualify as a swing pivot (default 2).</param>
        /// <param name="trendlineTolerancePct">Percent distance considered a "touch" of the extrapolated trendline (default 0.6).</param>
        public static (string Signal, string Detail) Analyze(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> barsNewestFirst,
            double overallScore,
            int swingLookback = 30,
            int continuationLookback = 20,
            double fibTolerancePct = 0.8,
            double continuationTolerancePct = 0.5,
            int fibLookbackBars = 5,
            double buyThreshold = 6.0,
            double sellThreshold = 4.0,
            int trendlinePivot = 2,
            double trendlineTolerancePct = 0.6)
        {
            if (barsNewestFirst == null || barsNewestFirst.Count < Math.Max(swingLookback, continuationLookback) + 3)
                return ("NONE", "Insufficient price history for scanner signal.");

            if (overallScore >= sellThreshold && overallScore <= buyThreshold)
                return ("WATCH", $"Score {overallScore:F2} in neutral band {sellThreshold:F1}–{buyThreshold:F1} — no directional bias.");

            var buyDir = overallScore > buyThreshold;
            var sellDir = overallScore < sellThreshold;

            var swingSlice = barsNewestFirst.Take(Math.Min(swingLookback + 1, barsNewestFirst.Count)).ToList();
            if (swingSlice.Count < 5) return ("NONE", "Insufficient swing window.");

            int highIdx = 0, lowIdx = 0;
            double hi = swingSlice[0].High;
            double lo = swingSlice[0].Low;
            for (var i = 1; i < swingSlice.Count; i++)
            {
                if (swingSlice[i].High > hi) { hi = swingSlice[i].High; highIdx = i; }
                if (swingSlice[i].Low < lo) { lo = swingSlice[i].Low; lowIdx = i; }
            }
            if (hi <= lo || lo <= 0) return ("NONE", "Invalid swing range.");

            var range = hi - lo;
            var close0 = barsNewestFirst[0].Close;
            var close1 = barsNewestFirst[1].Close;

            // In newest-first indexing, larger index = older bar.
            // If the swing HIGH is older than the swing LOW, the latest completed leg is DOWN → potential BUY reversal context.
            // If the swing LOW is older than the swing HIGH, the latest completed leg is UP → potential SELL reversal context.
            var downLegLast = highIdx > lowIdx;
            var upLegLast = lowIdx > highIdx;

            // BUY reversal: bounce up from recent low. Retrace % measured from the bottom of the prior drop.
            var fibBuy50 = lo + range * 0.500;
            var fibBuy618 = lo + range * 0.618;
            // SELL reversal: drop down from recent high. Retrace % measured from the top of the prior rally.
            var fibSell50 = hi - range * 0.500;
            var fibSell618 = hi - range * 0.618;

            var touchBars = Math.Min(Math.Max(1, fibLookbackBars), barsNewestFirst.Count);
            bool fibBuy50Touched = false, fibBuy618Touched = false;
            bool fibSell50Touched = false, fibSell618Touched = false;
            for (var i = 0; i < touchBars; i++)
            {
                var c = barsNewestFirst[i].Close;
                if (buyDir && downLegLast)
                {
                    if (IsNearPct(c, fibBuy618, fibTolerancePct)) fibBuy618Touched = true;
                    if (IsNearPct(c, fibBuy50, fibTolerancePct)) fibBuy50Touched = true;
                }
                if (sellDir && upLegLast)
                {
                    if (IsNearPct(c, fibSell618, fibTolerancePct)) fibSell618Touched = true;
                    if (IsNearPct(c, fibSell50, fibTolerancePct)) fibSell50Touched = true;
                }
            }

            var contSlice = barsNewestFirst.Skip(1).Take(continuationLookback).ToList();
            var contSupport = contSlice.Min(b => b.Low);
            var contResistance = contSlice.Max(b => b.High);

            var supportHold = buyDir
                && close0 >= contSupport
                && IsNearPct(close0, contSupport, continuationTolerancePct)
                && close0 >= close1;
            var resistanceReject = sellDir
                && close0 <= contResistance
                && IsNearPct(close0, contResistance, continuationTolerancePct)
                && close0 <= close1;

            // Diagonal trendline continuation confluence.
            // Uses the same lookback as the swing window; line fitted through the two most-recent qualifying pivots.
            var (trendlineBuyHold, trendlineBuyLevel) = EvaluateBuyTrendline(swingSlice, trendlinePivot, trendlineTolerancePct, close0, close1);
            var (trendlineSellReject, trendlineSellLevel) = EvaluateSellTrendline(swingSlice, trendlinePivot, trendlineTolerancePct, close0, close1);
            var trendlineBuy = buyDir && trendlineBuyHold;
            var trendlineSell = sellDir && trendlineSellReject;

            var anyFibBuy = fibBuy50Touched || fibBuy618Touched;
            var anyFibSell = fibSell50Touched || fibSell618Touched;
            // Continuation confluence is held if either the horizontal level holds OR the diagonal trendline respects.
            var buyContinuation = supportHold || trendlineBuy;
            var sellContinuation = resistanceReject || trendlineSell;

            if (buyDir && anyFibBuy && buyContinuation)
            {
                var fibTag = fibBuy618Touched ? "61.8%" : "50%";
                var fibLevel = fibBuy618Touched ? fibBuy618 : fibBuy50;
                var contTag = supportHold && trendlineBuy
                    ? $"support {contSupport:F4} + ascending trendline {trendlineBuyLevel:F4}"
                    : supportHold
                        ? $"support holding at {contSupport:F4}"
                        : $"ascending trendline bounce near {trendlineBuyLevel:F4}";
                return ("BUY NOW",
                    $"All aligned BUY: score {overallScore:F1}>{buyThreshold:F1}, Fib {fibTag} reversal near {fibLevel:F4} touched in last {touchBars} bars, {contTag}. close={close0:F4}.");
            }

            if (sellDir && anyFibSell && sellContinuation)
            {
                var fibTag = fibSell618Touched ? "61.8%" : "50%";
                var fibLevel = fibSell618Touched ? fibSell618 : fibSell50;
                var contTag = resistanceReject && trendlineSell
                    ? $"resistance {contResistance:F4} + descending trendline {trendlineSellLevel:F4}"
                    : resistanceReject
                        ? $"resistance rejecting at {contResistance:F4}"
                        : $"descending trendline rejection near {trendlineSellLevel:F4}";
                return ("SELL NOW",
                    $"All aligned SELL: score {overallScore:F1}<{sellThreshold:F1}, Fib {fibTag} reversal near {fibLevel:F4} touched in last {touchBars} bars, {contTag}. close={close0:F4}.");
            }

            if (fibBuy618Touched)
                return ("STRONG_REVERSAL_BUY",
                    $"Fib 61.8% bounce from bottom of last down-leg (level {fibBuy618:F4}); score {overallScore:F1}>{buyThreshold:F1}.");
            if (fibSell618Touched)
                return ("STRONG_REVERSAL_SELL",
                    $"Fib 61.8% drop from top of last up-leg (level {fibSell618:F4}); score {overallScore:F1}<{sellThreshold:F1}.");

            if (fibBuy50Touched)
                return ("REVERSAL_BUY",
                    $"Fib 50% retrace reached near {fibBuy50:F4} from bottom of last down-leg; score {overallScore:F1}>{buyThreshold:F1}.");
            if (fibSell50Touched)
                return ("REVERSAL_SELL",
                    $"Fib 50% retrace reached near {fibSell50:F4} from top of last up-leg; score {overallScore:F1}<{sellThreshold:F1}.");

            if (supportHold)
                return ("RESISTANCE_BUY",
                    $"Bounce off trend support {contSupport:F4} (continuation); score {overallScore:F1}>{buyThreshold:F1}.");
            if (resistanceReject)
                return ("RESISTANCE_SELL",
                    $"Rejection at trend resistance {contResistance:F4} (continuation); score {overallScore:F1}<{sellThreshold:F1}.");

            if (trendlineBuy)
                return ("TRENDLINE_BUY",
                    $"Bounce off ascending trendline (higher-lows) near {trendlineBuyLevel:F4}; score {overallScore:F1}>{buyThreshold:F1}.");
            if (trendlineSell)
                return ("TRENDLINE_SELL",
                    $"Rejection at descending trendline (lower-highs) near {trendlineSellLevel:F4}; score {overallScore:F1}<{sellThreshold:F1}.");

            return buyDir
                ? ("BUY", $"Directional BUY from scanner score {overallScore:F1}>{buyThreshold:F1}; awaiting Fib/continuation alignment.")
                : ("SELL", $"Directional SELL from scanner score {overallScore:F1}<{sellThreshold:F1}; awaiting Fib/continuation alignment.");
        }

        /// <summary>
        /// Finds local swing lows in a newest-first bar slice. A bar at index i is a pivot low when
        /// every bar within <paramref name="pivot"/> positions on either side has a strictly higher low.
        /// Returns pivots sorted by recency (newest first).
        /// </summary>
        private static List<(int Idx, double Low)> FindSwingLows(IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> bars, int pivot)
        {
            var result = new List<(int, double)>();
            for (var i = pivot; i < bars.Count - pivot; i++)
            {
                var low = bars[i].Low;
                var isPivot = true;
                for (var k = 1; k <= pivot; k++)
                {
                    if (bars[i - k].Low <= low || bars[i + k].Low <= low) { isPivot = false; break; }
                }
                if (isPivot) result.Add((i, low));
            }
            return result;
        }

        /// <summary>
        /// Finds local swing highs in a newest-first bar slice. A bar at index i is a pivot high when
        /// every bar within <paramref name="pivot"/> positions on either side has a strictly lower high.
        /// </summary>
        private static List<(int Idx, double High)> FindSwingHighs(IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> bars, int pivot)
        {
            var result = new List<(int, double)>();
            for (var i = pivot; i < bars.Count - pivot; i++)
            {
                var high = bars[i].High;
                var isPivot = true;
                for (var k = 1; k <= pivot; k++)
                {
                    if (bars[i - k].High >= high || bars[i + k].High >= high) { isPivot = false; break; }
                }
                if (isPivot) result.Add((i, high));
            }
            return result;
        }

        /// <summary>
        /// BUY trendline: ascending line through the two most-recent swing lows (higher-lows).
        /// Returns (hold, extrapolatedLevelAtBar0). hold = true when close0 is near the line, at/above it, and up-closing.
        /// </summary>
        private static (bool Hold, double Level) EvaluateBuyTrendline(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> bars, int pivot, double tolerancePct,
            double close0, double close1)
        {
            var lows = FindSwingLows(bars, pivot);
            if (lows.Count < 2) return (false, 0);
            // pivots are in ascending i order (oldest-first by index convention inside slice, which for newest-first slice means pivot[0] is more recent)
            // Keep the two most-recent pivots: smallest indices in newest-first slice.
            lows.Sort((a, b) => a.Idx.CompareTo(b.Idx));
            var p1 = lows[0];
            var p2 = lows[1];
            // Ascending trendline (higher-lows): newer pivot must be strictly above older pivot.
            if (p1.Low <= p2.Low) return (false, 0);

            // Line in (idx, price) space; newest-first so idx=0 is today. Extrapolate to idx=0.
            var slope = (p1.Low - p2.Low) / (double)(p1.Idx - p2.Idx); // negative
            var level0 = p1.Low + slope * (0 - p1.Idx);

            if (level0 <= 0) return (false, level0);
            var hold = IsNearPct(close0, level0, tolerancePct) && close0 >= level0 && close0 >= close1;
            return (hold, level0);
        }

        /// <summary>
        /// SELL trendline: descending line through the two most-recent swing highs (lower-highs).
        /// Returns (reject, extrapolatedLevelAtBar0). reject = true when close0 is near the line, at/below it, and down-closing.
        /// </summary>
        private static (bool Reject, double Level) EvaluateSellTrendline(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> bars, int pivot, double tolerancePct,
            double close0, double close1)
        {
            var highs = FindSwingHighs(bars, pivot);
            if (highs.Count < 2) return (false, 0);
            highs.Sort((a, b) => a.Idx.CompareTo(b.Idx));
            var p1 = highs[0];
            var p2 = highs[1];
            // Descending trendline (lower-highs): newer pivot must be strictly below older pivot.
            if (p1.High >= p2.High) return (false, 0);

            var slope = (p1.High - p2.High) / (double)(p1.Idx - p2.Idx); // positive
            var level0 = p1.High + slope * (0 - p1.Idx);

            if (level0 <= 0) return (false, level0);
            var reject = IsNearPct(close0, level0, tolerancePct) && close0 <= level0 && close0 <= close1;
            return (reject, level0);
        }

        private static bool IsNearPct(double value, double level, double tolerancePct)
        {
            if (level <= 0 || tolerancePct < 0) return false;
            return Math.Abs(value - level) / level * 100.0 <= tolerancePct;
        }

        /// <summary>Snapshot of every intermediate value used by <see cref="Analyze"/>. Returned by <see cref="Diagnose"/> for troubleshooting why a signal was (or wasn't) produced.</summary>
        public record SignalDiagnostic(
            string Signal,
            string Detail,
            int BarsAvailable,
            double OverallScore,
            double BuyThreshold,
            double SellThreshold,
            bool BuyDir,
            bool SellDir,
            int SwingLookback,
            int HighIdx,
            int LowIdx,
            double High,
            double Low,
            double Range,
            bool DownLegLast,
            bool UpLegLast,
            double Close0,
            double Close1,
            int FibTouchBars,
            double FibTolerancePct,
            double FibBuy50,
            double FibBuy618,
            double FibSell50,
            double FibSell618,
            bool FibBuy50Touched,
            bool FibBuy618Touched,
            bool FibSell50Touched,
            bool FibSell618Touched,
            double ContSupport,
            double ContResistance,
            double ContinuationTolerancePct,
            bool SupportHold,
            bool ResistanceReject,
            double TrendlineBuyLevel,
            bool TrendlineBuyHold,
            double TrendlineSellLevel,
            bool TrendlineSellReject);

        /// <summary>Runs the analyzer and returns every intermediate value plus the resulting signal. Use for diagnostic endpoints.</summary>
        public static SignalDiagnostic Diagnose(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> barsNewestFirst,
            double overallScore,
            int swingLookback = 30,
            int continuationLookback = 20,
            double fibTolerancePct = 0.8,
            double continuationTolerancePct = 0.5,
            int fibLookbackBars = 5,
            double buyThreshold = 6.0,
            double sellThreshold = 4.0,
            int trendlinePivot = 2,
            double trendlineTolerancePct = 0.6)
        {
            var (signal, detail) = Analyze(
                barsNewestFirst, overallScore, swingLookback, continuationLookback,
                fibTolerancePct, continuationTolerancePct, fibLookbackBars,
                buyThreshold, sellThreshold, trendlinePivot, trendlineTolerancePct);

            var barsCount = barsNewestFirst?.Count ?? 0;
            if (barsNewestFirst == null || barsCount < Math.Max(swingLookback, continuationLookback) + 3)
            {
                return new SignalDiagnostic(signal, detail, barsCount, overallScore, buyThreshold, sellThreshold,
                    false, false, swingLookback, -1, -1, 0, 0, 0, false, false, 0, 0,
                    fibLookbackBars, fibTolerancePct, 0, 0, 0, 0, false, false, false, false,
                    0, 0, continuationTolerancePct, false, false, 0, false, 0, false);
            }

            var buyDir = overallScore > buyThreshold;
            var sellDir = overallScore < sellThreshold;

            var swingSlice = barsNewestFirst.Take(Math.Min(swingLookback + 1, barsNewestFirst.Count)).ToList();
            int highIdx = 0, lowIdx = 0;
            double hi = swingSlice[0].High, lo = swingSlice[0].Low;
            for (var i = 1; i < swingSlice.Count; i++)
            {
                if (swingSlice[i].High > hi) { hi = swingSlice[i].High; highIdx = i; }
                if (swingSlice[i].Low < lo) { lo = swingSlice[i].Low; lowIdx = i; }
            }
            var range = hi - lo;
            var close0 = barsNewestFirst[0].Close;
            var close1 = barsNewestFirst[1].Close;
            var downLegLast = highIdx > lowIdx;
            var upLegLast = lowIdx > highIdx;

            var fibBuy50 = lo + range * 0.500;
            var fibBuy618 = lo + range * 0.618;
            var fibSell50 = hi - range * 0.500;
            var fibSell618 = hi - range * 0.618;

            var touchBars = Math.Min(Math.Max(1, fibLookbackBars), barsNewestFirst.Count);
            bool fibBuy50Touched = false, fibBuy618Touched = false;
            bool fibSell50Touched = false, fibSell618Touched = false;
            for (var i = 0; i < touchBars; i++)
            {
                var c = barsNewestFirst[i].Close;
                if (buyDir && downLegLast)
                {
                    if (IsNearPct(c, fibBuy618, fibTolerancePct)) fibBuy618Touched = true;
                    if (IsNearPct(c, fibBuy50, fibTolerancePct)) fibBuy50Touched = true;
                }
                if (sellDir && upLegLast)
                {
                    if (IsNearPct(c, fibSell618, fibTolerancePct)) fibSell618Touched = true;
                    if (IsNearPct(c, fibSell50, fibTolerancePct)) fibSell50Touched = true;
                }
            }

            var contSlice = barsNewestFirst.Skip(1).Take(continuationLookback).ToList();
            var contSupport = contSlice.Min(b => b.Low);
            var contResistance = contSlice.Max(b => b.High);
            var supportHold = buyDir && close0 >= contSupport && IsNearPct(close0, contSupport, continuationTolerancePct) && close0 >= close1;
            var resistanceReject = sellDir && close0 <= contResistance && IsNearPct(close0, contResistance, continuationTolerancePct) && close0 <= close1;

            var (trendlineBuyHold, trendlineBuyLevel) = EvaluateBuyTrendline(swingSlice, trendlinePivot, trendlineTolerancePct, close0, close1);
            var (trendlineSellReject, trendlineSellLevel) = EvaluateSellTrendline(swingSlice, trendlinePivot, trendlineTolerancePct, close0, close1);

            return new SignalDiagnostic(
                signal, detail, barsCount, overallScore, buyThreshold, sellThreshold,
                buyDir, sellDir, swingLookback, highIdx, lowIdx, hi, lo, range,
                downLegLast, upLegLast, close0, close1,
                touchBars, fibTolerancePct,
                fibBuy50, fibBuy618, fibSell50, fibSell618,
                fibBuy50Touched, fibBuy618Touched, fibSell50Touched, fibSell618Touched,
                contSupport, contResistance, continuationTolerancePct, supportHold, resistanceReject,
                trendlineBuyLevel, buyDir && trendlineBuyHold, trendlineSellLevel, sellDir && trendlineSellReject);
        }
    }
}
