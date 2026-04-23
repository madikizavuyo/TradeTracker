namespace TradeHelper.Services
{
    /// <summary>Computes RSI, SMA, EMA, MACD, Stochastic from OHLC bars (newest bar first).</summary>
    public static class OhlcTechnicalCalculator
    {
        public record OhlcBar(double Open, double High, double Low, double Close);

        public static Dictionary<string, double> Compute(IReadOnlyList<OhlcBar> bars)
        {
            var results = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["RSI"] = 50,
                ["SMA14"] = 0,
                ["SMA50"] = 0,
                ["EMA50"] = 0,
                ["EMA200"] = 0,
                ["MACD"] = 0,
                ["MACDSignal"] = 0,
                ["StochasticK"] = 50
            };
            if (bars == null || bars.Count < 14)
                return results;

            var closes = bars.Select(b => b.Close).ToList();
            results["RSI"] = RsiFromCloses(closes, 14);
            results["SMA14"] = Sma(closes, 14);
            if (closes.Count >= 50) results["SMA50"] = Sma(closes, 50);
            if (closes.Count >= 50) results["EMA50"] = Ema(closes, 50);
            if (closes.Count >= 200) results["EMA200"] = Ema(closes, 200);
            if (closes.Count > 0) results["Close"] = closes[0];

            var macd = MacdFromCloses(closes);
            if (macd.HasValue)
            {
                results["MACD"] = macd.Value.macd;
                results["MACDSignal"] = macd.Value.signal;
            }

            var stoch = StochasticFromOhlc(bars, 14);
            if (stoch.HasValue) results["StochasticK"] = stoch.Value;

            return results;
        }

        public static double RsiFromCloses(IReadOnlyList<double> closes, int period = 14)
        {
            if (closes.Count < period + 1) return 50;
            double gains = 0, losses = 0;
            for (var i = 0; i < period; i++)
            {
                var change = closes[i] - closes[i + 1];
                if (change > 0) gains += change;
                else losses -= change;
            }
            var avgGain = gains / period;
            var avgLoss = losses / period;
            if (avgLoss == 0) return 100;
            var rs = avgGain / avgLoss;
            return 100 - (100 / (1 + rs));
        }

        public static double Sma(IReadOnlyList<double> closes, int period)
        {
            if (closes.Count < period) return 0;
            double sum = 0;
            for (var i = 0; i < period; i++)
                sum += closes[i];
            return sum / period;
        }

        public static double SmaOldest(IReadOnlyList<double> closes, int period)
        {
            if (closes.Count < period) return 0;
            double sum = 0;
            for (var i = closes.Count - period; i < closes.Count; i++)
                sum += closes[i];
            return sum / period;
        }

        public static double Ema(IReadOnlyList<double> closes, int period)
        {
            if (closes.Count < period) return 0;
            var k = 2.0 / (period + 1);
            var ema = SmaOldest(closes, period);
            for (var i = closes.Count - period - 1; i >= 0; i--)
                ema = closes[i] * k + ema * (1 - k);
            return ema;
        }

        public static (double macd, double signal)? MacdFromCloses(IReadOnlyList<double> closes)
        {
            if (closes.Count < 35) return null;
            var chrono = closes.Reverse().ToList();
            var macdLine = new List<double>();
            for (var i = 25; i < chrono.Count; i++)
            {
                var slice = chrono.Take(i + 1).Reverse().ToList();
                var e12 = Ema(slice, 12);
                var e26 = Ema(slice, 26);
                macdLine.Add(e12 - e26);
            }
            if (macdLine.Count < 9) return null;
            var k9 = 2.0 / 10;
            var signalEma = macdLine.Take(9).Average();
            for (var i = 9; i < macdLine.Count; i++)
                signalEma = macdLine[i] * k9 + signalEma * (1 - k9);
            return (macdLine[^1], signalEma);
        }

        public static double? StochasticFromOhlc(IReadOnlyList<OhlcBar> bars, int period = 14)
        {
            if (bars.Count < period + 2) return null;
            var rawK = new List<double>();
            for (var i = 0; i <= bars.Count - period; i++)
            {
                var window = bars.Skip(i).Take(period).ToList();
                var high14 = window.Max(b => b.High);
                var low14 = window.Min(b => b.Low);
                var close = bars[i].Close;
                var range = high14 - low14;
                rawK.Add(range == 0 ? 50 : (close - low14) / range * 100);
            }
            if (rawK.Count < 3) return null;
            return rawK.Take(3).Average();
        }

        public static double? AtrFromOhlc(IReadOnlyList<OhlcBar> bars, int period = 14)
        {
            if (bars == null || bars.Count < period + 1) return null;

            double sumTr = 0;
            for (var i = 0; i < period; i++)
            {
                var bar = bars[i];
                var prevClose = bars[i + 1].Close;
                var trueRange = Math.Max(
                    bar.High - bar.Low,
                    Math.Max(Math.Abs(bar.High - prevClose), Math.Abs(bar.Low - prevClose)));
                sumTr += trueRange;
            }

            return sumTr / period;
        }
    }
}
