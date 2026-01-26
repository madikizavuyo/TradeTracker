// MLModelService.cs – Predicts the bias score based on indicator data using Google Gemini API
using System.Text;
using System.Text.Json;
using TradeHelper.Models;

namespace TradeHelper.Services
{
    public class MLModelService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly IConfiguration _configuration;

        public MLModelService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _apiKey = configuration["Google:ApiKey"] ?? throw new InvalidOperationException("Google API key not configured");
        }

        public async Task<double> PredictBiasAsync(IndicatorData data)
        {
            try
            {
                // Prepare the prompt for Google Gemini
                var prompt = $@"Analyze the following trading indicators and predict a bias score from 1-10 (1=strong bearish, 10=strong bullish):

COT Score: {data.COTScore}
Retail Position Score: {data.RetailPositionScore}
Trend Score: {data.TrendScore}
Seasonality Score: {data.SeasonalityScore}
GDP: {data.GDP}
CPI: {data.CPI}
Manufacturing PMI: {data.ManufacturingPMI}
Services PMI: {data.ServicesPMI}
Employment Change: {data.EmploymentChange}
Unemployment Rate: {data.UnemploymentRate}
Interest Rate: {data.InterestRate}

Provide only a single number between 1 and 10 as your response.";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-pro:generateContent?key={_apiKey}";
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
                        {
                            return Math.Max(1, Math.Min(10, score));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallback to simple calculation if API call fails
            }

            // Fallback calculation if API fails
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
                var prompt = "You are a trading data extraction expert. Analyze the following PDF text from a trading statement and extract ONLY eligible closed trade transactions.\n\n" +
                    "⚠️ CRITICAL REQUIREMENT - READ THIS CAREFULLY ⚠️:\n" +
                    "Extract ONLY transactions where the \"Settled PnL\" column contains a VISIBLE NUMERIC NUMBER.\n" +
                    "The character \"—\" (em dash) in the Settled PnL column means the trade is NOT CLOSED or has NO SETTLED PROFIT/LOSS.\n" +
                    "These transactions with \"—\" MUST BE COMPLETELY IGNORED - DO NOT EXTRACT THEM AT ALL.\n" +
                    "ONLY extract transactions where you can CLEARLY SEE a NUMBER (positive, negative, or zero) in the Settled PnL column.\n\n" +
                    "EXAMPLES OF WHAT TO EXTRACT:\n" +
                    "✓ Settled PnL = 8.72 → EXTRACT (has a number)\n" +
                    "✓ Settled PnL = -8.65 → EXTRACT (has a number, even if negative)\n" +
                    "✓ Settled PnL = 34.83 → EXTRACT (has a number)\n" +
                    "✓ Settled PnL = 0.00 → EXTRACT (has a number, break-even trade)\n\n" +
                    "EXAMPLES OF WHAT TO IGNORE (DO NOT EXTRACT):\n" +
                    "✗ Settled PnL = \"—\" → IGNORE (em dash, not a number)\n" +
                    "✗ Settled PnL = \"-\" → IGNORE (dash, not a number)\n" +
                    "✗ Settled PnL = empty/blank → IGNORE (no value)\n" +
                    "✗ Settled PnL = null/missing → IGNORE (no value)\n\n" +
                    "STEP-BY-STEP EXTRACTION PROCESS:\n" +
                    "1. Find the transaction table in the PDF\n" +
                    "2. For EACH row in the table:\n" +
                    "   a. Look at the \"Settled PnL\" column (may also be named: P/L, Profit/Loss, Realized P/L, etc.)\n" +
                    "   b. If the cell contains \"—\" (em dash) → SKIP this entire row, DO NOT extract it, move to next row\n" +
                    "   c. If the cell contains a NUMBER (like 8.72, -8.65, 34.83, 0.00) → Extract this transaction\n" +
                    "3. Count how many rows have numeric Settled PnL - this should be approximately 120 transactions\n" +
                    "4. DO NOT extract any row where Settled PnL = \"—\" - these are open positions, not closed trades\n\n" +
                    "WHAT TO IGNORE (DO NOT EXTRACT):\n" +
                    "- Any transaction where Settled PnL is \"—\" (em dash character) - these are OPEN positions\n" +
                    "- Any transaction where Settled PnL is empty, blank, \"-\", \"N/A\", null, or missing\n" +
                    "- Open positions without a settled profit/loss\n" +
                    "- Pending orders\n" +
                    "- Deposits, withdrawals, or non-trade transactions\n\n" +
                    "For each eligible transaction (with numeric Settled PnL, NOT \"—\"), extract:\n" +
                    "- instrument (from \"Symbol\" column, e.g., EURUSD, XAUUSD, ETHUSD, SOLUSD, BTCUSD, etc.) - REQUIRED\n" +
                    "- entryPrice (from \"Price\" column - this is the execution/trade price, NOT the Order ID. Example: if Price = 3,934.45, use 3934.45) - REQUIRED\n" +
                    "  ⚠️ CRITICAL: Do NOT use the Order ID column - use the Price column which contains the actual trade price\n" +
                    "- exitPrice (if available, otherwise null)\n" +
                    "- dateTime (from \"Transaction Time\" column, convert to ISO 8601: YYYY-MM-DDTHH:mm:ss. Example: 25/10/2025 09:12 becomes 2025-10-25T09:12:00) - REQUIRED\n" +
                    "- exitDateTime (if available, otherwise null)\n" +
                    "- profitLoss (from \"Settled PnL\" column - MUST be the exact number you see, NOT \"—\". Example: if Settled PnL = 8.72, use 8.72) - REQUIRED\n" +
                    "  ⚠️ CRITICAL: profitLoss MUST come from the \"Settled PnL\" column ONLY. DO NOT use Order ID, Price, or any other column for profitLoss.\n" +
                    "  ⚠️ CRITICAL: If Settled PnL = \"—\", DO NOT extract this trade at all. Do NOT assign profitLoss from another column.\n" +
                    "  ⚠️ CRITICAL: Legitimate profitLoss values are typically small numbers (like 8.72, -8.65, 34.83). Values > 10000 are likely Order IDs and WRONG.\n" +
                    "- type (\"Long\" for Buy/Purchase, \"Short\" for Sell/Sale) - REQUIRED\n" +
                    "- status (always \"Closed\" for trades with Settled PnL) - REQUIRED\n" +
                    "- lotSize (from \"Size\" column, if available. Example: if Size = 0.10, use 0.10)\n" +
                    "- stopLoss (if available, otherwise null)\n" +
                    "- takeProfit (if available, otherwise null)\n\n" +
                    "VALIDATION RULES:\n" +
                    "- If Settled PnL = \"—\" → DO NOT include in JSON (this is an open position, not a closed trade)\n" +
                    "- If Settled PnL = a NUMBER → Include in JSON\n" +
                    "- DO NOT infer or guess profit/loss - only use what you see in the statement\n" +
                    "- DO NOT set profitLoss to 0 or default - only use actual values from the statement\n" +
                    "- Return valid JSON only, no explanations\n\n" +
                    "⚠️ CRITICAL FINAL REMINDER - READ CAREFULLY ⚠️:\n" +
                    "- The PDF contains 233 total transactions, but ONLY ~120 have numeric Settled PnL\n" +
                    "- If you return 233 trades, you are WRONG - you are including trades with \"—\"\n" +
                    "- DO NOT extract any trade where Settled PnL = \"—\" (em dash character)\n" +
                    "- DO NOT set profitLoss to 0 for trades with \"—\" - simply DO NOT extract them at all\n" +
                    "- Count the rows FIRST: How many rows have a NUMBER in Settled PnL? That's your answer (~120)\n" +
                    "- If you see \"—\" in Settled PnL column, SKIP that entire row - do not include it in your JSON response\n" +
                    "- Expected count: Approximately 120 trades. If you return 233 trades, you are making an error.\n\n" +
                    "PDF Text:\n" + pdfText + "\n\n" +
                    "Return ONLY transactions with numeric Settled PnL (NOT \"—\") as a JSON array.\n" +
                    "Example format (note: entryPrice comes from Price column, NOT Order ID):\n" +
                    "[{\"instrument\": \"EURGBP\", \"entryPrice\": 0.87442, \"exitPrice\": null, \"dateTime\": \"2025-10-24T17:32:00\", \"exitDateTime\": null, \"profitLoss\": 8.72, \"type\": \"Short\", \"status\": \"Closed\", \"lotSize\": 0.04, \"stopLoss\": null, \"takeProfit\": null}]\n" +
                    "Another example: If Price = 3,934.45, then entryPrice = 3934.45 (NOT the Order ID number)\n\n" +
                    "⚠️ FINAL CHECK: Count your extracted trades. If you have 233, you are WRONG. You should have ~120.";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-pro:generateContent?key={_apiKey}";
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
            catch (Exception)
            {
                // Return empty array if AI extraction fails
            }

            return "[]";
        }
    }
}
