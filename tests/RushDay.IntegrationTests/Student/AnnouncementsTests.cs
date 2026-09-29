using System.Net;
using System.Text.Json;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// <c>GET /api/announcements</c> (02-api.md section 8.2, 00-overview.md section 4.4): visible now (published, not expired,
/// not deleted); a student sees university announcements and those of modules they are actively enrolled on this year, a
/// lecturer university and assigned modules, an administrator all; pinned first, then newest first.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnnouncementsTests(RushDayApiFactory factory)
{
    private static readonly string[] ViewProperties =
    [
        "id", "scope", "moduleCode", "title", "body", "pinned", "publishedAt", "expiresAt", "author", "createdAt", "updatedAt",
    ];

    [Fact]
    public async Task Each_role_sees_what_it_should()
    {
        const string student = "S000091";
        await factory.CreateModuleAsync("ZZ1401", Semester.Spring, capacity: 10);
        await factory.CreateModuleAsync("ZZ1402", Semester.Spring, capacity: 10);
        await factory.CreateModuleAsync("ZZ1403", Semester.Spring, capacity: 10);
        using var client = await factory.LoginStudentAsync(student);
        using (var enrol = await client.EnrolAsync("ZZ1401"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        // ZZ1403 is held only in an earlier year: its announcements are not this student's any more.
        await factory.InsertEnrolmentAsync(student, "ZZ1403", StudentData.PreviousYear);

        var now = factory.Clock.GetUtcNow();
        await factory.AddAnnouncementAsync("T-university-pinned", pinned: true, publishedAt: now.AddHours(-3));
        await factory.AddAnnouncementAsync("T-university-new", publishedAt: now.AddMinutes(-1));
        await factory.AddAnnouncementAsync("T-university-future", publishedAt: now.AddHours(1));
        await factory.AddAnnouncementAsync("T-university-expired", publishedAt: now.AddDays(-2), expiresAt: now.AddMinutes(-1));
        await factory.AddAnnouncementAsync("T-university-deleted", deleted: true);
        await factory.AddAnnouncementAsync("T-ZZ1401", moduleCode: "ZZ1401", publishedAt: now.AddMinutes(-2));
        await factory.AddAnnouncementAsync("T-ZZ1402", moduleCode: "ZZ1402");
        await factory.AddAnnouncementAsync("T-ZZ1403", moduleCode: "ZZ1403");

        // Student: university and ZZ1401 (enrolled this year); not ZZ1402, not ZZ1403 (earlier year), not CS3099.
        var forStudent = await client.GetJsonAsync("/api/announcements");
        var studentTitles = Titles(forStudent);
        Assert.Contains("T-university-pinned", studentTitles);
        Assert.Contains("T-university-new", studentTitles);
        Assert.Contains("T-ZZ1401", studentTitles);
        Assert.Contains("Spring 2026/27 enrolment is open", studentTitles);
        Assert.DoesNotContain("T-ZZ1402", studentTitles);
        Assert.DoesNotContain("T-ZZ1403", studentTitles);
        Assert.DoesNotContain("Welcome to Advanced Machine Learning", studentTitles);
        AssertHidden(studentTitles);
        AssertOrdered(forStudent);

        var pinned = forStudent.EnumerateArray().Single(a => a.GetProperty("title").GetString() == "T-university-pinned");
        Assert.Equal(ViewProperties.Order(), pinned.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("university", pinned.GetProperty("scope").GetString());
        Assert.Equal(JsonValueKind.Null, pinned.GetProperty("moduleCode").ValueKind);
        Assert.Equal("Demo Administrator", pinned.GetProperty("author").GetString());
        var module = forStudent.EnumerateArray().Single(a => a.GetProperty("title").GetString() == "T-ZZ1401");
        Assert.Equal(("module", "ZZ1401"), (module.GetProperty("scope").GetString(), module.GetProperty("moduleCode").GetString()));

        // Lecturer L00001 (leads CS3001 and CS3099): university and their modules only.
        using var lecturer = await factory.LoginAsync(DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword);
        var lecturerTitles = Titles(await lecturer.GetJsonAsync("/api/announcements"));
        Assert.Contains("T-university-pinned", lecturerTitles);
        Assert.Contains("Welcome to Advanced Machine Learning", lecturerTitles);
        Assert.DoesNotContain("T-ZZ1401", lecturerTitles);
        Assert.DoesNotContain("T-ZZ1402", lecturerTitles);
        AssertHidden(lecturerTitles);

        // Administrator: everything visible.
        using var admin = await factory.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);
        var forAdmin = await admin.GetJsonAsync("/api/announcements");
        var adminTitles = Titles(forAdmin);
        Assert.Contains("T-ZZ1401", adminTitles);
        Assert.Contains("T-ZZ1402", adminTitles);
        Assert.Contains("T-ZZ1403", adminTitles);
        Assert.Contains("Welcome to Advanced Machine Learning", adminTitles);
        AssertHidden(adminTitles);
        AssertOrdered(forAdmin);

        // The dashboard shows the latest five, pinned first.
        var dashboard = await client.GetJsonAsync("/api/me/dashboard");
        Assert.InRange(dashboard.GetProperty("announcements").GetArrayLength(), 1, 5);
        Assert.Equal("T-university-pinned", dashboard.GetProperty("announcements")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Announcements_need_a_session()
    {
        using var anonymous = factory.CreateCookieClient();
        using var response = await anonymous.GetAsync("/api/announcements");
        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    private static List<string> Titles(JsonElement list) => [.. list.EnumerateArray().Select(a => a.GetProperty("title").GetString()!)];

    private static void AssertHidden(List<string> titles)
    {
        Assert.DoesNotContain("T-university-future", titles);
        Assert.DoesNotContain("T-university-expired", titles);
        Assert.DoesNotContain("T-university-deleted", titles);
        Assert.InRange(titles.Count, 1, 50);
    }

    private static void AssertOrdered(JsonElement list)
    {
        var keys = list.EnumerateArray().Select(a => (Pinned: a.GetProperty("pinned").GetBoolean(), At: a.GetProperty("publishedAt").GetString()!)).ToList();
        Assert.Equal(keys.OrderByDescending(k => k.Pinned).ThenByDescending(k => k.At, StringComparer.Ordinal), keys);
    }
}
