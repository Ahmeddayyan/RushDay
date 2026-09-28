using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Api.Observability;
using RushDay.Infrastructure.Persistence;

namespace RushDay.IntegrationTests.Endpoints;

/// <summary>Liveness and readiness (D15, 04-performance-and-ops.md section 7).</summary>
[Collection(ApiCollection.Name)]
public sealed class HealthEndpointTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Live_is_healthy_without_dependencies()
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.ReadJsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_reports_the_database_check()
    {
        using var client = factory.CreateCookieClient();

        using var response = await client.GetAsync("/api/health/ready");
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        var check = Assert.Single(body.GetProperty("checks").EnumerateArray());
        Assert.Equal("database", check.GetProperty("name").GetString());
        Assert.Equal("Healthy", check.GetProperty("status").GetString());
        Assert.True(check.GetProperty("durationMs").GetDouble() >= 0);
    }

    [Fact]
    public async Task Ready_is_unhealthy_when_the_database_is_unreachable()
    {
        await using var host = factory.Derive(b => b.UseSetting(
            "ConnectionStrings:RushDay", "Host=127.0.0.1;Port=1;Database=rushday_never;Username=nobody;Password=nobody;Timeout=1"));
        using var client = host.CreateCookieClient();

        using var response = await client.GetAsync("/api/health/ready");
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Db_command_counter_sees_the_commands_of_the_request()
    {
        var counter = factory.Services.GetRequiredService<DbCommandCounter>();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();

        counter.Reset();
        _ = await db.Modules.CountAsync();
        _ = await db.Students.AnyAsync();

        Assert.Equal(2, counter.Count);
    }

    [Fact]
    public async Task Metrics_snapshot_counts_requests()
    {
        var metrics = factory.Services.GetRequiredService<MetricsSnapshotService>();
        using var client = factory.CreateCookieClient();
        var before = metrics.GetSnapshot().Series[^1].Requests;

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync("/api/health/live");
        }

        // The hosting meter records just after the response has been sent.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        long after;
        do
        {
            after = metrics.GetSnapshot().Series[^1].Requests;
            if (after >= before + 3)
            {
                break;
            }

            await Task.Delay(50);
        }
        while (DateTime.UtcNow < deadline);

        Assert.True(after >= before + 3, $"Expected at least {before + 3} requests in the current minute, saw {after}.");
    }
}
