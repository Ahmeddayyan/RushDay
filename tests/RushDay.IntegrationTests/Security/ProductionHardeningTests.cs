using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Npgsql;
using RushDay.Infrastructure.Persistence;

namespace RushDay.IntegrationTests.Security;

/// <summary>Production-only behaviour from the S2 review: headers on errors, logging, error detail, proxy trust (03 sections 3, 4 and 6).</summary>
[Collection(ApiCollection.Name)]
public sealed class ProductionHardeningTests(RushDayApiFactory factory)
{
    /// <summary>The exception handler clears the response headers before it writes; HSTS must survive that too.</summary>
    [Fact]
    public async Task Production_error_responses_carry_hsts_and_the_security_headers()
    {
        await using var probe = await ProbeApp.StartAsync(factory, environment: Environments.Production);
        using var client = probe.CreateClient(TestClients.ProductionHttps);

        using (var failed = await client.GetAsync(ProbeApp.ThrowPath))
        {
            await failed.AssertProblemAsync(HttpStatusCode.InternalServerError, "internal-error");
            AssertHardened(failed);
        }

        using var busy = await client.GetAsync(ProbeApp.TransientPath);
        await busy.AssertProblemAsync(HttpStatusCode.ServiceUnavailable, "server-busy");
        AssertHardened(busy);
    }

    /// <summary>
    /// The hosting scope carries the raw request path; with scopes on, every JSON line would repeat it. Asserted on the
    /// formatter's options: capturing the process's console output would interfere with every other test.
    /// </summary>
    [Fact]
    public async Task Production_json_logs_carry_no_scopes()
    {
        await using var production = factory.Production();

        var json = production.Services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue;

        Assert.False(json.IncludeScopes);
        Assert.True(production.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RushDay.Startup").IsEnabled(LogLevel.Information));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Include_error_detail_is_honoured_only_in_development(bool development, bool expected)
    {
        var connectionString = factory.ConnectionString + ";Include Error Detail=true";
        await using var host = development
            ? factory.Derive(b => b.UseSetting("ConnectionStrings:RushDay", connectionString))
            : factory.Production(b => b.UseSetting("ConnectionStrings:RushDay", connectionString));

        await using var scope = host.Services.CreateAsyncScope();
        var effective = new NpgsqlConnectionStringBuilder(scope.ServiceProvider.GetRequiredService<RushDayDbContext>().Database.GetConnectionString());

        Assert.Equal(expected, effective.IncludeErrorDetail);
    }

    [Fact]
    public async Task Production_warns_when_forwarded_headers_are_not_trusted()
    {
        await using var untrusted = factory.Production(b =>
        {
            b.UseSetting("Security:TrustForwardedHeaders", "false");
            b.ConfigureTestServices(s => s.AddFakeLogging());
        });

        var warnings = untrusted.Services.GetFakeLogCollector().GetSnapshot().Where(r => r.Level == LogLevel.Warning).Select(r => r.Message);

        Assert.Contains(warnings, m => m.Contains("Security__TrustForwardedHeaders is false", StringComparison.Ordinal));
    }

    private static void AssertHardened(HttpResponseMessage response)
    {
        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
