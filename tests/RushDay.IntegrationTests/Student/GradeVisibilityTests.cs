using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;

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
            Assert.Equal("published", g.GetProperty("status").GetString());
            Assert.True(g.GetProperty("visibleToStudent").GetBoolean());
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

    private static List<string> MarkCodes(JsonElement results) =>
        [.. results.EnumerateArray().Select(r => r.GetProperty("moduleCode").GetString()!).Order(StringComparer.Ordinal)];

    /// <summary>The test modules' codes among visible results (the seeded 2025/26 marks are visible too).</summary>
    private static List<string> TestCodes(JsonElement results) =>
        [.. MarkCodes(results).Where(code => code.StartsWith("ZZ", StringComparison.Ordinal))];

    private static JsonElement SemesterOf(JsonElement results, string academicYear, string semester) =>
        results.GetProperty("semesters").EnumerateArray().Single(s => s.GetProperty("academicYear").GetString() == academicYear && s.GetProperty("semester").GetString() == semester);
}
