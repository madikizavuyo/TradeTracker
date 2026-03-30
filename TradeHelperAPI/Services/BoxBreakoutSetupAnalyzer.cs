namespace TradeHelper.Services
{
    /// <summary>
    /// Detects tight-range consolidation then a daily close outside the range (box breakout).
    /// Combined with Asset Scanner overall score and bias for actionable STRONG_BUY / STRONG_SELL style labels.
    /// </summary>
    public static class BoxBreakoutSetupAnalyzer
    {
        /// <param name="barsNewestFirst">Daily OHLC, index 0 = last session.</param>
        /// <returns>Signal code and a short human-readable line for UI/email.</returns>
        public static (string Signal, string Detail) Analyze(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> barsNewestFirst,
            double overallScore,
            string bias,
            int boxLookback = 20,
            double maxRangePctOfMid = 4.0)
        {
            if (barsNewestFirst == null || barsNewestFirst.Count < boxLookback + 3)
                return ("NONE", "Insufficient price history for box setup.");

            // Consolidation window: bars [1 .. boxLookback] (exclude today bar [0] as breakout candidate)
            var slice = barsNewestFirst.Skip(1).Take(boxLookback).ToList();
            if (slice.Count < boxLookback)
                return ("NONE", "Insufficient bars for consolidation window.");

            var boxHigh = slice.Max(b => b.High);
            var boxLow = slice.Min(b => b.Low);
            if (boxHigh <= boxLow || boxLow <= 0)
                return ("NONE", "Invalid box range.");

            var mid = (boxHigh + boxLow) / 2.0;
            var rangePct = (boxHigh - boxLow) / mid * 100.0;
            var consolidated = rangePct <= maxRangePctOfMid;

            var close = barsNewestFirst[0].Close;
            const double eps = 0.0008; // ~0.08% buffer for FX noise

            var bullBreak = close > boxHigh * (1 + eps);
            var bearBreak = close < boxLow * (1 - eps);

            if (!bullBreak && !bearBreak)
            {
                var inside = consolidated
                    ? $"Consolidation ~{rangePct:F1}% range over {boxLookback}d; price inside box (H={boxHigh:F4}, L={boxLow:F4})."
                    : $"Wide range ({rangePct:F1}% of mid); no tight box breakout.";
                return ("NONE", inside);
            }

            if (!consolidated && (bullBreak || bearBreak))
                return ("WATCH", $"Close outside prior {boxLookback}d range but consolidation not tight ({rangePct:F1}% > {maxRangePctOfMid}%); monitor.");

            var biasBull = string.Equals(bias, "Bullish", StringComparison.OrdinalIgnoreCase);
            var biasBear = string.Equals(bias, "Bearish", StringComparison.OrdinalIgnoreCase);

            if (bullBreak)
            {
                var line = $"Bullish breakout above {boxLookback}d box (H={boxHigh:F4}, close={close:F4}). Scanner: {overallScore:F1}/10, {bias}.";
                if (overallScore >= 6.5 && !biasBear)
                    return ("STRONG_BUY", line);
                if (overallScore >= 5.5 || biasBull)
                    return ("BUY", line);
                return ("WATCH", line + " (Breakout vs weaker scanner — confirm).");
            }

            {
                var line = $"Bearish breakdown below {boxLookback}d box (L={boxLow:F4}, close={close:F4}). Scanner: {overallScore:F1}/10, {bias}.";
                if (overallScore <= 3.5 && !biasBull)
                    return ("STRONG_SELL", line);
                if (overallScore <= 4.5 || biasBear)
                    return ("SELL", line);
                return ("WATCH", line + " (Breakdown vs stronger scanner — confirm).");
            }
        }
    }
}
