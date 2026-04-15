namespace TradeHelper.Services
{
    /// <summary>Latest TraderNick (YouTube) transcript snapshot for one TrailBlazer refresh cycle.</summary>
    public sealed class TraderNickInsight
    {
        public bool HasData { get; init; }
        /// <summary>1–10 from <see cref="NewsSentimentHelper"/> on transcript text.</summary>
        public double SentimentScore { get; init; } = 5.0;
        public string? VideoId { get; init; }
        public string? VideoTitle { get; init; }
        public string? TranscriptText { get; init; }

        public bool MentionsSymbol(string symbol, string? assetClass)
        {
            if (string.IsNullOrEmpty(TranscriptText) || string.IsNullOrWhiteSpace(symbol)) return false;
            var t = TranscriptText;
            var upper = symbol.Trim().ToUpperInvariant().Replace("/", "").Replace("_", "");
            if (upper.Length == 6 && upper.All(char.IsLetter))
            {
                var b = upper[..3];
                var q = upper[3..];
                return t.Contains(upper, StringComparison.OrdinalIgnoreCase)
                    || t.Contains($"{b}/{q}", StringComparison.OrdinalIgnoreCase)
                    || t.Contains($"{b} {q}", StringComparison.OrdinalIgnoreCase)
                    || t.Contains($"{b}-{q}", StringComparison.OrdinalIgnoreCase);
            }
            if (upper.Contains("XAU", StringComparison.Ordinal))
                return t.Contains("gold", StringComparison.OrdinalIgnoreCase) || t.Contains("XAU", StringComparison.OrdinalIgnoreCase);
            if (upper.Contains("XAG", StringComparison.Ordinal))
                return t.Contains("silver", StringComparison.OrdinalIgnoreCase) || t.Contains("XAG", StringComparison.OrdinalIgnoreCase);
            return t.Contains(symbol, StringComparison.OrdinalIgnoreCase);
        }
    }
}
