// WebScraperService.cs – Retrieves data from APIs and scrapes technical summaries
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Net.Http.Headers;
using TradeHelper.Data;
using TradeHelper.Models;
using EconomicDataFetcher.Models;

namespace TradeHelper.Services
{
    public class EconomicData
    {
        public double GDP { get; set; }
        public double CPI { get; set; }
        public double ManufacturingPMI { get; set; }
        public double ServicesPMI { get; set; }
        public double EmploymentChange { get; set; }
        public double UnemploymentRate { get; set; }
        public double InterestRate { get; set; }
    }

    public class WebScraperService
    {
        private readonly HttpClient _client;

        public WebScraperService(HttpClient client)
        {
            _client = client;
        }

        public async Task<double> GetRetailSentimentAsync(string symbol)
        {
            try
            {
                var email = "madikizavuyo@gmail.com";
                var password = "dCP7WkV!T+cMd.2"; // Replace with your actual password
                var url = $"https://www.myfxbook.com/api/login.json?email={email}&password={password}";
                var response1 = await _client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response1);
                var session = doc.RootElement.GetProperty("session").GetString();
                if (session == null) return 50.0;
                
                var response = await _client.GetStringAsync("https://www.myfxbook.com/api/get-community-outlook.json?session=" + session);
                var json = JObject.Parse(response);
                var symbols = json["symbols"] as JArray;
                if (symbols == null) return 50.0;
                
                var pair = symbols.FirstOrDefault(x => x?["symbol"]?.ToString() == symbol);
                if (pair != null && pair["shortPercentage"] != null) 
                {
                    var shortPercentage = pair["shortPercentage"]!.ToString();
                    if (!string.IsNullOrEmpty(shortPercentage))
                        return double.Parse(shortPercentage);
                }
            }
            catch { }
            return 50.0;
        }

        public async Task<double> GetTechnicalScoreAsync(string symbol)
        {
            try
            {
                var html = await _client.GetStringAsync($"https://www.tradingview.com/symbols/{symbol}/technicals/");
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var node = doc.DocumentNode.SelectSingleNode("//div[contains(text(),'Summary')]/following-sibling::div");
                if (node != null)
                {
                    if (node.InnerText.Contains("Buy")) return 8.0;
                    if (node.InnerText.Contains("Sell")) return 2.0;
                }
            }
            catch { }
            return 5.0;
        }


        public async Task<EconomicData> GetEconomicDataAsync(string countryName)
        {
            // Map country name to country code (simplified - you may want to expand this)
            var countryCode = GetCountryCode(countryName);
            
            var indicators = await FetchIndicatorsForCountryAsync(countryCode, countryName);
            
            return new EconomicData
            {
                GDP = ParseDoubleValue(indicators.FirstOrDefault(i => i.IndicatorName == "GDP")?.Value),
                CPI = ParseDoubleValue(indicators.FirstOrDefault(i => i.IndicatorName == "CPI")?.Value),
                ManufacturingPMI = ParseDoubleValue(indicators.FirstOrDefault(i => i.IndicatorName == "Manufacturing PMI")?.Value),
                ServicesPMI = ParseDoubleValue(indicators.FirstOrDefault(i => i.IndicatorName == "Services PMI")?.Value),
                EmploymentChange = ParseDoubleValue(indicators.FirstOrDefault(i => i.IndicatorName == "Employment Change")?.Value),
                UnemploymentRate = ParseDoubleValue(indicators.FirstOrDefault(i => i.IndicatorName == "Unemployment Rate")?.Value),
                InterestRate = ParseDoubleValue(indicators.FirstOrDefault(i => i.IndicatorName == "Interest Rate")?.Value)
            };
        }

        private string GetCountryCode(string countryName)
        {
            // Map common country names to World Bank country codes
            return countryName.ToLower() switch
            {
                "united states" or "usa" or "us" => "US",
                "united kingdom" or "uk" => "GB",
                "canada" => "CA",
                "australia" => "AU",
                "japan" => "JP",
                "germany" => "DE",
                "france" => "FR",
                "italy" => "IT",
                "spain" => "ES",
                "china" => "CN",
                "india" => "IN",
                "brazil" => "BR",
                "russia" => "RU",
                "south africa" => "ZA",
                "mexico" => "MX",
                _ => "US" // Default to US if not found
            };
        }

        private double ParseDoubleValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "N/A")
                return 0.0;
            
            if (double.TryParse(value, out double result))
                return result;
            
            return 0.0;
        }

        public async Task<List<EconomicIndicator>> FetchIndicatorsForCountryAsync(string countryCode, string countryName)
        {
            var data = new List<EconomicIndicator>();

            // GDP (World Bank)
            data.Add(await FetchWorldBankAsync(countryCode, "NY.GDP.MKTP.CD", "GDP", countryName));

            // CPI (World Bank)
            data.Add(await FetchWorldBankAsync(countryCode, "FP.CPI.TOTL.ZG", "CPI", countryName));

            // Unemployment (World Bank)
            data.Add(await FetchWorldBankAsync(countryCode, "SL.UEM.TOTL.ZS", "Unemployment Rate", countryName));

            // Manufacturing PMI (OECD)
            data.Add(await FetchOECDAsync("MANEMP", "Manufacturing PMI", countryName));

            // Services PMI (OECD)
            data.Add(await FetchOECDAsync("SVCEMP", "Services PMI", countryName));

            // Employment Change (OECD)
            data.Add(await FetchOECDAsync("EMPL", "Employment Change", countryName));

            // Interest Rate (IMF)
            data.Add(await FetchIMFAsync(countryCode, "FIDSR_IX", "Interest Rate", countryName));

            return data;
        }

        private async Task<EconomicIndicator> FetchWorldBankAsync(string countryCode, string indicatorCode, string name, string countryName)
        {
            var url = $"https://api.worldbank.org/v2/country/{countryCode}/indicator/{indicatorCode}?format=json&per_page=1";
            var res = await _client.GetStringAsync(url);
            var doc = JsonDocument.Parse(res);

            var value = "N/A";
            try
            {
                value = doc.RootElement[1][0].GetProperty("value").ToString();
            }
            catch { }

            return new EconomicIndicator
            {
                IndicatorName = name,
                Value = value,
                Source = "World Bank",
                Country = countryName
            };
        }

               private async Task<EconomicIndicator> FetchOECDAsync(string subjectCode, string name, string countryName)
        {
            var url = $"https://stats.oecd.org/SDMX-JSON/data/DP_LIVE/.{subjectCode}.TOT.A/OECD?contentType=json";
            var res = await _client.GetStringAsync(url);
            var doc = JsonDocument.Parse(res);

            string value = "N/A";
            try
            {
                var dataPoints = doc.RootElement.GetProperty("dataSets")[0].GetProperty("series").EnumerateObject();
                foreach (var point in dataPoints)
                {
                    var obs = point.Value.GetProperty("observations");
                    foreach (var obsEntry in obs.EnumerateObject())
                    {
                        value = obsEntry.Value[0].ToString();
                        break;
                    }
                    break;
                }
            }
            catch { }

            return new EconomicIndicator
            {
                IndicatorName = name,
                Value = value,
                Source = "OECD",
                Country = countryName
            };
        }

        private async Task<EconomicIndicator> FetchIMFAsync(string countryCode, string indicatorCode, string name, string countryName)
        {
            var url = $"https://dataservices.imf.org/REST/SDMX_JSON.svc/CompactData/IFS/M.{countryCode}.{indicatorCode}?startPeriod=2023&endPeriod=2024";
            var res = await _client.GetStringAsync(url);
            var doc = JsonDocument.Parse(res);

            string value = "N/A";
            try
            {
                var data = doc.RootElement
                    .GetProperty("CompactData")
                    .GetProperty("DataSet")
                    .GetProperty("Series")
                    .GetProperty("Obs")[0]
                    .GetProperty("@OBS_VALUE")
                    .ToString();

                value = data;
            }
            catch { }

            return new EconomicIndicator
            {
                IndicatorName = name,
                Value = value,
                Source = "IMF",
                Country = countryName
            };
        }
    }
}