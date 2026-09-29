using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Enrolments;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// The visibility rule (00-overview.md section 4.3, T6): a student sees a grade iff it is Published, its instant has
/// passed and their enrolment on the module is Active. Draft, submitted and future-published marks never appear in any
/// student response (dashboard, results, export), and appear exactly when <c>factory.Clock</c> passes the instant.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GradeVisibilityTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Draft_submitted_and_future_published_marks_are_hidden_until_the_instant()
    {
        const string student = "S000051";
        string[] codes = ["ZZ2201", "ZZ2202", "ZZ2203", "ZZ2204"];
        foreach (var code in codes)
        {
            await factory.CreateModuleAsync(code, Semester.Spring, capacity: 100);
        }

        using var client = await factory.LoginStudentAsync(student);
        foreach (var code in codes)
        {
            using var enrol = await client.EnrolAsync(code);
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        var publishAt = factory.Clock.GetUtcNow().AddSeconds(30);
        await factory.PutGradeAsync(student, "ZZ2201", GradeStatus.Draft, 11);
        await factory.PutGradeAsync(student, "ZZ2202", GradeStatus.Submitted, 12);
        await factory.PutGradeAsync(student, "ZZ2203", GradeStatus.Published, 13, publishedAt: publishAt);
        await factory.PutGradeAsync(student, "ZZ2204", GradeStatus.Published, null, GradeOutcome.Absent, publishedAt: factory.Clock.GetUtcNow().AddMinutes(-1));

        // Before the instant: only the absence (published in the past) is visible, as an outcome without a mark.
        var dashboard = await client.GetJsonAsync("/api/me/dashboard");
        Assert.Equal(["ZZ2204"], TestCodes(dashboard.GetProperty("results")));
        var absent = dashboard.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("moduleCode").GetString() == "ZZ2204");
        Assert.Equal("absent", absent.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, absent.GetProperty("mark").ValueKind);

        var modules = dashboard.GetProperty("modules").EnumerateArray().ToDictionary(m => m.GetProperty("code").GetString()!);
        Assert.True(modules["ZZ2201"].GetProperty("canWithdraw").GetBoolean());
        Assert.Equal("results", modules["ZZ2202"].GetProperty("withdrawBlockedReason").GetString());
        Assert.Equal("results", modules["ZZ2203"].GetProperty("withdrawBlockedReason").GetString());

        var results = await client.GetJsonAsync("/api/me/results");
        var spring = SemesterOf(results, StudentData.CurrentYear, "spring");
        Assert.Equal("published", spring.GetProperty("state").GetString());
        Assert.Equal(["ZZ2204"], TestCodes(spring.GetProperty("results")));

        var export = await client.GetJsonAsync("/api/me/export.json");
        Assert.Equal(["ZZ2204"], TestCodes(export.GetProperty("grades")));
        Assert.All(export.GetProperty("grades").EnumerateArray(), g =>
        {
            Assert.False(g.TryGetProperty("status", out _));
            Assert.False(g.TryGetProperty("version", out _));
            Assert.False(g.TryGetProperty("visibleToStudent", out _));
        });

        // After the instant the future-published mark appears everywhere; draft and submitted never do.
        factory.Clock.Advance(TimeSpan.FromSeconds(31));

        dashboard = await client.GetJsonAsync("/api/me/dashboard");
        Assert.Equal(["ZZ2203", "ZZ2204"], TestCodes(dashboard.GetProperty("results")));
        Assert.Equal(13, dashboard.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("moduleCode").GetString() == "ZZ2203").GetProperty("mark").GetInt32());

        results = await client.GetJsonAsync("/api/me/results");
        Assert.Equal(["ZZ2203", "ZZ2204"], TestCodes(SemesterOf(results, StudentData.CurrentYear, "spring").GetProperty("results")));

        export = await client.GetJsonAsync("/api/me/export.json");
        Assert.Equal(["ZZ2203", "ZZ2204"], TestCodes(export.GetProperty("grades")));
    }

    [Fact]
    public async Task A_semester_whose_marks_are_all_scheduled_shows_its_instant_and_no_mark()
    {
        const string student = "S000054";
        await factory.CreateModuleAsync("ZZ2210", Semester.Spring, capacity: 100);
        using var client = await factory.LoginStudentAsync(student);
        using (var enrol = await client.EnrolAsync("ZZ2210"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        var publishAt = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        await factory.PutGradeAsync(student, "ZZ2210", GradeStatus.Published, 77, publishedAt: publishAt);

        var results = await client.GetJsonAsync("/api/me/results");
        var spring = SemesterOf(results, StudentData.CurrentYear, "spring");
        Assert.Equal("scheduled", spring.GetProperty("state").GetString());
        Assert.Equal("2026-12-01T09:00:00.000Z", spring.GetProperty("publishAt").GetString());
        Assert.Empty(spring.GetProperty("results").EnumerateArray());
        Assert.DoesNotContain("77", spring.GetRawText(), StringComparison.Ordinal);

        // Newest year first, autumn before spring; last year's autumn is published.
        var groups = results.GetProperty("semesters").EnumerateArray().Select(s => (s.GetProperty("academicYear").GetString(), s.GetProperty("semester").GetString())).ToList();
        Assert.Equal(groups.OrderByDescending(g => g.Item1, StringComparer.Ordinal).ThenBy(g => g.Item2 == "spring" ? 1 : 0), groups);
        Assert.Equal("published", SemesterOf(results, StudentData.PreviousYear, "autumn").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Withdrawn_student_never_sees_mark()
    {
        const string student = "S000052";
        using var client = await factory.LoginStudentAsync(student);

        var before = await client.GetJsonAsync("/api/me/dashboard");
        var visible = MarkCodes(before.GetProperty("results"));
        Assert.Equal(4, visible.Count);
        var withdrawnCode = visible[0];

        // An administrator withdraws the student from a module whose mark is published (admin withdrawal ignores
        // results-exist, 02-api.md section 8.5); the mark then disappears from every student response.
        await factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == student).Select(s => s.Id).SingleAsync();
            var moduleId = await db.Modules.Where(m => m.Code == withdrawnCode).Select(m => m.Id).SingleAsync();
            await db.Enrolments.Where(e => e.StudentId == studentId && e.ModuleId == moduleId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EnrolmentStatus.Withdrawn).SetProperty(e => e.WithdrawnAt, factory.Clock.GetUtcNow()));
        });

        var dashboard = await client.GetJsonAsync("/api/me/dashboard");
        Assert.DoesNotContain(withdrawnCode, MarkCodes(dashboard.GetProperty("results")));
        Assert.DoesNotContain(dashboard.GetProperty("completed").EnumerateArray(), c => c.GetProperty("code").GetString() == withdrawnCode);
        Assert.Equal(3, dashboard.GetProperty("results").GetArrayLength());

        var results = await client.GetJsonAsync("/api/me/results");
        Assert.DoesNotContain(withdrawnCode, MarkCodes(SemesterOf(results, StudentData.PreviousYear, "autumn").GetProperty("results")));

        var export = await client.GetJsonAsync("/api/me/export.json");
        Assert.DoesNotContain(withdrawnCode, MarkCodes(export.GetProperty("grades")));
        var row = export.GetProperty("enrolments").EnumerateArray().Single(e => e.GetProperty("moduleCode").GetString() == withdrawnCode);
        Assert.Equal("withdrawn", row.GetProperty("status").GetString());

        // The average is over the three marks that are still visible.
        Assert.Equal(dashboard.GetProperty("weightedAverage").GetDouble(), results.GetProperty("weightedAverage").GetDouble(), 6);
        Assert.Equal(dashboard.GetProperty("weightedAverage").GetDouble(), export.GetProperty("weightedAverage").GetDouble(), 6);
    }

    /// <summary>
    /// Cancel and unpublish revert grades to Submitted (02-api.md section 8.5), in the worst case keeping the instant: the
    /// mark disappears from every student response and the semester falls back to <c>pending</c>. Published exactly
    /// at the current instant is visible (<c>published_at &lt;= now</c>).
    /// </summary>
    [Fact]
    public async Task A_grade_reverted_to_submitted_is_hidden_and_its_semester_is_pending_again()
    {
        const string student = "S000034";
        await factory.CreateModuleAsync("ZZ2220", Semester.Spring, capacity: 10, credits: 5);
        using var client = await factory.LoginStudentAsync(student);
        using (var enrol = await client.EnrolAsync("ZZ2220"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        await factory.PutGradeAsync(student, "ZZ2220", GradeStatus.Published, 91, publishedAt: factory.Clock.GetUtcNow().AddMinutes(-5));
        Assert.Equal(["ZZ2220"], TestCodes(SemesterOf(await client.GetJsonAsync("/api/me/results"), StudentData.CurrentYear, "spring").GetProperty("results")));

        var (studentId, moduleId) = (await factory.StudentIdAsync(student), await factory.ModuleIdAsync("ZZ2220"));
        await factory.WithDbAsync(db => db.Grades.Where(g => g.StudentId == studentId && g.ModuleId == moduleId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, GradeStatus.Submitted).SetProperty(g => g.PublicationId, (Guid?)null)));

        foreach (var path in new[] { "/api/me/dashboard", "/api/me/results", "/api/me/export.json" })
        {
            var body = (await client.GetJsonAsync(path)).GetRawText();
            Assert.DoesNotContain("\"mark\":91", body, StringComparison.Ordinal);
        }

        var spring = SemesterOf(await client.GetJsonAsync("/api/me/results"), StudentData.CurrentYear, "spring");
        Assert.Equal("pending", spring.GetProperty("state").GetString());
        Assert.Empty(spring.GetProperty("results").EnumerateArray());

        await factory.WithDbAsync(db => db.Grades.Where(g => g.StudentId == studentId && g.ModuleId == moduleId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, GradeStatus.Published).SetProperty(g => g.PublishedAt, factory.Clock.GetUtcNow())));
        Assert.Contains("\"mark\":91", (await client.GetJsonAsync("/api/me/results")).GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A scheduled grade on a withdrawn enrolment is not even "scheduled": the pair is omitted, and neither the student
    /// nor an administrator's override can re-enrol past it (<c>results-exist</c>).
    /// </summary>
    [Fact]
    public async Task A_scheduled_grade_of_a_withdrawn_enrolment_is_not_scheduled_and_blocks_re_enrolment()
    {
        const string student = "S000035";
        await factory.CreateModuleAsync("ZZ2221", Semester.Spring, capacity: 10, credits: 5);
        using var client = await factory.LoginStudentAsync(student);
        using (var enrol = await client.EnrolAsync("ZZ2221"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        await factory.PutGradeAsync(student, "ZZ2221", GradeStatus.Published, 64, publishedAt: factory.Clock.GetUtcNow().AddDays(3));
        var (studentId, moduleId) = (await factory.StudentIdAsync(student), await factory.ModuleIdAsync("ZZ2221"));
        await factory.WithDbAsync(db => db.Enrolments.Where(e => e.StudentId == studentId && e.ModuleId == moduleId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EnrolmentStatus.Withdrawn).SetProperty(e => e.WithdrawnAt, factory.Clock.GetUtcNow())));

        var results = await client.GetJsonAsync("/api/me/results");
        Assert.DoesNotContain(results.GetProperty("semesters").EnumerateArray(), s => s.GetProperty("academicYear").GetString() == StudentData.CurrentYear && s.GetProperty("semester").GetString() == "spring");

        using (var again = await client.EnrolAsync("ZZ2221"))
        {
            await again.AssertProblemAsync(HttpStatusCode.Conflict, "results-exist");
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var overridden = await scope.ServiceProvider.GetRequiredService<EnrolmentService>()
            .EnrolAsync(studentId, "ZZ2221", null, new EnrolOptions(Override: true, Reason: "An override cannot retake a completed module."));
        Assert.Equal(EnrolmentError.ResultsExist, overridden.Failure?.Error);
    }

    /// <summary>
    /// <c>completed[]</c> shows an outcome and a mark only when the grade is visible: a Deferred or Absent outcome has no
    /// mark or band, a Draft shows nothing, and the weighted average counts visible marks only (a corrected one with
    /// <c>correctedAt</c>).
    /// </summary>
    [Fact]
    public async Task Completed_modules_show_visible_outcomes_only_and_the_average_counts_marks_only()
    {
        const string student = "S000036";
        using var client = await factory.LoginStudentAsync(student);
        var codes = (await client.GetJsonAsync("/api/me/dashboard")).GetProperty("completed").EnumerateArray().Select(c => c.GetProperty("code").GetString()!).ToList();
        Assert.Equal(4, codes.Count);

        var studentId = await factory.StudentIdAsync(student);
        var ids = new List<Guid>();
        foreach (var code in codes)
        {
            ids.Add(await factory.ModuleIdAsync(code));
        }

        var now = factory.Clock.GetUtcNow();
        await factory.WithDbAsync(db => db.Grades.Where(g => g.StudentId == studentId && g.ModuleId == ids[0])
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Outcome, GradeOutcome.Deferred).SetProperty(g => g.Mark, (int?)null)));
        await factory.WithDbAsync(db => db.Grades.Where(g => g.StudentId == studentId && g.ModuleId == ids[1])
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Outcome, GradeOutcome.Absent).SetProperty(g => g.Mark, (int?)null)));
        await factory.WithDbAsync(db => db.Grades.Where(g => g.StudentId == studentId && g.ModuleId == ids[2])
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, GradeStatus.Draft).SetProperty(g => g.Mark, 3).SetProperty(g => g.PublishedAt, (DateTimeOffset?)null).SetProperty(g => g.PublicationId, (Guid?)null)));
        await factory.WithDbAsync(db => db.Grades.Where(g => g.StudentId == studentId && g.ModuleId == ids[3])
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Mark, 97).SetProperty(g => g.CorrectedAt, now).SetProperty(g => g.Version, 7)));

        var dashboard = await client.GetJsonAsync("/api/me/dashboard");
        var completed = dashboard.GetProperty("completed").EnumerateArray().ToDictionary(c => c.GetProperty("code").GetString()!);
        Assert.Equal(("deferred", JsonValueKind.Null, JsonValueKind.Null), Outcome(completed[codes[0]]));
        Assert.Equal(("absent", JsonValueKind.Null, JsonValueKind.Null), Outcome(completed[codes[1]]));
        Assert.Equal((null, JsonValueKind.Null, JsonValueKind.Null), Outcome(completed[codes[2]]));
        Assert.Equal(97, completed[codes[3]].GetProperty("mark").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, completed[codes[3]].GetProperty("band").ValueKind);

        Assert.DoesNotContain(codes[2], MarkCodes(dashboard.GetProperty("results")));
        var corrected = dashboard.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("moduleCode").GetString() == codes[3]);
        Assert.NotEqual(JsonValueKind.Null, corrected.GetProperty("correctedAt").ValueKind);
        Assert.DoesNotContain(codes[2], MarkCodes((await client.GetJsonAsync("/api/me/export.json")).GetProperty("grades")));

        var results = await client.GetJsonAsync("/api/me/results");
        Assert.Equal(97.0, results.GetProperty("weightedAverage").GetDouble(), 3);
        Assert.Equal(97.0, dashboard.GetProperty("weightedAverage").GetDouble(), 3);

        static (string?, JsonValueKind, JsonValueKind) Outcome(JsonElement module) =>
            (module.GetProperty("outcome").GetString(), module.GetProperty("mark").ValueKind, module.GetProperty("band").ValueKind);
    }

    /// <summary>
    /// A semester with one visible and one scheduled grade is <c>published</c>, and the scheduled mark appears in no
    /// student response until its instant.
    /// </summary>
    [Fact]
    public async Task A_semester_mixing_a_visible_and_a_scheduled_grade_never_sends_the_scheduled_mark()
    {
        const string student = "S000037";
        await factory.CreateModuleAsync("ZZ2222", Semester.Spring, capacity: 10, credits: 5);
        await factory.CreateModuleAsync("ZZ2223", Semester.Spring, capacity: 10, credits: 5);
        using var client = await factory.LoginStudentAsync(student);
        foreach (var code in new[] { "ZZ2222", "ZZ2223" })
        {
            using var enrol = await client.EnrolAsync(code);
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        await factory.PutGradeAsync(student, "ZZ2222", GradeStatus.Published, 55, publishedAt: factory.Clock.GetUtcNow().AddMinutes(-1));
        await factory.PutGradeAsync(student, "ZZ2223", GradeStatus.Published, 88, publishedAt: factory.Clock.GetUtcNow().AddDays(2));

        foreach (var path in new[] { "/api/me/dashboard", "/api/me/results", "/api/me/export.json", "/api/me/enrolments" })
        {
            var body = (await client.GetJsonAsync(path)).GetRawText();
            Assert.DoesNotContain("\"mark\":88", body, StringComparison.Ordinal);
        }

        var spring = SemesterOf(await client.GetJsonAsync("/api/me/results"), StudentData.CurrentYear, "spring");
        Assert.Equal("published", spring.GetProperty("state").GetString());
        Assert.Equal(["ZZ2222"], TestCodes(spring.GetProperty("results")));
    }

    private static List<string> MarkCodes(JsonElement results) =>
        [.. results.EnumerateArray().Select(r => r.GetProperty("moduleCode").GetString()!).Order(StringComparer.Ordinal)];

    /// <summary>The test modules' codes among visible results (the seeded 2025/26 marks are visible too).</summary>
    private static List<string> TestCodes(JsonElement results) =>
        [.. MarkCodes(results).Where(code => code.StartsWith("ZZ", StringComparison.Ordinal))];

    private static JsonElement SemesterOf(JsonElement results, string academicYear, string semester) =>
        results.GetProperty("semesters").EnumerateArray().Single(s => s.GetProperty("academicYear").GetString() == academicYear && s.GetProperty("semester").GetString() == semester);
}
