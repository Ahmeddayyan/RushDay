using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RushDay.Api.Observability;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// <c>GET /api/modules</c> and <c>GET /api/modules/{code}</c> (02-api.md section 8.2, D13): the list is viewer-agnostic
/// and served from <c>catalogue:all</c> without the database on a hit; the detail reads the row live and returns an
/// inactive module with <c>isActive: false</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CatalogueTests(RushDayApiFactory factory)
{
    private static readonly string[] SummaryProperties =
    [
        "code", "title", "department", "level", "credits", "semester", "capacity", "enrolledCount", "placesRemaining", "isActive",
        "lecturers", "enrolmentState", "windowOpensAt", "windowClosesAt", "withdrawalDeadlineAt",
    ];

    [Fact]
    public async Task Catalogue_lists_active_modules_by_code_with_lecturers_and_window_state()
    {
        using var client = await factory.LoginStudentAsync("S000021");
        var modules = (await client.GetJsonAsync("/api/modules")).EnumerateArray().ToList();

        Assert.True(modules.Count >= 121);
        var codes = modules.Select(m => m.GetProperty("code").GetString()!).ToList();
        Assert.Equal(codes.Order(StringComparer.Ordinal), codes);
        Assert.All(modules, m =>
        {
            Assert.Equal(SummaryProperties.Order(), m.EnumerateObject().Select(p => p.Name).Order());
            Assert.True(m.GetProperty("isActive").GetBoolean());
            Assert.Equal(Math.Max(0, m.GetProperty("capacity").GetInt32() - m.GetProperty("enrolledCount").GetInt32()), m.GetProperty("placesRemaining").GetInt32());
        });

        var cs3099 = modules.Single(m => m.GetProperty("code").GetString() == "CS3099");
        Assert.Equal(30, cs3099.GetProperty("capacity").GetInt32());
        Assert.Equal(3, cs3099.GetProperty("level").GetInt32());
        Assert.Equal("CS", cs3099.GetProperty("department").GetString());
        Assert.Equal("spring", cs3099.GetProperty("semester").GetString());
        Assert.Equal("open", cs3099.GetProperty("enrolmentState").GetString());
        Assert.Equal("2026-09-14T09:00:00.000Z", cs3099.GetProperty("windowOpensAt").GetString());
        Assert.Equal("2027-01-29T17:00:00.000Z", cs3099.GetProperty("windowClosesAt").GetString());
        Assert.Equal("2027-02-26T17:00:00.000Z", cs3099.GetProperty("withdrawalDeadlineAt").GetString());

        var lecturers = cs3099.GetProperty("lecturers").EnumerateArray().ToList();
        Assert.Equal(["L00001", "L00006"], lecturers.Select(l => l.GetProperty("staffNumber").GetString()));
        Assert.Equal(["leader", "teacher"], lecturers.Select(l => l.GetProperty("role").GetString()));
        Assert.All(lecturers, l => Assert.False(l.GetProperty("left").GetBoolean()));
    }

    [Fact]
    public async Task Catalogue_is_the_same_for_every_role()
    {
        using var student = await factory.LoginStudentAsync("S000022");
        using var lecturer = await factory.LoginAsync(DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword);
        using var admin = await factory.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

        var bodies = new List<string>();
        foreach (var client in new[] { student, lecturer, admin })
        {
            using var response = await client.GetAsync("/api/modules");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        Assert.Single(bodies.Distinct());

        using var anonymous = factory.CreateCookieClient();
        using var refused = await anonymous.GetAsync("/api/modules");
        await refused.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    /// <summary>A warm catalogue is served from <c>catalogue:all</c> with no database command at all.</summary>
    [Fact]
    public async Task A_cache_hit_serves_without_the_database()
    {
        await using var host = factory.DeriveWithCommandLog();
        var logs = host.Services.GetFakeLogCollector();
        using var cacheRequests = new MetricCollector<long>(host.Services.GetRequiredService<RushDayMetrics>().CacheRequests);
        using var client = await host.LoginStudentAsync("S000023");

        using (var cold = await client.GetAsync("/api/modules"))
        {
            Assert.Equal(HttpStatusCode.OK, cold.StatusCode);
        }

        Assert.Equal("miss", CatalogueResults(cacheRequests).Last());

        logs.Clear();
        cacheRequests.Clear();
        using var warm = await client.GetAsync("/api/modules");

        Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        Assert.Equal(["hit"], CatalogueResults(cacheRequests));
        Assert.DoesNotContain(logs.GetSnapshot(), r => r.Category == RushDayApiFactory.CommandLogCategory && r.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Detail_reads_the_live_count_while_the_list_is_cached()
    {
        await factory.CreateModuleAsync("ZZ1902", Semester.Spring, capacity: 5);
        using var client = await factory.LoginStudentAsync("S000024");

        // Fill the list with ZZ1902 at 0, then take a place.
        Assert.Equal(0, ListEntry(await client.GetJsonAsync("/api/modules"), "ZZ1902").GetProperty("enrolledCount").GetInt32());
        using (var enrol = await client.EnrolAsync("ZZ1902"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        var detail = await client.GetJsonAsync("/api/modules/ZZ1902");
        Assert.Equal(1, detail.GetProperty("enrolledCount").GetInt32());
        Assert.Equal(4, detail.GetProperty("placesRemaining").GetInt32());

        // The list is the 30-second cache: it still says 0 (the SPA reads the detail for live numbers).
        Assert.Equal(0, ListEntry(await client.GetJsonAsync("/api/modules"), "ZZ1902").GetProperty("enrolledCount").GetInt32());
    }

    [Fact]
    public async Task Inactive_module_detail_returns_isActive_false()
    {
        await factory.CreateModuleAsync("ZZ1901", Semester.Autumn, capacity: 20, isActive: false);
        using var client = await factory.LoginStudentAsync("S000025");

        var detail = await client.GetJsonAsync("/api/modules/ZZ1901");
        Assert.Equal(SummaryProperties.Concat(["description", "timetable"]).Order(), detail.EnumerateObject().Select(p => p.Name).Order());
        Assert.False(detail.GetProperty("isActive").GetBoolean());
        Assert.Equal("Created by a test.", detail.GetProperty("description").GetString());
        Assert.Equal("open", detail.GetProperty("enrolmentState").GetString());

        var timetable = detail.GetProperty("timetable").EnumerateArray().ToList();
        Assert.Equal(2, timetable.Count);
        Assert.Equal(("tuesday", "10:00", "11:00", "lecture"), Slot(timetable[0]));
        Assert.Equal(("thursday", "14:00", "16:00", "lab"), Slot(timetable[1]));
        Assert.All(timetable, t => Assert.Equal("ZZ1901", t.GetProperty("moduleCode").GetString()));

        Assert.DoesNotContain((await client.GetJsonAsync("/api/modules")).EnumerateArray(), m => m.GetProperty("code").GetString() == "ZZ1901");

        using var unknown = await client.GetAsync("/api/modules/ZZ9998");
        await unknown.AssertProblemAsync(HttpStatusCode.NotFound, "module-not-found");
    }

    private static List<string> CatalogueResults(MetricCollector<long> collector) =>
        [.. collector.GetMeasurementSnapshot().Where(m => (string?)m.Tags["cache"] == CacheKeys.Catalogue).Select(m => (string)m.Tags["result"]!)];

    private static System.Text.Json.JsonElement ListEntry(System.Text.Json.JsonElement list, string code) =>
        list.EnumerateArray().Single(m => m.GetProperty("code").GetString() == code);

    private static (string?, string?, string?, string?) Slot(System.Text.Json.JsonElement slot) =>
        (slot.GetProperty("day").GetString(), slot.GetProperty("startTime").GetString(), slot.GetProperty("endTime").GetString(), slot.GetProperty("kind").GetString());
}
