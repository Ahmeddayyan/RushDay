using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// The S6 review's results-lifecycle findings (E1 stranded marks, E2 the publication's announcement, E3 an emptied
/// publication, E9 the return-to-draft lock order, E12 a correction that changes nothing). Modules <c>YG####</c>;
/// students created here as <c>S941###</c>; publications in the academic years 2081/82 to 2086/87, each removed by its
/// test.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ResultsGovernanceTests(RushDayApiFactory factory)
{
    private const string Reason = StaffData.Reason;

    /// <summary>
    /// E1: a Draft on an active enrolment of a module that has left draft (a student withdrawn before the submit and
    /// enrolled again, the reviewer's P1 and P13) is <c>missing</c>, so the module is not publishable and a publish
    /// cannot leave that mark behind while reporting the module published; return to draft reaches it again. Before
    /// the fix the module showed submitted, 2 of 2 entered, and a publish published one grade and stranded the other.
    /// </summary>
    [Fact]
    public async Task A_draft_left_on_a_submitted_module_is_missing_and_never_stranded_by_a_publish()
    {
        const string year = "2081/82";
        using var admin = await factory.DemoAdminAsync();
        var submitted = await NewStudentAsync(admin, "S941001");
        var drafted = await NewStudentAsync(admin, "S941002");
        await factory.SeedModuleAsync("YG1001", Semester.Autumn, year, [(submitted, 70)]);
        await factory.InsertEnrolmentAsync(drafted, "YG1001", year);
        await factory.PutGradeAsync(drafted, "YG1001", GradeStatus.Draft, 55);

        var marks = await ProgressAsync(admin, year, "YG1001");
        Assert.Equal(("submitted", 1, 1, 2), (marks.GetProperty("status").GetString(), marks.GetProperty("entered").GetInt32(), marks.GetProperty("missing").GetInt32(), marks.GetProperty("total").GetInt32()));

        try
        {
            using var publish = await admin.PostAsJsonAsync("/api/admin/results/publish", new { academicYear = year, semester = "autumn", publishAt = factory.Clock.GetUtcNow(), announce = false });
            await publish.AssertProblemAsync(HttpStatusCode.Conflict, "nothing-to-publish");
            Assert.Equal(GradeStatus.Draft, (await factory.GradeAsync(drafted, "YG1001")).Status);
            Assert.Equal(GradeStatus.Submitted, (await factory.GradeAsync(submitted, "YG1001")).Status);

            // The registry returns the module to draft; both marks are the lecturers' again.
            await admin.PostJsonAsync("/api/admin/results/modules/YG1001/return-to-draft", new { reason = Reason, academicYear = year });
            Assert.Equal((GradeStatus.Draft, 70), ((await factory.GradeAsync(submitted, "YG1001")).Status, (await factory.GradeAsync(submitted, "YG1001")).Mark!.Value));
            Assert.Equal((GradeStatus.Draft, 55), ((await factory.GradeAsync(drafted, "YG1001")).Status, (await factory.GradeAsync(drafted, "YG1001")).Mark!.Value));
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    /// <summary>
    /// E1, the rule: once a module's marks for the year have left draft nobody joins it, neither by an administrator's
    /// override (P1) nor by a student's own re-enrolment while the window is open (P13), until the registry returns it
    /// to draft. Then the returning student's draft is there for the lecturers and the resubmission carries both marks.
    /// Before the fix both re-enrolments were 201 and the draft could never be entered, corrected or published.
    /// </summary>
    [Fact]
    public async Task Nobody_joins_a_module_whose_marks_have_left_draft_until_it_is_returned_to_draft()
    {
        using var admin = await factory.DemoAdminAsync();
        var stays = await NewStudentAsync(admin, "S941011");
        var leaves = await NewStudentAsync(admin, "S941012");
        using var student = await StudentLoginAsync(admin, leaves);
        await admin.CreateModuleAsync("YG1002", Semester.Autumn, capacity: 50);
        await admin.AssignAsync("YG1002", "L00001");
        await admin.OverrideEnrolAsync(stays, "YG1002");
        using (var enrol = await student.EnrolAsync("YG1002"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        using var lecturer = await factory.LecturerAsync("L00001");
        await lecturer.SaveMarksAsync("YG1002", [new { studentNumber = stays, mark = 70, version = (int?)null }, new { studentNumber = leaves, mark = 55, version = (int?)null }]);
        using (var withdraw = await student.WithdrawAsync("YG1002"))
        {
            Assert.Equal(HttpStatusCode.NoContent, withdraw.StatusCode);
        }

        var submit = await lecturer.PostJsonAsync("/api/lecturer/modules/YG1002/marks/submit", null);
        Assert.Equal(1, submit.GetProperty("gradeCount").GetInt32());

        // The student's own re-enrolment (window open) and the registry's override are both refused.
        using (var again = await student.EnrolAsync("YG1002"))
        {
            await again.AssertProblemAsync(HttpStatusCode.Conflict, "module-locked");
        }

        using (var overridden = await admin.PostAsJsonAsync($"/api/admin/students/{leaves}/enrolments", new { moduleCode = "YG1002", reason = Reason }))
        {
            await overridden.AssertProblemAsync(HttpStatusCode.Conflict, "module-locked");
        }

        Assert.Equal(EnrolmentStatus.Withdrawn, (await factory.EnrolmentAsync(leaves, "YG1002"))!.Status);
        Assert.Equal((1, 1), await factory.CountsAsync("YG1002"));

        // Returned to draft, the module takes the student back and their mark is where the lecturers left it.
        await admin.PostJsonAsync("/api/admin/results/modules/YG1002/return-to-draft", new { reason = Reason });
        using (var back = await student.EnrolAsync("YG1002"))
        {
            Assert.Equal(HttpStatusCode.Created, back.StatusCode);
        }

        var sheet = await lecturer.GetJsonAsync("/api/lecturer/modules/YG1002/marks");
        var row = sheet.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("studentNumber").GetString() == leaves);
        Assert.Equal(("active", 55, "draft"), (row.GetProperty("enrolmentStatus").GetString(), row.GetProperty("mark").GetInt32(), row.GetProperty("gradeStatus").GetString()));
        var resubmitted = await lecturer.PostJsonAsync("/api/lecturer/modules/YG1002/marks/submit", null);
        Assert.Equal(2, resubmitted.GetProperty("gradeCount").GetInt32());
        var marks = (await admin.GetJsonAsync("/api/admin/modules")).EnumerateArray().Single(m => m.GetProperty("code").GetString() == "YG1002").GetProperty("marks");
        Assert.Equal(("submitted", 2, 0), (marks.GetProperty("status").GetString(), marks.GetProperty("entered").GetInt32(), marks.GetProperty("missing").GetInt32()));
    }

    /// <summary>
    /// E1, the race: an enrolment's "marks still in draft" check takes the module's marks lock shared, so a submit in
    /// progress (the lock held exclusively and the grades updated, not yet committed) makes it wait, and it then sees
    /// the submitted marks. Before the fix the enrolment did not wait: it committed 201 beside the submit and left the
    /// new student with no reachable mark.
    /// </summary>
    [Fact]
    public async Task An_enrolment_waits_for_a_submit_in_progress_and_then_sees_it()
    {
        using var admin = await factory.DemoAdminAsync();
        var marked = await NewStudentAsync(admin, "S941021");
        var late = await NewStudentAsync(admin, "S941022");
        await admin.CreateModuleAsync("YG1003", Semester.Autumn, capacity: 50);
        await admin.AssignAsync("YG1003", "L00001");
        await admin.OverrideEnrolAsync(marked, "YG1003");
        using (var lecturer = await factory.LecturerAsync("L00001"))
        {
            await lecturer.SaveMarksAsync("YG1003", [new { studentNumber = marked, mark = 60, version = (int?)null }]);
        }

        var moduleId = await factory.ModuleIdAsync("YG1003");
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var submit = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@class, hashtext(@key)); UPDATE grades SET status = 'Submitted', submitted_at = now(), version = version + 1 WHERE module_id = @module", connection, submit))
        {
            command.Parameters.AddWithValue("class", 0x52444D4B);
            command.Parameters.AddWithValue("key", moduleId.ToString("D"));
            command.Parameters.AddWithValue("module", moduleId);
            await command.ExecuteNonQueryAsync();
        }

        var enrol = Task.Run(async () =>
        {
            using var response = await admin.PostAsJsonAsync($"/api/admin/students/{late}/enrolments", new { moduleCode = "YG1003", reason = Reason });
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        });
        await DbProbe.WaitForLockWaitAsync(factory, "%pg_advisory_xact_lock_shared%");
        await submit.CommitAsync();

        var (status, body) = await enrol;
        Assert.True(status == HttpStatusCode.Conflict, $"The late enrolment answered {(int)status}: {body}");
        Assert.Contains("urn:rushday:module-locked", body, StringComparison.Ordinal);
        Assert.Null(await factory.EnrolmentAsync(late, "YG1003"));
    }

    /// <summary>
    /// E2: the "results are available" announcement of a publish with <c>announce</c> follows the publication. A
    /// reschedule moves it (a student sees neither the results nor the announcement at the old instant); a cancel and
    /// an unpublish delete it, each audited. Before the fix it stayed at the first instant and outlived both.
    /// </summary>
    [Fact]
    public async Task The_results_announcement_moves_with_a_reschedule_and_goes_with_a_cancel_or_unpublish()
    {
        const string year = "2082/83";
        const string title = "Autumn 2082/83 results are available";
        using var admin = await factory.DemoAdminAsync();
        var number = await NewStudentAsync(admin, "S941031");
        await factory.SeedModuleAsync("YG2001", Semester.Autumn, year, [(number, 64)]);
        using var student = await StudentLoginAsync(admin, number);

        try
        {
            var first = factory.Clock.GetUtcNow().AddSeconds(10);
            var published = await admin.PostJsonAsync("/api/admin/results/publish", new { academicYear = year, semester = "autumn", publishAt = first, announce = true });
            var publicationId = published.GetProperty("publication").GetProperty("id").GetGuid();
            var announcement = await factory.WithDbAsync(db => db.Announcements.AsNoTracking().SingleAsync(a => a.Title == title));
            Assert.Equal(announcement.Id, await factory.WithDbAsync(db => db.ResultsPublications.AsNoTracking().Where(p => p.Id == publicationId).Select(p => p.AnnouncementId).SingleAsync()));

            var later = factory.Clock.GetUtcNow().AddDays(2);
            await admin.PutJsonAsync($"/api/admin/results/publications/{publicationId}", new { publishAt = later });
            var moved = await factory.WithDbAsync(db => db.Announcements.AsNoTracking().SingleAsync(a => a.Id == announcement.Id));
            Assert.Equal(later, moved.PublishedAt);
            Assert.Single(await factory.AuditAsync(AuditActions.AnnouncementUpdated, announcement.Id.ToString()));

            // The old instant passes: neither the results nor their announcement are out.
            factory.Clock.Advance(TimeSpan.FromSeconds(11));
            Assert.Null(await student.VisibleResultAsync("YG2001"));
            Assert.DoesNotContain(title, (await student.GetJsonAsync("/api/announcements")).Strings("title"));

            // Cancelled, the announcement is deleted with the publication.
            await admin.SendJsonAsync(HttpMethod.Delete, $"/api/admin/results/publications/{publicationId}", null, HttpStatusCode.OK);
            Assert.NotNull((await factory.WithDbAsync(db => db.Announcements.AsNoTracking().SingleAsync(a => a.Id == announcement.Id))).DeletedAt);
            Assert.Single(await factory.AuditAsync(AuditActions.AnnouncementDeleted, announcement.Id.ToString()));

            // Live and announced, then unpublished: the students stop seeing both at once.
            var live = await admin.PostJsonAsync("/api/admin/results/publish", new { academicYear = year, semester = "autumn", publishAt = factory.Clock.GetUtcNow(), announce = true });
            var liveId = live.GetProperty("publication").GetProperty("id").GetGuid();
            Assert.Contains(title, (await student.GetJsonAsync("/api/announcements")).Strings("title"));
            Assert.NotNull(await student.VisibleResultAsync("YG2001"));
            await admin.PostJsonAsync($"/api/admin/results/publications/{liveId}/unpublish", new { reason = Reason });
            Assert.DoesNotContain(title, (await student.GetJsonAsync("/api/announcements")).Strings("title"));
            Assert.Null(await student.VisibleResultAsync("YG2001"));
            Assert.False(await factory.WithDbAsync(db => db.Announcements.AsNoTracking().AnyAsync(a => a.Title == title && a.DeletedAt == null)));
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
            await RemoveAnnouncementsAsync(admin, title);
        }
    }

    /// <summary>
    /// E3: a return to draft that takes the last grade out of a scheduled publication deletes the publication and its
    /// announcement in the same transaction (audited <c>results.cancelled</c> with <c>returnedToDraft</c>), so no empty
    /// publication drives the countdown or later shows as the latest live one. Before the fix it stayed with 0 grades
    /// and became a student's <c>latestPublication</c> at its instant.
    /// </summary>
    [Fact]
    public async Task Returning_a_publications_last_module_to_draft_removes_the_empty_publication()
    {
        const string year = "2083/84";
        const string title = "Autumn 2083/84 results are available";
        using var admin = await factory.DemoAdminAsync();
        var number = await NewStudentAsync(admin, "S941041");
        await factory.SeedModuleAsync("YG3001", Semester.Autumn, year, [(number, 58)]);
        using var student = await StudentLoginAsync(admin, number);

        try
        {
            var published = await admin.PostJsonAsync("/api/admin/results/publish", new { academicYear = year, semester = "autumn", publishAt = factory.Clock.GetUtcNow().AddSeconds(5), announce = true });
            var publicationId = published.GetProperty("publication").GetProperty("id").GetGuid();
            var announcementId = await factory.WithDbAsync(db => db.Announcements.AsNoTracking().Where(a => a.Title == title).Select(a => a.Id).SingleAsync());

            var returned = await admin.PostJsonAsync("/api/admin/results/modules/YG3001/return-to-draft", new { reason = Reason, academicYear = year });
            Assert.True(returned.GetProperty("fromScheduledPublication").GetBoolean());

            Assert.False(await factory.WithDbAsync(db => db.ResultsPublications.AsNoTracking().AnyAsync(p => p.Id == publicationId)));
            Assert.NotNull((await factory.WithDbAsync(db => db.Announcements.AsNoTracking().SingleAsync(a => a.Id == announcementId))).DeletedAt);
            var cancelled = StaffData.DetailsOf(Assert.Single(await factory.AuditAsync(AuditActions.ResultsCancelled, publicationId.ToString())));
            Assert.Equal(("YG3001", 0), (cancelled.GetProperty("returnedToDraft").GetString(), cancelled.GetProperty("grades").GetInt32()));

            // After the instant it would have had, it is nobody's latest publication.
            factory.Clock.Advance(TimeSpan.FromSeconds(6));
            var dashboard = await student.GetJsonAsync("/api/me/dashboard");
            foreach (var brief in new[] { dashboard.GetProperty("latestPublication"), dashboard.GetProperty("nextPublication") })
            {
                Assert.True(brief.ValueKind == JsonValueKind.Null || brief.GetProperty("academicYear").GetString() != year, $"The emptied publication is still a brief: {brief}");
            }
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
            await RemoveAnnouncementsAsync(admin, title);
        }
    }

    /// <summary>
    /// E9 (the reviewer's P3, deterministic): a reschedule holds the publication row and then updates its grades; a
    /// return to draft of one of its modules now takes the publication row before the grades, so it waits behind the
    /// reschedule instead of holding the grades the reschedule needs. Before the fix PostgreSQL broke the cycle with a
    /// deadlock (40P01) on one side.
    /// </summary>
    [Fact]
    public async Task Return_to_draft_queues_behind_a_reschedule_instead_of_deadlocking()
    {
        const string year = "2084/85";
        using var admin = await factory.DemoAdminAsync();
        var one = await NewStudentAsync(admin, "S941051");
        var two = await NewStudentAsync(admin, "S941052");
        await factory.SeedModuleAsync("YG4001", Semester.Spring, year, [(one, 61), (two, 62)]);

        try
        {
            var published = await admin.PostJsonAsync("/api/admin/results/publish", new { academicYear = year, semester = "spring", publishAt = factory.Clock.GetUtcNow().AddDays(1), announce = false });
            var publicationId = published.GetProperty("publication").GetProperty("id").GetGuid();

            await using var connection = new NpgsqlConnection(factory.ConnectionString);
            await connection.OpenAsync();
            await using var reschedule = await connection.BeginTransactionAsync();
            await using (var lockPublication = new NpgsqlCommand("SELECT id FROM results_publications WHERE id = @id FOR UPDATE", connection, reschedule))
            {
                lockPublication.Parameters.AddWithValue("id", publicationId);
                await lockPublication.ExecuteScalarAsync();
            }

            var returnToDraft = Task.Run(async () =>
            {
                using var response = await admin.PostAsJsonAsync("/api/admin/results/modules/YG4001/return-to-draft", new { reason = Reason, academicYear = year });
                return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
            });
            await DbProbe.WaitForLockWaitAsync(factory, "%results_publications%");

            // The reschedule's second statement: it must not find the grades held by the waiting return to draft.
            string? failure = null;
            try
            {
                await using var moveGrades = new NpgsqlCommand("UPDATE grades SET published_at = published_at WHERE publication_id = @id", connection, reschedule) { CommandTimeout = 20 };
                moveGrades.Parameters.AddWithValue("id", publicationId);
                Assert.Equal(2, await moveGrades.ExecuteNonQueryAsync());
                await reschedule.CommitAsync();
            }
            catch (PostgresException exception)
            {
                failure = exception.SqlState + " " + exception.MessageText;
            }

            var (status, body) = await returnToDraft;
            Assert.Null(failure);
            Assert.True(status == HttpStatusCode.OK, $"Return to draft answered {(int)status}: {body}");
            Assert.Equal(GradeStatus.Draft, (await factory.GradeAsync(one, "YG4001")).Status);
            Assert.False(await factory.WithDbAsync(db => db.ResultsPublications.AsNoTracking().AnyAsync(p => p.Id == publicationId)));
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    /// <summary>
    /// E12: a correction that leaves the mark and the outcome as they are is refused (400 <c>validation</c> on
    /// <c>mark</c>) and changes nothing, so a student never sees "Amended" on a mark nobody amended. Before the fix it
    /// answered 200, bumped the version and stamped <c>corrected_at</c>.
    /// </summary>
    [Fact]
    public async Task A_correction_that_changes_nothing_is_refused_and_marks_nothing_amended()
    {
        const string year = "2085/86";
        using var admin = await factory.DemoAdminAsync();
        var number = await NewStudentAsync(admin, "S941061");
        await factory.SeedModuleAsync("YG5001", Semester.Autumn, year, [(number, 64)]);
        var before = await factory.GradeAsync(number, "YG5001");

        using (var same = await admin.PostAsJsonAsync($"/api/admin/results/modules/YG5001/marks/{number}/correct", new { mark = 64, reason = Reason }))
        {
            var problem = await same.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
            Assert.True(problem.GetProperty("errors").TryGetProperty("mark", out _), problem.ToString());
        }

        var after = await factory.GradeAsync(number, "YG5001");
        Assert.Equal((before.Version, (DateTimeOffset?)null, 64), (after.Version, after.CorrectedAt, after.Mark!.Value));
        Assert.Empty(await factory.AuditAsync(AuditActions.GradeCorrected, before.Id.ToString()));

        // A real change still goes through.
        var corrected = await admin.PostJsonAsync($"/api/admin/results/modules/YG5001/marks/{number}/correct", new { outcome = "absent", mark = (int?)null, reason = Reason });
        Assert.Equal("absent", corrected.GetProperty("after").GetProperty("outcome").GetString());
    }

    /// <summary>A student record of the test's own, created through the registry's route (no account).</summary>
    internal static async Task<string> NewStudentAsync(HttpClient admin, string number)
    {
        await admin.PostJsonAsync("/api/admin/students", new { studentNumber = number, fullName = "Review Student " + number, programme = "BSc Test", yearOfStudy = 1 }, HttpStatusCode.Created);
        return number;
    }

    /// <summary>A login for a student of <see cref="NewStudentAsync"/> (the demo administrator provisions demo accounts), signed in.</summary>
    internal async Task<HttpClient> StudentLoginAsync(HttpClient admin, string number)
    {
        await admin.PostJsonAsync("/api/admin/accounts", new { username = number, displayName = "Review Student " + number, role = "Student", studentNumber = number, temporaryPassword = TestAccounts.Password }, HttpStatusCode.Created);
        return await factory.LoginAsync(number, TestAccounts.Password);
    }

    private static async Task<JsonElement> ProgressAsync(HttpClient admin, string year, string code)
    {
        var progress = await admin.GetJsonAsync($"/api/admin/results?semester=autumn&academicYear={year}");
        return progress.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == code).GetProperty("marks");
    }

    /// <summary>No pinned university announcement outlives the test (the student-side tests count them).</summary>
    private async Task RemoveAnnouncementsAsync(HttpClient admin, string title)
    {
        var ids = await factory.WithDbAsync(db => db.Announcements.AsNoTracking().Where(a => a.Title == title && a.DeletedAt == null).Select(a => a.Id).ToListAsync());
        foreach (var id in ids)
        {
            using var _ = await admin.DeleteAsync($"/api/admin/announcements/{id}");
        }
    }
}
