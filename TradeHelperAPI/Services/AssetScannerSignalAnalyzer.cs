namespace TradeHelper.Services
{
    /// <summary>
    /// Bias-led multi-timeframe setup logic:
    /// Daily bars provide horizontal confluence levels, while 4H bars provide Fib anchors and active trendlines.
    /// </summary>
    public static class AssetScannerSignalAnalyzer
    {
        private enum SignalDirection
        {
            Neutral,
            Bullish,
            Bearish
        }

        private sealed record AnchorSelection(
            bool IsValid,
            int StartIdx,
            int EndIdx,
            double StartPrice,
            double EndPrice,
            double Range,
            bool BearishLeg,
            bool BullishLeg,
            string Reason);

        private sealed record FibLevels(double Level382, double Level50, double Level618);

        private sealed record FibReference(string Label, double Level);

        private sealed record HorizontalLevel(int Idx, double Price, string LevelType);

        private sealed record TrendlineDefinition(
            bool IsValid,
            int StartIdx,
            int EndIdx,
            double StartPrice,
            double EndPrice,
            double Slope,
            double PriceAtCurrent,
            string TrendType,
            string Reason);

        private sealed record ConfluenceSignalResult(
            bool IsTriggered,
            bool HorizontalTriggered,
            bool TrendlineTriggered,
            bool DoubleConfluenceTriggered,
            bool CrossDistanceTriggered,
            string LevelType,
            int HorizontalIdx,
            double HorizontalPrice,
            double TrendlinePrice,
            double Buffer,
            double PercentBuffer,
            double Atr,
            double AtrBuffer,
            double HorizontalDistance,
            double TrendlineDistance,
            double LevelSeparation,
            double LevelSeparationPips,
            double MaxLevelSeparationPips,
            double PipSize,
            string Reason);

        /// <summary>Snapshot of every intermediate value used by <see cref="Analyze"/>.</summary>
        public record SignalDiagnostic(
            string Signal,
            string Detail,
            int DailyBarsAvailable,
            int FourHourBarsAvailable,
            string Bias,
            string InstrumentName,
            double OverallScore,
            double BuyThreshold,
            double SellThreshold,
            bool BuyDir,
            bool SellDir,
            int PreferredSwingLookback,
            int PreferredDailyLevelLookback,
            int PivotStrength,
            double MinLegSizePct,
            int AnchorStartIdx,
            int AnchorEndIdx,
            double AnchorStartPrice,
            double AnchorEndPrice,
            double AnchorRange,
            bool BearishLeg,
            bool BullishLeg,
            double FourHourClose,
            double FourHourAtr,
            double FibTolerancePct,
            double Fib382,
            double Fib50,
            double Fib618,
            double GoldenZoneLow,
            double GoldenZoneHigh,
            bool Fib382Touched,
            bool Fib50Touched,
            bool Fib618Touched,
            bool GoldenZoneTouched,
            bool HorizontalConfluenceTriggered,
            bool TrendlineConfluenceTriggered,
            bool DoubleConfluenceTriggered,
            string ClosestFibLabel,
            double ClosestFibLevel,
            double ClosestFibDistancePct,
            string TriggerFibLabel,
            double TriggerFibLevel,
            bool TrendlineValid,
            string TrendlineType,
            int TrendlineStartIdx,
            int TrendlineEndIdx,
            double TrendlineStartPrice,
            double TrendlineEndPrice,
            double TrendlinePriceAtCurrent,
            double TrendlineSlope,
            bool ConfluenceTriggered,
            string ConfluenceReason,
            double ConfluenceBuffer,
            double ConfluencePercentBuffer,
            double ConfluenceAtrBuffer,
            double ConfluenceMaxPips,
            double PipSize,
            string HorizontalLevelType,
            int HorizontalLevelIdx,
            double HorizontalLevelPrice,
            double HorizontalDistance,
            double TrendlineDistance,
            double LevelSeparation,
            double LevelSeparationPips);

        /// <summary>
        /// Returns the setup signal using Daily bias, Daily horizontal levels, and 4H Fib/trendline structure.
        /// </summary>
        public static (string Signal, string Detail) Analyze(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> dailyBarsNewestFirst,
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> fourHourBarsNewestFirst,
            double overallScore,
            string? bias,
            string? instrumentName = null,
            int swingLookback = 30,
            int continuationLookback = 20,
            double fibTolerancePct = 0.8,
            double continuationTolerancePct = 0.5,
            double buyThreshold = 6.0,
            double sellThreshold = 4.0,
            int pivotStrength = 2,
            double minLegSizePct = 1.2,
            double confluencePctBuffer = 0.1,
            double confluenceAtrMultiplier = 0.5,
            double maxLevelSeparationPips = 15)
        {
            var diagnostic = AnalyzeCore(
                dailyBarsNewestFirst,
                fourHourBarsNewestFirst,
                overallScore,
                bias,
                instrumentName,
                swingLookback,
                continuationLookback,
                fibTolerancePct,
                continuationTolerancePct,
                buyThreshold,
                sellThreshold,
                pivotStrength,
                minLegSizePct,
                confluencePctBuffer,
                confluenceAtrMultiplier,
                maxLevelSeparationPips);

            return (diagnostic.Signal, diagnostic.Detail);
        }

        /// <summary>Runs the analyzer and returns every intermediate value plus the resulting signal.</summary>
        public static SignalDiagnostic Diagnose(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> dailyBarsNewestFirst,
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> fourHourBarsNewestFirst,
            double overallScore,
            string? bias,
            string? instrumentName = null,
            int swingLookback = 30,
            int continuationLookback = 20,
            double fibTolerancePct = 0.8,
            double continuationTolerancePct = 0.5,
            double buyThreshold = 6.0,
            double sellThreshold = 4.0,
            int pivotStrength = 2,
            double minLegSizePct = 1.2,
            double confluencePctBuffer = 0.1,
            double confluenceAtrMultiplier = 0.5,
            double maxLevelSeparationPips = 15)
        {
            return AnalyzeCore(
                dailyBarsNewestFirst,
                fourHourBarsNewestFirst,
                overallScore,
                bias,
                instrumentName,
                swingLookback,
                continuationLookback,
                fibTolerancePct,
                continuationTolerancePct,
                buyThreshold,
                sellThreshold,
                pivotStrength,
                minLegSizePct,
                confluencePctBuffer,
                confluenceAtrMultiplier,
                maxLevelSeparationPips);
        }

        private static SignalDiagnostic AnalyzeCore(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> dailyBarsNewestFirst,
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> fourHourBarsNewestFirst,
            double overallScore,
            string? bias,
            string? instrumentName,
            int swingLookback,
            int continuationLookback,
            double fibTolerancePct,
            double continuationTolerancePct,
            double buyThreshold,
            double sellThreshold,
            int pivotStrength,
            double minLegSizePct,
            double confluencePctBuffer,
            double confluenceAtrMultiplier,
            double maxLevelSeparationPips)
        {
            var dailyCount = dailyBarsNewestFirst?.Count ?? 0;
            var fourHourCount = fourHourBarsNewestFirst?.Count ?? 0;
            var biasText = string.IsNullOrWhiteSpace(bias) ? "Unknown" : bias.Trim();
            var symbol = instrumentName?.Trim().ToUpperInvariant() ?? "";

            if (dailyBarsNewestFirst == null || dailyCount < pivotStrength * 2 + 3)
                return BuildDiagnostic("NONE", "Insufficient Daily history for horizontal level mapping.");
            if (fourHourBarsNewestFirst == null || fourHourCount < pivotStrength * 2 + 6)
                return BuildDiagnostic("NONE", "Insufficient 4H history for Fib/trendline structure mapping.");

            var direction = ResolveDirection(biasText, overallScore, buyThreshold, sellThreshold);
            var buyDir = direction == SignalDirection.Bullish;
            var sellDir = direction == SignalDirection.Bearish;
            if (direction == SignalDirection.Neutral)
                return BuildDiagnostic("WATCH", $"Bias {biasText} is neutral, so no directional 4H setup is active.");

            var anchor = direction == SignalDirection.Bullish
                ? FindBullishAnchors(fourHourBarsNewestFirst, pivotStrength, swingLookback, minLegSizePct)
                : FindBearishAnchors(fourHourBarsNewestFirst, pivotStrength, swingLookback, minLegSizePct);

            var close4h = fourHourBarsNewestFirst[0].Close;
            var atr = OhlcTechnicalCalculator.AtrFromOhlc(fourHourBarsNewestFirst, 14) ?? 0;
            var pipSize = ResolvePipSize(symbol, close4h);

            if (!anchor.IsValid)
            {
                var waitDetail = direction == SignalDirection.Bullish
                    ? $"Directional BUY from bias {biasText}; awaiting a valid 4H swing low followed by a meaningful rally. {anchor.Reason}"
                    : $"Directional SELL from bias {biasText}; awaiting a valid 4H swing high followed by a meaningful drop. {anchor.Reason}";
                return BuildDiagnostic(buyDir ? "BUY" : "SELL", waitDetail, buyDir: buyDir, sellDir: sellDir, close4h: close4h, atr: atr, pipSize: pipSize);
            }

            var fibs = BuildFibLevels(anchor);
            var fib382Touched = IsLevelTouched(fourHourBarsNewestFirst[0], fibs.Level382, fibTolerancePct);
            var fib50Touched = IsLevelTouched(fourHourBarsNewestFirst[0], fibs.Level50, fibTolerancePct);
            var fib618Touched = IsLevelTouched(fourHourBarsNewestFirst[0], fibs.Level618, fibTolerancePct);
            var goldenZoneLow = Math.Min(fibs.Level50, fibs.Level618);
            var goldenZoneHigh = Math.Max(fibs.Level50, fibs.Level618);
            var goldenZoneTouched = IsZoneTouched(fourHourBarsNewestFirst[0], goldenZoneLow, goldenZoneHigh);
            var triggerFib = SelectTriggerFib(goldenZoneTouched, goldenZoneLow, goldenZoneHigh);
            var closestFib = FindClosestFib(close4h, fibs);
            var closestFibDistancePct = closestFib.Level > 0
                ? Math.Abs(close4h - closestFib.Level) / closestFib.Level * 100.0
                : 0;

            var horizontalLevels = GetHorizontalLevels(dailyBarsNewestFirst, direction, pivotStrength, continuationLookback);
            var trendline = BuildActiveTrendline(fourHourBarsNewestFirst, direction, pivotStrength, swingLookback);
            var confluence = EvaluateConfluenceSignal(
                close4h,
                horizontalLevels,
                trendline,
                atr,
                confluencePctBuffer,
                confluenceAtrMultiplier,
                maxLevelSeparationPips,
                pipSize);

            var baseDetail = direction == SignalDirection.Bullish
                ? $"4H bullish leg {anchor.StartPrice:F4}->{anchor.EndPrice:F4}; current 4H close {close4h:F4}; closest Fib {closestFib.Label} at {closestFib.Level:F4} ({closestFibDistancePct:F2}% away)."
                : $"4H bearish leg {anchor.StartPrice:F4}->{anchor.EndPrice:F4}; current 4H close {close4h:F4}; closest Fib {closestFib.Label} at {closestFib.Level:F4} ({closestFibDistancePct:F2}% away).";

            if (triggerFib != null && confluence.DoubleConfluenceTriggered)
            {
                var signal = direction == SignalDirection.Bullish ? "BUY NOW" : "SELL NOW";
                var detail = direction == SignalDirection.Bullish
                    ? $"BUY NOW: bias {biasText}, 4H price is in the {triggerFib.Label} and both Daily support plus 4H trendline are aligned near {confluence.HorizontalPrice:F4} / {confluence.TrendlinePrice:F4}. {baseDetail}"
                    : $"SELL NOW: bias {biasText}, 4H price is in the {triggerFib.Label} and both Daily resistance plus 4H trendline are aligned near {confluence.HorizontalPrice:F4} / {confluence.TrendlinePrice:F4}. {baseDetail}";

                return BuildDiagnostic(
                    signal,
                    detail,
                    buyDir,
                    sellDir,
                    anchor,
                    close4h,
                    atr,
                    fibTolerancePct,
                    fibs,
                    fib382Touched,
                    fib50Touched,
                    fib618Touched,
                    goldenZoneLow,
                    goldenZoneHigh,
                    goldenZoneTouched,
                    confluence.HorizontalTriggered,
                    confluence.TrendlineTriggered,
                    confluence.DoubleConfluenceTriggered,
                    closestFib.Label,
                    closestFib.Level,
                    closestFibDistancePct,
                    triggerFib.Label,
                    triggerFib.Level,
                    trendline,
                    confluence,
                    pipSize);
            }

            if (confluence.DoubleConfluenceTriggered)
            {
                var signal = direction == SignalDirection.Bullish ? "DOUBLE_CONFLUENCE_BUY" : "DOUBLE_CONFLUENCE_SELL";
                var detail = direction == SignalDirection.Bullish
                    ? $"Double Confluence BUY: bias {biasText}, Daily support and 4H trendline are both active near {confluence.HorizontalPrice:F4}/{confluence.TrendlinePrice:F4}, but price is not yet inside the 50%-61.8% golden zone. {baseDetail}"
                    : $"Double Confluence SELL: bias {biasText}, Daily resistance and 4H trendline are both active near {confluence.HorizontalPrice:F4}/{confluence.TrendlinePrice:F4}, but price is not yet inside the 50%-61.8% golden zone. {baseDetail}";

                return BuildDiagnostic(
                    signal,
                    detail,
                    buyDir,
                    sellDir,
                    anchor,
                    close4h,
                    atr,
                    fibTolerancePct,
                    fibs,
                    fib382Touched,
                    fib50Touched,
                    fib618Touched,
                    goldenZoneLow,
                    goldenZoneHigh,
                    goldenZoneTouched,
                    confluence.HorizontalTriggered,
                    confluence.TrendlineTriggered,
                    confluence.DoubleConfluenceTriggered,
                    closestFib.Label,
                    closestFib.Level,
                    closestFibDistancePct,
                    "",
                    0,
                    trendline,
                    confluence,
                    pipSize);
            }

            if (goldenZoneTouched)
            {
                var signal = direction == SignalDirection.Bullish ? "GOLDEN_ZONE_BUY" : "GOLDEN_ZONE_SELL";
                var confluenceText = confluence.HorizontalTriggered
                    ? direction == SignalDirection.Bullish
                        ? $"Daily support near {confluence.HorizontalPrice:F4} is active, but the 4H trendline is not aligned yet."
                        : $"Daily resistance near {confluence.HorizontalPrice:F4} is active, but the 4H trendline is not aligned yet."
                    : confluence.TrendlineTriggered
                        ? direction == SignalDirection.Bullish
                            ? $"The 4H trendline near {confluence.TrendlinePrice:F4} is active, but Daily support is not aligned yet."
                            : $"The 4H trendline near {confluence.TrendlinePrice:F4} is active, but Daily resistance is not aligned yet."
                        : "No supporting horizontal/trendline confluence is confirmed yet.";
                var detail = direction == SignalDirection.Bullish
                    ? $"Golden Zone BUY: bias {biasText}, 4H price is inside the 50%-61.8% Fib zone ({goldenZoneLow:F4}-{goldenZoneHigh:F4}). {confluenceText} {baseDetail}"
                    : $"Golden Zone SELL: bias {biasText}, 4H price is inside the 50%-61.8% Fib zone ({goldenZoneLow:F4}-{goldenZoneHigh:F4}). {confluenceText} {baseDetail}";

                return BuildDiagnostic(
                    signal,
                    detail,
                    buyDir,
                    sellDir,
                    anchor,
                    close4h,
                    atr,
                    fibTolerancePct,
                    fibs,
                    fib382Touched,
                    fib50Touched,
                    fib618Touched,
                    goldenZoneLow,
                    goldenZoneHigh,
                    goldenZoneTouched,
                    confluence.HorizontalTriggered,
                    confluence.TrendlineTriggered,
                    confluence.DoubleConfluenceTriggered,
                    closestFib.Label,
                    closestFib.Level,
                    closestFibDistancePct,
                    triggerFib.Label,
                    triggerFib.Level,
                    trendline,
                    confluence,
                    pipSize);
            }

            if (confluence.HorizontalTriggered)
            {
                var signal = direction == SignalDirection.Bullish ? "HORIZONTAL_CONFLUENCE_BUY" : "HORIZONTAL_CONFLUENCE_SELL";
                var detail = direction == SignalDirection.Bullish
                    ? $"Horizontal Confluence BUY: bias {biasText}, price is respecting Daily support near {confluence.HorizontalPrice:F4}, but the 4H trendline is not aligned yet. {baseDetail}"
                    : $"Horizontal Confluence SELL: bias {biasText}, price is respecting Daily resistance near {confluence.HorizontalPrice:F4}, but the 4H trendline is not aligned yet. {baseDetail}";

                return BuildDiagnostic(
                    signal,
                    detail,
                    buyDir,
                    sellDir,
                    anchor,
                    close4h,
                    atr,
                    fibTolerancePct,
                    fibs,
                    fib382Touched,
                    fib50Touched,
                    fib618Touched,
                    goldenZoneLow,
                    goldenZoneHigh,
                    goldenZoneTouched,
                    confluence.HorizontalTriggered,
                    confluence.TrendlineTriggered,
                    confluence.DoubleConfluenceTriggered,
                    closestFib.Label,
                    closestFib.Level,
                    closestFibDistancePct,
                    "",
                    0,
                    trendline,
                    confluence,
                    pipSize);
            }

            if (confluence.TrendlineTriggered)
            {
                var signal = direction == SignalDirection.Bullish ? "TRENDLINE_CONFLUENCE_BUY" : "TRENDLINE_CONFLUENCE_SELL";
                var detail = direction == SignalDirection.Bullish
                    ? $"Trendline Confluence BUY: bias {biasText}, price is respecting the active 4H trendline near {confluence.TrendlinePrice:F4}, but Daily support is not aligned yet. {baseDetail}"
                    : $"Trendline Confluence SELL: bias {biasText}, price is respecting the active 4H trendline near {confluence.TrendlinePrice:F4}, but Daily resistance is not aligned yet. {baseDetail}";

                return BuildDiagnostic(
                    signal,
                    detail,
                    buyDir,
                    sellDir,
                    anchor,
                    close4h,
                    atr,
                    fibTolerancePct,
                    fibs,
                    fib382Touched,
                    fib50Touched,
                    fib618Touched,
                    goldenZoneLow,
                    goldenZoneHigh,
                    goldenZoneTouched,
                    confluence.HorizontalTriggered,
                    confluence.TrendlineTriggered,
                    confluence.DoubleConfluenceTriggered,
                    closestFib.Label,
                    closestFib.Level,
                    closestFibDistancePct,
                    "",
                    0,
                    trendline,
                    confluence,
                    pipSize);
            }

            var confluenceDetail = confluence.DoubleConfluenceTriggered
                ? $"Double confluence is active ({confluence.LevelType} {confluence.HorizontalPrice:F4} + trendline {confluence.TrendlinePrice:F4}), but the golden zone has not been reached."
                : confluence.HorizontalTriggered
                    ? $"Only horizontal {confluence.LevelType.ToLowerInvariant()} is active near {confluence.HorizontalPrice:F4}."
                    : confluence.TrendlineTriggered
                        ? $"Only trendline confluence is active near {confluence.TrendlinePrice:F4}."
                        : $"Confluence not confirmed yet. {confluence.Reason}";

            var waitForTrigger = goldenZoneTouched
                ? $"{baseDetail} Price is in the golden zone, but the horizontal/trendline confluence check failed. {confluence.Reason}"
                : $"{baseDetail} Waiting for the live 4H price to enter the 50%-61.8% golden zone. {confluenceDetail}";

            return BuildDiagnostic(
                buyDir ? "BUY" : "SELL",
                $"{(buyDir ? "Directional BUY" : "Directional SELL")} from bias {biasText}; {waitForTrigger}",
                buyDir,
                sellDir,
                anchor,
                close4h,
                atr,
                fibTolerancePct,
                fibs,
                fib382Touched,
                fib50Touched,
                fib618Touched,
                goldenZoneLow,
                goldenZoneHigh,
                goldenZoneTouched,
                confluence.HorizontalTriggered,
                confluence.TrendlineTriggered,
                confluence.DoubleConfluenceTriggered,
                closestFib.Label,
                closestFib.Level,
                closestFibDistancePct,
                triggerFib?.Label ?? "",
                triggerFib?.Level ?? 0,
                trendline,
                confluence,
                pipSize);

            SignalDiagnostic BuildDiagnostic(
                string signal,
                string detail,
                bool buyDir = false,
                bool sellDir = false,
                AnchorSelection? anchor = null,
                double close4h = 0,
                double atr = 0,
                double fibTol = 0,
                FibLevels? fibs = null,
                bool fib382TouchedLocal = false,
                bool fib50TouchedLocal = false,
                bool fib618TouchedLocal = false,
                double goldenZoneLowLocal = 0,
                double goldenZoneHighLocal = 0,
                bool goldenZoneTouchedLocal = false,
                bool horizontalConfluenceTriggeredLocal = false,
                bool trendlineConfluenceTriggeredLocal = false,
                bool doubleConfluenceTriggeredLocal = false,
                string closestFibLabelLocal = "",
                double closestFibLevelLocal = 0,
                double closestFibDistancePctLocal = 0,
                string triggerFibLabelLocal = "",
                double triggerFibLevelLocal = 0,
                TrendlineDefinition? trendline = null,
                ConfluenceSignalResult? confluence = null,
                double pipSize = 0)
            {
                return new SignalDiagnostic(
                    signal,
                    detail,
                    dailyCount,
                    fourHourCount,
                    biasText,
                    symbol,
                    overallScore,
                    buyThreshold,
                    sellThreshold,
                    buyDir,
                    sellDir,
                    swingLookback,
                    continuationLookback,
                    pivotStrength,
                    minLegSizePct,
                    anchor?.StartIdx ?? -1,
                    anchor?.EndIdx ?? -1,
                    anchor?.StartPrice ?? 0,
                    anchor?.EndPrice ?? 0,
                    anchor?.Range ?? 0,
                    anchor?.BearishLeg ?? false,
                    anchor?.BullishLeg ?? false,
                    close4h,
                    atr,
                    fibTol,
                    fibs?.Level382 ?? 0,
                    fibs?.Level50 ?? 0,
                    fibs?.Level618 ?? 0,
                    goldenZoneLowLocal,
                    goldenZoneHighLocal,
                    fib382TouchedLocal,
                    fib50TouchedLocal,
                    fib618TouchedLocal,
                    goldenZoneTouchedLocal,
                    horizontalConfluenceTriggeredLocal,
                    trendlineConfluenceTriggeredLocal,
                    doubleConfluenceTriggeredLocal,
                    closestFibLabelLocal,
                    closestFibLevelLocal,
                    closestFibDistancePctLocal,
                    triggerFibLabelLocal,
                    triggerFibLevelLocal,
                    trendline?.IsValid ?? false,
                    trendline?.TrendType ?? "",
                    trendline?.StartIdx ?? -1,
                    trendline?.EndIdx ?? -1,
                    trendline?.StartPrice ?? 0,
                    trendline?.EndPrice ?? 0,
                    trendline?.PriceAtCurrent ?? 0,
                    trendline?.Slope ?? 0,
                    confluence?.IsTriggered ?? false,
                    confluence?.Reason ?? "",
                    confluence?.Buffer ?? 0,
                    confluence?.PercentBuffer ?? 0,
                    confluence?.AtrBuffer ?? 0,
                    confluence?.MaxLevelSeparationPips ?? 0,
                    pipSize,
                    confluence?.LevelType ?? "",
                    confluence?.HorizontalIdx ?? -1,
                    confluence?.HorizontalPrice ?? 0,
                    confluence?.HorizontalDistance ?? 0,
                    confluence?.TrendlineDistance ?? 0,
                    confluence?.LevelSeparation ?? 0,
                    confluence?.LevelSeparationPips ?? 0);
            }
        }

        private static SignalDirection ResolveDirection(string bias, double overallScore, double buyThreshold, double sellThreshold)
        {
            if (string.Equals(bias, "Bullish", StringComparison.OrdinalIgnoreCase))
                return SignalDirection.Bullish;
            if (string.Equals(bias, "Bearish", StringComparison.OrdinalIgnoreCase))
                return SignalDirection.Bearish;
            if (overallScore > buyThreshold)
                return SignalDirection.Bullish;
            if (overallScore < sellThreshold)
                return SignalDirection.Bearish;
            return SignalDirection.Neutral;
        }

        private static AnchorSelection FindBearishAnchors(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> bars,
            int pivotStrength,
            int preferredLookback,
            double minLegSizePct)
        {
            var swingHighs = FindSwingHighs(bars, pivotStrength);
            if (swingHighs.Count == 0)
                return new AnchorSelection(false, -1, -1, 0, 0, 0, true, false, "No confirmed 4H swing high found.");

            foreach (var pivot in OrderByRecentFirstWithPreferredWindow(swingHighs.Select(h => h.Idx), preferredLookback))
            {
                var high = swingHighs.First(h => h.Idx == pivot).High;
                var low = double.MaxValue;
                var lowIdx = -1;
                for (var i = pivot - 1; i >= 0; i--)
                {
                    if (bars[i].Low < low)
                    {
                        low = bars[i].Low;
                        lowIdx = i;
                    }
                }

                if (lowIdx < 0 || low <= 0 || high <= low) continue;
                var legPct = (high - low) / high * 100.0;
                if (legPct < minLegSizePct || pivot - lowIdx < 2) continue;

                return new AnchorSelection(true, pivot, lowIdx, high, low, high - low, true, false, "");
            }

            return new AnchorSelection(false, -1, -1, 0, 0, 0, true, false, "No bearish 4H leg had enough separation or magnitude.");
        }

        private static AnchorSelection FindBullishAnchors(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> bars,
            int pivotStrength,
            int preferredLookback,
            double minLegSizePct)
        {
            var swingLows = FindSwingLows(bars, pivotStrength);
            if (swingLows.Count == 0)
                return new AnchorSelection(false, -1, -1, 0, 0, 0, false, true, "No confirmed 4H swing low found.");

            foreach (var pivot in OrderByRecentFirstWithPreferredWindow(swingLows.Select(l => l.Idx), preferredLookback))
            {
                var low = swingLows.First(l => l.Idx == pivot).Low;
                var high = 0.0;
                var highIdx = -1;
                for (var i = pivot - 1; i >= 0; i--)
                {
                    if (bars[i].High > high)
                    {
                        high = bars[i].High;
                        highIdx = i;
                    }
                }

                if (highIdx < 0 || low <= 0 || high <= low) continue;
                var legPct = (high - low) / low * 100.0;
                if (legPct < minLegSizePct || pivot - highIdx < 2) continue;

                return new AnchorSelection(true, pivot, highIdx, low, high, high - low, false, true, "");
            }

            return new AnchorSelection(false, -1, -1, 0, 0, 0, false, true, "No bullish 4H leg had enough separation or magnitude.");
        }

        private static FibLevels BuildFibLevels(AnchorSelection anchor)
        {
            if (anchor.BearishLeg)
            {
                return new FibLevels(
                    anchor.EndPrice + anchor.Range * 0.382,
                    anchor.EndPrice + anchor.Range * 0.500,
                    anchor.EndPrice + anchor.Range * 0.618);
            }

            return new FibLevels(
                anchor.EndPrice - anchor.Range * 0.382,
                anchor.EndPrice - anchor.Range * 0.500,
                anchor.EndPrice - anchor.Range * 0.618);
        }

        private static FibReference? SelectTriggerFib(bool goldenZoneTouched, double goldenZoneLow, double goldenZoneHigh)
        {
            if (!goldenZoneTouched) return null;
            return new FibReference("Golden Zone (50%-61.8%)", (goldenZoneLow + goldenZoneHigh) / 2.0);
        }

        private static FibReference FindClosestFib(double price, FibLevels fibs)
        {
            var levels = new[]
            {
                new FibReference("38.2%", fibs.Level382),
                new FibReference("50%", fibs.Level50),
                new FibReference("61.8%", fibs.Level618)
            };

            return levels.OrderBy(l => Math.Abs(price - l.Level)).First();
        }

        private static List<HorizontalLevel> GetHorizontalLevels(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> dailyBars,
            SignalDirection direction,
            int pivotStrength,
            int preferredLookback)
        {
            if (direction == SignalDirection.Bullish)
            {
                return FindSwingLows(dailyBars, pivotStrength)
                    .Where(l => l.Idx > 0)
                    .OrderBy(l => l.Idx <= preferredLookback ? 0 : 1)
                    .ThenBy(l => l.Idx)
                    .Take(12)
                    .Select(l => new HorizontalLevel(l.Idx, l.Low, "Support"))
                    .ToList();
            }

            return FindSwingHighs(dailyBars, pivotStrength)
                .Where(h => h.Idx > 0)
                .OrderBy(h => h.Idx <= preferredLookback ? 0 : 1)
                .ThenBy(h => h.Idx)
                .Take(12)
                .Select(h => new HorizontalLevel(h.Idx, h.High, "Resistance"))
                .ToList();
        }

        private static TrendlineDefinition BuildActiveTrendline(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> bars,
            SignalDirection direction,
            int pivotStrength,
            int preferredLookback)
        {
            return direction == SignalDirection.Bullish
                ? BuildBullishTrendline(bars, pivotStrength, preferredLookback)
                : BuildBearishTrendline(bars, pivotStrength, preferredLookback);
        }

        private static TrendlineDefinition BuildBullishTrendline(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> bars,
            int pivotStrength,
            int preferredLookback)
        {
            var lows = FindSwingLows(bars, pivotStrength)
                .OrderBy(l => l.Idx <= preferredLookback ? 0 : 1)
                .ThenBy(l => l.Idx)
                .ToList();

            for (var i = 0; i < lows.Count - 1; i++)
            {
                for (var j = i + 1; j < lows.Count; j++)
                {
                    var recent = lows[i];
                    var older = lows[j];
                    if (recent.Idx >= older.Idx || recent.Low <= older.Low) continue;

                    var slope = (recent.Low - older.Low) / (double)(recent.Idx - older.Idx);
                    var priceAtCurrent = recent.Low + slope * (0 - recent.Idx);
                    if (priceAtCurrent <= 0) continue;

                    return new TrendlineDefinition(true, older.Idx, recent.Idx, older.Low, recent.Low, slope, priceAtCurrent, "Ascending", "");
                }
            }

            return new TrendlineDefinition(false, -1, -1, 0, 0, 0, 0, "Ascending", "No valid ascending 4H trendline from recent swing lows.");
        }

        private static TrendlineDefinition BuildBearishTrendline(
            IReadOnlyList<OhlcTechnicalCalculator.OhlcBar> bars,
            int pivotStrength,
            int preferredLookback)
        {
            var highs = FindSwingHighs(bars, pivotStrength)
                .OrderBy(h => h.Idx <= preferredLookback ? 0 : 1)
                .ThenBy(h => h.Idx)
                .ToList();

            for (var i = 0; i < highs.Count - 1; i++)
            {
                for (var j = i + 1; j < highs.Count; j++)
                {
                    var recent = highs[i];
                    var older = highs[j];
                    if (recent.Idx >= older.Idx || recent.High >= older.High) continue;

                    var slope = (recent.High - older.High) / (double)(recent.Idx - older.Idx);
                    var priceAtCurrent = recent.High + slope * (0 - recent.Idx);
                    if (priceAtCurrent <= 0) continue;

                    return new TrendlineDefinition(true, older.Idx, recent.Idx, older.High, recent.High, slope, priceAtCurrent, "Descending", "");
                }
            }

            return new TrendlineDefinition(false, -1, -1, 0, 0, 0, 0, "Descending", "No valid descending 4H trendline from recent swing highs.");
        }

        private static ConfluenceSignalResult EvaluateConfluenceSignal(
            double currentPrice,
            IReadOnlyList<HorizontalLevel> horizontalLevels,
            TrendlineDefinition trendline,
            double atr,
            double pctBuffer,
            double atrMultiplier,
            double maxLevelSeparationPips,
            double pipSize)
        {
            if (!trendline.IsValid)
            {
                return new ConfluenceSignalResult(false, false, false, false, false, "", -1, 0, 0, 0, 0, atr, atr * atrMultiplier, 0, 0, 0, 0, maxLevelSeparationPips, pipSize, trendline.Reason);
            }

            if (horizontalLevels == null || horizontalLevels.Count == 0)
            {
                return new ConfluenceSignalResult(false, false, false, false, false, "", -1, 0, trendline.PriceAtCurrent, 0, 0, atr, atr * atrMultiplier, 0, 0, 0, 0, maxLevelSeparationPips, pipSize, "No recent horizontal swing levels available.");
            }

            var nearest = horizontalLevels
                .OrderBy(h => Math.Abs(currentPrice - h.Price))
                .First();

            var percentBuffer = currentPrice * (pctBuffer / 100.0);
            var atrBuffer = atr * atrMultiplier;
            var buffer = Math.Max(percentBuffer, atrBuffer);
            var horizontalDistance = Math.Abs(currentPrice - nearest.Price);
            var trendlineDistance = Math.Abs(currentPrice - trendline.PriceAtCurrent);
            var levelSeparation = Math.Abs(nearest.Price - trendline.PriceAtCurrent);
            var levelSeparationPips = pipSize > 0 ? levelSeparation / pipSize : 0;

            var condTrendline = trendlineDistance <= buffer;
            var condHorizontal = horizontalDistance <= buffer;
            var condCross = pipSize > 0
                ? levelSeparationPips <= maxLevelSeparationPips
                : levelSeparation <= buffer;

            var doubleConfluence = condTrendline && condHorizontal && condCross;
            var singleConfluence = condTrendline || condHorizontal;
            var reason = doubleConfluence
                ? "Current price is inside the horizontal/trendline double-confluence zone."
                : singleConfluence
                    ? $"Single confluence active: horizontal hit={condHorizontal}, trendline hit={condTrendline}, cross distance hit={condCross}."
                    : $"Trendline hit={condTrendline}, horizontal hit={condHorizontal}, cross distance hit={condCross}.";

            return new ConfluenceSignalResult(
                doubleConfluence,
                condHorizontal,
                condTrendline,
                doubleConfluence,
                condCross,
                nearest.LevelType,
                nearest.Idx,
                nearest.Price,
                trendline.PriceAtCurrent,
                buffer,
                percentBuffer,
                atr,
                atrBuffer,
                horizontalDistance,
                trendlineDistance,
                levelSeparation,
                levelSeparationPips,
                maxLevelSeparationPips,
                pipSize,
                reason);
        }

        private static double ResolvePipSize(string instrumentName, double currentPrice)
        {
            if (!string.IsNullOrWhiteSpace(instrumentName))
            {
                if (instrumentName.Length == 6 && instrumentName.All(char.IsLetter))
                    return instrumentName.EndsWith("JPY", StringComparison.OrdinalIgnoreCase) ? 0.01 : 0.0001;

                if (instrumentName.StartsWith("XAU", StringComparison.OrdinalIgnoreCase) ||
                    instrumentName.StartsWith("XAG", StringComparison.OrdinalIgnoreCase) ||
                    instrumentName.StartsWith("XPT", StringComparison.OrdinalIgnoreCase) ||
                    instrumentName.StartsWith("XPD", StringComparison.OrdinalIgnoreCase))
                    return 0.1;

                if (instrumentName == "BTC" || instrumentName == "ETH" || instrumentName == "SOL")
                    return currentPrice >= 1000 ? 1.0 : 0.1;

                if (instrumentName.StartsWith("US", StringComparison.OrdinalIgnoreCase) ||
                    instrumentName == "DE40" || instrumentName == "UK100" || instrumentName == "JP225")
                    return 1.0;
            }

            if (currentPrice >= 1000) return 1.0;
            if (currentPrice >= 100) return 0.1;
            if (currentPrice >= 10) return 0.01;
            if (currentPrice >= 1) return 0.0001;
            return 0.0001;
        }

        private static IEnumerable<int> OrderByRecentFirstWithPreferredWindow(IEnumerable<int> indices, int preferredLookback)
        {
            return indices
                .Distinct()
                .OrderBy(i => i <= preferredLookback ? 0 : 1)
                .ThenBy(i => i);
        }

        private static bool IsLevelTouched(OhlcTechnicalCalculator.OhlcBar bar, double level, double tolerancePct)
        {
            if (level <= 0) return false;
            return IsNearPct(bar.Close, level, tolerancePct)
                || IsNearPct(bar.High, level, tolerancePct)
                || IsNearPct(bar.Low, level, tolerancePct)
                || (bar.Low <= level && bar.High >= level);
        }

        private static bool IsZoneTouched(OhlcTechnicalCalculator.OhlcBar bar, double zoneLow, double zoneHigh)
        {
            if (zoneLow <= 0 || zoneHigh <= 0 || zoneHigh < zoneLow) return false;
            return (bar.Low <= zoneHigh && bar.High >= zoneLow)
                || (bar.Close >= zoneLow && bar.Close <= zoneHigh);
        }

        private static bool IsNearPct(double value, double level, double tolerancePct)
        {
            if (level <= 0 || tolerancePct < 0) return false;
            return Math.Abs(value - level) / level * 100.0 <= tolerancePct;
        }

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
    }
}
