using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Grades;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// The S6 review's registry findings: trim counts real enrolments (E4), a lecturer who has left has no authority and
/// gets no account (E5), a demo actor cannot rename a real account (E6), the semester is pinned by any history (E7),
/// leaving keeps enrolments that hold results (E8), one leader per module (E10), null list elements (E11), integer enum
/// values (E13), paging depth (E14) and the audited roster export (E15). Modules <c>YH####</c>; students created here
/// as <c>S942###</c>; lecturers <c>L942##</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RegistryRulesTests(RushDayApiFactory factory)
{
    private const string Reason = StaffData.Reason;

    /// <summary>
    /// E4 (the reviewer's P10): capacity 5, four students, a counter drifted to 7. Trim counts the real enrolments,
    /// withdraws nobody and repairs the counter. Before the fix it trusted the counter and withdrew two students from a
    /// module that was never over capacity.
    /// </summary>
    [Fact]
    public async Task Trim_counts_real_enrolments_and_never_trusts_a_drifted_counter()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YH1001", Semester.Spring, capacity: 5);
        foreach (var n in Enumerable.Range(1, 4))
        {
            await admin.OverrideEnrolAsync(await ResultsGovernanceTests.NewStudentAsync(admin, $"S94200{n}"), "YH1001");
        }

        await factory.WithDbAsync(db => db.Modules.Where(m => m.Code == "YH1001").ExecuteUpdateAsync(s => s.SetProperty(m => m.EnrolledCount, 7)));

        var trimmed = await admin.PostJsonAsync("/api/admin/modules/YH1001/trim-to-capacity", new { reason = Reason });
        Assert.Empty(trimmed.GetProperty("withdrawn").EnumerateArray());
        Assert.Equal((7, 4), (trimmed.GetProperty("before").GetInt32(), trimmed.GetProperty("after").GetInt32()));
        Assert.Equal((4, 4), await factory.CountsAsync("YH1001"));

        // Genuinely over capacity (6 real rows, a counter of 3): it withdraws exactly the real excess.
        foreach (var n in Enumerable.Range(5, 2))
        {
            await admin.OverrideEnrolAsync(await ResultsGovernanceTests.NewStudentAsync(admin, $"S94200{n}"), "YH1001", forceCapacity: true);
        }

        await factory.WithDbAsync(db => db.Modules.Where(m => m.Code == "YH1001").ExecuteUpdateAsync(s => s.SetProperty(m => m.EnrolledCount, 3).SetProperty(m => m.Capacity, 5)));
        var again = await admin.PostJsonAsync("/api/admin/modules/YH1001/trim-to-capacity", new { reason = Reason });
        Assert.Equal(["S942006"], again.GetProperty("withdrawn").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal((5, 5), await factory.CountsAsync("YH1001"));
        var details = StaffData.DetailsOf((await factory.AuditAsync(AuditActions.ModuleTrimmed, "YH1001")).First());
        Assert.Equal((3, 5), (details.GetProperty("enrolledCount").GetProperty("before").GetInt32(), details.GetProperty("enrolledCount").GetProperty("after").GetInt32()));
    }

    /// <summary>
    /// E5: a lecturer who has left is refused an account (409 <c>principal-left</c>, the reviewer's P5 path) and a
    /// disabled account of theirs cannot be re-enabled; a student who has left likewise. Before the fix the
    /// provisioning answered 201 and the login listed, marked and submitted the module.
    /// </summary>
    [Fact]
    public async Task Nobody_who_has_left_gets_or_regains_an_account()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.PostJsonAsync("/api/admin/lecturers", new { staffNumber = "L94201", fullName = "Left Lecturer", title = "Dr", department = "YH" }, HttpStatusCode.Created);
        await admin.CreateModuleAsync("YH2001", Semester.Autumn, capacity: 10);
        await admin.AssignAsync("YH2001", "L94201");
        await admin.PostJsonAsync("/api/admin/lecturers/L94201/leave", new { reason = Reason });

        using (var provision = await admin.PostAsJsonAsync("/api/admin/accounts", new { username = "l94201", displayName = "Left Lecturer", role = "Lecturer", staffNumber = "L94201", temporaryPassword = TestAccounts.Password }))
        {
            await provision.AssertProblemAsync(HttpStatusCode.Conflict, "principal-left");
        }

        var student = await ResultsGovernanceTests.NewStudentAsync(admin, "S942101");
        await admin.PostJsonAsync($"/api/admin/students/{student}/leave", new { reason = Reason });
        using (var provision = await admin.PostAsJsonAsync("/api/admin/accounts", new { username = "s942101", displayName = "Left Student", role = "Student", studentNumber = student, temporaryPassword = TestAccounts.Password }))
        {
            await provision.AssertProblemAsync(HttpStatusCode.Conflict, "principal-left");
        }

        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.UserName == "l94201" || u.UserName == "s942101")));

        // A real account (a real administrator provisions it), disabled by the leave, stays disabled.
        using var real = await factory.RealAdminAsync();
        await real.Client.PostJsonAsync("/api/admin/lecturers", new { staffNumber = "L94202", fullName = "Leaving Lecturer", title = "Dr", department = "YH" }, HttpStatusCode.Created);
        var account = await real.Client.PostJsonAsync("/api/admin/accounts", new { username = "l94202", displayName = "Leaving Lecturer", role = "Lecturer", staffNumber = "L94202", temporaryPassword = TestAccounts.Password }, HttpStatusCode.Created);
        var accountId = account.GetProperty("account").GetProperty("id").GetString();
        await real.Client.PostJsonAsync("/api/admin/lecturers/L94202/leave", new { reason = Reason });
        using (var enable = await real.Client.PostAsync($"/api/admin/accounts/{accountId}/enable", null))
        {
            await enable.AssertProblemAsync(HttpStatusCode.Conflict, "principal-left");
        }

        Assert.NotNull((await factory.ReadUserAsync("l94202")).DisabledAt);
    }

    /// <summary>
    /// E5, the lookups: a lecturer marked as left whose login still works (the state a pre-fix provisioning or enabling
    /// produced) lists no module and is refused the roster, the marks and the submit (403 <c>not-your-module</c>).
    /// Before the fix all three lookups ignored <c>left_at</c>.
    /// </summary>
    [Fact]
    public async Task A_lecturer_marked_as_left_has_no_module_authority()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.PostJsonAsync("/api/admin/lecturers", new { staffNumber = "L94203", fullName = "Former Leader", title = "Dr", department = "YH" }, HttpStatusCode.Created);
        await admin.PostJsonAsync("/api/admin/accounts", new { username = "l94203", displayName = "Former Leader", role = "Lecturer", staffNumber = "L94203", temporaryPassword = TestAccounts.Password }, HttpStatusCode.Created);
        await admin.CreateModuleAsync("YH2002", Semester.Autumn, capacity: 10);
        await admin.AssignAsync("YH2002", "L94203");
        await admin.OverrideEnrolAsync(await ResultsGovernanceTests.NewStudentAsync(admin, "S942102"), "YH2002");

        using var lecturer = await factory.LoginAsync("l94203", TestAccounts.Password);
        Assert.Equal(["YH2002"], (await lecturer.GetJsonAsync("/api/lecturer/modules")).Strings("code"));

        // Left, with the login left working and the assignment kept for the record.
        await factory.WithDbAsync(db => db.Lecturers.Where(l => l.StaffNumber == "L94203").ExecuteUpdateAsync(s => s.SetProperty(l => l.LeftAt, factory.Clock.GetUtcNow())));
        var lecturerId = await factory.WithDbAsync(db => db.Lecturers.Where(l => l.StaffNumber == "L94203").Select(l => l.Id).SingleAsync());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LecturerModuleCache>().InvalidateAsync(lecturerId);
        }

        Assert.Empty((await lecturer.GetJsonAsync("/api/lecturer/modules")).EnumerateArray());
        using (var roster = await lecturer.GetAsync("/api/lecturer/modules/YH2002/roster"))
        {
            await roster.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
        }

        using (var save = await lecturer.PutAsJsonAsync("/api/lecturer/modules/YH2002/marks", new { rows = new[] { new { studentNumber = "S942102", mark = 12, version = (int?)null } } }))
        {
            await save.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
        }

        using (var submit = await lecturer.PostAsync("/api/lecturer/modules/YH2002/marks/submit", null))
        {
            await submit.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
        }

        // The services check too, whatever the cached policy said (a fill in flight at the leave).
        await using var services = factory.Services.CreateAsyncScope();
        var marks = services.ServiceProvider.GetRequiredService<MarksService>();
        var direct = await marks.SubmitAsync(lecturerId, "YH2002");
        Assert.Equal(MarksError.NotYourModule, direct.Failure!.Error);
    }

    /// <summary>
    /// E6 (the reviewer's P4): the demo administrator (a public password) may not rename a real account's login through
    /// a student or lecturer record edit (409 <c>demo-account</c>, the rule of leave), while a real administrator may,
    /// and a demo account's display name follows its record. Before the fix the demo edit answered 200 and renamed the
    /// real account.
    /// </summary>
    [Fact]
    public async Task A_demo_actor_cannot_rename_a_real_account_through_a_record_edit()
    {
        using var admin = await factory.DemoAdminAsync();
        var number = await ResultsGovernanceTests.NewStudentAsync(admin, "S942201");
        await admin.PostJsonAsync("/api/admin/accounts", new { username = "s942201", displayName = "Review Student", role = "Student", studentNumber = number, temporaryPassword = TestAccounts.Password }, HttpStatusCode.Created);
        await admin.PostJsonAsync("/api/admin/lecturers", new { staffNumber = "L94204", fullName = "Real Lecturer", title = "Dr", department = "YH" }, HttpStatusCode.Created);
        await admin.PostJsonAsync("/api/admin/accounts", new { username = "l94204", displayName = "Real Lecturer", role = "Lecturer", staffNumber = "L94204", temporaryPassword = TestAccounts.Password }, HttpStatusCode.Created);

        // While the logins are demo accounts their display names follow the records.
        await admin.PutJsonAsync($"/api/admin/students/{number}", new { fullName = "Renamed Demo Student", programme = "BSc Test", yearOfStudy = 1, email = (string?)null });
        Assert.Equal("Renamed Demo Student", (await factory.ReadUserAsync("s942201")).DisplayName);

        // Real accounts: the demo administrator is refused and nothing changes.
        await factory.WithDbAsync(db => db.Users.Where(u => u.UserName == "s942201" || u.UserName == "l94204").ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDemo, false)));
        using (var student = await admin.PutAsJsonAsync($"/api/admin/students/{number}", new { fullName = "Renamed By Demo Visitor", programme = "BSc Test", yearOfStudy = 1, email = (string?)null }))
        {
            await student.AssertProblemAsync(HttpStatusCode.Conflict, "demo-account");
        }

        using (var lecturer = await admin.PutAsJsonAsync("/api/admin/lecturers/L94204", new { fullName = "Renamed By Demo Visitor", title = "Dr", department = "YH", email = (string?)null }))
        {
            await lecturer.AssertProblemAsync(HttpStatusCode.Conflict, "demo-account");
        }

        Assert.Equal("Renamed Demo Student", (await factory.ReadUserAsync("s942201")).DisplayName);
        Assert.Equal("Real Lecturer", (await factory.ReadUserAsync("l94204")).DisplayName);
        Assert.Equal("Renamed Demo Student", (await admin.GetJsonAsync($"/api/admin/students/{number}")).GetProperty("student").GetProperty("fullName").GetString());

        // A real administrator may.
        using var real = await factory.RealAdminAsync();
        await real.Client.PutJsonAsync("/api/admin/lecturers/L94204", new { fullName = "Renamed By The Registry", title = "Dr", department = "YH", email = (string?)null });
        Assert.Equal("Renamed By The Registry", (await factory.ReadUserAsync("l94204")).DisplayName);
    }

    /// <summary>
    /// E7: a module with nothing this year but earlier years' enrolments and published marks keeps its semester (422
    /// <c>semester-change-with-enrolments</c>, <c>enrolledCount</c> counting every year's enrolments). Before the fix the
    /// guard read only this year's count and the change moved last year's published results into another semester.
    /// </summary>
    [Fact]
    public async Task The_semester_is_pinned_by_any_earlier_years_enrolments_or_marks()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YH3001", Semester.Autumn, capacity: 10);
        var number = await ResultsGovernanceTests.NewStudentAsync(admin, "S942301");
        await factory.InsertEnrolmentAsync(number, "YH3001", StudentData.PreviousYear);
        await factory.PutGradeAsync(number, "YH3001", GradeStatus.Published, 64, publishedAt: RushDayApiFactory.ClockStart.AddDays(-1));

        var refused = await admin.PutJsonAsync("/api/admin/modules/YH3001", new { title = "Moved", description = (string?)null, credits = 15, capacity = 10, semester = "spring", isActive = true }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(("urn:rushday:semester-change-with-enrolments", 1), (refused.GetProperty("type").GetString(), refused.GetProperty("enrolledCount").GetInt32()));
        Assert.Equal(Semester.Autumn, await factory.WithDbAsync(db => db.Modules.Where(m => m.Code == "YH3001").Select(m => m.Semester).SingleAsync()));

        // A module nobody has ever enrolled on may still move.
        await admin.CreateModuleAsync("YH3002", Semester.Autumn, capacity: 10);
        var moved = await admin.PutJsonAsync("/api/admin/modules/YH3002", new { title = "Moved", description = (string?)null, credits = 15, capacity = 10, semester = "spring", isActive = true });
        Assert.Equal("spring", moved.GetProperty("semester").GetString());
    }

    /// <summary>
    /// E8 (the reviewer's P12): marking a student as left withdraws only this year's enrolments without a submitted or
    /// published mark; one with a published mark stays active, so the mark stays visible in the registry's view and in
    /// the subject-access export. Before the fix the leave withdrew it and the export lost the grade.
    /// </summary>
    [Fact]
    public async Task Leaving_keeps_the_enrolments_that_hold_results()
    {
        using var admin = await factory.DemoAdminAsync();
        var number = await ResultsGovernanceTests.NewStudentAsync(admin, "S942401");
        await admin.CreateModuleAsync("YH4001", Semester.Autumn, capacity: 10);
        await admin.CreateModuleAsync("YH4002", Semester.Autumn, capacity: 10);
        await admin.OverrideEnrolAsync(number, "YH4001");
        await admin.OverrideEnrolAsync(number, "YH4002");
        await factory.PutGradeAsync(number, "YH4001", GradeStatus.Published, 67, publishedAt: RushDayApiFactory.ClockStart.AddDays(-1));
        var before = (await admin.GetJsonAsync($"/api/admin/students/{number}/export.json")).GetProperty("grades").GetArrayLength();

        var left = await admin.PostJsonAsync($"/api/admin/students/{number}/leave", new { reason = Reason });
        Assert.Equal(1, left.GetProperty("withdrawn").GetInt32());
        Assert.Equal(EnrolmentStatus.Active, (await factory.EnrolmentAsync(number, "YH4001"))!.Status);
        Assert.Equal(EnrolmentStatus.Withdrawn, (await factory.EnrolmentAsync(number, "YH4002"))!.Status);

        var after = await admin.GetJsonAsync($"/api/admin/students/{number}/export.json");
        Assert.Equal(before, after.GetProperty("grades").GetArrayLength());
        Assert.Contains("YH4001", after.GetProperty("grades").Strings("moduleCode"));
        var withdrawal = (await factory.AuditAsync(AuditActions.EnrolmentAdminWithdrawn, (await factory.EnrolmentAsync(number, "YH4002"))!.Id.ToString())).Single();
        Assert.True(StaffData.DetailsOf(withdrawal).GetProperty("left").GetBoolean());
    }

    /// <summary>
    /// E10: at most one leader per module is a database rule now (a partial unique index), so no pair of concurrent
    /// assignments can leave two; the assignment route serialises on the module row. Before the fix a second leader row
    /// inserted, and two concurrent assignments left two leaders (the reviewer's P7, 2 in 15 rounds).
    /// </summary>
    [Fact]
    public async Task A_module_never_has_two_leaders()
    {
        using var admin = await factory.DemoAdminAsync();
        using var other = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YH5001", Semester.Spring, capacity: 10);
        await admin.AssignAsync("YH5001", "L00011");

        var moduleId = await factory.ModuleIdAsync("YH5001");
        var secondLeader = await factory.WithDbAsync(db => db.Lecturers.Where(l => l.StaffNumber == "L00012").Select(l => l.Id).SingleAsync());
        var insert = await Assert.ThrowsAsync<PostgresException>(() => DbProbe.ExecAsync(
            factory,
            "INSERT INTO module_lecturers (module_id, lecturer_id, role, assigned_at) VALUES (@m, @l, 'Leader', now())",
            ("m", moduleId),
            ("l", secondLeader)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, insert.SqlState);

        for (var round = 0; round < 8; round++)
        {
            var one = admin.PutAsJsonAsync("/api/admin/modules/YH5001/lecturers", new { assignments = new[] { new { staffNumber = "L00011", role = "teacher" }, new { staffNumber = "L00012", role = "leader" } } });
            var two = other.PutAsJsonAsync("/api/admin/modules/YH5001/lecturers", new { assignments = new[] { new { staffNumber = "L00013", role = "teacher" }, new { staffNumber = "L00011", role = "leader" } } });
            foreach (var response in await Task.WhenAll(one, two))
            {
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"An assignment answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                response.Dispose();
            }

            Assert.Equal(1, await factory.WithDbAsync(db => db.ModuleLecturers.CountAsync(ml => ml.ModuleId == moduleId && ml.Role == ModuleLecturerRole.Leader)));
        }
    }

    /// <summary>E11 (the reviewer's P6): a null element in a list body is 400 <c>validation</c>, never a 500.</summary>
    [Fact]
    public async Task Null_list_elements_are_validation_problems()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YH6001", Semester.Autumn, capacity: 10);
        await admin.AssignAsync("YH6001", "L00001");
        using var lecturer = await factory.LecturerAsync("L00001");

        using (var rows = await RawAsync(lecturer, HttpMethod.Put, "/api/lecturer/modules/YH6001/marks", "{\"rows\":[null]}"))
        {
            await rows.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        using (var assignments = await RawAsync(admin, HttpMethod.Put, "/api/admin/modules/YH6001/lecturers", "{\"assignments\":[null]}"))
        {
            await assignments.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        using (var mixed = await RawAsync(lecturer, HttpMethod.Put, "/api/lecturer/modules/YH6001/marks", "{\"rows\":[{\"studentNumber\":\"S000001\",\"mark\":50,\"version\":null},null]}"))
        {
            await mixed.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }
    }

    /// <summary>
    /// E13: enum members travel as names only; <c>"outcome": 1</c>, <c>"semester": 1</c> and <c>"role": 0</c> are 400
    /// <c>validation</c>. Before the fix they bound to a member (<c>1</c> saved an absence).
    /// </summary>
    [Fact]
    public async Task Integer_enum_values_are_validation_problems()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YH6002", Semester.Autumn, capacity: 10);
        await admin.AssignAsync("YH6002", "L00001");
        var number = await ResultsGovernanceTests.NewStudentAsync(admin, "S942601");
        await admin.OverrideEnrolAsync(number, "YH6002");
        using var lecturer = await factory.LecturerAsync("L00001");

        foreach (var (client, method, path, json) in new (HttpClient, HttpMethod, string, string)[]
        {
            (lecturer, HttpMethod.Put, "/api/lecturer/modules/YH6002/marks", $"{{\"rows\":[{{\"studentNumber\":\"{number}\",\"mark\":null,\"outcome\":1,\"version\":null}}]}}"),
            (lecturer, HttpMethod.Put, "/api/lecturer/modules/YH6002/marks", $"{{\"rows\":[{{\"studentNumber\":\"{number}\",\"mark\":null,\"outcome\":\"1\",\"version\":null}}]}}"),
            (admin, HttpMethod.Post, "/api/admin/results/publish", "{\"academicYear\":\"2090/91\",\"semester\":1,\"publishAt\":\"2026-09-27T12:00:00Z\",\"announce\":false}"),
            (admin, HttpMethod.Put, "/api/admin/modules/YH6002/lecturers", "{\"assignments\":[{\"staffNumber\":\"L00001\",\"role\":0}]}"),
        })
        {
            using var response = await RawAsync(client, method, path, json);
            await response.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        Assert.False(await factory.WithDbAsync(async db =>
        {
            var moduleId = await db.Modules.Where(m => m.Code == "YH6002").Select(m => m.Id).SingleAsync();
            return await db.Grades.AnyAsync(g => g.ModuleId == moduleId);
        }));
    }

    /// <summary>
    /// E14: a page may end at row 10,000 at most (<c>page x pageSize</c>); beyond it is 400 <c>validation</c> on
    /// <c>page</c>, and the filters or the CSV export reach older rows. Before the fix page 2,000 of the audit log ran
    /// a full-count, deep-offset query.
    /// </summary>
    [Fact]
    public async Task Paging_stops_at_row_ten_thousand()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YH6003", Semester.Autumn, capacity: 10);
        await admin.AssignAsync("YH6003", "L00001");
        using var lecturer = await factory.LecturerAsync("L00001");

        foreach (var path in new[]
        {
            "/api/admin/audit?page=201&pageSize=50",
            "/api/admin/audit?page=2000",
            "/api/admin/students?page=101&pageSize=100",
            "/api/admin/accounts?page=401",
            "/api/admin/modules/YH6003/roster?page=51&pageSize=200",
            "/api/admin/modules/YH6003/marks?page=21&pageSize=500",
            "/api/lecturer/modules/YH6003/roster?page=51&pageSize=200",
            "/api/lecturer/modules/YH6003/marks?page=101",
        })
        {
            var client = path.StartsWith("/api/lecturer", StringComparison.Ordinal) ? lecturer : admin;
            using var response = await client.GetAsync(path);
            var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
            Assert.True(problem.GetProperty("errors").TryGetProperty("page", out _), $"{path}: {problem}");
        }

        // The last allowed page is served.
        foreach (var path in new[] { "/api/admin/audit?page=200&pageSize=50", "/api/admin/students?page=100&pageSize=100", "/api/lecturer/modules/YH6003/roster?page=50&pageSize=200" })
        {
            var client = path.StartsWith("/api/lecturer", StringComparison.Ordinal) ? lecturer : admin;
            var page = await client.GetJsonAsync(path);
            Assert.Equal(int.Parse(path.Split("page=")[1].Split('&')[0], CultureInfo.InvariantCulture), page.GetProperty("page").GetInt32());
        }
    }

    /// <summary>
    /// E15: the lecturer's roster CSV is a bulk read of personal data, so it writes <c>roster.exported</c> (subject the
    /// module, the year and the row count, the lecturer as actor) before the file is sent. Before the fix it was not
    /// audited.
    /// </summary>
    [Fact]
    public async Task The_roster_export_is_audited()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YH7001", Semester.Autumn, capacity: 10);
        await admin.AssignAsync("YH7001", "L00001");
        await admin.OverrideEnrolAsync(await ResultsGovernanceTests.NewStudentAsync(admin, "S942701"), "YH7001");
        await admin.OverrideEnrolAsync(await ResultsGovernanceTests.NewStudentAsync(admin, "S942702"), "YH7001");
        using var lecturer = await factory.LecturerAsync("L00001");

        using var csv = await lecturer.GetAsync("/api/lecturer/modules/YH7001/roster.csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Contains("S942701", await csv.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var row = Assert.Single(await factory.AuditAsync(AuditActions.RosterExported, "YH7001"));
        Assert.Equal(("L00001", AuditSubjects.Module), (row.ActorUsername, row.SubjectType));
        Assert.Equal(await factory.ModuleIdAsync("YH7001"), row.ModuleId);
        var details = StaffData.DetailsOf(row);
        Assert.Equal(("YH7001", StudentData.CurrentYear, 2), (details.GetProperty("moduleCode").GetString(), details.GetProperty("academicYear").GetString(), details.GetProperty("rowCount").GetInt32()));
    }

    private static async Task<HttpResponseMessage> RawAsync(HttpClient client, HttpMethod method, string path, string json)
    {
        using var request = new HttpRequestMessage(method, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        return await client.SendAsync(request);
    }
}
