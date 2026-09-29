using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// Module administration (02-api.md section 8.5): create; the capacity rule (only a request that lowers capacity below
/// <c>enrolled_count</c> is refused, an unchanged capacity is accepted on an oversold module); no semester change while
/// students hold places; lecturer assignment and its effect on <c>TeachesModule</c>; trim to capacity; the read-only
/// roster and marks routes. Modules <c>YA####</c>, students <c>S000090</c>–<c>S000095</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AdminModuleTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Create_answers_the_detail_and_refuses_a_taken_code()
    {
        using var admin = await factory.DemoAdminAsync();

        var created = await admin.PostJsonAsync("/api/admin/modules", new { code = "ya7101", title = "Registry Studies", description = "A module.", credits = 20, capacity = 40, semester = "spring" }, HttpStatusCode.Created);
        Assert.Equal(("YA7101", "YA", 7, 20, "spring"), (created.GetProperty("code").GetString(), created.GetProperty("department").GetString(), created.GetProperty("level").GetInt32(), created.GetProperty("credits").GetInt32(), created.GetProperty("semester").GetString()));
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal("open", created.GetProperty("enrolmentState").GetString());
        Assert.Empty(created.GetProperty("timetable").EnumerateArray());
        Assert.Single(await factory.AuditAsync(AuditActions.ModuleCreated, "YA7101"));

        // The catalogue shows it at once (catalogue:all invalidated).
        Assert.Contains("YA7101", (await admin.GetJsonAsync("/api/modules")).Strings("code"));

        var taken = await admin.PostJsonAsync("/api/admin/modules", new { code = "YA7101", title = "Again", credits = 20, capacity = 40, semester = "spring" }, HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:module-code-taken", taken.GetProperty("type").GetString());

        foreach (var invalid in new object[]
        {
            new { code = "YA710", title = "x", credits = 20, capacity = 40, semester = "spring" },
            new { code = "YＡ７101", title = "x", credits = 20, capacity = 40, semester = "spring" },
            new { code = "YA7102", title = "x", credits = 4, capacity = 40, semester = "spring" },
            new { code = "YA7102", title = "x", credits = 20, capacity = -1, semester = "spring" },
            new { code = "YA7102", title = "x\u0000y", credits = 20, capacity = 40, semester = "spring" },
            new { code = "YA7102", title = "x", credits = 20, capacity = 40, semester = "summer" },
        })
        {
            using var response = await admin.PostAsJsonAsync("/api/admin/modules", invalid);
            await response.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }
    }

    [Fact]
    public async Task Capacity_rule_accepts_an_unchanged_capacity_on_an_oversold_module()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YA7201", capacity: 3);
        foreach (var student in new[] { "S000090", "S000099", "S000092" })
        {
            await admin.OverrideEnrolAsync(student, "YA7201");
        }

        var below = await admin.PutJsonAsync("/api/admin/modules/YA7201", Update(capacity: 2), HttpStatusCode.UnprocessableEntity);
        Assert.Equal(("urn:rushday:capacity-below-enrolled", 3), (below.GetProperty("type").GetString(), below.GetProperty("enrolledCount").GetInt32()));

        // Oversold, as v0 left CS3099: capacity 2 with 3 enrolled.
        await factory.WithDbAsync(db => db.Modules.Where(m => m.Code == "YA7201").ExecuteUpdateAsync(s => s.SetProperty(m => m.Capacity, 2)));

        var unchanged = await admin.PutJsonAsync("/api/admin/modules/YA7201", Update(capacity: 2, title: "Renamed while oversold"));
        Assert.Equal(("Renamed while oversold", 2, 3), (unchanged.GetProperty("title").GetString(), unchanged.GetProperty("capacity").GetInt32(), unchanged.GetProperty("enrolledCount").GetInt32()));

        // Lowering further is refused; raising is always allowed.
        await admin.PutJsonAsync("/api/admin/modules/YA7201", Update(capacity: 1), HttpStatusCode.UnprocessableEntity);
        var raised = await admin.PutJsonAsync("/api/admin/modules/YA7201", Update(capacity: 3, title: "Raised"));
        Assert.Equal(3, raised.GetProperty("capacity").GetInt32());

        var semester = await admin.PutJsonAsync("/api/admin/modules/YA7201", Update(capacity: 3, semester: "spring"), HttpStatusCode.UnprocessableEntity);
        Assert.Equal(("urn:rushday:semester-change-with-enrolments", 3), (semester.GetProperty("type").GetString(), semester.GetProperty("enrolledCount").GetInt32()));

        var update = (await factory.AuditAsync(AuditActions.ModuleUpdated, "YA7201")).First();
        var details = StaffData.DetailsOf(update);
        Assert.Equal(("Renamed while oversold", "Raised"), (details.GetProperty("before").GetProperty("title").GetString(), details.GetProperty("after").GetProperty("title").GetString()));
        Assert.Equal((2, 3), (details.GetProperty("before").GetProperty("capacity").GetInt32(), details.GetProperty("after").GetProperty("capacity").GetInt32()));

        using var missing = await admin.PutAsJsonAsync("/api/admin/modules/YA7299", Update(capacity: 2));
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "module-not-found");
    }

    [Fact]
    public async Task Trim_withdraws_the_latest_enrolments_down_to_capacity()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YA7301", capacity: 10);
        var students = new[] { "S000090", "S000099", "S000092", "S000093" };
        foreach (var student in students)
        {
            await admin.OverrideEnrolAsync(student, "YA7301");
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        await factory.WithDbAsync(db => db.Modules.Where(m => m.Code == "YA7301").ExecuteUpdateAsync(s => s.SetProperty(m => m.Capacity, 2)));

        var trimmed = await admin.PostJsonAsync("/api/admin/modules/YA7301/trim-to-capacity", new { reason = StaffData.Reason });
        Assert.Equal(("YA7301", 2, 4, 2), (trimmed.GetProperty("code").GetString(), trimmed.GetProperty("capacity").GetInt32(), trimmed.GetProperty("before").GetInt32(), trimmed.GetProperty("after").GetInt32()));
        Assert.Equal(["S000093", "S000092"], trimmed.GetProperty("withdrawn").EnumerateArray().Select(e => e.GetString()));

        Assert.Equal(2, await factory.ActiveCountAsync("YA7301"));
        Assert.Equal(EnrolmentStatus.Active, (await factory.EnrolmentAsync("S000090", "YA7301"))!.Status);
        Assert.Equal(EnrolmentStatus.Withdrawn, (await factory.EnrolmentAsync("S000093", "YA7301"))!.Status);

        var audit = Assert.Single(await factory.AuditAsync(AuditActions.ModuleTrimmed, "YA7301"));
        Assert.Equal(["S000093", "S000092"], StaffData.DetailsOf(audit).GetProperty("withdrawn").EnumerateArray().Select(e => e.GetString()));
        var withdrawal = (await factory.AuditAsync(AuditActions.EnrolmentAdminWithdrawn, (await factory.EnrolmentAsync("S000093", "YA7301"))!.Id.ToString())).Single();
        Assert.True(StaffData.DetailsOf(withdrawal).GetProperty("trim").GetBoolean());

        // At capacity: nothing more to withdraw.
        var again = await admin.PostJsonAsync("/api/admin/modules/YA7301/trim-to-capacity", new { reason = StaffData.Reason });
        Assert.Empty(again.GetProperty("withdrawn").EnumerateArray());
    }

    [Fact]
    public async Task Lecturer_assignment_follows_the_rules_and_the_policy_follows_it()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YA7401");
        using var lecturer = await factory.LecturerAsync("L00031");

        // Not assigned yet (and the policy's cache has seen that).
        using (var before = await lecturer.GetAsync("/api/lecturer/modules/YA7401/roster"))
        {
            await before.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
        }

        var detail = await admin.AssignAsync("YA7401", "l00031", "L00032");
        Assert.Equal(["L00031", "L00032"], detail.GetProperty("lecturers").Strings("staffNumber"));
        Assert.Equal(["leader", "teacher"], detail.GetProperty("lecturers").Strings("role"));
        var set = Assert.Single(await factory.AuditAsync(AuditActions.ModuleLecturersSet, "YA7401"));
        Assert.Equal(["L00031", "L00032"], StaffData.DetailsOf(set).GetProperty("after").EnumerateArray().Select(e => e.GetString()));

        // The assignment takes effect at once (lecturer-modules invalidated), and so does its removal.
        using (var after = await lecturer.GetAsync("/api/lecturer/modules/YA7401/roster"))
        {
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        }

        await admin.AssignAsync("YA7401", "L00032");
        using (var removed = await lecturer.GetAsync("/api/lecturer/modules/YA7401/marks"))
        {
            await removed.AssertProblemAsync(HttpStatusCode.Forbidden, "not-your-module");
        }

        foreach (var invalid in new object[]
        {
            new { assignments = new[] { new { staffNumber = "L00031", role = "teacher" } } },
            new { assignments = new[] { new { staffNumber = "L00031", role = "leader" }, new { staffNumber = "L00032", role = "leader" } } },
            new { assignments = new[] { new { staffNumber = "L00031", role = "leader" }, new { staffNumber = "L00031", role = "teacher" } } },
            new { assignments = Array.Empty<object>() },
        })
        {
            var problem = await admin.PutJsonAsync("/api/admin/modules/YA7401/lecturers", invalid, HttpStatusCode.UnprocessableEntity);
            Assert.Equal("urn:rushday:invalid-lecturer-assignment", problem.GetProperty("type").GetString());
        }

        var unknown = await admin.PutJsonAsync("/api/admin/modules/YA7401/lecturers", new { assignments = new[] { new { staffNumber = "L99999", role = "leader" } } }, HttpStatusCode.NotFound);
        Assert.Equal("urn:rushday:lecturer-not-found", unknown.GetProperty("type").GetString());

        // A lecturer who has left cannot be assigned.
        await admin.PostJsonAsync("/api/admin/lecturers", new { staffNumber = "L90401", fullName = "Left Lecturer", title = "Dr", department = "YA" }, HttpStatusCode.Created);
        await admin.PostJsonAsync("/api/admin/lecturers/L90401/leave", new { reason = StaffData.Reason });
        await admin.PutJsonAsync("/api/admin/modules/YA7401/lecturers", new { assignments = new[] { new { staffNumber = "L90401", role = "leader" } } }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Administrators_read_any_modules_roster_and_marks()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YA7501");
        await admin.AssignAsync("YA7501", "L00033");
        await admin.OverrideEnrolAsync("S000094", "YA7501");
        await admin.OverrideEnrolAsync("S000095", "YA7501");
        using (var lecturer = await factory.LecturerAsync("L00033"))
        {
            await lecturer.SaveMarksAsync("YA7501", [new { studentNumber = "S000094", mark = 67, version = (int?)null }]);
        }

        var roster = await admin.GetJsonAsync("/api/admin/modules/YA7501/roster");
        Assert.Equal(["S000094", "S000095"], roster.GetProperty("items").Strings("studentNumber"));
        Assert.Equal("YA7501", roster.GetProperty("module").GetProperty("code").GetString());

        var marks = await admin.GetJsonAsync("/api/admin/modules/YA7501/marks");
        Assert.Equal(JsonValueKind.Null, marks.GetProperty("myRole").ValueKind);
        Assert.Equal(67, marks.GetProperty("rows")[0].GetProperty("mark").GetInt32());
        Assert.Equal("draft", marks.GetProperty("status").GetString());

        // Another year has nobody on it.
        var earlier = await admin.GetJsonAsync("/api/admin/modules/YA7501/roster?academicYear=2025%2F26");
        Assert.Equal(0, earlier.GetProperty("total").GetInt32());

        using (var unknown = await admin.GetAsync("/api/admin/modules/YA7599/marks"))
        {
            await unknown.AssertProblemAsync(HttpStatusCode.NotFound, "module-not-found");
        }

        using (var badYear = await admin.GetAsync("/api/admin/modules/YA7501/roster?academicYear=2025-26"))
        {
            await badYear.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        // There is no administrator write path into marks.
        using (var put = await admin.PutAsJsonAsync("/api/admin/modules/YA7501/marks", new { rows = new[] { new { studentNumber = "S000094", mark = 1, version = 1 } } }))
        {
            await put.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        }

        // The registry list carries marks and description; inactive modules only on request.
        var list = await admin.GetJsonAsync("/api/admin/modules");
        var module = list.EnumerateArray().Single(m => m.GetProperty("code").GetString() == "YA7501");
        Assert.Equal(("draft", 2), (module.GetProperty("marks").GetProperty("status").GetString(), module.GetProperty("marks").GetProperty("total").GetInt32()));
        Assert.Equal("Created by a staff test.", module.GetProperty("description").GetString());
        await admin.PutJsonAsync("/api/admin/modules/YA7501", Update(capacity: 100, isActive: false));
        Assert.DoesNotContain("YA7501", (await admin.GetJsonAsync("/api/admin/modules")).Strings("code"));
        Assert.Contains("YA7501", (await admin.GetJsonAsync("/api/admin/modules?includeInactive=true")).Strings("code"));
    }

    private static object Update(int capacity, string title = "Staff test module", string semester = "autumn", bool isActive = true) =>
        new { title, description = "Updated by a staff test.", credits = 15, capacity, semester, isActive };
}
