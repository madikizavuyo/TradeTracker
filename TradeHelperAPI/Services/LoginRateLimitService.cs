using Microsoft.Extensions.Caching.Memory;

namespace TradeHelper.Services;

/// <summary>
/// Rate limits login attempts per IP to mitigate brute-force attacks.
/// </summary>
public class LoginRateLimitService
{
    private const int MaxAttemptsPerMinute = 5;
    private const string KeyPrefix = "LoginAttempt_";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly IMemoryCache _cache;
    private readonly ILogger<LoginRateLimitService> _logger;

    public LoginRateLimitService(IMemoryCache cache, ILogger<LoginRateLimitService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Returns true if the IP is allowed to attempt login.</summary>
    public bool IsAllowed(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return true;
        var key = KeyPrefix + ipAddress;
        var count = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Window;
            return 0;
        });
        return count < MaxAttemptsPerMinute;
    }

    /// <summary>Records a failed login attempt. Call before returning Unauthorized.</summary>
    public void RecordAttempt(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return;
        var key = KeyPrefix + ipAddress;
        var count = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Window;
            return 0;
        });
        count++;
        _cache.Set(key, count, Window);
        if (count >= MaxAttemptsPerMinute)
            _logger.LogWarning("Login rate limit exceeded for IP {Ip}", ipAddress);
    }

    /// <summary>Clears attempt count on successful login so the user can log in again later.</summary>
    public void ClearAttempts(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return;
        _cache.Remove(KeyPrefix + ipAddress);
    }
}
