using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;
using RushDay.Api.Observability;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// <c>POST /api/me/enrolments</c> and <c>DELETE /api/me/enrolments/{code}</c> (02-api.md section 8.3,
/// 04-performance-and-ops.md section 2): the 201/404/409/422 matrix with <c>S000001</c>–<c>S000050</c>, CS3099 and small
/// test modules, the three window-closed extension shapes, the withdrawal deadline, <c>results-exist</c>, reactivation
/// stamping the current year, the credit limit, audit rows and metrics.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EnrolmentTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Enrol_withdraw_and_enrol_again_on_CS3099()
    {
        const string student = "S000003";
        using var client = await factory.LoginStudentAsync(student);
        var before = await factory.CountsAsync("CS3099");

        // 201: a place taken, the count from RETURNING, no Location header.
        using (var enrol = await client.EnrolAsync("CS3099"))
        {
            var body = await enrol.ReadJsonAsync();
            Assert.True(enrol.StatusCode == HttpStatusCode.Created, body.ToString());
            Assert.Null(enrol.Headers.Location);
            Assert.Equal(["enrolledAt", "moduleCode", "placesRemaining"], body.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal("CS3099", body.GetProperty("moduleCode").GetString());
            Assert.Equal(30 - (before.EnrolledCount + 1), body.GetProperty("placesRemaining").GetInt32());
            Assert.StartsWith("2026-09-27T", body.GetProperty("enrolledAt").GetString(), StringComparison.Ordinal);
        }

        var enrolled = await factory.CountsAsync("CS3099");
        Assert.Equal(before.EnrolledCount + 1, enrolled.EnrolledCount);
        Assert.Equal(enrolled.EnrolledCount, enrolled.ActiveCount);
        var row = (await factory.EnrolmentAsync(student, "CS3099"))!;
        Assert.Equal((EnrolmentStatus.Active, EnrolmentSource.Self, StudentData.CurrentYear), (row.Status, row.Source, row.AcademicYear));
        Assert.Null(row.CreatedByUserId);
        var created = await AuditAsync(row.Id, AuditActions.EnrolmentCreated);
        var createdDetails = Assert.Single(created);
        Assert.Contains("\"reactivated\": false", createdDetails, StringComparison.Ordinal);
        Assert.Contains("\"source\": \"self\"", createdDetails, StringComparison.Ordinal);

        // 409 already-enrolled: the same request again changes nothing.
        using (var again = await client.EnrolAsync("CS3099"))
        {
            await again.AssertProblemAsync(HttpStatusCode.Conflict, "already-enrolled");
        }

        Assert.Equal(enrolled, await factory.CountsAsync("CS3099"));

        // 204: the place is released immediately and the row toggles to Withdrawn.
        using (var withdraw = await client.WithdrawAsync("CS3099"))
        {
            Assert.Equal(HttpStatusCode.NoContent, withdraw.StatusCode);
        }

        Assert.Equal((before.EnrolledCount, before.EnrolledCount), await factory.CountsAsync("CS3099"));
        var withdrawnRow = (await factory.EnrolmentAsync(student, "CS3099"))!;
        Assert.Equal(EnrolmentStatus.Withdrawn, withdrawnRow.Status);
        Assert.NotNull(withdrawnRow.WithdrawnAt);
        Assert.Single(await AuditAsync(row.Id, AuditActions.EnrolmentWithdrawn));

        // Enrol again: the withdrawn row is reactivated (never a second row) and stamped with the current year.
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var reenrol = await client.EnrolAsync("CS3099"))
        {
            Assert.Equal(HttpStatusCode.Created, reenrol.StatusCode);
        }

        var reactivated = (await factory.EnrolmentAsync(student, "CS3099"))!;
        Assert.Equal(row.Id, reactivated.Id);
        Assert.Equal((EnrolmentStatus.Active, StudentData.CurrentYear), (reactivated.Status, reactivated.AcademicYear));
        Assert.Null(reactivated.WithdrawnAt);
        Assert.True(reactivated.EnrolledAt > row.EnrolledAt);
        Assert.Contains(await AuditAsync(row.Id, AuditActions.EnrolmentCreated), d => d.Contains("\"reactivated\": true", StringComparison.Ordinal));
        var after = await factory.CountsAsync("CS3099");
        Assert.Equal((before.EnrolledCount + 1, before.EnrolledCount + 1), after);
    }

    [Fact]
    public async Task Codes_are_upper_cased_and_unknown_codes_are_404()
    {
        using var client = await factory.LoginStudentAsync("S000006");

        using (var unknown = await client.EnrolAsync("ZZ9999"))
        {
            await unknown.AssertProblemAsync(HttpStatusCode.NotFound, "module-not-found");
        }

        await factory.CreateModuleAsync("ZZ1106", Semester.Spring, capacity: 10);
        using (var lower = await client.EnrolAsync(" zz1106 "))
        {
            var body = await lower.ReadJsonAsync();
            Assert.Equal(HttpStatusCode.Created, lower.StatusCode);
            Assert.Equal("ZZ1106", body.GetProperty("moduleCode").GetString());
        }

        using (var notEnrolled = await client.WithdrawAsync("CS3099"))
        {
            await notEnrolled.AssertProblemAsync(HttpStatusCode.NotFound, "not-enrolled");
        }

        using (var noSuchModule = await client.WithdrawAsync("ZZ9999"))
        {
            await noSuchModule.AssertProblemAsync(HttpStatusCode.NotFound, "not-enrolled");
        }

        using (var empty = await client.PostAsJsonAsync("/api/me/enrolments", new { moduleCode = "" }))
        {
            await empty.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
        }

        // Every mutation needs the antiforgery header.
        client.DefaultRequestHeaders.Remove(TestClients.CsrfHeader);
        using var forged = await client.EnrolAsync("CS3099");
        await forged.AssertProblemAsync(HttpStatusCode.BadRequest, "antiforgery");
    }

    [Fact]
    public async Task Full_and_inactive_modules_are_refused()
    {
        await factory.CreateModuleAsync("ZZ1101", Semester.Spring, capacity: 1);
        await factory.CreateModuleAsync("ZZ1102", Semester.Spring, capacity: 10, isActive: false);

        using var first = await factory.LoginStudentAsync("S000009");
        using var second = await factory.LoginStudentAsync("S000010");

        using (var taken = await first.EnrolAsync("ZZ1101"))
        {
            Assert.Equal(HttpStatusCode.Created, taken.StatusCode);
            Assert.Equal(0, (await taken.ReadJsonAsync()).GetProperty("placesRemaining").GetInt32());
        }

        using (var full = await second.EnrolAsync("ZZ1101"))
        {
            await full.AssertProblemAsync(HttpStatusCode.Conflict, "module-full");
        }

        using (var inactive = await second.EnrolAsync("ZZ1102"))
        {
            await inactive.AssertProblemAsync(HttpStatusCode.Conflict, "module-inactive");
        }

        Assert.Equal((1, 1), await factory.CountsAsync("ZZ1101"));
        Assert.Equal((0, 0), await factory.CountsAsync("ZZ1102"));
    }

    /// <summary>Not yet open and closed carry the window's instants; with no window both are null.</summary>
    [Fact]
    public async Task Window_closed_has_the_three_extension_shapes()
    {
        var autumnModule = (await factory.FreeModulesAsync("S000012", Semester.Autumn, 1)).Single();

        await using (var early = factory.Derive(clock: new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero))))
        {
            using var client = await early.LoginStudentAsync("S000012");
            using var response = await client.EnrolAsync(autumnModule);
            var problem = await response.AssertProblemAsync(HttpStatusCode.Conflict, "enrolment-window-closed");
            Assert.Equal("autumn", problem.GetProperty("semester").GetString());
            Assert.Equal("2026-09-14T09:00:00.000Z", problem.GetProperty("opensAt").GetString());
            Assert.Equal("2026-10-02T17:00:00.000Z", problem.GetProperty("closesAt").GetString());
        }

        await factory.CreateModuleAsync("ZZ1107", Semester.Spring, capacity: 10);
        await using (var late = factory.Derive(clock: new FakeTimeProvider(new DateTimeOffset(2027, 2, 15, 9, 0, 0, TimeSpan.Zero))))
        {
            using var client = await late.LoginStudentAsync("S000012");
            using var response = await client.EnrolAsync("ZZ1107");
            var problem = await response.AssertProblemAsync(HttpStatusCode.Conflict, "enrolment-window-closed");
            Assert.Equal("spring", problem.GetProperty("semester").GetString());
            Assert.Equal("2026-09-14T09:00:00.000Z", problem.GetProperty("opensAt").GetString());
            Assert.Equal("2027-01-29T17:00:00.000Z", problem.GetProperty("closesAt").GetString());
        }

        // No window for (2026/27, spring): removed for the duration of the request on a host with its own caches, then
        // restored exactly (enrolment_windows has no dependants).
        var spring = await factory.WithDbAsync(db => db.EnrolmentWindows.AsNoTracking().SingleAsync(w => w.AcademicYear == StudentData.CurrentYear && w.Semester == Semester.Spring));
        try
        {
            await factory.WithDbAsync(db => db.EnrolmentWindows.Where(w => w.Id == spring.Id).ExecuteDeleteAsync());
            await using var none = factory.Derive();
            using var client = await none.LoginStudentAsync("S000012");
            using var response = await client.EnrolAsync("ZZ1107");
            var problem = await response.AssertProblemAsync(HttpStatusCode.Conflict, "enrolment-window-closed");
            Assert.Equal("spring", problem.GetProperty("semester").GetString());
            Assert.Equal(JsonValueKind.Null, problem.GetProperty("opensAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, problem.GetProperty("closesAt").ValueKind);

            // The catalogue says so too.
            var module = (await client.GetJsonAsync("/api/modules")).EnumerateArray().Single(m => m.GetProperty("code").GetString() == "ZZ1107");
            Assert.Equal("noWindow", module.GetProperty("enrolmentState").GetString());
        }
        finally
        {
            await factory.WithDbAsync(async db =>
            {
                db.EnrolmentWindows.Add(spring);
                await db.SaveChangesAsync();
            });
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<EnrolmentWindowCache>().InvalidateAsync();
        }

        Assert.Equal((0, 0), await factory.CountsAsync("ZZ1107"));
    }

    [Fact]
    public async Task Withdrawal_after_the_deadline_or_of_an_earlier_year_is_409()
    {
        // S000001's CS3001 (autumn 2026/27) after the autumn withdrawal deadline.
        await using (var late = factory.Derive(clock: new FakeTimeProvider(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero))))
        {
            using var client = await late.LoginStudentAsync("S000001");
            using var response = await client.WithdrawAsync("CS3001");
            var problem = await response.AssertProblemAsync(HttpStatusCode.Conflict, "withdrawal-deadline-passed");
            Assert.Equal("2026-10-30T17:00:00.000Z", problem.GetProperty("withdrawalDeadlineAt").GetString());
        }

        Assert.Equal(EnrolmentStatus.Active, (await factory.EnrolmentAsync("S000001", "CS3001"))!.Status);

        // A completed 2025/26 module: an earlier year's row has no open deadline.
        using var student = await factory.LoginStudentAsync("S000013");
        var completed = (await student.GetJsonAsync("/api/me/dashboard")).GetProperty("completed").EnumerateArray().First().GetProperty("code").GetString()!;
        using var earlier = await student.WithdrawAsync(completed);
        var earlierProblem = await earlier.AssertProblemAsync(HttpStatusCode.Conflict, "withdrawal-deadline-passed");
        Assert.Equal(JsonValueKind.Null, earlierProblem.GetProperty("withdrawalDeadlineAt").ValueKind);
    }

    [Fact]
    public async Task Results_exist_blocks_retaking_and_withdrawing()
    {
        const string student = "S000014";
        using var client = await factory.LoginStudentAsync(student);

        // A completed module with a published mark cannot be taken again (no retakes in v1).
        var completed = (await client.GetJsonAsync("/api/me/dashboard")).GetProperty("completed").EnumerateArray().First().GetProperty("code").GetString()!;
        using (var retake = await client.EnrolAsync(completed))
        {
            await retake.AssertProblemAsync(HttpStatusCode.Conflict, "results-exist");
        }

        // A submitted mark blocks self-withdrawal.
        await factory.CreateModuleAsync("ZZ1103", Semester.Spring, capacity: 10);
        using (var enrol = await client.EnrolAsync("ZZ1103"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        await factory.PutGradeAsync(student, "ZZ1103", GradeStatus.Submitted, 58);
        using (var withdraw = await client.WithdrawAsync("ZZ1103"))
        {
            await withdraw.AssertProblemAsync(HttpStatusCode.Conflict, "results-exist");
        }

        Assert.Equal((1, 1), await factory.CountsAsync("ZZ1103"));
    }

    [Fact]
    public async Task Reactivating_an_earlier_year_row_stamps_the_current_year()
    {
        const string student = "S000015";
        await factory.CreateModuleAsync("ZZ1104", Semester.Spring, capacity: 10);
        await factory.InsertEnrolmentAsync(student, "ZZ1104", StudentData.PreviousYear);
        var old = (await factory.EnrolmentAsync(student, "ZZ1104"))!;

        // The earlier year's row holds no place this year.
        Assert.Equal((0, 0), await factory.CountsAsync("ZZ1104"));

        using var client = await factory.LoginStudentAsync(student);
        using (var enrol = await client.EnrolAsync("ZZ1104"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
            Assert.Equal(9, (await enrol.ReadJsonAsync()).GetProperty("placesRemaining").GetInt32());
        }

        var row = (await factory.EnrolmentAsync(student, "ZZ1104"))!;
        Assert.Equal(old.Id, row.Id);
        Assert.Equal((EnrolmentStatus.Active, EnrolmentSource.Self, StudentData.CurrentYear), (row.Status, row.Source, row.AcademicYear));
        Assert.Null(row.CreatedByUserId);
        Assert.Equal((1, 1), await factory.CountsAsync("ZZ1104"));
    }

    [Fact]
    public async Task Fifth_autumn_module_exceeds_the_credit_limit()
    {
        // S000002 is a year-2 student with no 2026/27 enrolment (01-domain-and-data.md section 6 step 12 enrols year 1 only).
        const string student = "S000002";
        var modules = await factory.FreeModulesAsync(student, Semester.Autumn, 5);
        Assert.Equal(5, modules.Count);
        using var client = await factory.LoginStudentAsync(student);

        foreach (var code in modules.Take(4))
        {
            using var enrol = await client.EnrolAsync(code);
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        using var fifth = await client.EnrolAsync(modules[4]);
        var problem = await fifth.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "credit-limit-exceeded");
        Assert.Equal(60, problem.GetProperty("currentCredits").GetInt32());
        Assert.Equal(15, problem.GetProperty("moduleCredits").GetInt32());
        Assert.Equal(60, problem.GetProperty("limit").GetInt32());
        Assert.Equal("autumn", problem.GetProperty("semester").GetString());

        var credits = (await client.GetJsonAsync("/api/me/dashboard")).GetProperty("credits");
        Assert.Equal(60, credits.GetProperty("autumn").GetInt32());
    }

    [Fact]
    public async Task Student_who_left_cannot_be_enrolled()
    {
        const string student = "S000050";
        using var client = await factory.LoginStudentAsync(student);
        await factory.WithDbAsync(db => db.Students.Where(s => s.StudentNumber == student)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeftAt, RushDayApiFactory.ClockStart)));

        using var response = await client.EnrolAsync("CS3099");
        await response.AssertProblemAsync(HttpStatusCode.Conflict, "student-left");
        Assert.Null(await factory.EnrolmentAsync(student, "CS3099"));
    }

    [Fact]
    public async Task Metrics_record_accepted_rejected_and_duration()
    {
        var metrics = factory.Services.GetRequiredService<RushDayMetrics>();
        using var accepted = new MetricCollector<long>(metrics.EnrolmentsAccepted);
        using var rejected = new MetricCollector<long>(metrics.EnrolmentsRejected);
        using var duration = new MetricCollector<double>(metrics.EnrolmentsDuration);

        await factory.CreateModuleAsync("ZZ1105", Semester.Spring, capacity: 2);
        using var first = await factory.LoginStudentAsync("S000016");
        using var second = await factory.LoginStudentAsync("S000017");
        using var third = await factory.LoginStudentAsync("S000018");

        using (var ok = await first.EnrolAsync("ZZ1105"))
        {
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        // While places remain, a repeat is refused by the rules as already enrolled.
        using (var again = await first.EnrolAsync("ZZ1105"))
        {
            await again.AssertProblemAsync(HttpStatusCode.Conflict, "already-enrolled");
        }

        using (var last = await second.EnrolAsync("ZZ1105"))
        {
            Assert.Equal(HttpStatusCode.Created, last.StatusCode);
        }

        // Once full, the fast path answers module-full before any lock (04-performance-and-ops.md section 2.1).
        using (var full = await third.EnrolAsync("ZZ1105"))
        {
            await full.AssertProblemAsync(HttpStatusCode.Conflict, "module-full");
        }

        Assert.Equal(2, accepted.GetMeasurementSnapshot().Sum(m => m.Value));
        var reasons = rejected.GetMeasurementSnapshot().Select(m => (string)m.Tags["reason"]!).Order().ToList();
        Assert.Equal(["already_enrolled", "module_full"], reasons);
        Assert.Equal(4, duration.GetMeasurementSnapshot().Count);
        Assert.All(duration.GetMeasurementSnapshot(), m => Assert.True(m.Value > 0));
    }

    /// <summary>
    /// Review S4 D1: a module code must be two ASCII letters and four ASCII digits (white space around it allowed), so a
    /// NUL character, a non-ASCII digit or any other malformed body is 400 <c>validation</c> before a query runs; a NUL
    /// once reached PostgreSQL (22021) and answered 500.
    /// </summary>
    [Fact]
    public async Task Malformed_module_codes_are_validation_problems_never_500()
    {
        using var client = await factory.LoginStudentAsync("S000032");
        string[] bodies =
        [
            "{\"moduleCode\":\"CS3099\\u0000\"}",
            "{\"moduleCode\":\"CS30\\u000099\"}",
            "{\"moduleCode\":\"\\u0000\"}",
            "{\"moduleCode\":\"CS\\uFF13\\uFF10\\uFF19\\uFF19\"}",
            "{\"moduleCode\":\"CS\\u0663\\u0660\\u0669\\u0669\"}",
            "{\"moduleCode\":\"CS 3099\"}",
            "{\"moduleCode\":\"CS30999\"}",
            "{\"moduleCode\":\"cs' OR 1=1 --\"}",
            "{\"moduleCode\":\"%\"}",
            "{\"moduleCode\":\"   \"}",
            "{\"moduleCode\":123}",
            "{\"moduleCode\":null}",
            "{}",
            "[]",
        ];

        foreach (var json in bodies)
        {
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/api/me/enrolments", content);
            var problem = await response.ReadJsonAsync();
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{json} answered {(int)response.StatusCode}: {problem}");
            Assert.Equal("urn:rushday:validation", problem.GetProperty("type").GetString());
        }

        Assert.Null(await factory.EnrolmentAsync("S000032", "CS3099"));
    }

    /// <summary>
    /// Review S4 D3: route constraints use <c>[0-9]</c>, so a path whose "digits" are Arabic-Indic or full-width is no
    /// module code at all and falls through to the <c>/api</c> 404 <c>not-found</c> (with <c>\d</c> it reached the
    /// handlers and answered <c>module-not-found</c> or <c>not-enrolled</c>).
    /// </summary>
    [Fact]
    public async Task Non_ascii_digits_in_a_module_path_are_not_a_module_code()
    {
        using var client = await factory.LoginStudentAsync("S000033");
        foreach (var path in new[] { "/api/modules/CS%D9%A3%D9%A0%D9%A9%D9%A9", "/api/modules/CS%EF%BC%93%EF%BC%90%EF%BC%99%EF%BC%99" })
        {
            using var response = await client.GetAsync(path);
            await response.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        }

        using (var withdraw = await client.DeleteAsync("/api/me/enrolments/CS%D9%A3%D9%A0%D9%A9%D9%A9"))
        {
            await withdraw.AssertProblemAsync(HttpStatusCode.NotFound, "not-found");
        }

        // ASCII codes still match in either case.
        using var lower = await client.GetAsync("/api/modules/cs3099");
        Assert.Equal(HttpStatusCode.OK, lower.StatusCode);
    }

    /// <summary>
    /// Review S4 D2: a grade's year is always its enrolment's year (01-domain-and-data.md section 3). Reactivating a row
    /// from an earlier academic year deletes that module's Draft grade for the student in the same transaction (a Draft
    /// is all <c>results-exist</c> lets through) and says so in the audit row; a same-year re-enrolment keeps its draft.
    /// </summary>
    [Fact]
    public async Task Re_enrolling_from_an_earlier_year_discards_its_draft_but_a_same_year_re_enrolment_keeps_it()
    {
        await factory.CreateModuleAsync("ZZ1110", Semester.Spring, capacity: 10, credits: 5);
        await factory.CreateModuleAsync("ZZ1111", Semester.Spring, capacity: 10, credits: 5);

        // Earlier year: a withdrawn 2025/26 row with a Draft mark of 12.
        const string returning = "S000030";
        await factory.InsertEnrolmentAsync(returning, "ZZ1110", StudentData.PreviousYear, EnrolmentStatus.Withdrawn);
        await factory.PutGradeAsync(returning, "ZZ1110", GradeStatus.Draft, 12);
        using (var client = await factory.LoginStudentAsync(returning))
        using (var enrol = await client.EnrolAsync("ZZ1110"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        var row = (await factory.EnrolmentAsync(returning, "ZZ1110"))!;
        Assert.Equal((EnrolmentStatus.Active, StudentData.CurrentYear), (row.Status, row.AcademicYear));
        Assert.Null(await GradeAsync(returning, "ZZ1110"));
        var created = Assert.Single(await AuditAsync(row.Id, AuditActions.EnrolmentCreated));
        Assert.True(JsonDocument.Parse(created).RootElement.GetProperty("discardedDraft").GetBoolean());

        // Same year: enrol, a lecturer drafts 40, withdraw and enrol again; the draft is this year's and stays.
        const string wavering = "S000031";
        using var second = await factory.LoginStudentAsync(wavering);
        using (var enrol = await second.EnrolAsync("ZZ1111"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        await factory.PutGradeAsync(wavering, "ZZ1111", GradeStatus.Draft, 40);
        using (var withdraw = await second.WithdrawAsync("ZZ1111"))
        {
            Assert.Equal(HttpStatusCode.NoContent, withdraw.StatusCode);
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var again = await second.EnrolAsync("ZZ1111"))
        {
            Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        }

        Assert.Equal("Draft/40", await GradeAsync(wavering, "ZZ1111"));
        var sameYear = (await factory.EnrolmentAsync(wavering, "ZZ1111"))!;
        Assert.All(await AuditAsync(sameYear.Id, AuditActions.EnrolmentCreated), d => Assert.False(JsonDocument.Parse(d).RootElement.TryGetProperty("discardedDraft", out _)));
    }

    /// <summary>The grade of (student, module) as <c>Status/Mark</c>, or null when there is none.</summary>
    private Task<string?> GradeAsync(string studentNumber, string code) =>
        factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == studentNumber).Select(s => s.Id).SingleAsync();
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            var grade = await db.Grades.AsNoTracking().Where(g => g.StudentId == studentId && g.ModuleId == moduleId).Select(g => new { g.Status, g.Mark }).SingleOrDefaultAsync();
            return grade is null ? null : $"{grade.Status}/{grade.Mark}";
        });

    /// <summary>The <c>details</c> of the audit rows of an enrolment with the given action, as stored (jsonb text).</summary>
    private Task<List<string>> AuditAsync(Guid enrolmentId, string action) =>
        factory.WithDbAsync(db => db.AuditEvents.AsNoTracking()
            .Where(a => a.SubjectId == enrolmentId.ToString() && a.Action == action && a.SubjectType == AuditSubjects.Enrolment)
            .OrderBy(a => a.OccurredAt)
            .Select(a => a.Details!)
            .ToListAsync());
}
