namespace TradeHelper.Services
{
    /// <summary>Upgrades BUY/SELL to STRONG_* when TraderNick transcript aligns with breakout direction.</summary>
    public static class TraderNickSetupAdjuster
    {
        public static (string Signal, string Detail) Apply(
            string signal,
            string detail,
            bool bullBreak,
            bool bearBreak,
            double? transcriptSentiment,
            bool transcriptMentionsSymbol)
        {
            if (!transcriptMentionsSymbol || !transcriptSentiment.HasValue)
                return (signal, detail);

            if (bullBreak && string.Equals(signal, "BUY", StringComparison.OrdinalIgnoreCase) && transcriptSentiment >= 7.0)
                return ("STRONG_BUY", detail);
            if (bearBreak && string.Equals(signal, "SELL", StringComparison.OrdinalIgnoreCase) && transcriptSentiment <= 4.0)
                return ("STRONG_SELL", detail);
            return (signal, detail);
        }
    }
}
