using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Seeding;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// The registry's remaining reads and the concurrency guarantees of the marks and results writes: the overview's
/// shape; lecturer records (list, edit, leave with the account disabled, idempotent, demo accounts refused); parallel
/// saves of one row let exactly one through; parallel publishes publish each grade once. Modules <c>YR####</c>,
/// lecturers <c>L92###</c>, years <c>2071/72</c>..
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RegistryTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Overview_has_its_shape()
    {
        using var admin = await factory.DemoAdminAsync();

        var overview = await admin.GetJsonAsync("/api/admin/overview");
        Assert.Equal(
            ["academicYear", "counts", "database", "enrolmentWindows", "latestPublication", "nextPublication", "recentAudit", "submissionProgress"],
            overview.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var counts = overview.GetProperty("counts");
        Assert.True(counts.GetProperty("students").GetInt32() >= RushDayApiFactory.SeedStudentCount);
        Assert.True(counts.GetProperty("lecturers").GetInt32() >= 40);
        Assert.True(counts.GetProperty("modules").GetInt32() >= 121);
        Assert.True(counts.GetProperty("activeEnrolments").GetInt32() > 0);
        Assert.True(counts.GetProperty("accounts").GetInt32() > counts.GetProperty("students").GetInt32());
        Assert.Equal(StaffData.CurrentYear, overview.GetProperty("academicYear").GetString());
        Assert.Equal("ok", overview.GetProperty("database").GetString());
        Assert.Equal(["autumn", "spring"], overview.GetProperty("enrolmentWindows").Strings("semester"));
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("nextPublication").ValueKind);

        var latest = overview.GetProperty("latestPublication");
        Assert.Equal(("2025/26", "live", 60), (latest.GetProperty("academicYear").GetString(), latest.GetProperty("state").GetString(), latest.GetProperty("moduleCount").GetInt32()));
        Assert.Equal(JsonValueKind.Null, latest.GetProperty("createdBy").ValueKind);

        var progress = overview.GetProperty("submissionProgress").EnumerateArray().ToList();
        Assert.Equal(["autumn", "spring"], progress.Select(p => p.GetProperty("semester").GetString()));
        var autumn = progress[0];
        Assert.Equal(
            autumn.GetProperty("modulesTotal").GetInt32(),
            autumn.GetProperty("draft").GetInt32() + autumn.GetProperty("submitted").GetInt32() + autumn.GetProperty("scheduled").GetInt32() + autumn.GetProperty("published").GetInt32());
        Assert.InRange(overview.GetProperty("recentAudit").GetArrayLength(), 1, 10);
    }

    [Fact]
    public async Task Lecturer_records_are_listed_edited_and_left()
    {
        using var real = await factory.RealAdminAsync();
        var admin = real.Client;

        var created = await admin.PostJsonAsync("/api/admin/lecturers", new { staffNumber = "l92001", fullName = "Kerensa Hocking", title = "Prof", department = "cs", email = "k.h@example.test" }, HttpStatusCode.Created);
        Assert.Equal(("L92001", "CS", false), (created.GetProperty("staffNumber").GetString(), created.GetProperty("department").GetString(), created.GetProperty("hasAccount").GetBoolean()));
        var taken = await admin.PostJsonAsync("/api/admin/lecturers", new { staffNumber = "L92001", fullName = "Again", title = "Dr", department = "CS" }, HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:staff-number-taken", taken.GetProperty("type").GetString());
        using (var badTitle = await admin.PostAsJsonAsync("/api/admin/lecturers", new { staffNumber = "L92002", fullName = "x", title = "Sir", department = "CS" }))
        {
            await badTitle.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        await admin.PostJsonAsync("/api/admin/accounts", new { username = "khocking", displayName = "Kerensa Hocking", role = "Lecturer", staffNumber = "L92001", temporaryPassword = TestAccounts.Password }, HttpStatusCode.Created);
        await admin.CreateModuleAsync("YR9901");
        await admin.AssignAsync("YR9901", "L92001");

        var updated = await admin.PutJsonAsync("/api/admin/lecturers/L92001", new { fullName = "Kerensa Hocking-Pascoe", title = "Prof", department = "CS", email = (string?)null });
        Assert.Equal(("Kerensa Hocking-Pascoe", true), (updated.GetProperty("fullName").GetString(), updated.GetProperty("hasAccount").GetBoolean()));
        Assert.Equal(["YR9901"], updated.GetProperty("moduleCodes").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Kerensa Hocking-Pascoe", (await factory.ReadUserAsync("khocking")).DisplayName);

        var listed = await admin.GetJsonAsync("/api/admin/lecturers?q=hocking");
        Assert.Equal(["L92001"], listed.Strings("staffNumber"));
        Assert.Equal(["L92001"], (await admin.GetJsonAsync("/api/admin/lecturers?q=L920")).Strings("staffNumber"));

        var left = await admin.PostJsonAsync("/api/admin/lecturers/L92001/leave", new { reason = StaffData.Reason });
        Assert.NotEqual(JsonValueKind.Null, left.GetProperty("leftAt").ValueKind);
        var user = await factory.ReadUserAsync("khocking");
        Assert.NotNull(user.DisabledAt);
        Assert.Single(await factory.AuditAsync(AuditActions.AccountDisabled, user.Id.ToString()));
        Assert.Single(await factory.AuditAsync(AuditActions.LecturerLeft, "L92001"));

        // Assignments stay and carry left: true; leaving again changes nothing and writes nothing.
        var module = await admin.GetJsonAsync("/api/modules/YR9901");
        Assert.True(module.GetProperty("lecturers")[0].GetProperty("left").GetBoolean());
        var again = await admin.PostJsonAsync("/api/admin/lecturers/L92001/leave", new { reason = StaffData.Reason });
        Assert.Equal(left.GetProperty("leftAt").GetString(), again.GetProperty("leftAt").GetString());
        Assert.Single(await factory.AuditAsync(AuditActions.LecturerLeft, "L92001"));

        using var missing = await admin.PostAsJsonAsync("/api/admin/lecturers/L92999/leave", new { reason = StaffData.Reason });
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "lecturer-not-found");

        // A demo lecturer's login is read-only: the demo administrator cannot mark them as left.
        using var demo = await factory.DemoAdminAsync();
        var refused = await demo.PostJsonAsync("/api/admin/lecturers/L00040/leave", new { reason = StaffData.Reason }, HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:demo-account", refused.GetProperty("type").GetString());
        Assert.Null(await factory.WithDbAsync(db => db.Lecturers.Where(l => l.StaffNumber == "L00040").Select(l => l.LeftAt).SingleAsync()));
    }

    [Fact]
    public async Task Parallel_saves_of_one_row_let_exactly_one_through()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YR9902");
        await admin.AssignAsync("YR9902", "L00039");
        await admin.OverrideEnrolAsync("S000094", "YR9902");
        using var lecturer = await factory.LecturerAsync("L00039");
        await lecturer.SaveMarksAsync("YR9902", [new { studentNumber = "S000094", mark = 40, version = (int?)null }]);

        var attempts = Enumerable.Range(0, 8).Select(i => lecturer.PutAsJsonAsync("/api/lecturer/modules/YR9902/marks", new { rows = new[] { new { studentNumber = "S000094", mark = 50 + i, version = (int?)1 } } })).ToList();
        var responses = await Task.WhenAll(attempts);
        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            var grade = await factory.GradeAsync("S000094", "YR9902");
            Assert.Equal(2, grade.Version);
            Assert.InRange(grade.Mark!.Value, 50, 57);
            Assert.Single(await factory.AuditAsync(AuditActions.GradeChanged, grade.Id.ToString()));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Parallel_publishes_publish_each_grade_once()
    {
        const string year = "2071/72";
        await factory.SeedModuleAsync("YR9903", Semester.Autumn, year, [("S000095", 61), ("S000093", 71)]);
        using var admin = await factory.DemoAdminAsync();

        try
        {
            var body = new { academicYear = year, semester = "autumn", publishAt = factory.Clock.GetUtcNow(), announce = false };
            var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => admin.PostAsJsonAsync("/api/admin/results/publish", body)));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            foreach (var response in responses)
            {
                response.Dispose();
            }

            var publications = await factory.WithDbAsync(db => db.ResultsPublications.AsNoTracking().Where(p => p.AcademicYear == year).ToListAsync());
            var publication = Assert.Single(publications);
            Assert.Equal((2, 1), (publication.GradeCount, publication.ModuleCount));
            Assert.Equal(publication.Id, (await factory.GradeAsync("S000095", "YR9903")).PublicationId);
            Assert.Equal(GradeStatus.Published, (await factory.GradeAsync("S000093", "YR9903")).Status);
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    [Fact]
    public async Task The_demo_administrator_can_run_the_registry()
    {
        // In demo mode the demo administrator is exempt from the second factor and reaches every read.
        using var admin = await factory.DemoAdminAsync();
        foreach (var path in new[] { "/api/admin/overview", "/api/admin/settings", "/api/admin/enrolment-windows", "/api/admin/results?semester=autumn", "/api/admin/students", "/api/admin/modules", "/api/admin/lecturers", "/api/admin/accounts", "/api/admin/announcements", "/api/admin/audit" })
        {
            using var response = await admin.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}");
        }

        var accounts = await admin.GetJsonAsync("/api/admin/accounts?q=S00000&role=Student&pageSize=5");
        Assert.Equal(5, accounts.GetProperty("items").GetArrayLength());
        Assert.All(accounts.GetProperty("items").EnumerateArray(), a =>
        {
            Assert.Equal(("Student", true), (a.GetProperty("role").GetString(), a.GetProperty("isDemo").GetBoolean()));
            Assert.StartsWith("S00000", a.GetProperty("studentNumber").GetString(), StringComparison.Ordinal);
        });
        Assert.Equal(accounts.GetProperty("items").Strings("username").Order(StringComparer.Ordinal), accounts.GetProperty("items").Strings("username"));
        Assert.Equal(DemoAccounts.StudentUsername, accounts.GetProperty("items")[0].GetProperty("username").GetString());
    }
}
