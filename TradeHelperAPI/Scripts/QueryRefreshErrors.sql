-- Query ApplicationLogs for refresh-related errors and warnings
-- Run against production database to diagnose TrailBlazer refresh issues
-- Adjust @HoursBack as needed (default: last 7 days)

DECLARE @HoursBack INT = 168;  -- 7 days
DECLARE @Cutoff DATETIME2 = DATEADD(HOUR, -@HoursBack, GETUTCDATE());

-- 1. Errors and Warnings (refresh-related categories)
SELECT 
    Id,
    Timestamp,
    Level,
    Category,
    LEFT(Message, 200) AS MessagePreview,
    LEFT(Exception, 300) AS ExceptionPreview
FROM ApplicationLogs
WHERE Timestamp >= @Cutoff
  AND Level IN ('Error', 'Warning')
  AND (
      Category LIKE '%TrailBlazer%'
      OR Category LIKE '%TwelveData%'
      OR Category LIKE '%FmpService%'
      OR Category LIKE '%MarketStack%'
      OR Category LIKE '%Eodhd%'
      OR Category LIKE '%iTick%'
      OR Category LIKE '%NasdaqDataLink%'
      OR Category LIKE '%ApiRateLimit%'
      OR Category LIKE '%MLModel%'
      OR Message LIKE '%refresh%'
      OR Message LIKE '%TrailBlazer%'
      OR Message LIKE '%blocked%'
      OR Message LIKE '%rate limit%'
      OR Message LIKE '%429%'
      OR Message LIKE '%failed%'
      OR Message LIKE '%error%'
  )
ORDER BY Timestamp DESC;

-- 2. Error count by category (last 7 days)
SELECT 
    Category,
    Level,
    COUNT(*) AS Count
FROM ApplicationLogs
WHERE Timestamp >= @Cutoff
  AND Level IN ('Error', 'Warning')
GROUP BY Category, Level
ORDER BY Count DESC;

-- 3. Most recent 50 log entries (any level) from TrailBlazer/DataService
SELECT TOP 50
    Id,
    Timestamp,
    Level,
    Category,
    LEFT(Message, 150) AS MessagePreview
FROM ApplicationLogs
WHERE Timestamp >= @Cutoff
  AND (Category LIKE '%TrailBlazer%' OR Category LIKE '%TrailBlazerDataService%')
ORDER BY Timestamp DESC;
