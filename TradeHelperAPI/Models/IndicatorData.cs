// IndicatorData.cs – Stores all input values used for prediction
using System;

namespace TradeHelper.Models
{
    public class IndicatorData
    {
        public int Id { get; set; }
        public int InstrumentId { get; set; }
        public double COTScore { get; set; }
        public double RetailPositionScore { get; set; }
        public double TrendScore { get; set; }
        public double SeasonalityScore { get; set; }
        public double GDP { get; set; }
        public double CPI { get; set; }
        public double ManufacturingPMI { get; set; }
        public double ServicesPMI { get; set; }
        public double EmploymentChange { get; set; }
        public double UnemploymentRate { get; set; }
        public double InterestRate { get; set; }
        public DateTime DateCollected { get; set; }
    }
}