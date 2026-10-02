using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// The administrator's override enrolment and withdrawal (02-api.md section 8.5): windows and the credit limit are
/// ignored; capacity applies unless <c>forceCapacity</c>, which raises it by one only when the module is full;
/// <c>results-exist</c>, <c>student-left</c> and <c>module-inactive</c> still apply; reasons are required and audited.
/// Modules <c>YO####</c>, students <c>S000085</c>–<c>S000089</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AdminOverrideTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Window_and_credit_limit_are_ignored()
    {
        string[] codes = ["YO6101", "YO6102", "YO6103", "YO6104", "YO6105"];
        using var admin = await factory.DemoAdminAsync();
        foreach (var code in codes)
        {
            await admin.CreateModuleAsync(code, capacity: 50);
        }

        // Four autumn modules take S000086 to the 60-credit limit; the fifth is refused to the student...
        foreach (var code in codes[..4])
        {
            await admin.OverrideEnrolAsync("S000086", code);
        }

        using var student = await factory.LoginStudentAsync("S000086");
        using (var self = await student.EnrolAsync(codes[4]))
        {
            await self.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "credit-limit-exceeded");
        }

        // ...and accepted on the administrator's override.
        var body = await admin.OverrideEnrolAsync("S000086", codes[4]);
        Assert.Equal((codes[4], false), (body.GetProperty("moduleCode").GetString(), body.GetProperty("capacityRaised").GetBoolean()));
        Assert.Equal(49, body.GetProperty("placesRemaining").GetInt32());
        var enrolment = await factory.EnrolmentAsync("S000086", codes[4]);
        Assert.Equal((EnrolmentSource.Admin, StaffData.CurrentYear), (enrolment!.Source, enrolment.AcademicYear));
        var audit = (await factory.AuditAsync(AuditActions.EnrolmentAdminCreated, enrolment.Id.ToString())).Single();
        var details = StaffData.DetailsOf(audit);
        Assert.True(details.GetProperty("override").GetBoolean());
        Assert.Equal(StaffData.Reason, details.GetProperty("reason").GetString());

        // A closed window: on a host whose clock is past both 2026/27 windows, a student is refused, the override is not.
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 3, 1, 12, 0, 0, TimeSpan.Zero));
        await using var later = factory.Derive(clock: clock);
        await admin.CreateModuleAsync("YO6106", RushDay.Domain.Modules.Semester.Spring, capacity: 50);
        using var closedStudent = await later.LoginStudentAsync("S000085");
        using (var closed = await closedStudent.EnrolAsync("YO6106"))
        {
            await closed.AssertProblemAsync(HttpStatusCode.Conflict, "enrolment-window-closed");
        }

        using var laterAdmin = await later.DemoAdminAsync();
        await laterAdmin.OverrideEnrolAsync("S000085", "YO6106");
    }

    [Fact]
    public async Task Capacity_is_respected_unless_forced_and_raised_only_when_full()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YO6201", capacity: 2);
        await admin.OverrideEnrolAsync("S000087", "YO6201");
        await admin.OverrideEnrolAsync("S000088", "YO6201");

        var full = await admin.OverrideEnrolAsync("S000089", "YO6201", expected: HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:module-full", full.GetProperty("type").GetString());

        var forced = await admin.OverrideEnrolAsync("S000089", "YO6201", forceCapacity: true);
        Assert.True(forced.GetProperty("capacityRaised").GetBoolean());
        Assert.Equal(0, forced.GetProperty("placesRemaining").GetInt32());
        var detail = await admin.GetJsonAsync("/api/modules/YO6201");
        Assert.Equal((3, 3), (detail.GetProperty("capacity").GetInt32(), detail.GetProperty("enrolledCount").GetInt32()));

        var raised = Assert.Single(await factory.AuditAsync(AuditActions.ModuleUpdated, "YO6201"), a => StaffData.DetailsOf(a).TryGetProperty("capacity", out _));
        var capacity = StaffData.DetailsOf(raised).GetProperty("capacity");
        Assert.Equal((2, 3), (capacity.GetProperty("before").GetInt32(), capacity.GetProperty("after").GetInt32()));
        var created = (await factory.AuditAsync(AuditActions.EnrolmentAdminCreated, (await factory.EnrolmentAsync("S000089", "YO6201"))!.Id.ToString())).Single();
        Assert.True(StaffData.DetailsOf(created).GetProperty("forceCapacity").GetBoolean());
        Assert.True(StaffData.DetailsOf(created).GetProperty("capacityRaised").GetBoolean());

        // Not full: forceCapacity changes nothing.
        await admin.CreateModuleAsync("YO6202", capacity: 5);
        var notFull = await admin.OverrideEnrolAsync("S000087", "YO6202", forceCapacity: true);
        Assert.False(notFull.GetProperty("capacityRaised").GetBoolean());
        Assert.Equal(4, notFull.GetProperty("placesRemaining").GetInt32());
        Assert.Equal(5, (await admin.GetJsonAsync("/api/modules/YO6202")).GetProperty("capacity").GetInt32());
        Assert.DoesNotContain(await factory.AuditAsync(AuditActions.ModuleUpdated, "YO6202"), a => StaffData.DetailsOf(a).TryGetProperty("capacity", out _));

        // Already enrolled.
        var twice = await admin.OverrideEnrolAsync("S000087", "YO6202", expected: HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:already-enrolled", twice.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Results_exist_and_inactive_modules_still_apply_and_reasons_are_required()
    {
        using var admin = await factory.DemoAdminAsync();

        // S000087 holds published 2025/26 marks: that module cannot be retaken, override included.
        var completed = await factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == "S000087").Select(s => s.Id).SingleAsync();
            return await (from g in db.Grades
                          join m in db.Modules on g.ModuleId equals m.Id
                          where g.StudentId == studentId && g.Status == GradeStatus.Published
                          orderby m.Code
                          select m.Code).FirstAsync();
        });
        var exists = await admin.OverrideEnrolAsync("S000087", completed, expected: HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:results-exist", exists.GetProperty("type").GetString());

        await admin.CreateModuleAsync("YO6301");
        await admin.PutJsonAsync("/api/admin/modules/YO6301", new { title = "Retired", description = (string?)null, credits = 15, capacity = 100, semester = "autumn", isActive = false });
        var inactive = await admin.OverrideEnrolAsync("S000088", "YO6301", expected: HttpStatusCode.Conflict);
        Assert.Equal("urn:rushday:module-inactive", inactive.GetProperty("type").GetString());

        using (var noReason = await admin.PostAsJsonAsync("/api/admin/students/S000088/enrolments", new { moduleCode = "YO6301" }))
        {
            await noReason.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        using (var unknownStudent = await admin.PostAsJsonAsync("/api/admin/students/S999999/enrolments", new { moduleCode = "YO6301", reason = StaffData.Reason }))
        {
            await unknownStudent.AssertProblemAsync(HttpStatusCode.NotFound, "student-not-found");
        }

        using (var unknownModule = await admin.PostAsJsonAsync("/api/admin/students/S000088/enrolments", new { moduleCode = "YO6399", reason = StaffData.Reason }))
        {
            await unknownModule.AssertProblemAsync(HttpStatusCode.NotFound, "module-not-found");
        }
    }

    [Fact]
    public async Task Override_withdrawal_ignores_the_rules_and_is_audited()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YO6401", capacity: 10);
        await admin.OverrideEnrolAsync("S000089", "YO6401");

        // A submitted mark would block the student's own withdrawal; the administrator's is allowed.
        await factory.PutGradeAsync("S000089", "YO6401", GradeStatus.Submitted, 55);

        using (var noReason = await admin.PostAsJsonAsync("/api/admin/students/S000089/enrolments/YO6401/withdraw", new { }))
        {
            await noReason.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        using (var withdraw = await admin.PostAsJsonAsync("/api/admin/students/S000089/enrolments/YO6401/withdraw", new { reason = StaffData.Reason }))
        {
            Assert.Equal(HttpStatusCode.NoContent, withdraw.StatusCode);
        }

        var enrolment = await factory.EnrolmentAsync("S000089", "YO6401");
        Assert.Equal(EnrolmentStatus.Withdrawn, enrolment!.Status);
        Assert.Equal(0, (await admin.GetJsonAsync("/api/modules/YO6401")).GetProperty("enrolledCount").GetInt32());
        var audit = (await factory.AuditAsync(AuditActions.EnrolmentAdminWithdrawn, enrolment.Id.ToString())).Single();
        Assert.Equal((StaffData.Reason, true), (StaffData.DetailsOf(audit).GetProperty("reason").GetString(), StaffData.DetailsOf(audit).GetProperty("override").GetBoolean()));

        using var again = await admin.PostAsJsonAsync("/api/admin/students/S000089/enrolments/YO6401/withdraw", new { reason = StaffData.Reason });
        await again.AssertProblemAsync(HttpStatusCode.NotFound, "not-enrolled");
    }
}
