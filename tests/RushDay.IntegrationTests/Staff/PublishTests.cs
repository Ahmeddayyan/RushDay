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
/// The results lifecycle (02-api.md section 8.5, 00-overview.md section 4.3): only publishable modules are published
/// and the rest are listed with their reason; a future instant is hidden until <c>factory.Clock</c> passes it;
/// scheduled publications can be rescheduled, cancelled or have a module returned to draft; live ones can only be
/// unpublished; publishing twice publishes only what became publishable since. Each test owns an academic year
/// (<c>2041/42</c>..), so its (year, semester) holds only its own modules, and removes its publications at the end.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PublishTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Only_publishable_modules_are_published_and_the_rest_are_excluded_with_reasons()
    {
        const string year = "2041/42";
        await factory.SeedModuleAsync("YP4101", Semester.Autumn, year, [("S000065", 71), ("S000066", 64)]);
        await factory.SeedModuleAsync("YP4102", Semester.Autumn, year, [("S000065", 50)], GradeStatus.Draft);
        await factory.SeedModuleAsync("YP4103", Semester.Autumn, year, [("S000065", 58), ("S000067", null)]);
        await factory.SeedModuleAsync("YP4104", Semester.Spring, year, [("S000065", 49)]);
        using var admin = await factory.DemoAdminAsync();

        try
        {
            var body = await admin.PostJsonAsync("/api/admin/results/publish", Publish(year, "autumn", factory.Clock.GetUtcNow().AddMinutes(-5)));

            var publication = body.GetProperty("publication");
            Assert.Equal((year, "autumn", "live"), (publication.GetProperty("academicYear").GetString(), publication.GetProperty("semester").GetString(), publication.GetProperty("state").GetString()));
            Assert.Equal((2, 1), (publication.GetProperty("gradeCount").GetInt32(), publication.GetProperty("moduleCount").GetInt32()));
            Assert.Equal("Demo Administrator", publication.GetProperty("createdBy").GetString());

            // An instant in the past means now.
            Assert.Equal(factory.Clock.GetUtcNow(), publication.GetProperty("publishAt").GetDateTimeOffset());
            Assert.Equal((1, 2), (body.GetProperty("published").GetProperty("modules").GetInt32(), body.GetProperty("published").GetProperty("grades").GetInt32()));

            var excluded = body.GetProperty("excluded").EnumerateArray().ToDictionary(e => e.GetProperty("code").GetString()!);
            Assert.Equal(["YP4102", "YP4103"], excluded.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(("draft", "notSubmitted"), (excluded["YP4102"].GetProperty("status").GetString(), excluded["YP4102"].GetProperty("reason").GetString()));
            Assert.Equal(("submitted", "marksMissing", 1), (excluded["YP4103"].GetProperty("status").GetString(), excluded["YP4103"].GetProperty("reason").GetString(), excluded["YP4103"].GetProperty("marksMissing").GetInt32()));

            var id = publication.GetProperty("id").GetGuid();
            Assert.All(await GradesAsync("YP4101"), g => Assert.Equal((GradeStatus.Published, id), (g.Status, g.PublicationId!.Value)));
            Assert.Equal(GradeStatus.Draft, (await factory.GradeAsync("S000065", "YP4102")).Status);
            Assert.Equal(GradeStatus.Submitted, (await factory.GradeAsync("S000065", "YP4104")).Status);

            var audit = Assert.Single(await factory.AuditAsync(AuditActions.ResultsPublished, id.ToString()));
            var details = StaffData.DetailsOf(audit);
            Assert.Equal((1, 2, false), (details.GetProperty("modules").GetInt32(), details.GetProperty("grades").GetInt32(), details.GetProperty("announced").GetBoolean()));
            Assert.Equal(["YP4102", "YP4103"], details.GetProperty("excluded").EnumerateArray().Select(e => e.GetString()));

            // The progress table for the (year, semester) shows each module's state and the publication.
            var progress = await admin.GetJsonAsync($"/api/admin/results?semester=AUTUMN&academicYear={Uri.EscapeDataString(year)}");
            var modules = progress.GetProperty("modules").EnumerateArray().Where(m => m.GetProperty("code").GetString()!.StartsWith("YP41", StringComparison.Ordinal)).ToList();
            Assert.Equal(["YP4102", "YP4103", "YP4101"], modules.Select(m => m.GetProperty("code").GetString()));
            Assert.Equal(["draft", "submitted", "published"], modules.Select(m => m.GetProperty("marks").GetProperty("status").GetString()));
            Assert.Equal(2, modules[1].GetProperty("enrolledCount").GetInt32());
            Assert.Equal(id, Assert.Single(progress.GetProperty("publications").EnumerateArray()).GetProperty("id").GetGuid());
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    [Fact]
    public async Task Submitted_module_with_missing_mark_is_excluded_and_not_published()
    {
        const string year = "2042/43";
        await factory.SeedModuleAsync("YP4201", Semester.Spring, year, [("S000068", 66), ("S000069", null)]);
        using var admin = await factory.DemoAdminAsync();

        using var response = await admin.PostAsJsonAsync("/api/admin/results/publish", Publish(year, "spring", factory.Clock.GetUtcNow()));
        await response.AssertProblemAsync(HttpStatusCode.Conflict, "nothing-to-publish");
        Assert.Equal(GradeStatus.Submitted, (await factory.GradeAsync("S000068", "YP4201")).Status);
        Assert.Empty(await factory.WithDbAsync(db => db.ResultsPublications.AsNoTracking().Where(p => p.AcademicYear == year).ToListAsync()));

        // The rest of the request rules.
        using var tooFar = await admin.PostAsJsonAsync("/api/admin/results/publish", Publish(year, "spring", factory.Clock.GetUtcNow().AddDays(91)));
        await tooFar.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "publish-too-far-ahead");
        using var numericSemester = await admin.PostAsJsonAsync("/api/admin/results/publish", new { academicYear = year, semester = 7, publishAt = factory.Clock.GetUtcNow(), announce = false });
        await numericSemester.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        using var noInstant = await admin.PostAsJsonAsync("/api/admin/results/publish", new { academicYear = year, semester = "spring", announce = false });
        await noInstant.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        using var badQuery = await admin.GetAsync("/api/admin/results?semester=1");
        await badQuery.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Future_instant_is_hidden_then_visible_with_the_clock()
    {
        const string year = "2043/44";
        await factory.SeedModuleAsync("YP4301", Semester.Autumn, year, [("S000070", 77)]);
        using var admin = await factory.DemoAdminAsync();
        using var student = await factory.LoginStudentAsync("S000070");

        try
        {
            var publishAt = factory.Clock.GetUtcNow().AddSeconds(20);
            var body = await admin.PostJsonAsync("/api/admin/results/publish", Publish(year, "autumn", publishAt));
            Assert.Equal("scheduled", body.GetProperty("publication").GetProperty("state").GetString());

            // Scheduled: the group shows its instant and no mark; the dashboard's countdown names it.
            Assert.Null(await student.VisibleResultAsync("YP4301"));
            var results = await student.GetJsonAsync("/api/me/results");
            var group = results.GetProperty("semesters").EnumerateArray().Single(s => s.GetProperty("academicYear").GetString() == year);
            Assert.Equal("scheduled", group.GetProperty("state").GetString());
            Assert.DoesNotContain("77", group.GetRawText(), StringComparison.Ordinal);
            var next = (await student.GetJsonAsync("/api/me/dashboard")).GetProperty("nextPublication");
            Assert.Equal((year, "scheduled"), (next.GetProperty("academicYear").GetString(), next.GetProperty("state").GetString()));

            factory.Clock.Advance(TimeSpan.FromSeconds(21));

            var visible = await student.VisibleResultAsync("YP4301");
            Assert.NotNull(visible);
            Assert.Equal(77, visible.Value.GetProperty("mark").GetInt32());
            Assert.Equal(JsonValueKind.Null, visible.Value.GetProperty("correctedAt").ValueKind);
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }

        // Unpublished: gone again at once.
        Assert.Null(await student.VisibleResultAsync("YP4301"));
    }

    [Fact]
    public async Task Scheduled_publications_can_be_rescheduled_and_cancelled_but_not_unpublished()
    {
        const string year = "2044/45";
        await factory.SeedModuleAsync("YP4401", Semester.Spring, year, [("S000071", 52), ("S000072", 47)]);
        using var admin = await factory.DemoAdminAsync();
        using var student = await factory.LoginStudentAsync("S000071");

        try
        {
            var body = await admin.PostJsonAsync("/api/admin/results/publish", Publish(year, "spring", factory.Clock.GetUtcNow().AddHours(1)));
            var id = body.GetProperty("publication").GetProperty("id").GetGuid();

            var later = factory.Clock.GetUtcNow().AddHours(2);
            var rescheduled = await admin.PutJsonAsync($"/api/admin/results/publications/{id}", new { publishAt = later });
            Assert.Equal(later, rescheduled.GetProperty("publishAt").GetDateTimeOffset());
            Assert.All(await GradesAsync("YP4401"), g => Assert.Equal(later, g.PublishedAt));
            Assert.Single(await factory.AuditAsync(AuditActions.ResultsRescheduled, id.ToString()));

            using (var tooFar = await admin.PutAsJsonAsync($"/api/admin/results/publications/{id}", new { publishAt = factory.Clock.GetUtcNow().AddDays(95) }))
            {
                await tooFar.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "publish-too-far-ahead");
            }

            using (var unpublish = await admin.PostAsJsonAsync($"/api/admin/results/publications/{id}/unpublish", new { reason = StaffData.Reason }))
            {
                await unpublish.AssertProblemAsync(HttpStatusCode.Conflict, "publication-scheduled");
            }

            using (var shortReason = await admin.PostAsJsonAsync($"/api/admin/results/publications/{id}/unpublish", new { reason = "too short" }))
            {
                await shortReason.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
            }

            var cancelled = await admin.SendJsonAsync(HttpMethod.Delete, $"/api/admin/results/publications/{id}", null, HttpStatusCode.OK);
            Assert.Equal((year, "spring", 2), (cancelled.GetProperty("academicYear").GetString(), cancelled.GetProperty("semester").GetString(), cancelled.GetProperty("grades").GetInt32()));
            Assert.All(await GradesAsync("YP4401"), g => Assert.Equal((GradeStatus.Submitted, (DateTimeOffset?)null, (Guid?)null), (g.Status, g.PublishedAt, g.PublicationId)));
            Assert.False(await factory.WithDbAsync(db => db.ResultsPublications.AnyAsync(p => p.Id == id)));
            var audit = Assert.Single(await factory.AuditAsync(AuditActions.ResultsCancelled, id.ToString()));
            Assert.Equal(2, StaffData.DetailsOf(audit).GetProperty("grades").GetInt32());

            using (var gone = await admin.DeleteAsync($"/api/admin/results/publications/{id}"))
            {
                await gone.AssertProblemAsync(HttpStatusCode.NotFound, "publication-not-found");
            }

            // A cancelled publication's marks never reached the student.
            Assert.Null(await student.VisibleResultAsync("YP4401"));
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    [Fact]
    public async Task Live_publications_can_be_unpublished_but_not_rescheduled_or_cancelled()
    {
        const string year = "2045/46";
        await factory.SeedModuleAsync("YP4501", Semester.Autumn, year, [("S000083", 81)]);
        using var admin = await factory.DemoAdminAsync();
        using var student = await factory.LoginStudentAsync("S000083");

        try
        {
            var body = await admin.PostJsonAsync("/api/admin/results/publish", Publish(year, "autumn", factory.Clock.GetUtcNow()));
            var id = body.GetProperty("publication").GetProperty("id").GetGuid();
            Assert.Equal(81, (await student.VisibleResultAsync("YP4501"))!.Value.GetProperty("mark").GetInt32());

            using (var reschedule = await admin.PutAsJsonAsync($"/api/admin/results/publications/{id}", new { publishAt = factory.Clock.GetUtcNow().AddHours(1) }))
            {
                await reschedule.AssertProblemAsync(HttpStatusCode.Conflict, "publication-live");
            }

            using (var cancel = await admin.DeleteAsync($"/api/admin/results/publications/{id}"))
            {
                await cancel.AssertProblemAsync(HttpStatusCode.Conflict, "publication-live");
            }

            // Once live, a module can no longer be returned to draft: unpublish or correct single marks instead.
            using (var returned = await admin.PostAsJsonAsync("/api/admin/results/modules/YP4501/return-to-draft", new { reason = StaffData.Reason, academicYear = year }))
            {
                await returned.AssertProblemAsync(HttpStatusCode.Conflict, "module-locked");
            }

            var unpublished = await admin.PostJsonAsync($"/api/admin/results/publications/{id}/unpublish", new { reason = StaffData.Reason });
            Assert.Equal(1, unpublished.GetProperty("grades").GetInt32());
            Assert.Null(await student.VisibleResultAsync("YP4501"));
            Assert.Equal(GradeStatus.Submitted, (await factory.GradeAsync("S000083", "YP4501")).Status);
            var audit = Assert.Single(await factory.AuditAsync(AuditActions.ResultsUnpublished, id.ToString()));
            Assert.Equal(StaffData.Reason, StaffData.DetailsOf(audit).GetProperty("reason").GetString());
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    [Fact]
    public async Task Return_to_draft_allowed_before_scheduled_instant()
    {
        const string year = "2046/47";
        await factory.SeedModuleAsync("YP4601", Semester.Autumn, year, [("S000074", 59), ("S000075", 62)]);
        await factory.SeedModuleAsync("YP4602", Semester.Autumn, year, [("S000074", 68)]);
        using var admin = await factory.DemoAdminAsync();
        using var student = await factory.LoginStudentAsync("S000074");

        try
        {
            var body = await admin.PostJsonAsync("/api/admin/results/publish", Publish(year, "autumn", factory.Clock.GetUtcNow().AddSeconds(30)));
            var id = body.GetProperty("publication").GetProperty("id").GetGuid();
            Assert.Equal((3, 2), (body.GetProperty("publication").GetProperty("gradeCount").GetInt32(), body.GetProperty("publication").GetProperty("moduleCount").GetInt32()));

            var returned = await admin.PostJsonAsync("/api/admin/results/modules/YP4601/return-to-draft", new { reason = StaffData.Reason, academicYear = year });
            Assert.Equal(("YP4601", "draft", true), (returned.GetProperty("code").GetString(), returned.GetProperty("status").GetString(), returned.GetProperty("fromScheduledPublication").GetBoolean()));
            Assert.All(await GradesAsync("YP4601"), g => Assert.Equal((GradeStatus.Draft, (DateTimeOffset?)null, (Guid?)null, (DateTimeOffset?)null), (g.Status, g.PublishedAt, g.PublicationId, g.SubmittedAt)));

            var publication = await factory.WithDbAsync(db => db.ResultsPublications.AsNoTracking().SingleAsync(p => p.Id == id));
            Assert.Equal((1, 1), (publication.GradeCount, publication.ModuleCount));
            var audit = Assert.Single(await factory.AuditAsync(AuditActions.ModuleReturnedToDraft, "YP4601"));
            var details = StaffData.DetailsOf(audit);
            Assert.Equal((2, true, year), (details.GetProperty("gradeCount").GetInt32(), details.GetProperty("fromScheduledPublication").GetBoolean(), details.GetProperty("academicYear").GetString()));

            // A draft module has nothing to return.
            using (var again = await admin.PostAsJsonAsync("/api/admin/results/modules/YP4601/return-to-draft", new { reason = StaffData.Reason, academicYear = year }))
            {
                await again.AssertProblemAsync(HttpStatusCode.Conflict, "module-not-submitted");
            }

            using (var unknown = await admin.PostAsJsonAsync("/api/admin/results/modules/YP4699/return-to-draft", new { reason = StaffData.Reason }))
            {
                await unknown.AssertProblemAsync(HttpStatusCode.NotFound, "module-not-found");
            }

            // At the instant the rest of the publication goes live; the returned module's mark never does.
            factory.Clock.Advance(TimeSpan.FromSeconds(31));
            Assert.Equal(68, (await student.VisibleResultAsync("YP4602"))!.Value.GetProperty("mark").GetInt32());
            Assert.Null(await student.VisibleResultAsync("YP4601"));
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    [Fact]
    public async Task Publishing_twice_publishes_only_newly_submitted_modules()
    {
        const string year = "2047/48";
        await factory.SeedModuleAsync("YP4701", Semester.Spring, year, [("S000084", 55)]);
        await factory.SeedModuleAsync("YP4702", Semester.Spring, year, [("S000084", 45), ("S000077", 65)], GradeStatus.Draft);
        using var admin = await factory.DemoAdminAsync();

        try
        {
            var first = await admin.PostJsonAsync("/api/admin/results/publish", Publish(year, "spring", factory.Clock.GetUtcNow()));
            var firstId = first.GetProperty("publication").GetProperty("id").GetGuid();
            Assert.Equal((1, 1), (first.GetProperty("published").GetProperty("modules").GetInt32(), first.GetProperty("published").GetProperty("grades").GetInt32()));

            // Nothing new yet.
            using (var nothing = await admin.PostAsJsonAsync("/api/admin/results/publish", Publish(year, "spring", factory.Clock.GetUtcNow())))
            {
                await nothing.AssertProblemAsync(HttpStatusCode.Conflict, "nothing-to-publish");
            }

            await factory.SetGradeStatusAsync("YP4702", GradeStatus.Submitted);
            var second = await admin.PostJsonAsync("/api/admin/results/publish", Publish(year, "spring", factory.Clock.GetUtcNow()));
            var secondId = second.GetProperty("publication").GetProperty("id").GetGuid();
            Assert.NotEqual(firstId, secondId);
            Assert.Equal((1, 2), (second.GetProperty("published").GetProperty("modules").GetInt32(), second.GetProperty("published").GetProperty("grades").GetInt32()));
            Assert.Empty(second.GetProperty("excluded").EnumerateArray());

            Assert.Equal(firstId, (await factory.GradeAsync("S000084", "YP4701")).PublicationId);
            Assert.All(await GradesAsync("YP4702"), g => Assert.Equal(secondId, g.PublicationId));
            var history = await admin.GetJsonAsync($"/api/admin/results?semester=spring&academicYear={Uri.EscapeDataString(year)}");
            Assert.Equal([secondId, firstId], history.GetProperty("publications").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    [Fact]
    public async Task Announce_creates_the_pinned_announcement()
    {
        const string year = "2048/49";
        await factory.SeedModuleAsync("YP4801", Semester.Autumn, year, [("S000078", 70)]);
        using var admin = await factory.DemoAdminAsync();
        Guid? announcementId = null;

        try
        {
            var publishAt = factory.Clock.GetUtcNow().AddMinutes(10);
            await admin.PostJsonAsync("/api/admin/results/publish", Publish(year, "autumn", publishAt, announce: true));

            var announcements = await admin.GetJsonAsync("/api/admin/announcements");
            var announcement = announcements.EnumerateArray().Single(a => a.GetProperty("title").GetString() == $"Autumn {year} results are available");
            announcementId = announcement.GetProperty("id").GetGuid();
            Assert.Equal(("university", true), (announcement.GetProperty("scope").GetString(), announcement.GetProperty("pinned").GetBoolean()));
            Assert.Equal(publishAt, announcement.GetProperty("publishedAt").GetDateTimeOffset());
            Assert.StartsWith("Sign in to see your marks.\n\n", announcement.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.EndsWith(RushDay.Api.Options.BrandingOptions.DefaultResultsFootnote, announcement.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Single(await factory.AuditAsync(AuditActions.AnnouncementCreated, announcementId.ToString()));

            // It is scheduled with the results: no student sees it before the instant.
            using var student = await factory.LoginStudentAsync("S000078");
            var visible = await student.GetJsonAsync("/api/announcements");
            Assert.DoesNotContain(visible.EnumerateArray(), a => a.GetProperty("id").GetGuid() == announcementId);
        }
        finally
        {
            if (announcementId is { } id)
            {
                using var delete = await admin.DeleteAsync($"/api/admin/announcements/{id}");
                Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            }

            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    [Fact]
    public async Task Only_active_enrolments_are_published()
    {
        const string year = "2049/50";
        await factory.SeedModuleAsync("YP4901", Semester.Autumn, year, [("S000079", 63), ("S000065", 41)]);

        // S000065 was withdrawn after submission: their grade is never published or shown.
        await factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == "S000065").Select(s => s.Id).SingleAsync();
            var moduleId = await db.Modules.Where(m => m.Code == "YP4901").Select(m => m.Id).SingleAsync();
            await db.Enrolments.Where(e => e.StudentId == studentId && e.ModuleId == moduleId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, RushDay.Domain.Enrolments.EnrolmentStatus.Withdrawn));
        });
        using var admin = await factory.DemoAdminAsync();

        try
        {
            var body = await admin.PostJsonAsync("/api/admin/results/publish", Publish(year, "autumn", factory.Clock.GetUtcNow()));
            Assert.Equal(1, body.GetProperty("published").GetProperty("grades").GetInt32());
            Assert.Equal(GradeStatus.Submitted, (await factory.GradeAsync("S000065", "YP4901")).Status);
        }
        finally
        {
            await factory.RemovePublicationsAsync(admin, year);
        }
    }

    private static object Publish(string year, string semester, DateTimeOffset publishAt, bool announce = false) =>
        new { academicYear = year, semester, publishAt, announce, note = "Test publication" };

    private Task<List<Grade>> GradesAsync(string code) =>
        factory.WithDbAsync(async db =>
        {
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            return await db.Grades.AsNoTracking().Where(g => g.ModuleId == moduleId).ToListAsync();
        });
}
