namespace TradeHelper.Services;

/// <summary>Thread-safe progress state for TrailBlazer refresh. Updated by background service, polled by API.</summary>
public class TrailBlazerRefreshProgressService
{
    private volatile RefreshProgress _progress = new() { Status = "idle" };
    private readonly object _lock = new();

    public RefreshProgress GetProgress()
    {
        lock (_lock)
        {
            return _progress with { };
        }
    }

    public void SetProgress(RefreshProgress progress)
    {
        lock (_lock)
        {
            _progress = progress;
        }
    }

    public void SetIdle() => SetProgress(new RefreshProgress { Status = "idle" });
    public void SetRunning(string step, string message, int current = 0, int total = 0) =>
        SetProgress(new RefreshProgress { Status = "running", Step = step, Message = message, Current = current, Total = total });
    public void SetCompleted(int total) =>
        SetProgress(new RefreshProgress { Status = "completed", Step = "done", Message = "Refresh completed", Current = total, Total = total, CompletedAt = DateTime.UtcNow });
    public void SetFailed(string error) =>
        SetProgress(new RefreshProgress { Status = "failed", Message = error, CompletedAt = DateTime.UtcNow });
}

public record RefreshProgress
{
    public string Status { get; init; } = "idle"; // idle | running | completed | failed
    public string? Step { get; init; }
    public string? Message { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Error { get; init; }
    public double Percent => Total > 0 ? Math.Min(100, 100.0 * Current / Total) : 0;
}
