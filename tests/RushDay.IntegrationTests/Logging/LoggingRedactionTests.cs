using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RushDay.Api.Endpoints;
using RushDay.Api.Startup;
using RushDay.Infrastructure.Seeding;
using RushDay.IntegrationTests.Staff;

namespace RushDay.IntegrationTests.Logging;

/// <summary>
/// 03-security.md section 6's logging rules, on a Production host (JSON console, no scopes): names, usernames,
/// passwords and raw IP addresses never reach a log line; a failed login logs only an outcome, a 12-hex-character
/// <c>usernameHash</c> and an <c>ipHash</c> (<see cref="AuthEndpoints"/>); the EF Core command-log category defaults
/// to Warning so query parameters (marks, names) cannot leak through it; the one-line-per-request log
/// (<see cref="RequestLoggingMiddleware"/>) carries the route <b>template</b>, never the raw path or query string.
/// Module <c>YG9901</c>, lecturer <c>L00028</c> (seeded, referenced by no other test). Production hosts are reached
/// over <see cref="TestClients.ProductionHttps"/>: their session and antiforgery cookies are Secure-only.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LoggingRedactionTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Failed_login_logs_hashes_not_the_plaintext_password_username_or_raw_ip()
    {
        const string forwardedFor = "203.0.113.77";
        const string wrongPassword = "Definitely-Not-The-Password-1";

        await using var production = factory.Production(b => b.ConfigureTestServices(s => s.AddFakeLogging()));
        var user = await production.ProvisionAsync();
        using var client = production.CreateCookieClient(forwardedFor, TestClients.ProductionHttps);
        await client.RefreshCsrfAsync();

        var logs = production.Services.GetFakeLogCollector();
        logs.Clear();

        using var failed = await client.PostLoginAsync(user.UserName!, wrongPassword);
        await failed.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");

        var records = logs.GetSnapshot();
        Assert.NotEmpty(records);
        foreach (var record in records)
        {
            Assert.DoesNotContain(user.UserName!, record.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(wrongPassword, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(forwardedFor, record.Message, StringComparison.Ordinal);

            foreach (var value in (record.StructuredState ?? []).Select(kv => kv.Value ?? string.Empty))
            {
                Assert.DoesNotContain(wrongPassword, value, StringComparison.Ordinal);
                Assert.DoesNotContain(forwardedFor, value, StringComparison.Ordinal);
                Assert.DoesNotContain(user.UserName!, value, StringComparison.OrdinalIgnoreCase);
            }
        }

        // The login outcome line: RushDay.Auth logs only the outcome and the two pseudonymous hashes (D32, section 6).
        var loginRecord = Assert.Single(records, r => r.Category == AuthEndpoints.LoggerCategory);
        Assert.Equal("failed", loginRecord.GetStructuredStateValue("Outcome"));

        var usernameHash = loginRecord.GetStructuredStateValue("UsernameHash");
        Assert.NotNull(usernameHash);
        Assert.Matches("^[0-9a-f]{12}$", usernameHash);

        var ipHash = loginRecord.GetStructuredStateValue("IpHash");
        Assert.NotNull(ipHash);
        Assert.NotEmpty(ipHash);
        Assert.DoesNotContain(forwardedFor, ipHash, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(ipHash, "^\\d+\\.\\d+\\.\\d+\\.\\d+$"), "ipHash looks like a raw dotted-quad address.");
    }

    /// <summary>
    /// Mirrors <c>ProductionHardeningTests</c>' style: the category is checked directly on the logger, never by
    /// capturing real query traffic (03-security.md section 6: Information is the seed's own flood of one line per
    /// statement; Warning is what a customer deployment runs with).
    /// </summary>
    [Fact]
    public async Task Ef_core_command_log_defaults_to_warning_in_production()
    {
        await using var production = factory.Production();

        var logger = production.Services.GetRequiredService<ILoggerFactory>().CreateLogger(RushDayApiFactory.CommandLogCategory);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public async Task Request_log_carries_the_route_template_not_the_raw_query_string()
    {
        const string code = "YG9901";
        const string leader = "L00028";
        const string secret = "SuperSecretSearchTerm12345";

        // A Production host issues Secure cookies only, so every client talks to it over HTTPS.
        await using var production = factory.Production(b => b.ConfigureTestServices(s => s.AddFakeLogging()));
        using var admin = await production.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword, baseAddress: TestClients.ProductionHttps);
        await admin.CreateModuleAsync(code);
        await admin.AssignAsync(code, leader);
        using var lecturer = await production.LoginAsync(leader, DemoAccounts.LecturerPassword, baseAddress: TestClients.ProductionHttps);

        var logs = production.Services.GetFakeLogCollector();
        logs.Clear();

        using var response = await lecturer.GetAsync($"/api/lecturer/modules/{code}/roster?q={Uri.EscapeDataString(secret)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var record = Assert.Single(logs.GetSnapshot(), r => r.Category == typeof(RequestLoggingMiddleware).FullName);

        var route = record.GetStructuredStateValue("route");
        Assert.NotNull(route);
        Assert.Contains("roster", route, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, route, StringComparison.Ordinal);
        Assert.DoesNotContain("?", route, StringComparison.Ordinal);

        Assert.DoesNotContain(secret, record.Message, StringComparison.Ordinal);

        Assert.NotNull(record.GetStructuredStateValue("statusCode"));
        Assert.Equal("200", record.GetStructuredStateValue("statusCode"));
        Assert.NotNull(record.GetStructuredStateValue("elapsedMs"));
        Assert.NotNull(record.GetStructuredStateValue("traceId"));
        Assert.False(string.IsNullOrEmpty(record.GetStructuredStateValue("traceId")));
    }
}
