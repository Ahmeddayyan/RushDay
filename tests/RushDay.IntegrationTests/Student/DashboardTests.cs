using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using RushDay.Api.Observability;
using RushDay.Domain.Grades;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Seeding;
using Xunit.Abstractions;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// <c>GET /api/me/dashboard</c> (02-api.md section 8.3): the shape, including <c>completed</c> and <c>currentSemester</c>,
/// and the five set-based queries of 04-performance-and-ops.md section 3, measured by <c>DbCommandCounter</c> into
/// <c>rushday.dashboard.queries</c> and cross-checked against EF's command log (section 6.1).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DashboardTests(RushDayApiFactory factory, ITestOutputHelper output)
{
    private static readonly string[] TopLevel =
    [
        "studentNumber", "fullName", "programme", "yearOfStudy", "academicYear", "currentSemester", "modules", "completed",
        "timetable", "results", "weightedAverage", "classification", "credits", "nextPublication", "latestPublication",
        "enrolmentWindows", "announcements",
    ];

    [Fact]
    public async Task Dashboard_shape_for_the_demo_student()
    {
        using var client = await factory.LoginStudentAsync(DemoAccounts.StudentUsername);
        var body = await client.GetJsonAsync("/api/me/dashboard");

        Assert.Equal(TopLevel.Order(), body.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("S000001", body.GetProperty("studentNumber").GetString());
        Assert.Equal(1, body.GetProperty("yearOfStudy").GetInt32());
        Assert.Equal(StudentData.CurrentYear, body.GetProperty("academicYear").GetString());
        Assert.Equal("autumn", body.GetProperty("currentSemester").GetString());

        // This year's modules: the demo cohort's CS3001, withdrawable until the autumn deadline.
        var cs3001 = Assert.Single(body.GetProperty("modules").EnumerateArray(), m => m.GetProperty("code").GetString() == "CS3001");
        Assert.Equal("autumn", cs3001.GetProperty("semester").GetString());
        Assert.Equal(StudentData.CurrentYear, cs3001.GetProperty("academicYear").GetString());
        Assert.True(cs3001.GetProperty("canWithdraw").GetBoolean());
        Assert.Equal(JsonValueKind.Null, cs3001.GetProperty("withdrawBlockedReason").ValueKind);
        Assert.Equal("2026-10-30T17:00:00.000Z", cs3001.GetProperty("withdrawalDeadlineAt").GetString());
        Assert.Equal(9, cs3001.EnumerateObject().Count());

        // Earlier years' active enrolments are completed modules with their visible mark and band.
        var completed = body.GetProperty("completed").EnumerateArray().ToList();
        Assert.Equal(4, completed.Count);
        Assert.All(completed, c =>
        {
            Assert.Equal(StudentData.PreviousYear, c.GetProperty("academicYear").GetString());
            Assert.Equal("mark", c.GetProperty("outcome").GetString());
            var mark = c.GetProperty("mark").GetInt32();
            Assert.Equal(Classification.Band(mark), c.GetProperty("band").GetString());
        });
        Assert.DoesNotContain(completed, c => c.GetProperty("code").GetString() == "CS3001");

        // Visible results of every year, with their year and instant.
        var results = body.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(4, results.Count);
        Assert.All(results, r =>
        {
            Assert.Equal(StudentData.PreviousYear, r.GetProperty("academicYear").GetString());
            Assert.Equal("autumn", r.GetProperty("semester").GetString());
            Assert.Equal("2026-01-26T09:00:00.000Z", r.GetProperty("publishedAt").GetString());
            Assert.Equal(JsonValueKind.Null, r.GetProperty("correctedAt").ValueKind);
            Assert.Equal(9, r.EnumerateObject().Count());
        });
        Assert.Equal(completed.Select(c => c.GetProperty("code").GetString()).Order(), results.Select(r => r.GetProperty("moduleCode").GetString()).Order());
        Assert.True(body.GetProperty("weightedAverage").GetDouble() > 0);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("classification").GetString()));

        // The whole week of this year's current-semester modules: CS3001 only, lecture and lab.
        var timetable = body.GetProperty("timetable").EnumerateArray().ToList();
        Assert.NotEmpty(timetable);
        Assert.All(timetable, t =>
        {
            Assert.Equal("CS3001", t.GetProperty("moduleCode").GetString());
            Assert.Matches("^[0-2][0-9]:[0-5][0-9]$", t.GetProperty("startTime").GetString()!);
            Assert.Contains(t.GetProperty("day").GetString(), new[] { "monday", "tuesday", "wednesday", "thursday", "friday" });
            Assert.Equal(t.GetProperty("room").GetString()!.Contains("-Lab", StringComparison.Ordinal) ? "lab" : "lecture", t.GetProperty("kind").GetString());
        });

        var credits = body.GetProperty("credits");
        Assert.Equal(15, credits.GetProperty("autumn").GetInt32());
        Assert.Equal(0, credits.GetProperty("spring").GetInt32());
        Assert.Equal(60, credits.GetProperty("limit").GetInt32());

        // The seeded 2025/26 autumn publication is live in the tests (D24); nothing is scheduled.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextPublication").ValueKind);
        var latest = body.GetProperty("latestPublication");
        Assert.Equal("live", latest.GetProperty("state").GetString());
        Assert.Equal(StudentData.PreviousYear, latest.GetProperty("academicYear").GetString());

        var windows = body.GetProperty("enrolmentWindows").EnumerateArray().ToList();
        Assert.Equal(["autumn", "spring"], windows.Select(w => w.GetProperty("semester").GetString()));
        Assert.All(windows, w => Assert.Equal("open", w.GetProperty("state").GetString()));

        var announcements = body.GetProperty("announcements").EnumerateArray().ToList();
        Assert.InRange(announcements.Count, 1, 5);
        Assert.All(announcements, a => Assert.True(a.GetProperty("scope").GetString() == "university" || a.GetProperty("moduleCode").GetString() == "CS3001"));
    }

    /// <summary>
    /// The metric is 5 on a plain request, on one whose session re-checks its security stamp (the clock moved and the
    /// test interval is 0), and on one that fills every cache it reads: the counter starts after authorization and
    /// skips cache fills, so neither is charged to the dashboard.
    /// </summary>
    [Fact]
    public async Task Dashboard_runs_exactly_five_queries()
    {
        var metrics = factory.Services.GetRequiredService<RushDayMetrics>();
        using var collector = new MetricCollector<int>(metrics.DashboardQueries);
        using var client = await factory.LoginStudentAsync("S000019");

        await AssertFiveAsync(client, collector, "plain");

        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        await AssertFiveAsync(client, collector, "with a security-stamp re-check");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SettingsCache>().InvalidateAsync();
            await scope.ServiceProvider.GetRequiredService<EnrolmentWindowCache>().InvalidateAsync();
            await scope.ServiceProvider.GetRequiredService<PublicationCache>().InvalidateAsync();
        }

        await AssertFiveAsync(client, collector, "while filling the settings, windows and publications caches");

        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PublicationCache>().InvalidateAsync();
        }

        await AssertFiveAsync(client, collector, "with a stamp re-check and a cache fill together");

        // The count does not depend on the rows: a student with no module this year runs the same five.
        using var other = await factory.LoginStudentAsync("S000020");
        await AssertFiveAsync(other, collector, "for a student with no modules this year");
    }

    /// <summary>The log cross-check: exactly five commands for a warm call made without moving the clock.</summary>
    [Fact]
    public async Task Dashboard_commands_are_five_in_the_command_log()
    {
        await using var host = factory.DeriveWithCommandLog();
        var logs = host.Services.GetFakeLogCollector();
        using var collector = new MetricCollector<int>(host.Services.GetRequiredService<RushDayMetrics>().DashboardQueries);
        using var client = await host.LoginStudentAsync(DemoAccounts.StudentUsername);

        // Warm-up: fills the host's caches.
        using (var warm = await client.GetAsync("/api/me/dashboard"))
        {
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        }

        logs.Clear();
        using var response = await client.GetAsync("/api/me/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var commands = logs.GetSnapshot().Where(r => r.Category == RushDayApiFactory.CommandLogCategory && r.Level == LogLevel.Information).ToList();
        output.WriteLine(string.Join("\n---\n", commands.Select(c => c.Message)));
        Assert.True(commands.Count == 5, $"Expected 5 commands, got {commands.Count}:\n{string.Join("\n---\n", commands.Select(c => c.Message))}");
        Assert.Equal(5, collector.LastMeasurement!.Value);
    }

    /// <summary><c>withdrawBlockedReason</c> names the first failing condition: results, then (after the deadline) deadline.</summary>
    [Fact]
    public async Task Withdraw_blocked_reasons_are_reported()
    {
        // S000004 is in the demo CS3001 cohort; a submitted mark blocks self-withdrawal.
        await factory.PutGradeAsync("S000004", "CS3001", GradeStatus.Submitted, 64);
        using (var client = await factory.LoginStudentAsync("S000004"))
        {
            var cs3001 = (await client.GetJsonAsync("/api/me/dashboard")).GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == "CS3001");
            Assert.False(cs3001.GetProperty("canWithdraw").GetBoolean());
            Assert.Equal("results", cs3001.GetProperty("withdrawBlockedReason").GetString());

            // The submitted mark itself is nowhere in the response.
            var dashboard = await client.GetJsonAsync("/api/me/dashboard");
            Assert.DoesNotContain(dashboard.GetProperty("results").EnumerateArray(), r => r.GetProperty("moduleCode").GetString() == "CS3001");
        }

        // After the autumn withdrawal deadline (2026-10-30T17:00Z) the deadline blocks first.
        var later = new FakeTimeProvider(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
        await using var host = factory.Derive(clock: later);
        using var student = await host.LoginStudentAsync(DemoAccounts.StudentUsername);
        var module = (await student.GetJsonAsync("/api/me/dashboard")).GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == "CS3001");
        Assert.False(module.GetProperty("canWithdraw").GetBoolean());
        Assert.Equal("deadline", module.GetProperty("withdrawBlockedReason").GetString());
    }

    private static async Task AssertFiveAsync(HttpClient client, MetricCollector<int> collector, string label)
    {
        collector.Clear();
        using var response = await client.GetAsync("/api/me/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var measurement = Assert.Single(collector.GetMeasurementSnapshot());
        Assert.True(measurement.Value == 5, $"Dashboard {label}: {measurement.Value} queries.");
    }
}
