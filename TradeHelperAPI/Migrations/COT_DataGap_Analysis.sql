-- COT Data Gap Analysis for Production Database
-- Run this against your production DB to find instruments missing COT data.
-- COTReports.Symbol must match Instruments.Name for TrailBlazer to use COT.

-- 1. Instruments that have NO COT data in COTReports
SELECT i.Id, i.Name, i.AssetClass, i.Type
FROM Instruments i
LEFT JOIN (
    SELECT DISTINCT Symbol FROM COTReports
) cot ON cot.Symbol = i.Name
WHERE cot.Symbol IS NULL
ORDER BY i.AssetClass, i.Name;

-- 2. Instruments that HAVE COT data (with latest report date)
SELECT i.Name, i.AssetClass, MAX(c.ReportDate) AS LatestReportDate
FROM Instruments i
INNER JOIN COTReports c ON c.Symbol = i.Name
GROUP BY i.Name, i.AssetClass
ORDER BY i.AssetClass, i.Name;

-- 3. COT symbols in DB that don't match any Instrument (orphaned)
SELECT DISTINCT c.Symbol
FROM COTReports c
LEFT JOIN Instruments i ON i.Name = c.Symbol
WHERE i.Id IS NULL
ORDER BY c.Symbol;

-- 4. Summary counts
SELECT
    (SELECT COUNT(*) FROM Instruments) AS TotalInstruments,
    (SELECT COUNT(DISTINCT Symbol) FROM COTReports) AS UniqueCOTSymbols,
    (SELECT COUNT(*) FROM Instruments i WHERE EXISTS (SELECT 1 FROM COTReports c WHERE c.Symbol = i.Name)) AS InstrumentsWithCOT,
    (SELECT COUNT(*) FROM Instruments i WHERE NOT EXISTS (SELECT 1 FROM COTReports c WHERE c.Symbol = i.Name)) AS InstrumentsWithoutCOT;
