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

    /// <summary>The <c>details</c> of the audit rows of an enrolment with the given action, as stored (jsonb text).</summary>
    private Task<List<string>> AuditAsync(Guid enrolmentId, string action) =>
        factory.WithDbAsync(db => db.AuditEvents.AsNoTracking()
            .Where(a => a.SubjectId == enrolmentId.ToString() && a.Action == action && a.SubjectType == AuditSubjects.Enrolment)
            .OrderBy(a => a.OccurredAt)
            .Select(a => a.Details!)
            .ToListAsync());
}
