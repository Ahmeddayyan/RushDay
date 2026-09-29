using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RushDay.Domain.Audit;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// Single-mark corrections by the registry (02-api.md section 8.5): allowed on Submitted and Published grades only,
/// the status, instant and publication are kept, so a live correction is what the student sees at once, labelled by
/// <c>correctedAt</c> ("Amended"); every correction is audited with before, after and reason. Students
/// <c>S000080</c>, <c>S000082</c>–<c>S000084</c> and <c>S000092</c> (S4 uses S000081), years <c>2051/52</c>..
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CorrectionTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task A_corrected_published_mark_is_visible_at_once_with_correctedAt()
    {
        const string year = "2051/52";
        await factory.SeedModuleAsync("YC5101", Semester.Autumn, year, [("S000080", 55), ("S000092", 60)]);
        using var admin = await factory.DemoAdminAsync();
        using var student = await factory.LoginStudentAsync("S000080");

        try
        {
            var published = await admin.PostJsonAsync("/api/admin/results/publish", new { academicYear = year, semester = "autumn", publishAt = factory.Clock.GetUtcNow(), announce = false });
            var publicationId = published.GetProperty("publication").GetProperty("id").GetGuid();
            var before = (await student.VisibleResultAsync("YC5101"))!.Value;
            Assert.Equal(55, before.GetProperty("mark").GetInt32());
            Assert.Equal(JsonValueKind.Null, before.GetProperty("correctedAt").ValueKind);
            var versionBefore = (await factory.GradeAsync("S000080", "YC5101")).Version;

            var corrected = await admin.PostJsonAsync("/api/admin/results/modules/YC5101/marks/S000080/correct", new { outcome = "mark", mark = 65, reason = StaffData.Reason });
            Assert.Equal("S000080", corrected.GetProperty("studentNumber").GetString());
            Assert.Equal((55, "mark"), (corrected.GetProperty("before").GetProperty("mark").GetInt32(), corrected.GetProperty("before").GetProperty("outcome").GetString()));
            Assert.Equal((65, "mark"), (corrected.GetProperty("after").GetProperty("mark").GetInt32(), corrected.GetProperty("after").GetProperty("outcome").GetString()));
            Assert.Equal(versionBefore + 1, corrected.GetProperty("version").GetInt32());
            Assert.Equal(factory.Clock.GetUtcNow(), corrected.GetProperty("correctedAt").GetDateTimeOffset());

            // The student sees the corrected mark immediately, with the amendment instant.
            var after = (await student.VisibleResultAsync("YC5101"))!.Value;
            Assert.Equal(65, after.GetProperty("mark").GetInt32());
            Assert.Equal(corrected.GetProperty("correctedAt").GetString(), after.GetProperty("correctedAt").GetString());
            var dashboard = await student.GetJsonAsync("/api/me/dashboard");
            var onDashboard = dashboard.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("moduleCode").GetString() == "YC5101");
            Assert.Equal(65, onDashboard.GetProperty("mark").GetInt32());

            // Status, instant and publication are kept.
            var grade = await factory.GradeAsync("S000080", "YC5101");
            Assert.Equal((GradeStatus.Published, publicationId), (grade.Status, grade.PublicationId!.Value));

            var audit = Assert.Single(await factory.AuditAsync(AuditActions.GradeCorrected, grade.Id.ToString()));
            var details = StaffData.DetailsOf(audit);
            Assert.Equal((55, 65), (details.GetProperty("before").GetProperty("mark").GetInt32(), details.GetProperty("after").GetProperty("mark").GetInt32()));
            Assert.Equal((StaffData.Reason, "YC5101", "S000080"), (details.GetProperty("reason").GetString(), details.GetProperty("moduleCode").GetString(), details.GetProperty("studentNumber").GetString()));
            Assert.Equal(audit.StudentId, await factory.StudentIdAsync("S000080"));
            Assert.Equal(DemoAdministrator, audit.ActorUsername);

            // An outcome correction clears the mark.
            var absent = await admin.PostJsonAsync("/api/admin/results/modules/YC5101/marks/S000092/correct", new { outcome = "absent", mark = (int?)null, reason = StaffData.Reason });
            Assert.Equal(("absent", JsonValueKind.Null), (absent.GetProperty("after").GetProperty("outcome").GetString(), absent.GetProperty("after").GetProperty("mark").ValueKind));
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    [Fact]
    public async Task A_submitted_mark_can_be_corrected_and_stays_hidden()
    {
        const string year = "2052/53";
        await factory.SeedModuleAsync("YC5201", Semester.Spring, year, [("S000082", 38)]);
        using var admin = await factory.DemoAdminAsync();
        using var student = await factory.LoginStudentAsync("S000082");

        var corrected = await admin.PostJsonAsync("/api/admin/results/modules/YC5201/marks/s000082/correct", new { mark = 42, reason = StaffData.Reason });
        Assert.Equal(42, corrected.GetProperty("after").GetProperty("mark").GetInt32());
        var grade = await factory.GradeAsync("S000082", "YC5201");
        Assert.Equal((GradeStatus.Submitted, 42), (grade.Status, grade.Mark!.Value));
        Assert.NotNull(grade.CorrectedAt);
        Assert.Null(await student.VisibleResultAsync("YC5201"));
    }

    [Fact]
    public async Task A_draft_answers_module_not_submitted_and_missing_rows_are_not_found()
    {
        const string year = "2053/54";
        await factory.SeedModuleAsync("YC5301", Semester.Autumn, year, [("S000083", 50), ("S000084", null)], GradeStatus.Draft);
        using var admin = await factory.DemoAdminAsync();

        using (var draft = await admin.PostAsJsonAsync("/api/admin/results/modules/YC5301/marks/S000083/correct", new { mark = 51, reason = StaffData.Reason }))
        {
            await draft.AssertProblemAsync(HttpStatusCode.Conflict, "module-not-submitted");
        }

        Assert.Equal(50, (await factory.GradeAsync("S000083", "YC5301")).Mark);

        using (var noGrade = await admin.PostAsJsonAsync("/api/admin/results/modules/YC5301/marks/S000084/correct", new { mark = 51, reason = StaffData.Reason }))
        {
            await noGrade.AssertProblemAsync(HttpStatusCode.NotFound, "grade-not-found");
        }

        using (var noModule = await admin.PostAsJsonAsync("/api/admin/results/modules/YC5399/marks/S000083/correct", new { mark = 51, reason = StaffData.Reason }))
        {
            await noModule.AssertProblemAsync(HttpStatusCode.NotFound, "module-not-found");
        }

        // The mark/outcome rule and the reason are request validation.
        using (var noReason = await admin.PostAsJsonAsync("/api/admin/results/modules/YC5301/marks/S000083/correct", new { mark = 51, reason = "short" }))
        {
            await noReason.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        using (var markWithAbsence = await admin.PostAsJsonAsync("/api/admin/results/modules/YC5301/marks/S000083/correct", new { outcome = "deferred", mark = 51, reason = StaffData.Reason }))
        {
            await markWithAbsence.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        // A lecturer has no correction route: the administrator's group refuses them.
        using var lecturer = await factory.LecturerAsync("L00001");
        using var byLecturer = await lecturer.PostAsJsonAsync("/api/admin/results/modules/YC5301/marks/S000083/correct", new { mark = 51, reason = StaffData.Reason });
        await byLecturer.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    private const string DemoAdministrator = RushDay.Infrastructure.Seeding.DemoAccounts.AdminUsername;
}
