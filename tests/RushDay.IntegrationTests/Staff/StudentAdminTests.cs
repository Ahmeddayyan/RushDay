using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// Student records (02-api.md section 8.5): create and search; edit keeps the login's display name in step; the
/// support view is audited; leaving withdraws this year's enrolments and disables the account in one transaction; a
/// demo session cannot disable a real account and a demo account cannot be disabled. Students <c>S96####</c> are
/// created here; <c>S000096</c>–<c>S000100</c> are seeded.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StudentAdminTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Create_search_and_edit_keep_the_display_name_in_step()
    {
        using var admin = await factory.DemoAdminAsync();

        var created = await admin.PostJsonAsync("/api/admin/students", new { studentNumber = "s960001", fullName = "Morwenna Tregarthen", programme = "BSc Registry Science", yearOfStudy = 2, email = "m.t@example.test" }, HttpStatusCode.Created);
        Assert.Equal(("S960001", "none", JsonValueKind.Null), (created.GetProperty("studentNumber").GetString(), created.GetProperty("accountState").GetString(), created.GetProperty("leftAt").ValueKind));
        Assert.Single(await factory.AuditAsync(AuditActions.StudentCreated, "S960001"));

        var taken = await admin.PostJsonAsync("/api/admin/students", new { studentNumber = "S960001", fullName = "Again", programme = "BSc", yearOfStudy = 1 }, HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:student-number-taken", taken.GetProperty("type").GetString());
        foreach (var invalid in new object[]
        {
            new { studentNumber = "S96000", fullName = "x", programme = "x", yearOfStudy = 1 },
            new { studentNumber = "S96000１", fullName = "x", programme = "x", yearOfStudy = 1 },
            new { studentNumber = "S960002", fullName = "x", programme = "x", yearOfStudy = 7 },
            new { studentNumber = "S960002", fullName = "x", programme = "x", yearOfStudy = 1, email = "not-an-email" },
        })
        {
            using var response = await admin.PostAsJsonAsync("/api/admin/students", invalid);
            await response.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        // A login for the new student (a demo actor provisions demo accounts), then the list shows its state.
        await admin.PostJsonAsync("/api/admin/accounts", new { username = "S960001", displayName = "Morwenna Tregarthen", role = "Student", studentNumber = "S960001" }, HttpStatusCode.Created);
        var search = await admin.GetJsonAsync("/api/admin/students?q=tregarth");
        var row = Assert.Single(search.GetProperty("items").EnumerateArray());
        Assert.Equal(("S960001", "active"), (row.GetProperty("studentNumber").GetString(), row.GetProperty("accountState").GetString()));
        var none = await admin.GetJsonAsync("/api/admin/students?accountState=none&q=S96");
        Assert.DoesNotContain("S960001", none.GetProperty("items").Strings("studentNumber"));
        using (var badState = await admin.GetAsync("/api/admin/students?accountState=gone"))
        {
            await badState.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        var updated = await admin.PutJsonAsync("/api/admin/students/S960001", new { fullName = "Morwenna Pascoe", programme = "BSc Registry Science", yearOfStudy = 3, email = (string?)null });
        Assert.Equal(("Morwenna Pascoe", 3), (updated.GetProperty("fullName").GetString(), updated.GetProperty("yearOfStudy").GetInt32()));
        Assert.Equal("Morwenna Pascoe", (await factory.UserOfStudentAsync("S960001"))!.DisplayName);
        var audit = Assert.Single(await factory.AuditAsync(AuditActions.StudentUpdated, "S960001"));
        Assert.Equal(("Morwenna Tregarthen", "Morwenna Pascoe"), (StaffData.DetailsOf(audit).GetProperty("before").GetProperty("fullName").GetString(), StaffData.DetailsOf(audit).GetProperty("after").GetProperty("fullName").GetString()));

        using var missing = await admin.PutAsJsonAsync("/api/admin/students/S969999", new { fullName = "x", programme = "x", yearOfStudy = 1 });
        await missing.AssertProblemAsync(HttpStatusCode.NotFound, "student-not-found");
    }

    [Fact]
    public async Task Student_view_shows_statuses_and_visibility_and_is_audited()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YS8201");
        await admin.OverrideEnrolAsync("S000096", "YS8201");
        await factory.PutGradeAsync("S000096", "YS8201", GradeStatus.Draft, 44);

        var view = await admin.GetJsonAsync("/api/admin/students/s000096");
        Assert.Equal("S000096", view.GetProperty("student").GetProperty("studentNumber").GetString());
        Assert.Equal(("S000096", "Student", true), (view.GetProperty("account").GetProperty("username").GetString(), view.GetProperty("account").GetProperty("role").GetString(), view.GetProperty("account").GetProperty("isDemo").GetBoolean()));
        var grades = view.GetProperty("grades").EnumerateArray().ToList();
        var draft = grades.Single(g => g.GetProperty("moduleCode").GetString() == "YS8201");
        Assert.Equal(("draft", false, 44), (draft.GetProperty("status").GetString(), draft.GetProperty("visibleToStudent").GetBoolean(), draft.GetProperty("mark").GetInt32()));
        Assert.Contains(grades, g => g.GetProperty("status").GetString() == "published" && g.GetProperty("visibleToStudent").GetBoolean());
        Assert.Contains(view.GetProperty("enrolments").EnumerateArray(), e => e.GetProperty("moduleCode").GetString() == "YS8201" && e.GetProperty("source").GetString() == "admin");
        Assert.Equal(JsonValueKind.Number, view.GetProperty("weightedAverage").ValueKind);

        // The student sees the same average; the draft is not part of it.
        using var student = await factory.LoginStudentAsync("S000096");
        var results = await student.GetJsonAsync("/api/me/results");
        Assert.Equal(results.GetProperty("weightedAverage").GetDouble(), view.GetProperty("weightedAverage").GetDouble(), 6);

        var viewed = (await factory.AuditAsync(AuditActions.StudentViewed, "S000096")).First();
        Assert.Equal((AuditSubjects.Student, "S000096"), (viewed.SubjectType, StaffData.DetailsOf(viewed).GetProperty("studentNumber").GetString()));

        // The next view lists the previous one among the recent audit rows.
        var again = await admin.GetJsonAsync("/api/admin/students/S000096");
        Assert.Contains(again.GetProperty("recentAudit").EnumerateArray(), a => a.GetProperty("action").GetString() == AuditActions.StudentViewed);

        using var export = await admin.GetAsync("/api/admin/students/S000096/export.json");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("attachment; filename=\"rushday-S000096.json\"", export.Content.Headers.ContentDisposition?.ToString());
        var exported = await export.ReadJsonAsync();
        Assert.DoesNotContain(exported.GetProperty("grades").EnumerateArray(), g => g.GetProperty("moduleCode").GetString() == "YS8201");
        Assert.Single(await factory.AuditAsync(AuditActions.StudentExported, "S000096"));

        using var unknown = await admin.GetAsync("/api/admin/students/S999998");
        await unknown.AssertProblemAsync(HttpStatusCode.NotFound, "student-not-found");
    }

    [Fact]
    public async Task Leave_withdraws_this_years_enrolments_and_disables_the_account()
    {
        using var real = await factory.RealAdminAsync();
        var admin = real.Client;
        await admin.PostJsonAsync("/api/admin/students", new { studentNumber = "S960101", fullName = "Leaving Student", programme = "BSc", yearOfStudy = 1 }, HttpStatusCode.Created);
        var provisioned = await admin.PostJsonAsync("/api/admin/accounts", new { username = "S960101", displayName = "Leaving Student", role = "Student", studentNumber = "S960101", temporaryPassword = TestAccounts.Password }, HttpStatusCode.Created);
        Assert.False(provisioned.GetProperty("account").GetProperty("isDemo").GetBoolean());
        await admin.CreateModuleAsync("YS8301");
        await admin.CreateModuleAsync("YS8302", RushDay.Domain.Modules.Semester.Spring);
        await admin.OverrideEnrolAsync("S960101", "YS8301");
        await admin.OverrideEnrolAsync("S960101", "YS8302");
        await admin.CreateModuleAsync("YS8303");
        await factory.InsertEnrolmentAsync("S960101", "YS8303", StudentData.PreviousYear);
        var stampBefore = (await factory.UserOfStudentAsync("S960101"))!.SecurityStamp;

        var left = await admin.PostJsonAsync("/api/admin/students/S960101/leave", new { reason = StaffData.Reason });
        Assert.Equal(("S960101", 2), (left.GetProperty("studentNumber").GetString(), left.GetProperty("withdrawn").GetInt32()));
        Assert.Equal(factory.Clock.GetUtcNow(), left.GetProperty("leftAt").GetDateTimeOffset());

        Assert.Equal(EnrolmentStatus.Withdrawn, (await factory.EnrolmentAsync("S960101", "YS8301"))!.Status);
        Assert.Equal(EnrolmentStatus.Withdrawn, (await factory.EnrolmentAsync("S960101", "YS8302"))!.Status);
        Assert.Equal(EnrolmentStatus.Active, (await factory.EnrolmentAsync("S960101", "YS8303"))!.Status);
        Assert.Equal(0, await factory.ActiveCountAsync("YS8301"));

        var user = (await factory.UserOfStudentAsync("S960101"))!;
        Assert.NotNull(user.DisabledAt);
        Assert.NotEqual(stampBefore, user.SecurityStamp);
        Assert.Single(await factory.AuditAsync(AuditActions.AccountDisabled, user.Id.ToString()));
        var audit = Assert.Single(await factory.AuditAsync(AuditActions.StudentLeft, "S960101"));
        Assert.Equal(StaffData.Reason, StaffData.DetailsOf(audit).GetProperty("reason").GetString());
        var withdrawal = (await factory.AuditAsync(AuditActions.EnrolmentAdminWithdrawn, (await factory.EnrolmentAsync("S960101", "YS8301"))!.Id.ToString())).Single();
        Assert.True(StaffData.DetailsOf(withdrawal).GetProperty("left").GetBoolean());

        // Left is final: a second leave, and any enrolment, are refused.
        var again = await admin.PostJsonAsync("/api/admin/students/S960101/leave", new { reason = StaffData.Reason }, HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:student-left", again.GetProperty("type").GetString());
        var enrol = await admin.OverrideEnrolAsync("S960101", "YS8301", expected: HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:student-left", enrol.GetProperty("type").GetString());

        // The disabled account cannot sign in.
        using var client = factory.CreateCookieClient();
        await client.RefreshCsrfAsync();
        using var login = await client.PostLoginAsync("S960101", TestAccounts.Password);
        await login.AssertProblemAsync(HttpStatusCode.Unauthorized, "invalid-credentials");
    }

    [Fact]
    public async Task A_leave_that_would_disable_a_demo_account_is_refused_and_changes_nothing()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YS8401");
        await admin.OverrideEnrolAsync("S000097", "YS8401");

        var refused = await admin.PostJsonAsync("/api/admin/students/S000097/leave", new { reason = StaffData.Reason }, HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:demo-account", refused.GetProperty("type").GetString());
        Assert.Equal(EnrolmentStatus.Active, (await factory.EnrolmentAsync("S000097", "YS8401"))!.Status);
        Assert.Null((await factory.UserOfStudentAsync("S000097"))!.DisabledAt);
        Assert.Empty(await factory.AuditAsync(AuditActions.StudentLeft, "S000097"));

        // A student without a login can be marked as left by the demo administrator.
        await admin.PostJsonAsync("/api/admin/students", new { studentNumber = "S960201", fullName = "No Login", programme = "BSc", yearOfStudy = 1 }, HttpStatusCode.Created);
        var left = await admin.PostJsonAsync("/api/admin/students/S960201/leave", new { reason = StaffData.Reason });
        Assert.Equal(0, left.GetProperty("withdrawn").GetInt32());

        using var shortReason = await admin.PostAsJsonAsync("/api/admin/students/S000097/leave", new { reason = "no" });
        await shortReason.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
    }
}
