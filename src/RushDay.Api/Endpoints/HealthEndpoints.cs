using Microsoft.Extensions.Diagnostics.HealthChecks;
using RushDay.Api.Contracts;
using RushDay.Api.Security;

namespace RushDay.Api.Endpoints;

/// <summary>
/// Health (D15, 04-performance-and-ops.md section 7). <c>live</c> touches nothing and sits outside every limiter
/// (Render's health check); <c>ready</c> runs the DbContext check inside the global limiter and behind the per-address
/// <c>health-ready</c> policy, so an anonymous loop cannot hold the pool.
/// </summary>
public static class HealthEndpoints
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";

    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(LivePath, () => TypedResults.Ok(new LiveResponse(nameof(HealthStatus.Healthy))))
            .AllowAnonymous()
            .DisableRateLimiting()
            .WithName("HealthLive")
            .WithTags("Health");

        api.MapGet(ReadyPath, async (HealthCheckService health, CancellationToken cancellationToken) =>
            {
                var report = await health.CheckHealthAsync(cancellationToken);
                var body = new ReadyResponse(
                    report.Status == HealthStatus.Unhealthy ? nameof(HealthStatus.Unhealthy) : nameof(HealthStatus.Healthy),
                    [.. report.Entries.Select(e => new ReadyCheck(e.Key, e.Value.Status.ToString(), Math.Round(e.Value.Duration.TotalMilliseconds, 1)))]);
                return TypedResults.Json(body, statusCode: report.Status == HealthStatus.Unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.HealthReady)
            .WithName("HealthReady")
            .WithTags("Health");

        return api;
    }
}
