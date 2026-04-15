namespace TradeHelper.Services
{
    /// <summary>Merges box-breakout signals with pullback-reversal signals (bearish box wins; reversal fills NONE/WATCH).</summary>
    public static class TradeSetupMergeHelper
    {
        public static (string Signal, string Detail) MergeBoxAndPullbackReversal(
            string boxSig,
            string boxDet,
            string revSig,
            string revDet)
        {
            if (string.Equals(boxSig, "STRONG_SELL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(boxSig, "SELL", StringComparison.OrdinalIgnoreCase))
                return (boxSig, Truncate(boxDet));

            if (string.Equals(revSig, "NONE", StringComparison.OrdinalIgnoreCase))
                return (boxSig, Truncate(boxDet));

            if (string.Equals(revSig, "STRONG_REVERSAL_BUY", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(boxSig, "STRONG_BUY", StringComparison.OrdinalIgnoreCase))
                    return ("STRONG_BUY", Truncate(Combine(boxDet, revDet)));
                if (string.Equals(boxSig, "BUY", StringComparison.OrdinalIgnoreCase))
                    return ("STRONG_BUY", Truncate(Combine(revDet, boxDet)));
                if (string.Equals(boxSig, "NONE", StringComparison.OrdinalIgnoreCase) || string.Equals(boxSig, "WATCH", StringComparison.OrdinalIgnoreCase))
                    return ("STRONG_REVERSAL_BUY", Truncate(revDet));
                return (boxSig, Truncate(Combine(boxDet, revDet)));
            }

            if (string.Equals(revSig, "REVERSAL_BUY", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(boxSig, "NONE", StringComparison.OrdinalIgnoreCase) || string.Equals(boxSig, "WATCH", StringComparison.OrdinalIgnoreCase))
                    return ("REVERSAL_BUY", Truncate(revDet));
                if (string.Equals(boxSig, "BUY", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(boxSig, "STRONG_BUY", StringComparison.OrdinalIgnoreCase))
                    return (boxSig, Truncate(Combine(boxDet, revDet)));
            }

            return (boxSig, Truncate(boxDet));
        }

        private static string Combine(string a, string b) => $"{a} | {b}";

        private static string Truncate(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 500 ? s[..500] : s;
        }
    }
}
