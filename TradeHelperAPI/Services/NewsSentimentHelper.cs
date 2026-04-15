namespace TradeHelper.Services
{
    /// <summary>Keyword + phrase sentiment for news headlines and transcript text (1–10).</summary>
    public static class NewsSentimentHelper
    {
        private static readonly HashSet<string> PositiveWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "rally", "surge", "strong", "gains", "bullish", "positive", "breakout", "rise", "soar", "jump", "climb",
            "recovery", "rebound", "outperform", "upgrade", "optimistic", "growth", "hawkish", "tightening",
            "beat", "beats", "profit", "expansion", "momentum", "uptrend", "support", "resilience"
        };

        private static readonly HashSet<string> NegativeWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "slump", "crash", "fall", "drop", "bearish", "negative", "recession", "tumble", "plunge", "decline",
            "sell-off", "selloff", "downgrade", "pessimistic", "weak", "loss", "collapse", "crisis", "fear",
            "dovish", "cut", "cuts", "miss", "misses", "warning", "downside", "selloff", "struggle", "downturn"
        };

        private static readonly (string Phrase, int Weight)[] PositivePhrases =
        {
            ("risk-on", 2), ("risk on", 2), ("all-time high", 2), ("record high", 2), ("flight to quality", 1),
            ("bull market", 2), ("breaks above", 1), ("to the upside", 1),
            ("easing", 1), ("accommodative", 1)
        };

        private static readonly (string Phrase, int Weight)[] NegativePhrases =
        {
            ("risk-off", 2), ("risk off", 2), ("sell-off", 2), ("bear market", 2), ("recession fears", 2),
            ("record low", 2), ("to the downside", 1), ("breaks below", 1),
            ("tightening cycle", 2), ("hawkish", 1), ("bankruptcy", 2), ("default", 1)
        };

        /// <summary>Scores concatenated texts. Uses per-sentence token hits plus phrase scan on full blob so scores vary when single words do not match.</summary>
        public static double ComputeFromTexts(IReadOnlyList<string> texts)
        {
            if (texts == null || texts.Count == 0) return 5.0;

            var blob = string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t)));
            if (string.IsNullOrWhiteSpace(blob)) return 5.0;

            var lineScores = new List<double>();
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var words = text.Split([' ', '.', ',', '!', '?', ':', ';', '-', '\'', '"', '\n', '\r', '\t', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
                var pos = words.Count(w => PositiveWords.Contains(w));
                var neg = words.Count(w => NegativeWords.Contains(w));
                var diff = pos - neg;
                if (diff != 0 || pos + neg > 0)
                    lineScores.Add(Math.Clamp(5.0 + diff * 1.2, 1.0, 10.0));
            }

            var lower = blob.ToLowerInvariant();
            var phraseBias = 0;
            foreach (var (phrase, weight) in PositivePhrases)
            {
                if (lower.Contains(phrase, StringComparison.Ordinal))
                    phraseBias += weight;
            }
            foreach (var (phrase, weight) in NegativePhrases)
            {
                if (lower.Contains(phrase, StringComparison.Ordinal))
                    phraseBias -= weight;
            }

            double phraseScore = Math.Clamp(5.0 + phraseBias * 0.35, 1.0, 10.0);

            if (lineScores.Count == 0)
                return phraseScore;

            var lineAvg = lineScores.Average();
            return Math.Clamp(lineAvg * 0.55 + phraseScore * 0.45, 1.0, 10.0);
        }
    }
}
