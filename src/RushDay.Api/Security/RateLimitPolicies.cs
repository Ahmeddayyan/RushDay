using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using RushDay.Api.Auth;
using RushDay.Api.Observability;
using RushDay.Api.Options;

namespace RushDay.Api.Security;

/// <summary>
/// The limiter set of 02-api.md section 5 (T8). A global concurrency limiter covers every <c>/api</c> route except
/// <c>/api/health/live</c> and <c>/api/admin/ops/metrics</c> and sheds with 503 <c>server-busy</c>; the named
/// policies answer 429 <c>rate-limited</c> (the readiness probe answers 503, "busy" rather than "rate-limited").
/// Every rejection carries <c>Retry-After</c> in whole seconds and increments <c>rushday.load_shed.rejected{policy}</c>.
/// The per-username and failed-login windows and the CPU guard live in <see cref="LoginThrottle"/>.
/// </summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string PasswordChange = "password-change";
    public const string Enrol = "enrol";
    public const string Write = "write";
    public const string HealthReady = "health-ready";
    public const string OpsMetrics = "ops-metrics";

    /// <summary>Paths outside the global concurrency limiter, so Render's probe and the ops page answer while everything else is shed.</summary>
    public static readonly IReadOnlyList<string> GlobalExemptPaths = ["/api/health/live", "/api/admin/ops/metrics"];

    public static void Configure(RateLimiterOptions options, RateLimitingOptions limits, RushDayMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(metrics);

        options.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            IsGloballyLimited(context.Request.Path)
                ? RateLimitPartition.GetConcurrencyLimiter("api", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = Math.Max(1, limits.MaxConcurrent),
                    QueueLimit = Math.Max(0, limits.MaxQueued),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                })
                : RateLimitPartition.GetNoLimiter("exempt"));

        // Used for the global limiter only: every named policy below brings its own OnRejected.
        options.OnRejected = (context, _) =>
        {
            metrics.LoadShed(RushDayMetrics.ShedPolicies.Api);
            return new ValueTask(ProblemResults.WriteAsync(context.HttpContext, StatusCodes.Status503ServiceUnavailable, ProblemTypes.ServerBusy, retryAfterSeconds: 1));
        };

        options.AddPolicy(Login, new Policy(
            context => RateLimitPartition.GetSlidingWindowLimiter(ClientKey(context), _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, limits.LoginPerIpPerMinute),
                Window = TimeSpan.FromSeconds(60),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }),
            Rejected(metrics, RushDayMetrics.ShedPolicies.Login, StatusCodes.Status429TooManyRequests, ProblemTypes.RateLimited, TimeSpan.FromSeconds(10))));

        options.AddPolicy(PasswordChange, new Policy(
            context => RateLimitPartition.GetFixedWindowLimiter(UserKey(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, limits.PasswordChangePerUserPerMinute),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }),
            Rejected(metrics, RushDayMetrics.ShedPolicies.Login, StatusCodes.Status429TooManyRequests, ProblemTypes.RateLimited, TimeSpan.FromSeconds(60))));

        options.AddPolicy(Enrol, new Policy(
            context => RateLimitPartition.GetTokenBucketLimiter(UserKey(context), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, limits.EnrolPerUserPer10s),
                TokensPerPeriod = Math.Max(1, limits.EnrolPerUserPer10s),
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                QueueLimit = 0,
            }),
            Rejected(metrics, RushDayMetrics.ShedPolicies.Enrol, StatusCodes.Status429TooManyRequests, ProblemTypes.RateLimited, TimeSpan.FromSeconds(10))));

        options.AddPolicy(Write, new Policy(
            context => RateLimitPartition.GetTokenBucketLimiter(UserKey(context), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, limits.WritePerUserPerMinute),
                TokensPerPeriod = Math.Max(1, limits.WritePerUserPerMinute),
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }),
            Rejected(metrics, RushDayMetrics.ShedPolicies.Write, StatusCodes.Status429TooManyRequests, ProblemTypes.RateLimited, TimeSpan.FromSeconds(60))));

        options.AddPolicy(HealthReady, new Policy(
            context => RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, limits.HealthReadyPerIpPerMinute),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }),
            Rejected(metrics, RushDayMetrics.ShedPolicies.HealthReady, StatusCodes.Status503ServiceUnavailable, ProblemTypes.ServerBusy, TimeSpan.FromSeconds(60), fixedRetryAfter: true)));

        options.AddPolicy(OpsMetrics, new Policy(
            context => RateLimitPartition.GetTokenBucketLimiter(UserKey(context), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, limits.OpsMetricsPer2s),
                TokensPerPeriod = Math.Max(1, limits.OpsMetricsPer2s),
                ReplenishmentPeriod = TimeSpan.FromSeconds(2),
                QueueLimit = 0,
            }),
            Rejected(metrics, RushDayMetrics.ShedPolicies.OpsMetrics, StatusCodes.Status429TooManyRequests, ProblemTypes.RateLimited, TimeSpan.FromSeconds(2))));
    }

    public static bool IsGloballyLimited(PathString path) =>
        path.StartsWithSegments("/api") && !GlobalExemptPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The client address after forwarded headers, an IPv4-mapped IPv6 address in its IPv4 form ("unknown" when the
    /// server has none). Used only where the full address matters: the keyed <c>ipHash</c> of logs and audit rows.
    /// </summary>
    public static string ClientIp(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var address = context.Connection.RemoteIpAddress;
        return address is null ? "unknown" : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    /// <summary>
    /// The key of every per-address limit (the <c>login</c> and <c>health-ready</c> policies, the per-address
    /// failed-login window, anonymous callers of the per-user policies) and of the "distinct addresses" lockout rule:
    /// IPv4 as is (an IPv4-mapped IPv6 address becomes its IPv4 form), native IPv6 truncated to its /64, because one
    /// subscriber or host routinely holds a whole /64 and could otherwise present a new address on every request.
    /// </summary>
    public static string ClientKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ClientKey(context.Connection.RemoteIpAddress);
    }

    /// <inheritdoc cref="ClientKey(HttpContext)"/>
    public static string ClientKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    private static string UserKey(HttpContext context) =>
        context.User.FindFirst(RushDayClaims.Subject)?.Value is { Length: > 0 } sub ? "user:" + sub : "ip:" + ClientKey(context);

    private static Func<OnRejectedContext, CancellationToken, ValueTask> Rejected(
        RushDayMetrics metrics,
        string policyTag,
        int status,
        string slug,
        TimeSpan fallbackRetryAfter,
        bool fixedRetryAfter = false) =>
        (context, _) =>
        {
            metrics.LoadShed(policyTag);
            var seconds = fixedRetryAfter
                ? (int)fallbackRetryAfter.TotalSeconds
                : LoginThrottle.RetryAfter(context.Lease, fallbackRetryAfter);
            return new ValueTask(ProblemResults.WriteAsync(context.HttpContext, status, slug, retryAfterSeconds: seconds));
        };

    private sealed class Policy(Func<HttpContext, RateLimitPartition<string>> partitioner, Func<OnRejectedContext, CancellationToken, ValueTask> onRejected)
        : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => onRejected;

        public RateLimitPartition<string> GetPartition(HttpContext httpContext) => partitioner(httpContext);
    }
}
