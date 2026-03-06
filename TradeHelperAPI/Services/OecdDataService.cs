// OecdDataService.cs – Fetches economic data from OECD SDMX REST API
// API docs: https://www.oecd.org/en/data/insights/data-explainers/2024/09/api.html
// Format: https://sdmx.oecd.org/public/rest/data/{agency},{dataflow},{version}/{filter}?params

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradeHelper.Services
{
    public class OecdDataService
    {
        private readonly HttpClient _client;
        private readonly ILogger<OecdDataService> _logger;
        private const string BaseUrl = "https://sdmx.oecd.org/public/rest/data";

        // Currency code -> OECD REF_AREA (ISO 3166-1 alpha-3 or OECD codes)
        private static readonly Dictionary<string, string> CurrencyToOecdArea = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = "USA",
            ["EUR"] = "EA19",  // Euro area 19
            ["GBP"] = "GBR",
            ["JPY"] = "JPN",
            ["AUD"] = "AUS",
            ["NZD"] = "NZL",
            ["CAD"] = "CAN",
            ["CHF"] = "CHE",
            ["SEK"] = "SWE",
            ["ZAR"] = "ZAF",
            ["CNY"] = "CHN",  // Chinese Yuan
        };

        public OecdDataService(HttpClient client, ILogger<OecdDataService> logger)
        {
            _client = client;
            _logger = logger;
            _client.Timeout = TimeSpan.FromSeconds(30);
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
        }

        /// <summary>Fetches unemployment rate for a currency/country. Returns latest value or null.</summary>
        public async Task<double?> FetchUnemploymentRateAsync(string currency)
        {
            if (!CurrencyToOecdArea.TryGetValue(currency, out var area))
                return null;

            // DF_IALFS_UNE_M: Monthly unemployment. Filter: REF_AREA.MEASURE.UNIT.TRANSFORMATION.ADJUSTMENT.SEX.AGE
            // UNE_M=unemployment rate, Y=seasonally adjusted, _T=total, Y_GE15=15+
            var filter = $"{area}.UNE_M._Z.Y._T.Y_GE15";
            var url = $"{BaseUrl}/OECD.SDD.TPS,DSD_LFS@DF_IALFS_UNE_M,1.0/{filter}?dimensionAtObservation=AllDimensions&format=jsondata&lastNObservations=1";

            try
            {
                var json = await _client.GetStringAsync(url);
                var val = ParseOecdJsonObservation(json);
                if (val.HasValue) return val;
                var csvUrl = url.Replace("format=jsondata", "format=csvfile");
                var csv = await _client.GetStringAsync(csvUrl);
                return ParseOecdCsvObservation(csv, area);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "OECD unemployment fetch failed for {Area}", area);
                return null;
            }
        }

        /// <summary>Fetches GDP growth (YoY) for a currency/country. Returns latest value or null.</summary>
        public async Task<double?> FetchGdpGrowthAsync(string currency)
        {
            if (!CurrencyToOecdArea.TryGetValue(currency, out var area))
                return null;

            // National accounts - GDP volume, market prices, growth rate same period previous year
            // DF_NAAG: National Accounts. May need different dataflow.
            // Try MEI (Main Economic Indicators) GDP growth
            var url = $"{BaseUrl}/OECD.SDD.STES,DSD_STES@DF_CLI/.M.LI...AA...H?dimensionAtObservation=AllDimensions&format=jsondata&lastNObservations=1";
            // Fallback: use structure query to find correct dataflow
            return null;
        }

        /// <summary>Fetches CPI inflation (YoY) for a currency/country. Returns latest value or null.</summary>
        public async Task<double?> FetchCpiInflationAsync(string currency)
        {
            if (!CurrencyToOecdArea.TryGetValue(currency, out var area))
                return null;

            // CPI - need correct dataflow. OECD has PRICES@CPICOP
            return null;
        }

        /// <summary>Batch fetch unemployment for multiple currencies.</summary>
        public async Task<Dictionary<string, double>> FetchUnemploymentBatchAsync(IEnumerable<string> currencies)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var areas = currencies
                .Where(c => CurrencyToOecdArea.ContainsKey(c))
                .Select(c => (currency: c, area: CurrencyToOecdArea[c]))
                .ToList();

            if (areas.Count == 0) return result;

            // Build filter: USA+GBR+ZAF+... for single request
            var areaList = string.Join("+", areas.Select(a => a.area));
            var filter = $"{areaList}.UNE_M._Z.Y._T.Y_GE15";
            var url = $"{BaseUrl}/OECD.SDD.TPS,DSD_LFS@DF_IALFS_UNE_M,1.0/{filter}?dimensionAtObservation=AllDimensions&format=jsondata&lastNObservations=1";

            try
            {
                var json = await _client.GetStringAsync(url);
                var values = ParseOecdJsonObservationsBySeries(json);
                if (values.Count == 0)
                {
                    var csvUrl = url.Replace("format=jsondata", "format=csvfile");
                    var csv = await _client.GetStringAsync(csvUrl);
                    values = ParseOecdCsvByArea(csv);
                }
                foreach (var (currency, area) in areas)
                {
                    if (values.TryGetValue(area, out var val))
                        result[currency] = val;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OECD unemployment batch fetch failed");
            }

            return result;
        }

        /// <summary>Parses SDMX-JSON v2. Structure: dataSets[0].series["REF_AREA:0:0:..."].observations["0"][value,attributes].</summary>
        private static double? ParseOecdJsonObservation(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("dataSets", out var dataSets) || dataSets.GetArrayLength() == 0)
                    return null;

                var ds = dataSets[0];
                if (ds.TryGetProperty("series", out var series))
                {
                    var firstSeries = series.EnumerateObject().FirstOrDefault();
                    if (firstSeries.Value.TryGetProperty("observations", out var obs))
                    {
                        var firstProp = obs.EnumerateObject().FirstOrDefault();
                        if (firstProp.Value.ValueKind == JsonValueKind.Undefined) return null;
                        var firstObsKey = firstProp.Name;
                        var arr = obs.GetProperty(firstObsKey);
                        return ExtractNumericFromObservation(arr);
                    }
                }
                if (ds.TryGetProperty("observations", out var flatObs))
                {
                    var first = flatObs.EnumerateObject().FirstOrDefault();
                    if (first.Value.ValueKind == JsonValueKind.Undefined) return null;
                    var arr = first.Value;
                    return ExtractNumericFromObservation(arr);
                }
            }
            catch { }
            return null;
        }

        /// <summary>Parses SDMX-JSON and returns REF_AREA -> value. Series keys are "REF_AREA:dim2:dim3:...".</summary>
        private static Dictionary<string, double> ParseOecdJsonObservationsBySeries(string json)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("dataSets", out var dataSets) || dataSets.GetArrayLength() == 0)
                    return result;

                var ds = dataSets[0];
                if (ds.TryGetProperty("series", out var series))
                {
                    foreach (var s in series.EnumerateObject())
                    {
                        var key = s.Name;
                        var area = key.Split(':')[0];
                        if (s.Value.TryGetProperty("observations", out var obs))
                        {
                            var firstProp = obs.EnumerateObject().FirstOrDefault();
                            if (firstProp.Value.ValueKind == JsonValueKind.Undefined) continue;
                            var arr = firstProp.Value;
                            var num = ExtractNumericFromObservation(arr);
                            if (num.HasValue)
                                result[area] = num.Value;
                        }
                    }
                }
                else if (ds.TryGetProperty("observations", out var flatObs))
                {
                    var structure = root.TryGetProperty("structure", out var st) ? (JsonElement?)st : null;
                    var refAreaIndex = GetRefAreaDimensionIndex(structure);
                    foreach (var prop in flatObs.EnumerateObject())
                    {
                        var keyParts = prop.Name.Split(':');
                        var area = refAreaIndex >= 0 && refAreaIndex < keyParts.Length ? keyParts[refAreaIndex] : keyParts[0];
                        var num = ExtractNumericFromObservation(prop.Value);
                        if (num.HasValue && !string.IsNullOrEmpty(area))
                            result[area] = num.Value;
                    }
                }
            }
            catch { }
            return result;
        }

        private static double? ExtractNumericFromObservation(JsonElement arr)
        {
            if (arr.ValueKind == JsonValueKind.Number)
                return arr.GetDouble();
            if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                var first = arr[0];
                if (first.ValueKind == JsonValueKind.Number)
                    return first.GetDouble();
                if (first.ValueKind == JsonValueKind.String && double.TryParse(first.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                    return p;
            }
            return null;
        }

        private static int GetRefAreaDimensionIndex(JsonElement? structure)
        {
            if (structure == null) return 0;
            try
            {
                if (structure.Value.TryGetProperty("dimensions", out var dimList) && dimList.ValueKind == JsonValueKind.Array)
                {
                    int idx = 0;
                    foreach (var d in dimList.EnumerateArray())
                    {
                        var id = d.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        if (id == "REF_AREA") return idx;
                        idx++;
                    }
                }
            }
            catch { }
            return 0;
        }

        /// <summary>Parse OECD CSV. Expects REF_AREA column and OBS_VALUE (or similar).</summary>
        private static double? ParseOecdCsvObservation(string csv, string area)
        {
            var lines = csv.Split('\n');
            if (lines.Length < 2) return null;
            var headers = lines[0].Split(',');
            var refIdx = Array.FindIndex(headers, h => h.Contains("REF_AREA", StringComparison.OrdinalIgnoreCase) || h.Contains("Reference", StringComparison.OrdinalIgnoreCase));
            var valIdx = Array.FindIndex(headers, h => h.Contains("OBS_VALUE", StringComparison.OrdinalIgnoreCase) || h.Contains("Value", StringComparison.OrdinalIgnoreCase) || h == "0");
            if (refIdx < 0) refIdx = 0;
            if (valIdx < 0) valIdx = headers.Length - 1;

            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length > Math.Max(refIdx, valIdx) && cols[refIdx].Trim().Equals(area, StringComparison.OrdinalIgnoreCase))
                {
                    var s = cols[valIdx].Trim().Trim('"');
                    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                        return v;
                }
            }
            return null;
        }

        private static Dictionary<string, double> ParseOecdCsvByArea(string csv)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var lines = csv.Split('\n');
            if (lines.Length < 2) return result;
            var headers = lines[0].Split(',');
            var refIdx = Array.FindIndex(headers, h => h.Contains("REF_AREA", StringComparison.OrdinalIgnoreCase));
            var valIdx = Array.FindIndex(headers, h => h.Contains("OBS_VALUE", StringComparison.OrdinalIgnoreCase));
            if (refIdx < 0) refIdx = 0;
            if (valIdx < 0) valIdx = headers.Length - 1;

            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length > Math.Max(refIdx, valIdx))
                {
                    var area = cols[refIdx].Trim().Trim('"');
                    var s = cols[valIdx].Trim().Trim('"');
                    if (!string.IsNullOrEmpty(area) && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                        result[area] = v;
                }
            }
            return result;
        }
    }
}
