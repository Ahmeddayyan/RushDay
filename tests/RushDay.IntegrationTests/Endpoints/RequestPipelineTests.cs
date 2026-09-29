using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using RushDay.Api.Startup;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Endpoints;

/// <summary>
/// The request log and the command counter (03-security.md section 6, 04-performance-and-ops.md sections 6.1 and 7).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RequestPipelineTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Request_log_records_the_status_the_client_received()
    {
        await using var probe = await ProbeApp.StartAsync(factory, configure: b => b.Services.AddFakeLogging());
        var logs = probe.Services.GetFakeLogCollector();
        using var client = probe.CreateClient();

        using (var busy = await client.GetAsync(ProbeApp.TransientPath))
        {
            await busy.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "server-busy");
        }

        using (var failed = await client.GetAsync(ProbeApp.ThrowPath))
        {
            await failed.AssertProblemAsync(HttpStatusCode.InternalServerError, "internal-error");
        }

        var lines = logs.GetSnapshot().Where(r => r.Category == typeof(RequestLoggingMiddleware).FullName).ToList();
        var transient = Assert.Single(lines, r => r.GetStructuredStateValue("route") == ProbeApp.TransientPath);
        Assert.Equal("503", transient.GetStructuredStateValue("statusCode"));
        var thrown = Assert.Single(lines, r => r.GetStructuredStateValue("route") == ProbeApp.ThrowPath);
        Assert.Equal("500", thrown.GetStructuredStateValue("statusCode"));
    }

    /// <summary>
    /// The endpoint runs one query; the request also fills a cold cache and, because the clock moved (interval 0 in
    /// tests), re-validates the session's security stamp. Only the endpoint's own query is its count.
    /// </summary>
    [Fact]
    public async Task Command_counter_counts_only_the_endpoint_queries()
    {
        await using var probe = await ProbeApp.StartAsync(factory);
        using var client = await probe.LoginAsync("S000022", DemoAccounts.StudentPassword);
        factory.Clock.Advance(TimeSpan.FromSeconds(1));

        using var response = await client.GetAsync(ProbeApp.CommandsPath);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("commands").GetInt32());
    }

    /// <summary>The cross-check S4's DashboardTests use: a derived host that logs every command at Information.</summary>
    [Fact]
    public async Task Command_log_host_captures_each_command()
    {
        await using var host = factory.DeriveWithCommandLog();
        using var client = host.CreateCookieClient();
        var logs = host.Services.GetFakeLogCollector();
        logs.Clear();

        // A cold public status fills three caches: settings, publications and windows, one command each.
        using var response = await client.GetAsync("/api/public/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, logs.GetSnapshot().Count(r => r.Category == RushDayApiFactory.CommandLogCategory && r.Level == Microsoft.Extensions.Logging.LogLevel.Information));
    }
}
