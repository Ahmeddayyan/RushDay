using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Security;

/// <summary>Load shedding and rate limits (02-api.md section 5, T8), and the request timeout (02 section 6).</summary>
[Collection(ApiCollection.Name)]
public sealed class RateLimitTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Login_per_user_window_answers_429_with_retry_after()
    {
        await using var host = factory.Derive(b => b.UseSetting("RateLimiting:LoginPerUserPerMinute", "2"));
        using var client = host.CreateCookieClient();
        await client.RefreshCsrfAsync();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await client.PostLoginAsync("S000011", DemoAccounts.StudentPassword);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            await client.RefreshCsrfAsync();
        }

        using var limited = await client.PostLoginAsync("s000011", DemoAccounts.StudentPassword);
        await limited.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate-limited");
        Assert.InRange(limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);

        // Another username is unaffected: the window is per username.
        await client.RefreshCsrfAsync();
        using var other = await client.PostLoginAsync("S000012", DemoAccounts.StudentPassword);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Global_limiter_sheds_with_503_and_retry_after()
    {
        var blocker = new BlockingHealthCheck();
        await using var host = factory.Derive(b =>
        {
            b.UseSetting("RateLimiting:MaxConcurrent", "1");
            b.UseSetting("RateLimiting:MaxQueued", "0");
            b.ConfigureTestServices(s => s.AddHealthChecks().AddCheck("blocker", blocker));
        });
        using var client = host.CreateCookieClient();

        var holding = client.GetAsync("/api/health/ready");
        try
        {
            await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

            using var shed = await client.GetAsync("/api");
            await shed.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "server-busy");
            Assert.Equal(TimeSpan.FromSeconds(1), shed.Headers.RetryAfter?.Delta);

            // Render's health check sits outside every limiter.
            using var live = await client.GetAsync("/api/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
        finally
        {
            blocker.Release.TrySetResult();
        }

        using var ready = await holding;
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Ready_probe_is_limited()
    {
        await using var host = factory.Derive(b => b.UseSetting("RateLimiting:HealthReadyPerIpPerMinute", "2"));
        using var client = host.CreateCookieClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await client.GetAsync("/api/health/ready");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await client.GetAsync("/api/health/ready");
        await limited.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "server-busy");
        Assert.Equal(TimeSpan.FromSeconds(60), limited.Headers.RetryAfter?.Delta);

        using var live = await client.GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Request_timeout_answers_503_timeout()
    {
        var blocker = new BlockingHealthCheck();
        await using var host = factory.Derive(b => b.ConfigureTestServices(s =>
        {
            s.AddHealthChecks().AddCheck("blocker", blocker);
            // The 15 s default, shortened so the test does not wait for it; policy, status and body are the app's.
            s.PostConfigure<RequestTimeoutOptions>(o => o.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromMilliseconds(300),
                TimeoutStatusCode = o.DefaultPolicy!.TimeoutStatusCode,
                WriteTimeoutResponse = o.DefaultPolicy.WriteTimeoutResponse,
            });
        }));
        using var client = host.CreateCookieClient();

        try
        {
            using var response = await client.GetAsync("/api/health/ready");
            await response.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "timeout");
        }
        finally
        {
            blocker.Release.TrySetResult();
        }
    }

    /// <summary>A readiness check that holds its request until released (or until the request is cancelled).</summary>
    private sealed class BlockingHealthCheck : IHealthCheck
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
    }
}
