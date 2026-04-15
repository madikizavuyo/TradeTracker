namespace TradeHelper.Services
{
    /// <summary>
    /// Detects "catch the bottom" style setups after a measurable drawdown: prior swing high,
    /// recent washout low, then reclaim + RSI stabilization. Same rules apply to any instrument with enough daily bars (indices, FX, commodities).
    /// </summary>
    public static class PullbackReversalSetupAnalyzer
    {
        /// <param name="barsNewestFirst">Daily OHLC, index 0 = last session.</param>
        /// <returns>NONE, REVERSAL_BUY (moderate), or STRONG_REVERSAL_BUY (deeper dip + stronger reclaim/momentum).</returns>
        public static (string Signal, string Detail) Analyze(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> barsNewestFirst,
            double overallScore,
            string bias,
            int priorHighStart = 10,
            int priorHighEnd = 50,
            int recentLowStart = 1,
            int recentLowEnd = 14,
            double minDrawdownPct = 4.0,
            double minReclaimFromLowPct = 1.0,
            double strongDrawdownPct = 6.5)
        {
            const int minBars = 55;
            if (barsNewestFirst == null || barsNewestFirst.Count < minBars)
                return ("NONE", "Insufficient history for pullback-reversal.");

            var highs = barsNewestFirst.Select(b => b.High).ToList();
            var lows = barsNewestFirst.Select(b => b.Low).ToList();
            var closes = barsNewestFirst.Select(b => b.Close).ToList();

            var end = Math.Min(priorHighEnd, highs.Count - 1);
            var start = Math.Min(priorHighStart, end);
            if (start >= end) return ("NONE", "Invalid prior-high window.");

            double priorSwingHigh = 0;
            for (var i = start; i <= end; i++)
                priorSwingHigh = Math.Max(priorSwingHigh, highs[i]);

            var lowEnd = Math.Min(recentLowEnd, lows.Count - 1);
            var lowStart = Math.Min(recentLowStart, lowEnd);
            double recentLow = double.MaxValue;
            for (var i = lowStart; i <= lowEnd; i++)
                recentLow = Math.Min(recentLow, lows[i]);

            if (priorSwingHigh <= 0 || recentLow <= 0 || recentLow >= priorSwingHigh)
                return ("NONE", "No valid drawdown structure.");

            var drawdownPct = (priorSwingHigh - recentLow) / priorSwingHigh * 100.0;
            if (drawdownPct < minDrawdownPct)
                return ("NONE", $"Drawdown {drawdownPct:F1}% from prior swing high below threshold ({minDrawdownPct}%); no reversal setup.");

            var close0 = closes[0];
            var reclaimPct = (close0 - recentLow) / recentLow * 100.0;
            if (reclaimPct < minReclaimFromLowPct)
                return ("NONE", $"Reclaim from recent low {reclaimPct:F2}% (need ≥{minReclaimFromLowPct}% above washout low).");

            if (closes.Count < 2 || close0 <= closes[1])
                return ("NONE", "No bullish session yet (need up close vs prior day).");

            var sma10 = OhlcTechnicalCalculator.Sma(closes, 10);
            if (sma10 <= 0 || close0 < sma10 * 0.998)
                return ("NONE", "Close has not reclaimed short-term mean (SMA10).");

            var rsiNow = RsiAtOffset(closes, 0);
            var rsiWasStressed = false;
            for (var off = 2; off <= 12 && off < closes.Count - 15; off++)
            {
                var r = RsiAtOffset(closes, off);
                if (r < 40) rsiWasStressed = true;
            }

            var biasBull = string.Equals(bias, "Bullish", StringComparison.OrdinalIgnoreCase);
            var line = $"Pullback reversal: {drawdownPct:F1}% drop from swing high to recent low, reclaim +{reclaimPct:F1}% from low, close {close0:F4} vs SMA10. RSI≈{rsiNow:F0}. Scanner {overallScore:F1}/10, {bias}.";

            var strongMomentum = drawdownPct >= strongDrawdownPct && rsiWasStressed && rsiNow >= 44 && overallScore >= 5.5 && !string.Equals(bias, "Bearish", StringComparison.OrdinalIgnoreCase);
            if (strongMomentum)
                return ("STRONG_REVERSAL_BUY", line + " Strong washout + momentum.");

            if (overallScore >= 5.0 || biasBull || rsiNow >= 46)
                return ("REVERSAL_BUY", line);

            return ("NONE", "Pullback structure present but scanner/momentum not aligned for reversal label.");
        }

        private static double RsiAtOffset(IReadOnlyList<double> closesNewestFirst, int offset)
        {
            if (offset < 0 || closesNewestFirst.Count < offset + 15) return 50;
            var slice = closesNewestFirst.Skip(offset).ToList();
            return OhlcTechnicalCalculator.RsiFromCloses(slice, 14);
        }
    }
}
