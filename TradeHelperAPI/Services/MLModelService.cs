// MLModelService.cs – Predicts the bias score based on indicator data using Google Gemini API
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    public class MLModelService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MLModelService> _logger;
        private readonly IMemoryCache? _cache;

        public MLModelService(HttpClient httpClient, IConfiguration configuration, ILogger<MLModelService> logger, IMemoryCache? cache = null)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
            _apiKey = configuration["Google:ApiKey"] ?? throw new InvalidOperationException("Google API key not configured");
        }

        public async Task<double> PredictBiasAsync(IndicatorData data)
        {
            if (!_configuration.GetValue("Google:UseAIPrediction", false))
                return PredictBiasFallback(data);

            try
            {
                var prompt = $"Trading indicators → bias 1-10 (1=bearish, 10=bullish). Reply with ONE number.\nCOT:{data.COTScore} Retail:{data.RetailPositionScore} Trend:{data.TrendScore} Season:{data.SeasonalityScore} GDP:{data.GDP} CPI:{data.CPI} MfgPMI:{data.ManufacturingPMI} SvcPMI:{data.ServicesPMI} EmpChg:{data.EmploymentChange} Unemp:{data.UnemploymentRate} Rate:{data.InterestRate}";

                var model = _configuration["Google:PredictionModel"] ?? _configuration["Google:GeminiModel"] ?? "gemini-2.0-flash-lite";
                var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var responseJson = JsonDocument.Parse(responseContent);
                    var candidates = responseJson.RootElement.GetProperty("candidates");
                    if (candidates.GetArrayLength() > 0)
                    {
                        var text = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                        if (double.TryParse(text?.Trim(), out double score))
                            return Math.Max(1, Math.Min(10, score));
                    }
                }
            }
            catch (Exception) { /* fallback */ }
            return PredictBiasFallback(data);
        }

        public double PredictBias(IndicatorData data)
        {
            // Synchronous version for backward compatibility
            return PredictBiasFallback(data);
        }

        private double PredictBiasFallback(IndicatorData data)
        {
            double sentimentScore = (data.COTScore + data.RetailPositionScore) / 2;
            double technicalScore = (data.TrendScore + data.SeasonalityScore) / 2;
            double economicScore = (data.GDP + data.CPI + data.ManufacturingPMI + data.ServicesPMI - data.UnemploymentRate + data.EmploymentChange - data.InterestRate) / 7;

            double combinedScore = (sentimentScore + technicalScore + economicScore) / 3;

            return Math.Max(1, Math.Min(10, combinedScore));
        }

        public async Task<string> ExtractTradesFromPdfTextAsync(string pdfText)
        {
            try
            {
                if (pdfText.Length > 80_000)
                    pdfText = pdfText[..40_000] + "\n\n[Middle section omitted. Extract from visible text above and below.]\n\n" + pdfText[^40_000..];

                var prompt = @"Extract closed trades from the PDF. RULES: (1) ONLY rows where Settled PnL has a NUMBER (8.72, -8.65, 0.00). (2) SKIP rows where Settled PnL = ""—"" (em dash) - those are open positions. (3) entryPrice from Price column, NOT Order ID. (4) profitLoss from Settled PnL only; values >10000 are likely wrong. (5) dateTime as ISO 8601 (YYYY-MM-DDTHH:mm:ss). (6) type: Long/Short, status: Closed.
Return JSON array only. Example: [{""instrument"":""EURGBP"",""entryPrice"":0.87442,""exitPrice"":null,""dateTime"":""2025-10-24T17:32:00"",""exitDateTime"":null,""profitLoss"":8.72,""type"":""Short"",""status"":""Closed"",""lotSize"":0.04,""stopLoss"":null,""takeProfit"":null}]

PDF Text:
" + pdfText;

                var model = _configuration["Google:PdfExtractionModel"] ?? _configuration["Google:GeminiModel"] ?? "gemini-2.0-flash-lite";
                var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var responseJson = JsonDocument.Parse(responseContent);
                    var candidates = responseJson.RootElement.GetProperty("candidates");
                    if (candidates.GetArrayLength() > 0)
                    {
                        var text = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                        return text ?? "[]";
                    }
                }
            }
            catch (Exception) { /* return empty */ }
            return "[]";
        }

        /// <summary>Generates a detailed, opinionated AI analysis of an instrument's TrailBlazer score and underlying data.</summary>
        /// <param name="instrumentId">Optional. When provided with scoreDateComputed, enables caching.</param>
        /// <param name="scoreDateComputed">Optional. Used with instrumentId for cache key.</param>
        public async Task<string?> GenerateInstrumentAnalysisAsync(InstrumentAnalysisContext ctx, int? instrumentId = null, DateTime? scoreDateComputed = null)
        {
            var cacheMins = _configuration.GetValue("Google:AnalysisCacheMinutes", 60);
            if (_cache != null && instrumentId.HasValue && scoreDateComputed.HasValue && cacheMins > 0)
            {
                var key = $"analysis:{instrumentId}:{scoreDateComputed.Value:yyyyMMddHH}";
                if (_cache.TryGetValue(key, out string? cached))
                    return cached;
            }

            try
            {
                var heatmapText = string.Join("; ", ctx.HeatmapEntries.Take(12).Select(e =>
                    $"{e.Currency} {e.Indicator}:{e.Value:F1}({e.Impact})"));
                if (string.IsNullOrEmpty(heatmapText)) heatmapText = "none";

                var cotText = ctx.COTReport != null
                    ? $"Comm L:{ctx.COTReport.CommercialLong:N0} S:{ctx.COTReport.CommercialShort:N0}; NC L:{ctx.COTReport.NonCommercialLong:N0} S:{ctx.COTReport.NonCommercialShort:N0}"
                    : "none";

                var snippetCount = Math.Min(2, ctx.WebSnippets?.Count ?? 0);
                var prompt = $@"You are a forex/commodity analyst. Give a SHORT, direct analysis. MAX 100 words. No fluff.

INSTRUMENT: {ctx.InstrumentName} ({ctx.AssetClass})
Overall: {ctx.OverallScore:F1} ({ctx.Bias}) | Fundamental: {ctx.FundamentalScore:F1} | COT: {ctx.COTScore:F1} | Retail: {ctx.RetailLongPct:F0}% long | Technical: {ctx.TechnicalScore:F1}

HEATMAP: {heatmapText}
COT: {cotText}
{(snippetCount > 0 ? $"\nOUTLOOK: {string.Join(" ", ctx.WebSnippets!.Take(2).Select(s => s.Title))}" : "")}

Output format (strict):
1. One sentence: score suggests [attractive/caution/avoid] because [key reason].
2. One sentence: main risk or support (fundamentals, COT, or retail).
3. Verdict: [Attractive / Caution / Avoid].";

                var requestBody = new
                {
                    contents = new[] { new { parts = new[] { new { text = prompt } } } },
                    generationConfig = new { maxOutputTokens = 200, temperature = 0.4 }
                };
                var json = JsonSerializer.Serialize(requestBody);

                var preferredModel = _configuration["Google:AnalysisModel"] ?? _configuration["Google:GeminiModel"];
                var modelsToTry = new List<string>();
                if (!string.IsNullOrWhiteSpace(preferredModel))
                    modelsToTry.Add(preferredModel.Trim());
                modelsToTry.AddRange(new[] { "gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-2.5-flash", "gemini-1.5-flash", "gemini-pro" });

                foreach (var model in modelsToTry.Distinct())
                {
                    try
                    {
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";
                        var response = await _httpClient.PostAsync(url, content);
                        var responseContent = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            var responseJson = JsonDocument.Parse(responseContent);
                            var candidates = responseJson.RootElement.GetProperty("candidates");
                            if (candidates.GetArrayLength() > 0)
                            {
                                var text = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                                var result = text?.Trim();
                                if (!string.IsNullOrEmpty(result))
                                {
                                    _logger.LogDebug("GenerateInstrumentAnalysisAsync succeeded with model {Model}", model);
                                    if (_cache != null && instrumentId.HasValue && scoreDateComputed.HasValue && cacheMins > 0)
                                    {
                                        var key = $"analysis:{instrumentId}:{scoreDateComputed.Value:yyyyMMddHH}";
                                        _cache.Set(key, result, TimeSpan.FromMinutes(cacheMins));
                                    }
                                    return result;
                                }
                            }
                        }
                        else
                        {
                            _logger.LogDebug("Gemini model {Model} failed {StatusCode}: {Snippet}", model, response.StatusCode,
                                responseContent.Length > 200 ? responseContent[..200] + "..." : responseContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Gemini model {Model} threw", model);
                    }
                }

                _logger.LogWarning("All Gemini models failed for GenerateInstrumentAnalysisAsync");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GenerateInstrumentAnalysisAsync failed");
            }

            return null;
        }

        /// <summary>Analyzes global economic news and returns currency strength scores (1-10) per currency. Used for 80% of currency strength.</summary>
        public async Task<Dictionary<string, double>?> GenerateCurrencyStrengthFromNewsAsync(IEnumerable<string> headlineSummaries)
        {
            var headlines = headlineSummaries.Take(15).ToList();
            if (headlines.Count == 0) return null;

            var headlinesText = string.Join("\n", headlines.Select((h, i) => $"{i + 1}. {h}"));

            var prompt = $@"Analyze these global economic/news headlines and rate each major currency's strength (1=weak, 10=strong) based on how current events affect them.

HEADLINES:
{headlinesText}

Currencies to rate: USD, EUR, GBP, JPY, AUD, NZD, CAD, CHF, ZAR, CNY, SEK

Consider: geopolitical events, oil/commodity prices, inflation, trade wars, central bank policy, safe-haven flows, commodity exporter benefits (CAD, AUD, ZAR), etc.

Reply ONLY with a JSON object, no other text. Example format:
{{""USD"":6.5,""EUR"":5.2,""GBP"":5.8,""JPY"":6.0,""AUD"":5.5,""NZD"":5.3,""CAD"":6.2,""CHF"":6.8,""ZAR"":4.5,""CNY"":5.0,""SEK"":5.2}}";

            try
            {
                var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { maxOutputTokens = 300, temperature = 0.3 } };
                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var model = _configuration["Google:AnalysisModel"] ?? _configuration["Google:GeminiModel"] ?? "gemini-2.0-flash";
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode) return null;

                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonDocument.Parse(responseContent);
                var candidates = responseJson.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() == 0) return null;

                var text = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()?.Trim();
                if (string.IsNullOrEmpty(text)) return null;

                // Extract JSON (handle markdown code blocks)
                var jsonStart = text.IndexOf('{');
                var jsonEnd = text.LastIndexOf('}');
                if (jsonStart < 0 || jsonEnd <= jsonStart) return null;
                text = text.Substring(jsonStart, jsonEnd - jsonStart + 1);

                var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                using var doc = JsonDocument.Parse(text);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.TryGetDouble(out var val))
                        result[prop.Name] = Math.Clamp(val, 1.0, 10.0);
                }
                return result.Count > 0 ? result : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GenerateCurrencyStrengthFromNewsAsync failed");
                return null;
            }
        }
    }

    /// <summary>Context for AI instrument analysis.</summary>
    public record InstrumentAnalysisContext(
        string InstrumentName,
        string AssetClass,
        double OverallScore,
        string Bias,
        double FundamentalScore,
        double COTScore,
        double RetailSentimentScore,
        double RetailLongPct,
        double RetailShortPct,
        double NewsSentimentScore,
        double TechnicalScore,
        string? DataSources,
        List<HeatmapEntryForAnalysis> HeatmapEntries,
        COTReport? COTReport,
        List<WebSnippetForAnalysis>? WebSnippets = null);

    public record HeatmapEntryForAnalysis(string Currency, string Indicator, double Value, double PreviousValue, string Impact);

    public record WebSnippetForAnalysis(string Title, string Description, string? Source);
}
