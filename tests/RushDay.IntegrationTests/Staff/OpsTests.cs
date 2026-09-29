using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Infrastructure.Seeding;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// Operations (02-api.md section 8.5, 04-performance-and-ops.md section 6): the snapshot's shape (data quality is
/// refreshed in the background after the first poll, so it is current from the second one on); the reconciliation
/// corrects <c>enrolled_count</c> drift and reports it; the demo reset exists only in demo mode.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OpsTests(RushDayApiFactory factory)
{
    private static readonly string[] SnapshotProperties =
    [
        "sampledAt", "startedAt", "uptimeSeconds", "commit", "environment", "runtime", "process", "http", "db", "cache",
        "enrolment", "dashboard", "auth", "series", "backfills", "dataQuality",
    ];

    [Fact]
    public async Task Snapshot_has_its_shape_and_data_quality_from_the_second_poll()
    {
        using var admin = await factory.DemoAdminAsync();

        var first = await admin.GetJsonAsync("/api/admin/ops/metrics");
        Assert.Equal(SnapshotProperties.Order(StringComparer.Ordinal), first.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(60, first.GetProperty("series").GetArrayLength());
        Assert.Equal("workstation", first.GetProperty("runtime").GetProperty("gcMode").GetString());
        foreach (var name in new[] { "requests", "perSecond", "p50Ms", "p95Ms", "p99Ms", "status2xx", "status4xx", "status5xx", "rateLimited429", "shed503" })
        {
            Assert.True(first.GetProperty("http").GetProperty("last60s").TryGetProperty(name, out _), name);
        }

        // One token per two seconds per user: an immediate second poll is refused.
        using (var tooSoon = await admin.GetAsync("/api/admin/ops/metrics"))
        {
            await tooSoon.AssertProblemAsync(HttpStatusCode.TooManyRequests, "rate-limited");
        }

        JsonElement dataQuality = default;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2.2));
            var next = await admin.GetJsonAsync("/api/admin/ops/metrics");
            dataQuality = next.GetProperty("dataQuality");
            if (dataQuality.GetProperty("refreshedAt").ValueKind != JsonValueKind.Null)
            {
                Assert.NotEmpty(next.GetProperty("backfills").EnumerateArray());
                break;
            }
        }

        Assert.NotEqual(JsonValueKind.Null, dataQuality.GetProperty("refreshedAt").ValueKind);
        Assert.Equal(JsonValueKind.Array, dataQuality.GetProperty("modulesOverCapacity").ValueKind);
        Assert.Equal(JsonValueKind.Number, dataQuality.GetProperty("enrolledCountDrift").ValueKind);

        // A student or lecturer never reaches it.
        using var student = await factory.LoginStudentAsync("S000100");
        using var refused = await student.GetAsync("/api/admin/ops/metrics");
        await refused.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Reconcile_corrects_drift_and_reports_it()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YX9501");
        await admin.OverrideEnrolAsync("S000098", "YX9501");
        await admin.OverrideEnrolAsync("S000099", "YX9501");
        await factory.WithDbAsync(db => db.Modules.Where(m => m.Code == "YX9501").ExecuteUpdateAsync(s => s.SetProperty(m => m.EnrolledCount, 7)));

        var body = await admin.PostJsonAsync("/api/admin/ops/reconcile", null);
        var corrected = Assert.Single(body.GetProperty("modulesCorrected").EnumerateArray(), m => m.GetProperty("code").GetString() == "YX9501");
        Assert.Equal((7, 2), (corrected.GetProperty("before").GetInt32(), corrected.GetProperty("after").GetInt32()));
        Assert.Equal((2, 2), await factory.CountsAsync("YX9501"));
        Assert.Equal(2, (await admin.GetJsonAsync("/api/modules")).EnumerateArray().Single(m => m.GetProperty("code").GetString() == "YX9501").GetProperty("enrolledCount").GetInt32());

        var audit = (await factory.AuditAsync(AuditActions.OpsReconciled)).First();
        var reported = StaffData.DetailsOf(audit).GetProperty("modulesCorrected").EnumerateArray().Single(m => m.GetProperty("code").GetString() == "YX9501");
        Assert.Equal((7, 2), (reported.GetProperty("before").GetInt32(), reported.GetProperty("after").GetInt32()));

        // Nothing left to correct.
        var again = await admin.PostJsonAsync("/api/admin/ops/reconcile", null);
        Assert.DoesNotContain(again.GetProperty("modulesCorrected").EnumerateArray(), m => m.GetProperty("code").GetString() == "YX9501");
    }

    [Fact]
    public async Task Demo_reset_is_mapped_only_in_demo_mode()
    {
        // Demo mode (the main host): an anonymous caller is refused by authorization, so the route exists...
        using (var anonymous = factory.CreateCookieClient())
        {
            await anonymous.RefreshCsrfAsync();
            using var response = await anonymous.PostAsync("/api/admin/ops/demo-reset", null);
            await response.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        }

        // ...and the demo administrator runs it: unmarked self-service CS3099 enrolments are withdrawn.
        using var admin = await factory.DemoAdminAsync();
        await factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == "S000100").Select(s => s.Id).SingleAsync();
            var moduleId = await db.Modules.Where(m => m.Code == DatabaseSeeder.HotModuleCode).Select(m => m.Id).SingleAsync();
            if (!await db.Enrolments.AnyAsync(e => e.StudentId == studentId && e.ModuleId == moduleId))
            {
                db.Enrolments.Add(new Enrolment
                {
                    Id = Guid.CreateVersion7(),
                    StudentId = studentId,
                    ModuleId = moduleId,
                    EnrolledAt = factory.Clock.GetUtcNow(),
                    Status = EnrolmentStatus.Active,
                    Source = EnrolmentSource.Self,
                    AcademicYear = StaffData.CurrentYear,
                });
                await db.SaveChangesAsync();
            }
        });

        var reset = await admin.PostJsonAsync("/api/admin/ops/demo-reset", null);
        Assert.True(reset.GetProperty("withdrawn").GetInt32() >= 1);
        Assert.Equal(EnrolmentStatus.Withdrawn, (await factory.EnrolmentAsync("S000100", DatabaseSeeder.HotModuleCode))!.Status);
        var counts = await factory.CountsAsync(DatabaseSeeder.HotModuleCode);
        Assert.Equal(counts.ActiveCount, counts.EnrolledCount);
        var audit = (await factory.AuditAsync(AuditActions.SystemDemoReset)).First();
        Assert.Equal((DemoAccounts.AdminUsername, "CS3099"), (audit.ActorUsername, StaffData.DetailsOf(audit).GetProperty("moduleCode").GetString()));

        // With demo mode off the path is not mapped: the /api fallback answers 404.
        await using var customer = factory.Derive(builder => builder.UseSetting("Demo:Enabled", "false"));
        using var client = customer.CreateCookieClient();
        await client.RefreshCsrfAsync();
        using var notMapped = await client.PostAsync("/api/admin/ops/demo-reset", null);
        await notMapped.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        using var reconcile = await client.PostAsync("/api/admin/ops/reconcile", null);
        await reconcile.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }
}
