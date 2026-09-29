using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// The audit trail (03-security.md section 7, D30): every staff mutation writes its row in its own transaction (and a
/// failure part-way leaves none); the database refuses to change or delete a row; bulk reads are audited (the CSV
/// export before its first byte, the student view); the export is capped, signals truncation and neutralises
/// spreadsheet formulas. Modules <c>YU####</c>, students <c>S000100</c> and <c>S98####</c>, years <c>2061/62</c>..
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Every_mutation_writes_a_row()
    {
        using var admin = await factory.DemoAdminAsync();
        var expected = new List<(string Action, string? SubjectId, string Actor)>();
        const string adminName = DemoAccounts.AdminUsername;

        // Modules and lecturers.
        await admin.CreateModuleAsync("YU9101");
        expected.Add((AuditActions.ModuleCreated, "YU9101", adminName));
        await admin.PutJsonAsync("/api/admin/modules/YU9101", new { title = "Audited", description = (string?)null, credits = 15, capacity = 100, semester = "autumn", isActive = true });
        expected.Add((AuditActions.ModuleUpdated, "YU9101", adminName));
        await admin.PostJsonAsync("/api/admin/lecturers", new { staffNumber = "L98001", fullName = "Audit Lecturer", title = "Mx", department = "YU" }, HttpStatusCode.Created);
        expected.Add((AuditActions.LecturerCreated, "L98001", adminName));
        await admin.PutJsonAsync("/api/admin/lecturers/L98001", new { fullName = "Audit Lecturer II", title = "Dr", department = "YU", email = (string?)null });
        expected.Add((AuditActions.LecturerUpdated, "L98001", adminName));
        await admin.AssignAsync("YU9101", "L00034");
        expected.Add((AuditActions.ModuleLecturersSet, "YU9101", adminName));
        await admin.PostJsonAsync("/api/admin/lecturers/L98001/leave", new { reason = StaffData.Reason });
        expected.Add((AuditActions.LecturerLeft, "L98001", adminName));

        // Students and enrolments.
        await admin.PostJsonAsync("/api/admin/students", new { studentNumber = "S980001", fullName = "Audit Student", programme = "BSc", yearOfStudy = 1 }, HttpStatusCode.Created);
        expected.Add((AuditActions.StudentCreated, "S980001", adminName));
        await admin.PutJsonAsync("/api/admin/students/S980001", new { fullName = "Audit Student II", programme = "BSc", yearOfStudy = 1 });
        expected.Add((AuditActions.StudentUpdated, "S980001", adminName));
        await admin.OverrideEnrolAsync("S980001", "YU9101");
        var enrolmentId = (await factory.EnrolmentAsync("S980001", "YU9101"))!.Id.ToString();
        expected.Add((AuditActions.EnrolmentAdminCreated, enrolmentId, adminName));
        await admin.OverrideEnrolAsync("S000100", "YU9101");

        // Marks: entered, changed, submitted.
        using var lecturer = await factory.LecturerAsync("L00034");
        await lecturer.SaveMarksAsync("YU9101", [new { studentNumber = "S980001", mark = 50, version = (int?)null }, new { studentNumber = "S000100", mark = 60, version = (int?)null }]);
        var gradeId = (await factory.GradeAsync("S980001", "YU9101")).Id.ToString();
        expected.Add((AuditActions.GradeEntered, gradeId, "L00034"));
        await lecturer.SaveMarksAsync("YU9101", [new { studentNumber = "S980001", mark = 52, version = 1 }]);
        expected.Add((AuditActions.GradeChanged, gradeId, "L00034"));
        await lecturer.PostJsonAsync("/api/lecturer/modules/YU9101/marks/submit", null);
        expected.Add((AuditActions.ModuleMarksSubmitted, "YU9101", "L00034"));

        // Results: correct, return to draft (resubmit), publish scheduled, reschedule, cancel; publish live, unpublish.
        await admin.PostJsonAsync("/api/admin/results/modules/YU9101/marks/S980001/correct", new { mark = 53, reason = StaffData.Reason });
        expected.Add((AuditActions.GradeCorrected, gradeId, adminName));
        await admin.PostJsonAsync("/api/admin/results/modules/YU9101/return-to-draft", new { reason = StaffData.Reason });
        expected.Add((AuditActions.ModuleReturnedToDraft, "YU9101", adminName));
        await factory.SeedModuleAsync("YU9102", Semester.Spring, "2061/62", [("S000100", 70)]);
        var scheduled = await admin.PostJsonAsync("/api/admin/results/publish", new { academicYear = "2061/62", semester = "spring", publishAt = factory.Clock.GetUtcNow().AddHours(1), announce = false });
        var scheduledId = scheduled.GetProperty("publication").GetProperty("id").GetString();
        expected.Add((AuditActions.ResultsPublished, scheduledId, adminName));
        await admin.PutJsonAsync($"/api/admin/results/publications/{scheduledId}", new { publishAt = factory.Clock.GetUtcNow().AddHours(2) });
        expected.Add((AuditActions.ResultsRescheduled, scheduledId, adminName));
        await admin.SendJsonAsync(HttpMethod.Delete, $"/api/admin/results/publications/{scheduledId}", null, HttpStatusCode.OK);
        expected.Add((AuditActions.ResultsCancelled, scheduledId, adminName));
        var live = await admin.PostJsonAsync("/api/admin/results/publish", new { academicYear = "2061/62", semester = "spring", publishAt = factory.Clock.GetUtcNow(), announce = false });
        var liveId = live.GetProperty("publication").GetProperty("id").GetString();
        await admin.PostJsonAsync($"/api/admin/results/publications/{liveId}/unpublish", new { reason = StaffData.Reason });
        expected.Add((AuditActions.ResultsUnpublished, liveId, adminName));

        // Announcements (administrator and lecturer), windows, trim, withdrawal, leave, ops.
        var announcement = await admin.PostJsonAsync("/api/admin/announcements", new { title = "Audited notice", body = "Body." }, HttpStatusCode.Created);
        var announcementId = announcement.GetProperty("id").GetString();
        expected.Add((AuditActions.AnnouncementCreated, announcementId, adminName));
        await admin.PutJsonAsync($"/api/admin/announcements/{announcementId}", new { title = "Audited notice II", body = "Body." });
        expected.Add((AuditActions.AnnouncementUpdated, announcementId, adminName));
        await admin.SendJsonAsync(HttpMethod.Delete, $"/api/admin/announcements/{announcementId}", null, HttpStatusCode.NoContent);
        expected.Add((AuditActions.AnnouncementDeleted, announcementId, adminName));
        var moduleNotice = await lecturer.PostJsonAsync("/api/lecturer/modules/YU9101/announcements", new { title = "Module notice", body = "Body." }, HttpStatusCode.Created);
        expected.Add((AuditActions.AnnouncementCreated, moduleNotice.GetProperty("id").GetString(), "L00034"));

        var opens = new DateTimeOffset(2061, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var window = await admin.PostJsonAsync("/api/admin/enrolment-windows", new { academicYear = "2061/62", semester = "spring", opensAt = opens, closesAt = opens.AddDays(7), withdrawalDeadlineAt = opens.AddDays(14) }, HttpStatusCode.Created);
        var windowId = window.GetProperty("id").GetString();
        expected.Add((AuditActions.WindowCreated, windowId, adminName));
        await admin.PutJsonAsync($"/api/admin/enrolment-windows/{windowId}", new { opensAt = opens, closesAt = opens.AddDays(8), withdrawalDeadlineAt = opens.AddDays(14) });
        expected.Add((AuditActions.WindowUpdated, windowId, adminName));
        await admin.SendJsonAsync(HttpMethod.Delete, $"/api/admin/enrolment-windows/{windowId}", null, HttpStatusCode.NoContent);
        expected.Add((AuditActions.WindowDeleted, windowId, adminName));

        await admin.PostJsonAsync("/api/admin/modules/YU9101/trim-to-capacity", new { reason = StaffData.Reason });
        expected.Add((AuditActions.ModuleTrimmed, "YU9101", adminName));
        await admin.PostJsonAsync("/api/admin/students/S980001/enrolments/YU9101/withdraw", new { reason = StaffData.Reason }, HttpStatusCode.NoContent);
        expected.Add((AuditActions.EnrolmentAdminWithdrawn, enrolmentId, adminName));
        await admin.PostJsonAsync("/api/admin/students/S980001/leave", new { reason = StaffData.Reason });
        expected.Add((AuditActions.StudentLeft, "S980001", adminName));
        await admin.GetJsonAsync("/api/admin/students/S980001");
        expected.Add((AuditActions.StudentViewed, "S980001", adminName));

        var before = await factory.WithDbAsync(db => db.AuditEvents.CountAsync(a => a.Action == AuditActions.OpsReconciled));
        await admin.PostJsonAsync("/api/admin/ops/reconcile", null);
        Assert.Equal(before + 1, await factory.WithDbAsync(db => db.AuditEvents.CountAsync(a => a.Action == AuditActions.OpsReconciled)));

        foreach (var (action, subjectId, actor) in expected)
        {
            var rows = await factory.AuditAsync(action, subjectId);
            var row = Assert.Single(rows);
            Assert.Equal(AuditActions.SubjectOf(action), row.SubjectType);
            Assert.Equal(actor, row.ActorUsername);
            Assert.NotNull(row.ActorUserId);
            Assert.False(string.IsNullOrEmpty(row.RequestId), $"{action} has no request id");
            Assert.False(string.IsNullOrEmpty(row.IpHash), $"{action} has no ip hash");
            Assert.NotNull(row.Details);
        }
    }

    [Fact]
    public async Task A_failure_part_way_leaves_no_audit_row()
    {
        const string year = "2062/63";
        await factory.SeedModuleAsync("YU9201", Semester.Autumn, year, [("S000100", 64)]);

        // A host whose database refuses the announcement insert, the last write of a publish with announce: the
        // publication row, the grade update and both audit rows written before it must all roll back.
        await using var failing = factory.Derive(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<RushDayDbContext>(options => options.AddInterceptors(new FailOnInsertInto("announcements")))));
        using var admin = await failing.DemoAdminAsync();

        using var response = await admin.PostAsJsonAsync("/api/admin/results/publish", new { academicYear = year, semester = "autumn", publishAt = factory.Clock.GetUtcNow(), announce = true });
        await response.AssertProblemAsync(HttpStatusCode.InternalServerError, "internal-error");

        Assert.Empty(await factory.WithDbAsync(db => db.ResultsPublications.AsNoTracking().Where(p => p.AcademicYear == year).ToListAsync()));
        var grade = await factory.GradeAsync("S000100", "YU9201");
        Assert.Equal((GradeStatus.Submitted, (Guid?)null, (DateTimeOffset?)null), (grade.Status, grade.PublicationId, grade.PublishedAt));
        var rows = await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == AuditActions.ResultsPublished || a.Action == AuditActions.AnnouncementCreated)
            .ToListAsync());
        Assert.DoesNotContain(rows, a => a.Details!.Contains(year, StringComparison.Ordinal));
        Assert.False(await factory.WithDbAsync(db => db.Announcements.AnyAsync(a => a.Title == $"Autumn {year} results are available")));
    }

    [Fact]
    public async Task Update_and_delete_are_rejected_by_the_database()
    {
        await factory.WithDbAsync(async db =>
        {
            var id = await db.AuditEvents.Select(a => a.Id).FirstAsync();
            var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE audit_events SET action = 'tampered' WHERE id = {id}"));
            Assert.Contains("audit_events is append-only", update.MessageText, StringComparison.Ordinal);
            var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM audit_events WHERE id = {id}"));
            Assert.Contains("audit_events is append-only", delete.MessageText, StringComparison.Ordinal);
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Id == id && a.Action != "tampered"));
        });
    }

    [Fact]
    public async Task Student_view_writes_an_audit_row()
    {
        using var admin = await factory.DemoAdminAsync();
        var before = (await factory.AuditAsync(AuditActions.StudentViewed, "S000100")).Count;

        await admin.GetJsonAsync("/api/admin/students/S000100");

        var rows = await factory.AuditAsync(AuditActions.StudentViewed, "S000100");
        Assert.Equal(before + 1, rows.Count);
        Assert.Equal((DemoAccounts.AdminUsername, "Admin", await factory.StudentIdAsync("S000100")), (rows[0].ActorUsername, rows[0].ActorRole, rows[0].StudentId!.Value));

        // The log finds it by student, actor and action.
        var page = await admin.GetJsonAsync($"/api/admin/audit?studentNumber=S000100&actor=ADMIN&action={AuditActions.StudentViewed}&pageSize=500");
        Assert.Equal(200, page.GetProperty("pageSize").GetInt32());
        Assert.True(page.GetProperty("total").GetInt32() >= 1);
        Assert.All(page.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal((AuditActions.StudentViewed, "S000100", "Student"), (item.GetProperty("action").GetString(), item.GetProperty("studentNumber").GetString(), item.GetProperty("subjectType").GetString()));
            Assert.Equal("S000100", item.GetProperty("details").GetProperty("studentNumber").GetString());
        });

        var future = await admin.GetJsonAsync($"/api/admin/audit?action={AuditActions.StudentViewed}&from={Uri.EscapeDataString(factory.Clock.GetUtcNow().AddDays(1).ToString("O"))}");
        Assert.Equal(0, future.GetProperty("total").GetInt32());
        using var invalid = await admin.GetAsync("/api/admin/audit?studentNumber=S12");
        await invalid.AssertProblemAsync(HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Export_writes_an_audit_row_before_it_streams()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YU9301");
        await admin.PutJsonAsync("/api/admin/modules/YU9301", new { title = "Exported", description = (string?)null, credits = 15, capacity = 100, semester = "autumn", isActive = true });
        var from = factory.Clock.GetUtcNow().AddDays(-1);
        var to = factory.Clock.GetUtcNow().AddDays(1);
        var query = $"moduleCode=YU9301&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        var exportsBefore = (await factory.AuditAsync(AuditActions.AuditExported)).Count;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/audit/export.csv?" + query);
        using var response = await admin.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The headers have arrived and the body has not been read: the audit row is already committed.
        var exports = await factory.AuditAsync(AuditActions.AuditExported);
        Assert.Equal(exportsBefore + 1, exports.Count);
        var details = StaffData.DetailsOf(exports[0]);
        Assert.Equal(("YU9301", 2, false), (details.GetProperty("filters").GetProperty("moduleCode").GetString(), details.GetProperty("rowCount").GetInt32(), details.GetProperty("truncated").GetBoolean()));
        Assert.Equal((AuditSubjects.System, DemoAccounts.AdminUsername), (exports[0].SubjectType, exports[0].ActorUsername));

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.False(response.Headers.Contains("X-RushDay-Truncated"));
        Assert.Equal($"attachment; filename=\"audit-{from.UtcDateTime:yyyyMMdd}-{to.UtcDateTime:yyyyMMdd}.csv\"", response.Content.Headers.ContentDisposition?.ToString());
        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("\"occurredAt\",\"actorUsername\",\"actorRole\",\"action\",\"subjectType\",\"subjectId\",\"studentNumber\",\"moduleCode\",\"details\",\"requestId\"", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("\"" + factory.Clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), lines[1], StringComparison.Ordinal);
        Assert.Contains("\"module.updated\"", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"module.created\"", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_is_capped_and_signals_truncation()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YU9401");
        var moduleId = await factory.ModuleIdAsync("YU9401");
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO audit_events (id, occurred_at, action, subject_type, subject_id, module_id, details)
            SELECT gen_random_uuid(), {factory.Clock.GetUtcNow()} - make_interval(secs => g), 'ops.reconciled', 'System', NULL, {moduleId}, jsonb_build_object('synthetic', true)
            FROM generate_series(1, 10005) g
            """));

        using var response = await admin.GetAsync("/api/admin/audit/export.csv?moduleCode=YU9401");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("X-RushDay-Truncated")));
        Assert.Equal("attachment; filename=\"audit-all-all.csv\"", response.Content.Headers.ContentDisposition?.ToString());
        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + 10_000 + 1, lines.Length);
        Assert.Equal("# truncated at 10000 rows; narrow the date range", lines[^1]);

        var export = (await factory.AuditAsync(AuditActions.AuditExported)).First();
        var details = StaffData.DetailsOf(export);
        Assert.Equal((10_000, true), (details.GetProperty("rowCount").GetInt32(), details.GetProperty("truncated").GetBoolean()));

        // A range of at most 31 days raises the cap to 50,000: all 10,006 rows (the synthetic ones and module.created).
        var from = factory.Clock.GetUtcNow().AddDays(-20);
        var to = factory.Clock.GetUtcNow().AddDays(1);
        using var ranged = await admin.GetAsync($"/api/admin/audit/export.csv?moduleCode=YU9401&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
        Assert.False(ranged.Headers.Contains("X-RushDay-Truncated"));
        Assert.Equal(1 + 10_006, (await ranged.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Export_neutralises_spreadsheet_formulas()
    {
        using var demo = await factory.DemoAdminAsync();
        var provisioned = await demo.PostJsonAsync("/api/admin/accounts", new { username = "-injected", displayName = "=HYPERLINK(\"http://evil.test\")", role = "Admin" }, HttpStatusCode.Created);
        using var injected = await factory.LoginAsync("-injected", provisioned.GetProperty("temporaryPassword").GetString()!);
        var announcement = await injected.PostJsonAsync("/api/admin/announcements", new { title = "=1+2", body = "@SUM(A1)" }, HttpStatusCode.Created);
        using (var cleanup = await demo.DeleteAsync($"/api/admin/announcements/{announcement.GetProperty("id").GetGuid()}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, cleanup.StatusCode);
        }

        using var response = await demo.GetAsync($"/api/admin/audit/export.csv?actor=-injected&action={AuditActions.AnnouncementCreated}");
        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var row = Assert.Single(lines[1..]);
        Assert.Contains(",\"'-injected\",", row, StringComparison.Ordinal);
        Assert.DoesNotContain(",\"-injected\",", row, StringComparison.Ordinal);

        // Every cell is quoted; the JSON details start with a brace, so the title inside them is inert text.
        Assert.StartsWith("\"", row, StringComparison.Ordinal);
        Assert.Contains(",\"{\"\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_cells_are_quoted_and_formula_triggers_prefixed()
    {
        Assert.Equal("\"'=1+2\"", AuditCsvWriter.Cell("=1+2"));
        Assert.Equal("\"'+1\"", AuditCsvWriter.Cell("+1"));
        Assert.Equal("\"'-1\"", AuditCsvWriter.Cell("-1"));
        Assert.Equal("\"'@SUM(A1)\"", AuditCsvWriter.Cell("@SUM(A1)"));
        Assert.Equal("\"'\tcmd\"", AuditCsvWriter.Cell("\tcmd"));
        Assert.Equal("\"'\rcmd\"", AuditCsvWriter.Cell("\rcmd"));
        Assert.Equal("\"say \"\"hi\"\"\"", AuditCsvWriter.Cell("say \"hi\""));
        Assert.Equal("\"a,b\"", AuditCsvWriter.Cell("a,b"));
        Assert.Equal("\"\"", AuditCsvWriter.Cell(null));
        Assert.Equal("\"x=1\"", AuditCsvWriter.Cell("x=1"));
    }

    /// <summary>Makes the database refuse any insert into one table, to prove a transaction rolls back as a whole.</summary>
    private sealed class FailOnInsertInto(string table) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Check(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO " + table, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Simulated failure writing {table}.");
            }
        }
    }
}
