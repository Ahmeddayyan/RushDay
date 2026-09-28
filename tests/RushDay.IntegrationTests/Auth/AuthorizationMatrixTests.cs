using System.Net;
using System.Net.Http.Json;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Auth;

/// <summary>
/// Every cross-role call answers 403 (00-overview.md section 8, 02-api.md section 4, T4, T5, T17). Created by S2; the
/// cases for <c>/api/me/*</c> are un-skipped by S4 and those for <c>/api/lecturer/*</c> and <c>/api/admin/*</c> by S6.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthorizationMatrixTests(RushDayApiFactory factory)
{
    private const string StudentSkip = "S4/S6: the student routes arrive in S4";
    private const string StaffSkip = "S4/S6: the lecturer and administrator routes arrive in S6";

    [Fact]
    public async Task Anonymous_caller_is_401_problem_on_authenticated_routes()
    {
        using var client = factory.CreateCookieClient();

        using var me = await client.GetAsync("/api/auth/me");
        await me.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");

        // Authorization runs before antiforgery, so an anonymous mutation is 401, not a redirect or a 400.
        await client.RefreshCsrfAsync();
        using var logout = await client.PostAsync("/api/auth/logout", null);
        await logout.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
        using var setup = await client.PostAsync("/api/auth/mfa/setup", null);
        await setup.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthenticated");
    }

    // The policies themselves, on probe routes of the production composition (ProbeApp), until S4 and S6 add the
    // real routes below.

    [Fact]
    public async Task StudentOnly_admits_students_only()
    {
        await using var probe = await ProbeApp.StartAsync(factory);
        using var student = await probe.LoginAsync(DemoAccounts.StudentUsername, DemoAccounts.StudentPassword);
        using var lecturer = await probe.LoginAsync(DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword);
        using var admin = await probe.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

        using (var allowed = await student.GetAsync(ProbeApp.StudentPath))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        foreach (var other in new[] { lecturer, admin })
        {
            using var refused = await other.GetAsync(ProbeApp.StudentPath);
            await refused.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }
    }

    [Fact]
    public async Task AdminOnly_admits_administrators_only()
    {
        await using var probe = await ProbeApp.StartAsync(factory);
        using var student = await probe.LoginAsync(DemoAccounts.StudentUsername, DemoAccounts.StudentPassword);
        using var lecturer = await probe.LoginAsync(DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword);
        using var admin = await probe.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

        using (var allowed = await admin.GetAsync(ProbeApp.AdminPath))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        foreach (var other in new[] { student, lecturer })
        {
            using var refused = await other.GetAsync(ProbeApp.AdminPath);
            await refused.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }
    }

    [Fact]
    public async Task TeachesModule_admits_only_the_modules_a_lecturer_is_assigned_to()
    {
        await using var probe = await ProbeApp.StartAsync(factory);
        using var lecturer = await probe.LoginAsync(DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword);
        using var admin = await probe.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);
        using var student = await probe.LoginAsync(DemoAccounts.StudentUsername, DemoAccounts.StudentPassword);

        // L00001 leads CS3099 and CS3001 (01-domain-and-data.md section 6 step 7).
        foreach (var own in new[] { "CS3099", "CS3001" })
        {
            using var allowed = await lecturer.GetAsync(ProbeApp.ModulePath(own));
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // Another department's module, and one that does not exist: both 403 not-your-module, never 404.
        foreach (var foreign in new[] { "MA1002", "ZZ9999" })
        {
            using var refused = await lecturer.GetAsync(ProbeApp.ModulePath(foreign));
            await refused.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
        }

        // Not a lecturer at all: forbidden, not "not your module".
        foreach (var other in new[] { admin, student })
        {
            using var refused = await other.GetAsync(ProbeApp.ModulePath("CS3099"));
            await refused.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }
    }

    [Fact(Skip = StudentSkip)]
    public async Task Lecturer_on_student_routes_is_403()
    {
        using var lecturer = await factory.LoginAsync(DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword);

        foreach (var path in new[] { "/api/me/dashboard", "/api/me/results", "/api/me/timetable", "/api/me/enrolments" })
        {
            using var response = await lecturer.GetAsync(path);
            await response.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }
    }

    [Fact(Skip = StudentSkip)]
    public async Task Admin_on_student_routes_is_403()
    {
        using var admin = await factory.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

        using var dashboard = await admin.GetAsync("/api/me/dashboard");
        await dashboard.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        using var enrol = await admin.PostAsJsonAsync("/api/me/enrolments", new { moduleCode = "CS3099" });
        await enrol.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact(Skip = StaffSkip)]
    public async Task Student_on_admin_student_route_is_403()
    {
        using var student = await factory.LoginAsync(DemoAccounts.StudentUsername, DemoAccounts.StudentPassword);

        using var response = await student.GetAsync("/api/admin/students/S000002");
        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact(Skip = StaffSkip)]
    public async Task Student_on_lecturer_routes_is_403()
    {
        using var student = await factory.LoginAsync(DemoAccounts.StudentUsername, DemoAccounts.StudentPassword);

        using var modules = await student.GetAsync("/api/lecturer/modules");
        await modules.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        using var roster = await student.GetAsync("/api/lecturer/modules/CS3099/roster");
        Assert.Equal(HttpStatusCode.Forbidden, roster.StatusCode);
    }

    [Fact(Skip = StaffSkip)]
    public async Task Lecturer_on_admin_routes_is_403()
    {
        using var lecturer = await factory.LoginAsync(DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword);

        using var overview = await lecturer.GetAsync("/api/admin/overview");
        await overview.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact(Skip = StaffSkip)]
    public async Task Admin_on_lecturer_modules_is_403()
    {
        using var admin = await factory.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

        using var response = await admin.GetAsync("/api/lecturer/modules");
        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact(Skip = StaffSkip)]
    public async Task Admin_on_lecturer_marks_put_is_403()
    {
        using var admin = await factory.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);

        using var response = await admin.PutAsJsonAsync("/api/lecturer/modules/CS3099/marks", new { rows = new[] { new { studentNumber = "S000001", mark = 70, version = (int?)null } } });
        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact(Skip = StaffSkip)]
    public async Task Lecturer_on_foreign_module_is_403_not_your_module()
    {
        using var lecturer = await factory.LoginAsync(DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword);

        // L00001 teaches CS modules only; MA1002 belongs to the MA department.
        using var foreign = await lecturer.GetAsync("/api/lecturer/modules/MA1002/roster");
        await foreign.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");

        // An unknown code is not a 404: the answer never reveals whether a module exists.
        using var unknown = await lecturer.GetAsync("/api/lecturer/modules/ZZ9999/marks");
        await unknown.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
    }

    [Fact(Skip = StaffSkip)]
    public async Task Teacher_cannot_submit_marks()
    {
        // L00006 teaches CS3099; L00001 leads it (01-domain-and-data.md section 6 step 7).
        using var teacher = await factory.LoginAsync("L00006", DemoAccounts.LecturerPassword);

        using var response = await teacher.PostAsync("/api/lecturer/modules/CS3099/marks/submit", null);
        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "not-module-leader");
    }

    [Fact(Skip = StaffSkip)]
    public async Task Lecturer_cannot_edit_announcement_of_another_module()
    {
        using var admin = await factory.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);
        using var announcements = await admin.GetAsync("/api/admin/announcements");
        var moduleAnnouncement = (await announcements.ReadJsonAsync()).EnumerateArray()
            .First(a => a.GetProperty("scope").GetString() == "module" && a.GetProperty("moduleCode").GetString() == "CS3099");
        var id = moduleAnnouncement.GetProperty("id").GetString();

        // L00011 leads MA modules, not CS3099: through their own module code the announcement does not exist.
        using var lecturer = await factory.LoginAsync("L00011", DemoAccounts.LecturerPassword);
        using var response = await lecturer.PutAsJsonAsync($"/api/lecturer/modules/MA1002/announcements/{id}", new { title = "Hijacked", body = "Not mine." });
        await response.AssertProblemAsync(HttpStatusCode.NotFound, "announcement-not-found");
    }

    [Fact(Skip = StaffSkip)]
    public async Task Lecturer_cannot_delete_university_announcement()
    {
        using var admin = await factory.LoginAsync(DemoAccounts.AdminUsername, DemoAccounts.AdminPassword);
        using var announcements = await admin.GetAsync("/api/admin/announcements");
        var university = (await announcements.ReadJsonAsync()).EnumerateArray()
            .First(a => a.GetProperty("scope").GetString() == "university");
        var id = university.GetProperty("id").GetString();

        using var lecturer = await factory.LoginAsync(DemoAccounts.LecturerUsername, DemoAccounts.LecturerPassword);
        using var response = await lecturer.DeleteAsync($"/api/lecturer/modules/CS3099/announcements/{id}");
        await response.AssertProblemAsync(HttpStatusCode.NotFound, "announcement-not-found");
    }

    [Fact(Skip = StaffSkip)]
    public async Task Admin_gated_by_mfa_cannot_publish()
    {
        var admin = await factory.ProvisionAsync();
        using var client = await factory.LoginAsync(admin.UserName!, TestAccounts.Password);

        using var response = await client.PostAsJsonAsync("/api/admin/results/publish", new { academicYear = "2026/27", semester = "autumn", publishAt = DateTimeOffset.UtcNow, announce = false });
        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "mfa-setup-required");
    }
}
