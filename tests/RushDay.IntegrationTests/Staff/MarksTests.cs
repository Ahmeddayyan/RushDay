using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Grades;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// Marks entry and submission (02-api.md section 8.4): saves are all-or-nothing per request with optimistic versions;
/// only active enrolments of the current year may be marked; the outcome/mark rule is request validation; only the
/// leader submits, and only a complete module; a withdrawn student's draft stays Draft; a submitted module is locked.
/// Modules <c>YM####</c>, led by L00021 with L00022 teaching, students <c>S000055</c>–<c>S000060</c>, <c>S000063</c> and <c>S000064</c> (S4 uses S000061 and S000062).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MarksTests(RushDayApiFactory factory)
{
    private const string Leader = "L00021";
    private const string Teacher = "L00022";

    [Fact]
    public async Task Save_is_all_or_nothing_and_stale_rows_are_refused()
    {
        const string code = "YM3101";
        using var admin = await factory.DemoAdminAsync();
        await SetUpAsync(admin, code, "S000055", "S000056");
        using var lecturer = await factory.LecturerAsync(Leader);

        var saved = await lecturer.SaveMarksAsync(code, [Row("S000055", 61, null), Row("S000056", 48, null)]);
        var rows = saved.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(["S000055", "S000056"], rows.Select(r => r.GetProperty("studentNumber").GetString()));
        Assert.All(rows, r =>
        {
            Assert.Equal(1, r.GetProperty("version").GetInt32());
            Assert.Equal("draft", r.GetProperty("gradeStatus").GetString());
            Assert.Equal("mark", r.GetProperty("outcome").GetString());
            Assert.Equal("active", r.GetProperty("enrolmentStatus").GetString());
            Assert.False(string.IsNullOrEmpty(r.GetProperty("enteredBy").GetString()));
        });
        var summary = saved.GetProperty("summary");
        Assert.Equal((2, 0, 2), (summary.GetProperty("entered").GetInt32(), summary.GetProperty("missing").GetInt32(), summary.GetProperty("total").GetInt32()));
        Assert.Equal(2, (await AuditForModuleAsync(AuditActions.GradeEntered, code)).Count);

        // One stale row in the batch: 409 naming it, and nothing of the batch is written.
        var problem = await PutMarksProblemAsync(lecturer, code, [Row("S000055", 70, 1), Row("S000056", 50, 7)], HttpStatusCode.Conflict, "stale-mark");
        Assert.Equal(["S000056"], problem.GetProperty("studentNumbers").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(61, (await factory.GradeAsync("S000055", code)).Mark);
        Assert.Equal(1, (await factory.GradeAsync("S000055", code)).Version);
        Assert.Empty(await AuditForModuleAsync(AuditActions.GradeChanged, code));

        // A client that saw an empty row (version null) while another saved it is stale too.
        await PutMarksProblemAsync(lecturer, code, [Row("S000056", 50, null)], HttpStatusCode.Conflict, "stale-mark");

        // With the right versions the change is written, versioned and audited with before and after.
        var changed = await lecturer.SaveMarksAsync(code, [Row("S000055", 70, 1), Row("S000056", 48, 1)]);
        var first = changed.GetProperty("rows")[0];
        Assert.Equal((70, 2), (first.GetProperty("mark").GetInt32(), first.GetProperty("version").GetInt32()));

        // The unchanged row was not written: still version 1, and only one grade.changed row exists.
        Assert.Equal(1, changed.GetProperty("rows")[1].GetProperty("version").GetInt32());
        var change = Assert.Single(await AuditForModuleAsync(AuditActions.GradeChanged, code));
        var details = StaffData.DetailsOf(change);
        Assert.Equal(61, details.GetProperty("before").GetProperty("mark").GetInt32());
        Assert.Equal(70, details.GetProperty("after").GetProperty("mark").GetInt32());
        Assert.Equal("S000055", details.GetProperty("studentNumber").GetString());
        Assert.Equal(2, details.GetProperty("version").GetInt32());

        // A teacher may save drafts too.
        using var teacher = await factory.LecturerAsync(Teacher);
        await teacher.SaveMarksAsync(code, [Row("S000056", 49, 1)]);
    }

    [Fact]
    public async Task Only_active_enrolments_of_this_year_can_be_marked()
    {
        const string code = "YM3102";
        using var admin = await factory.DemoAdminAsync();
        await SetUpAsync(admin, code, "S000057");
        using var lecturer = await factory.LecturerAsync(Leader);

        // S000058 is not enrolled; S000055 is enrolled elsewhere, not here. Nothing is saved for S000057 either.
        var problem = await PutMarksProblemAsync(lecturer, code, [Row("S000057", 50, null), Row("S000058", 50, null), Row("s000055", 50, null)], HttpStatusCode.UnprocessableEntity, "not-enrolled-students");
        Assert.Equal(["S000058", "S000055"], problem.GetProperty("studentNumbers").EnumerateArray().Select(e => e.GetString()));
        Assert.Null(await TryGradeAsync("S000057", code));
    }

    [Fact]
    public async Task Outcomes_follow_the_mark_rule()
    {
        const string code = "YM3103";
        using var admin = await factory.DemoAdminAsync();
        await SetUpAsync(admin, code, "S000059", "S000060");
        using var lecturer = await factory.LecturerAsync(Leader);

        await PutMarksProblemAsync(lecturer, code, [new { studentNumber = "S000059", mark = 40, outcome = "absent", version = (int?)null }], HttpStatusCode.BadRequest, "validation");
        await PutMarksProblemAsync(lecturer, code, [new { studentNumber = "S000059", mark = (int?)null, outcome = "mark", version = (int?)null }], HttpStatusCode.BadRequest, "validation");
        await PutMarksProblemAsync(lecturer, code, [Row("S000059", 101, null)], HttpStatusCode.BadRequest, "validation");
        await PutMarksProblemAsync(lecturer, code, [Row("S000059", 50, null), Row("S000059", 51, null)], HttpStatusCode.BadRequest, "validation");
        await PutMarksProblemAsync(lecturer, code, [Row("S٣٠٠٠٥٩", 50, null)], HttpStatusCode.BadRequest, "validation");
        await PutMarksProblemAsync(lecturer, code, [], HttpStatusCode.BadRequest, "validation");

        var saved = await lecturer.SaveMarksAsync(code,
        [
            new { studentNumber = "S000059", mark = (int?)null, outcome = "absent", version = (int?)null },
            new { studentNumber = "S000060", mark = (int?)null, outcome = "deferred", version = (int?)null },
        ]);
        var rows = saved.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(["absent", "deferred"], rows.Select(r => r.GetProperty("outcome").GetString()));
        Assert.All(rows, r => Assert.Equal(JsonValueKind.Null, r.GetProperty("mark").ValueKind));

        // An absence can later become a mark.
        var mark = await lecturer.SaveMarksAsync(code, [Row("S000059", 38, 1)]);
        Assert.Equal(("mark", 38), (mark.GetProperty("rows")[0].GetProperty("outcome").GetString(), mark.GetProperty("rows")[0].GetProperty("mark").GetInt32()));
    }

    [Fact]
    public async Task Submit_is_the_leaders_and_needs_every_mark()
    {
        const string code = "YM3104";
        using var admin = await factory.DemoAdminAsync();
        await SetUpAsync(admin, code, "S000060", "S000064", "S000063");
        using var lecturer = await factory.LecturerAsync(Leader);
        using var teacher = await factory.LecturerAsync(Teacher);

        await lecturer.SaveMarksAsync(code, [Row("S000060", 72, null), Row("S000064", 55, null)]);

        using (var byTeacher = await teacher.PostAsync($"/api/lecturer/modules/{code}/marks/submit", null))
        {
            var body = await byTeacher.AssertProblemAsync(HttpStatusCode.Forbidden, "not-module-leader");
            Assert.Equal("Only the module leader can submit marks", body.GetProperty("detail").GetString());
        }

        using (var incomplete = await lecturer.PostAsync($"/api/lecturer/modules/{code}/marks/submit", null))
        {
            var body = await incomplete.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "marks-incomplete");
            Assert.Equal(["S000063"], body.GetProperty("missing").EnumerateArray().Select(e => e.GetString()));
        }

        // S000063 is withdrawn by an administrator: no longer counted as missing, and their draft (none here) stays out.
        using (var withdraw = await admin.PostAsJsonAsync($"/api/admin/students/S000063/enrolments/{code}/withdraw", new { reason = StaffData.Reason }))
        {
            Assert.Equal(HttpStatusCode.NoContent, withdraw.StatusCode);
        }

        var submitted = await lecturer.PostJsonAsync($"/api/lecturer/modules/{code}/marks/submit", null);
        Assert.Equal((code, "submitted", 2), (submitted.GetProperty("code").GetString(), submitted.GetProperty("status").GetString(), submitted.GetProperty("gradeCount").GetInt32()));
        Assert.Equal(GradeStatus.Submitted, (await factory.GradeAsync("S000060", code)).Status);
        var audit = Assert.Single(await factory.AuditAsync(AuditActions.ModuleMarksSubmitted, code));
        Assert.Equal(2, StaffData.DetailsOf(audit).GetProperty("gradeCount").GetInt32());
        Assert.Equal(StaffData.CurrentYear, StaffData.DetailsOf(audit).GetProperty("academicYear").GetString());

        // Locked for everyone once submitted.
        await PutMarksProblemAsync(lecturer, code, [Row("S000060", 73, 2)], HttpStatusCode.Conflict, "module-locked");
        await PutMarksProblemAsync(teacher, code, [Row("S000064", 56, 2)], HttpStatusCode.Conflict, "module-locked");
        using (var again = await lecturer.PostAsync($"/api/lecturer/modules/{code}/marks/submit", null))
        {
            await again.AssertProblemAsync(HttpStatusCode.Conflict, "already-submitted");
        }

        var sheet = await lecturer.GetJsonAsync($"/api/lecturer/modules/{code}/marks");
        Assert.Equal("submitted", sheet.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, sheet.GetProperty("submittedAt").ValueKind);
    }

    [Fact]
    public async Task Withdrawn_students_drafts_stay_draft()
    {
        const string code = "YM3105";
        using var admin = await factory.DemoAdminAsync();
        await SetUpAsync(admin, code, "S000064", "S000055");
        using var lecturer = await factory.LecturerAsync(Leader);
        await lecturer.SaveMarksAsync(code, [Row("S000064", 44, null), Row("S000055", 66, null)]);

        using (var withdraw = await admin.PostAsJsonAsync($"/api/admin/students/S000064/enrolments/{code}/withdraw", new { reason = StaffData.Reason }))
        {
            Assert.Equal(HttpStatusCode.NoContent, withdraw.StatusCode);
        }

        var submitted = await lecturer.PostJsonAsync($"/api/lecturer/modules/{code}/marks/submit", null);
        Assert.Equal(1, submitted.GetProperty("gradeCount").GetInt32());
        Assert.Equal(GradeStatus.Draft, (await factory.GradeAsync("S000064", code)).Status);
        Assert.Equal(GradeStatus.Submitted, (await factory.GradeAsync("S000055", code)).Status);

        // The withdrawn student is listed after the active one, with their draft, and cannot be marked.
        var sheet = await lecturer.GetJsonAsync($"/api/lecturer/modules/{code}/marks");
        var rows = sheet.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(["S000055", "S000064"], rows.Select(r => r.GetProperty("studentNumber").GetString()));
        Assert.Equal(["active", "withdrawn"], rows.Select(r => r.GetProperty("enrolmentStatus").GetString()));
        Assert.Equal("draft", rows[1].GetProperty("gradeStatus").GetString());
        Assert.Equal((1, 0, 1), (sheet.GetProperty("summary").GetProperty("entered").GetInt32(), sheet.GetProperty("summary").GetProperty("missing").GetInt32(), sheet.GetProperty("summary").GetProperty("total").GetInt32()));
    }

    [Fact]
    public async Task A_module_without_students_has_nothing_to_submit()
    {
        const string code = "YM3106";
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync(code);
        await admin.AssignAsync(code, Leader, Teacher);
        using var lecturer = await factory.LecturerAsync(Leader);

        using var response = await lecturer.PostAsync($"/api/lecturer/modules/{code}/marks/submit", null);
        await response.AssertProblemAsync(HttpStatusCode.Conflict, "nothing-to-submit");

        var modules = await lecturer.GetJsonAsync("/api/lecturer/modules");
        var module = modules.EnumerateArray().Single(m => m.GetProperty("code").GetString() == code);
        Assert.Equal("noStudents", module.GetProperty("marks").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Lecturer_reads_are_scoped_and_shaped()
    {
        const string code = "YM3107";
        using var admin = await factory.DemoAdminAsync();
        await SetUpAsync(admin, code, "S000056", "S000057", "S000058");
        using var lecturer = await factory.LecturerAsync(Leader);
        using var teacher = await factory.LecturerAsync(Teacher);
        await lecturer.SaveMarksAsync(code, [Row("S000057", 58, null)]);

        var modules = await lecturer.GetJsonAsync("/api/lecturer/modules");
        var module = modules.EnumerateArray().Single(m => m.GetProperty("code").GetString() == code);
        Assert.Equal("leader", module.GetProperty("myRole").GetString());
        var marks = module.GetProperty("marks");
        Assert.Equal(("draft", 1, 2, 3), (marks.GetProperty("status").GetString(), marks.GetProperty("entered").GetInt32(), marks.GetProperty("missing").GetInt32(), marks.GetProperty("total").GetInt32()));
        Assert.Equal(3, module.GetProperty("enrolledCount").GetInt32());
        Assert.Equal(2, module.GetProperty("lecturers").GetArrayLength());
        Assert.Equal("teacher", (await teacher.GetJsonAsync("/api/lecturer/modules")).EnumerateArray().Single(m => m.GetProperty("code").GetString() == code).GetProperty("myRole").GetString());

        var roster = await lecturer.GetJsonAsync($"/api/lecturer/modules/{code}/roster?pageSize=2");
        Assert.Equal(code, roster.GetProperty("module").GetProperty("code").GetString());
        Assert.Equal((1, 2, 3), (roster.GetProperty("page").GetInt32(), roster.GetProperty("pageSize").GetInt32(), roster.GetProperty("total").GetInt32()));
        Assert.Equal(["S000056", "S000057"], roster.GetProperty("items").Strings("studentNumber"));

        // The q predicate: a student number prefix, or any fragment of the name.
        var byNumber = await lecturer.GetJsonAsync($"/api/lecturer/modules/{code}/roster?q=S00005");
        Assert.Equal(3, byNumber.GetProperty("total").GetInt32());
        var name = roster.GetProperty("items")[1].GetProperty("fullName").GetString()!;
        var byName = await lecturer.GetJsonAsync($"/api/lecturer/modules/{code}/roster?q={Uri.EscapeDataString(name.Split(' ')[1].ToLowerInvariant())}");
        Assert.Contains("S000057", byName.GetProperty("items").Strings("studentNumber"));
        var wildcard = await lecturer.GetJsonAsync($"/api/lecturer/modules/{code}/roster?q=%25");
        Assert.Equal(0, wildcard.GetProperty("total").GetInt32());

        var sheet = await lecturer.GetJsonAsync($"/api/lecturer/modules/{code}/marks?pageSize=1&page=2");
        Assert.Equal((2, 1, 3), (sheet.GetProperty("page").GetInt32(), sheet.GetProperty("pageSize").GetInt32(), sheet.GetProperty("total").GetInt32()));
        Assert.Equal("S000057", sheet.GetProperty("rows")[0].GetProperty("studentNumber").GetString());
        Assert.Equal(58, sheet.GetProperty("rows")[0].GetProperty("mark").GetInt32());
        Assert.Equal("leader", sheet.GetProperty("myRole").GetString());
        Assert.StartsWith("Dr ", sheet.GetProperty("leader").GetString(), StringComparison.Ordinal);
        Assert.Equal(StaffData.CurrentYear, sheet.GetProperty("academicYear").GetString());
        Assert.Equal((1, 2, 3), (sheet.GetProperty("summary").GetProperty("entered").GetInt32(), sheet.GetProperty("summary").GetProperty("missing").GetInt32(), sheet.GetProperty("summary").GetProperty("total").GetInt32()));

        // A long search term is a validation problem, never a query.
        using var tooLong = await lecturer.GetAsync($"/api/lecturer/modules/{code}/roster?q={new string('a', 101)}");
        await tooLong.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");

        // The CSV roster: the canonical code in the file name, even for a lower-case route.
        using var csv = await lecturer.GetAsync($"/api/lecturer/modules/{code.ToLowerInvariant()}/roster.csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal($"attachment; filename=\"roster-{code}.csv\"", csv.Content.Headers.ContentDisposition?.ToString());
        var lines = (await csv.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
    }

    private static object Row(string studentNumber, int? mark, int? version) => new { studentNumber, mark, outcome = "mark", version };

    private static async Task<JsonElement> PutMarksProblemAsync(HttpClient lecturer, string code, object[] rows, HttpStatusCode status, string slug)
    {
        using var response = await lecturer.PutAsJsonAsync($"/api/lecturer/modules/{code}/marks", new { rows });
        return await response.AssertProblemAsync(status, slug);
    }

    private async Task SetUpAsync(HttpClient admin, string code, params string[] students)
    {
        await admin.CreateModuleAsync(code);
        await admin.AssignAsync(code, Leader, Teacher);
        foreach (var student in students)
        {
            await admin.OverrideEnrolAsync(student, code);
        }
    }

    private Task<List<AuditEvent>> AuditForModuleAsync(string action, string code) =>
        factory.WithDbAsync(async db =>
        {
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            return await db.AuditEvents.AsNoTracking().Where(a => a.Action == action && a.ModuleId == moduleId).ToListAsync();
        });

    private Task<Grade?> TryGradeAsync(string studentNumber, string code) =>
        factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == studentNumber).Select(s => s.Id).SingleAsync();
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            return await db.Grades.AsNoTracking().SingleOrDefaultAsync(g => g.StudentId == studentId && g.ModuleId == moduleId);
        });
}
