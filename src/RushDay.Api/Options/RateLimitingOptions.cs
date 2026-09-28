namespace RushDay.Api.Options;

/// <summary>
/// Limiter numbers (02-api.md section 5), bound from "RateLimiting" so Render can tune them by environment variable.
/// The defaults are the Production values; appsettings.Development.json relaxes them so k6 can sign in hundreds of
/// students from one machine.
/// </summary>
public sealed record RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Global concurrency limiter on /api: permits.</summary>
    public int MaxConcurrent { get; init; } = 24;

    /// <summary>Global concurrency limiter on /api: queue length (oldest first).</summary>
    public int MaxQueued { get; init; } = 96;

    /// <summary>Named policy <c>login</c>: per client IP, sliding 60 s window.</summary>
    public int LoginPerIpPerMinute { get; init; } = 600;

    /// <summary><c>LoginThrottle</c>: per normalised username, sliding 60 s window.</summary>
    public int LoginPerUserPerMinute { get; init; } = 10;

    /// <summary><c>LoginThrottle</c>: failed outcomes per client IP, sliding 10 min window.</summary>
    public int LoginFailuresPerIpPer10Minutes { get; init; } = 20;

    /// <summary>Identity lockout is triggered only when an account's recent failures come from this many addresses.</summary>
    public int LockoutDistinctIps { get; init; } = 3;

    /// <summary>CPU guard around PBKDF2: concurrent permits.</summary>
    public int LoginConcurrency { get; init; } = 8;

    /// <summary>CPU guard around PBKDF2: queue length.</summary>
    public int LoginQueue { get; init; } = 64;

    public int PasswordChangePerUserPerMinute { get; init; } = 5;

    public int EnrolPerUserPer10s { get; init; } = 5;

    public int WritePerUserPerMinute { get; init; } = 120;

    public int HealthReadyPerIpPerMinute { get; init; } = 30;

    public int OpsMetricsPer2s { get; init; } = 1;
}
