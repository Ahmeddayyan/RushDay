using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;
using Xunit.Abstractions;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// The lock order of 04-performance-and-ops.md section 2, one deterministic interleaving per test (review S4, findings
/// C1 and C3 to C9 and C11): a transaction of the test's own holds a row, the API's request queues behind it (observed in
/// <c>pg_stat_activity</c>), the test changes the world, and releases. Modules are created here (<c>ZY4xxx</c>, spring,
/// 5 credits) and the students are <c>S000161</c>–<c>S000199</c>, which no other test enrols on them.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EnrolmentLockingTests(RushDayApiFactory factory, ITestOutputHelper output)
{
    private const string NextYear = "2027/28";

    /// <summary>
    /// C1: two forced enrolments each insert their row (the foreign-key check takes <c>FOR KEY SHARE</c> on the module
    /// row) and then claim. With <c>FOR UPDATE</c> in the forced claim each waited for the other's key-share lock and one
    /// died with 40P01; with <c>FOR NO KEY UPDATE</c> they serialise.
    /// </summary>
    [Fact]
    public async Task Two_forced_enrolments_on_one_module_serialise_instead_of_deadlocking()
    {
        var moduleId = await factory.CreateModuleAsync("ZY4001", Semester.Spring, capacity: 1, credits: 5);
        var first = await factory.StudentIdAsync("S000161");
        var second = await factory.StudentIdAsync("S000162");

        // Hold the module row as a claim does, so both enrolments insert and then queue at their claim together.
        HeldLock blocker = await DbProbe.HoldAsync(factory, "SELECT 1 FROM modules WHERE id = @m FOR NO KEY UPDATE", ("m", moduleId));
        Task<string> a, b;
        await using (blocker)
        {
            a = ForceAsync(first);
            b = ForceAsync(second);
            await DbProbe.WaitForLockWaitAsync(factory, "%WITH before AS%", count: 2);
            await blocker.ReleaseAsync();
        }

        var outcomes = await Task.WhenAll(a, b);
        output.WriteLine("forced enrolments: " + string.Join(" | ", outcomes));
        Assert.All(outcomes, o => Assert.StartsWith("ok", o, StringComparison.Ordinal));
        Assert.Single(outcomes, o => o == "ok raised");

        // The first took the one place, the second raised capacity to 2 (02-api.md section 8.5).
        Assert.Equal((2, 2, 2), await DbProbe.CountsAsync(factory, moduleId));

        async Task<string> ForceAsync(Guid studentId)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<EnrolmentService>();
            try
            {
                var result = await service.EnrolAsync(studentId, "ZY4001", null, new EnrolOptions(Override: true, ForceCapacity: true, Reason: "C1 forced claims"));
                return result.Succeeded ? (result.Receipt!.CapacityRaised ? "ok raised" : "ok") : "refused " + result.Failure!.Error;
            }
            catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException)
            {
                return "threw " + DbProbe.Describe(exception);
            }
        }
    }

    /// <summary>
    /// C3: an enrolment that read the (cached) year and then waited on its student lock while the year changed. The year
    /// is read again under a share lock after the student lock, so the row and the count belong to the same, new year.
    /// </summary>
    [Fact]
    public async Task A_year_change_while_an_enrolment_waits_stamps_and_counts_the_new_year()
    {
        const string student = "S000163";
        var moduleId = await factory.CreateModuleAsync("ZY4031", Semester.Spring, capacity: 10, credits: 5);
        var studentId = await factory.StudentIdAsync(student);
        var windowId = await AddOpenSpringWindowAsync(NextYear);
        using var client = await factory.LoginStudentAsync(student);
        try
        {
            // Warm the settings (2026/27) and windows caches: the request below reads them before its transaction.
            await client.GetJsonAsync("/api/me/enrolments");

            Task<(HttpStatusCode Status, string Body)> enrol;
            await using (var blocker = await DbProbe.HoldAsync(factory, "SELECT 1 FROM students WHERE id = @s FOR UPDATE", ("s", studentId)))
            {
                enrol = Task.Run(async () =>
                {
                    using var response = await client.EnrolAsync("ZY4031");
                    return (response.StatusCode, await response.Content.ReadAsStringAsync());
                });
                await DbProbe.WaitForLockWaitAsync(factory, "%FROM students WHERE id%");
                await ChangeYearAsync(NextYear);
                await blocker.ReleaseAsync();
            }

            var (status, body) = await enrol;
            var row = await factory.EnrolmentAsync(student, "ZY4031");
            var counts = await DbProbe.CountsAsync(factory, moduleId, NextYear);
            output.WriteLine($"enrol answered {(int)status} {body}; row year {row?.AcademicYear}; {NextYear} count/active {counts.EnrolledCount}/{counts.Active}");

            Assert.Equal(HttpStatusCode.Created, status);
            Assert.Equal(NextYear, row!.AcademicYear);
            Assert.Equal((1, 1), (counts.EnrolledCount, counts.Active));
        }
        finally
        {
            await factory.WithDbAsync(db => db.Enrolments.Where(e => e.StudentId == studentId && e.ModuleId == moduleId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EnrolmentStatus.Withdrawn).SetProperty(e => e.WithdrawnAt, RushDayApiFactory.ClockStart)));
            await factory.WithDbAsync(db => db.EnrolmentWindows.Where(w => w.Id == windowId).ExecuteDeleteAsync());
            await ChangeYearAsync(StudentData.CurrentYear);
        }
    }

    /// <summary>
    /// C3, the withdrawal side: a withdrawal that waited on its student lock across a year change must not release one of
    /// the new year's places for a row of the old year.
    /// </summary>
    [Fact]
    public async Task A_year_change_while_a_withdrawal_waits_releases_no_place_of_the_new_year()
    {
        const string leaver = "S000164";
        const string nextYearHolder = "S000165";
        var moduleId = await factory.CreateModuleAsync("ZY4032", Semester.Spring, capacity: 10, credits: 5);
        var leaverId = await factory.StudentIdAsync(leaver);
        using var client = await factory.LoginStudentAsync(leaver);
        using (var enrol = await client.EnrolAsync("ZY4032"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        // Somebody already holds a place in the next year (an administrator's early enrolment).
        await factory.InsertEnrolmentAsync(nextYearHolder, "ZY4032", NextYear);
        try
        {
            await client.GetJsonAsync("/api/me/enrolments");

            Task<(HttpStatusCode Status, string Body)> withdraw;
            await using (var blocker = await DbProbe.HoldAsync(factory, "SELECT 1 FROM students WHERE id = @s FOR UPDATE", ("s", leaverId)))
            {
                withdraw = Task.Run(async () =>
                {
                    using var response = await client.WithdrawAsync("ZY4032");
                    return (response.StatusCode, await response.Content.ReadAsStringAsync());
                });
                await DbProbe.WaitForLockWaitAsync(factory, "%FROM students WHERE id%");
                await ChangeYearAsync(NextYear);
                await blocker.ReleaseAsync();
            }

            var (status, body) = await withdraw;
            var counts = await DbProbe.CountsAsync(factory, moduleId, NextYear);
            output.WriteLine($"withdraw answered {(int)status} {body}; {NextYear} count/active {counts.EnrolledCount}/{counts.Active}");

            // The leaver's row belongs to 2026/27, no longer the current year: no deadline is open for it, and the new
            // year's count still matches its one active row.
            Assert.Equal(HttpStatusCode.Conflict, status);
            Assert.Contains("urn:rushday:withdrawal-deadline-passed", body, StringComparison.Ordinal);
            Assert.Equal((1, 1), (counts.EnrolledCount, counts.Active));
        }
        finally
        {
            var holderId = await factory.StudentIdAsync(nextYearHolder);
            await factory.WithDbAsync(db => db.Enrolments.Where(e => e.StudentId == holderId && e.ModuleId == moduleId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EnrolmentStatus.Withdrawn).SetProperty(e => e.WithdrawnAt, RushDayApiFactory.ClockStart)));
            await ChangeYearAsync(StudentData.CurrentYear);
        }
    }

    /// <summary>
    /// C4: the reconciliation running while an enrolment's commit is in progress on an already drifted module (5 counted,
    /// 3 rows). The counting subquery's snapshot predated the commit, so the old reconcile wrote 3 over the committed 6 and
    /// left 3 counted for 4 rows; locking the module rows first makes the counts fresh statements.
    /// </summary>
    [Fact]
    public async Task Reconcile_racing_an_enrolment_commit_counts_the_committed_row()
    {
        var moduleId = await factory.CreateModuleAsync("ZY4041", Semester.Spring, capacity: 30, credits: 5);
        foreach (var number in new[] { "S000166", "S000167", "S000168" })
        {
            using var client = await factory.LoginStudentAsync(number);
            using var enrol = await client.EnrolAsync("ZY4041");
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        await DbProbe.ExecAsync(factory, "UPDATE modules SET enrolled_count = 5 WHERE id = @m", ("m", moduleId));
        var slowStudent = await factory.StudentIdAsync("S000169");
        await DbProbe.ExecAsync(
            factory,
            $"""
            CREATE FUNCTION zy_slow_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.student_id = '{slowStudent}' THEN PERFORM pg_sleep(1.5); END IF; RETURN NEW; END $$;
            CREATE CONSTRAINT TRIGGER zy_slow_commit AFTER INSERT ON enrolments DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION zy_slow_commit();
            """);
        try
        {
            using var slow = await factory.LoginStudentAsync("S000169");
            var enrol = Task.Run(async () =>
            {
                using var response = await slow.EnrolAsync("ZY4041");
                return response.StatusCode;
            });

            // The claim has run (count 6) and the commit sleeps in the deferred trigger with the module row locked.
            await DbProbe.WaitForWaitEventAsync(factory, "PgSleep");
            var reconcile = Task.Run(async () =>
            {
                await using var scope = factory.Services.CreateAsyncScope();
                return await StartupBackfills.ReconcileEnrolledCountAsync(scope.ServiceProvider.GetRequiredService<RushDayDbContext>());
            });
            await DbProbe.WaitForLockWaitAsync(factory, "%modules%");

            Assert.Equal(HttpStatusCode.Created, await enrol);
            var corrected = await reconcile;
            var counts = await DbProbe.CountsAsync(factory, moduleId);
            output.WriteLine($"reconcile corrected {corrected} rows; count/active {counts.EnrolledCount}/{counts.Active}");
            Assert.Equal((4, 4), (counts.EnrolledCount, counts.Active));
        }
        finally
        {
            await DbProbe.ExecAsync(factory, "DROP TRIGGER IF EXISTS zy_slow_commit ON enrolments; DROP FUNCTION IF EXISTS zy_slow_commit();");
            await ReconcileAsync();
        }
    }

    /// <summary>C5: a module deactivated after the fast path read it active; the claim itself re-checks <c>is_active</c>.</summary>
    [Fact]
    public async Task A_module_deactivated_after_the_fast_path_claims_no_place()
    {
        const string student = "S000170";
        var moduleId = await factory.CreateModuleAsync("ZY4051", Semester.Spring, capacity: 10, credits: 5);
        var studentId = await factory.StudentIdAsync(student);
        using var client = await factory.LoginStudentAsync(student);

        Task<(HttpStatusCode Status, string Body)> enrol;
        await using (var blocker = await DbProbe.HoldAsync(factory, "SELECT 1 FROM students WHERE id = @s FOR UPDATE", ("s", studentId)))
        {
            enrol = Task.Run(async () =>
            {
                using var response = await client.EnrolAsync("ZY4051");
                return (response.StatusCode, await response.Content.ReadAsStringAsync());
            });
            await DbProbe.WaitForLockWaitAsync(factory, "%FROM students WHERE id%");
            await DbProbe.ExecAsync(factory, "UPDATE modules SET is_active = false WHERE id = @m", ("m", moduleId));
            await blocker.ReleaseAsync();
        }

        var (status, body) = await enrol;
        output.WriteLine($"enrol answered {(int)status} {body}");
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("urn:rushday:module-inactive", JsonDocument.Parse(body).RootElement.GetProperty("type").GetString());
        Assert.Equal((0, 0, 10), await DbProbe.CountsAsync(factory, moduleId));
        Assert.Null(await factory.EnrolmentAsync(student, "ZY4051"));
    }

    /// <summary>
    /// C6: while an enrolment holds its student lock (here, queued at its claim), a foreign-key check on the same student
    /// (a lecturer saving a grade) is not blocked: the lock is <c>FOR NO KEY UPDATE</c>, which admits <c>FOR KEY SHARE</c>.
    /// </summary>
    [Fact]
    public async Task The_student_lock_does_not_block_foreign_key_checks_on_the_student()
    {
        const string student = "S000171";
        var moduleId = await factory.CreateModuleAsync("ZY4061", Semester.Spring, capacity: 10, credits: 5);
        var otherModuleId = await factory.CreateModuleAsync("ZY4062", Semester.Spring, capacity: 10, credits: 5);
        var studentId = await factory.StudentIdAsync(student);
        using var client = await factory.LoginStudentAsync(student);

        Task<HttpStatusCode> enrol;
        string gradeInsert;
        await using (var blocker = await DbProbe.HoldAsync(factory, "SELECT 1 FROM modules WHERE id = @m FOR NO KEY UPDATE", ("m", moduleId)))
        {
            enrol = Task.Run(async () =>
            {
                using var response = await client.EnrolAsync("ZY4061");
                return response.StatusCode;
            });
            await DbProbe.WaitForLockWaitAsync(factory, "%enrolled_count = enrolled_count + 1%");

            // The enrolment now holds the student row and waits for the module row.
            await using var connection = new NpgsqlConnection(factory.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await using var insert = new NpgsqlCommand(
                    "SET LOCAL lock_timeout = '2s'; INSERT INTO grades (id, student_id, module_id, mark, outcome, status, updated_at, version) VALUES (gen_random_uuid(), @s, @m, 50, 'Mark', 'Draft', now(), 1)",
                    connection,
                    transaction);
                insert.Parameters.AddWithValue("s", studentId);
                insert.Parameters.AddWithValue("m", otherModuleId);
                await insert.ExecuteNonQueryAsync();
                gradeInsert = "inserted";
            }
            catch (PostgresException exception)
            {
                gradeInsert = exception.SqlState + " " + exception.MessageText;
            }

            await transaction.RollbackAsync();
            await blocker.ReleaseAsync();
        }

        output.WriteLine("grade insert while the enrolment held the student: " + gradeInsert);
        Assert.Equal("inserted", gradeInsert);
        Assert.Equal(HttpStatusCode.Created, await enrol);
    }

    /// <summary>
    /// C7: a unique violation of another row in the enrolment's batch (here the audit row, through an index the test adds)
    /// is a failure, not "already enrolled"; only the (student, module) index means that.
    /// </summary>
    [Fact]
    public async Task A_unique_violation_of_another_row_is_not_already_enrolled()
    {
        var moduleId = await factory.CreateModuleAsync("ZY4071", Semester.Spring, capacity: 10, credits: 5);
        using (var first = await factory.LoginStudentAsync("S000172"))
        using (var enrol = await first.EnrolAsync("ZY4071"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        // At most one audit row about this module: the next enrolment's audit insert violates it.
        await DbProbe.ExecAsync(factory, $"CREATE UNIQUE INDEX zy_one_audit_row_for_module ON audit_events ((1)) WHERE module_id = '{moduleId}'");
        try
        {
            var second = await factory.StudentIdAsync("S000173");
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<EnrolmentService>();
            EnrolmentResult? result = null;
            var exception = await Record.ExceptionAsync(async () => result = await service.EnrolAsync(second, "ZY4071", null, EnrolOptions.Self));
            output.WriteLine($"result {result?.Failure?.Error.ToString() ?? (result is null ? "none" : "ok")}; exception {DbProbe.Describe(exception)}");

            Assert.Null(result);
            var update = Assert.IsType<DbUpdateException>(exception);
            Assert.Equal("zy_one_audit_row_for_module", Assert.IsType<PostgresException>(update.InnerException).ConstraintName);
            Assert.Null(await factory.EnrolmentAsync("S000173", "ZY4071"));
            Assert.Equal((1, 1, 10), await DbProbe.CountsAsync(factory, moduleId));
        }
        finally
        {
            await DbProbe.ExecAsync(factory, "DROP INDEX IF EXISTS zy_one_audit_row_for_module");
        }
    }

    /// <summary>
    /// C8: the hot module row is locked only for the claim and the commit, so no statement after the claim writes an
    /// audit row. A plain override's row goes in before the claim with the enrolment insert; a forced override's rows
    /// (<c>enrolment.admin_created</c> with <c>capacityRaised</c>, <c>module.updated</c>) are inserted by the claiming
    /// statement itself. Traced by triggers that record each statement's <c>statement_timestamp()</c>.
    /// </summary>
    [Fact]
    public async Task An_override_writes_no_audit_row_after_its_claim()
    {
        var plain = await factory.CreateModuleAsync("ZY4081", Semester.Spring, capacity: 10, credits: 5);
        var full = await factory.CreateModuleAsync("ZY4082", Semester.Spring, capacity: 1, credits: 5);
        using (var holder = await factory.LoginStudentAsync("S000177"))
        using (var enrol = await holder.EnrolAsync("ZY4082"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        await DbProbe.ExecAsync(
            factory,
            $"""
            CREATE TABLE zy_statement_trace (tbl text NOT NULL, action text, at timestamptz NOT NULL, tx xid8 NOT NULL);
            CREATE FUNCTION zy_statement_trace() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              INSERT INTO zy_statement_trace VALUES (TG_TABLE_NAME, to_jsonb(NEW) ->> 'action', statement_timestamp(), pg_current_xact_id());
              RETURN NEW;
            END $$;
            CREATE TRIGGER zy_trace_modules AFTER UPDATE ON modules FOR EACH ROW
              WHEN (NEW.id IN ('{plain}', '{full}')) EXECUTE FUNCTION zy_statement_trace();
            CREATE TRIGGER zy_trace_audit AFTER INSERT ON audit_events FOR EACH ROW
              WHEN (NEW.module_id IN ('{plain}', '{full}')) EXECUTE FUNCTION zy_statement_trace();
            """);
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<EnrolmentService>();
                var overridden = await service.EnrolAsync(await factory.StudentIdAsync("S000178"), "ZY4081", null, new EnrolOptions(Override: true, Reason: "C8 plain override"));
                Assert.True(overridden.Succeeded);
                var forced = await service.EnrolAsync(await factory.StudentIdAsync("S000179"), "ZY4082", null, new EnrolOptions(Override: true, ForceCapacity: true, Reason: "C8 forced override"));
                Assert.True(forced.Succeeded);
                Assert.True(forced.Receipt!.CapacityRaised);
            }

            var trace = await ReadTraceAsync();
            foreach (var row in trace)
            {
                output.WriteLine($"{row.Tx} {row.Table} {row.Action} {row.At:HH:mm:ss.ffffff}");
            }

            var transactions = trace.GroupBy(r => r.Tx).ToList();
            Assert.Equal(2, transactions.Count);
            foreach (var transaction in transactions)
            {
                var claim = Assert.Single(transaction, r => r.Table == "modules");
                var audits = transaction.Where(r => r.Table == "audit_events").ToList();
                Assert.NotEmpty(audits);
                Assert.All(audits, a => Assert.True(a.At <= claim.At, $"{a.Action} was written by a statement after the claim"));
            }

            // The forced claim's audit rows are the claim statement's own, and they carry what only the claim knew.
            var forcedTransaction = transactions.Single(t => t.Any(r => r.Action == AuditActions.ModuleUpdated));
            Assert.All(forcedTransaction, r => Assert.Equal(forcedTransaction.Single(x => x.Table == "modules").At, r.At));
            var rows = await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().Where(a => a.ModuleId == full && a.Action != AuditActions.EnrolmentCreated).ToListAsync());
            var created = JsonDocument.Parse(rows.Single(r => r.Action == AuditActions.EnrolmentAdminCreated).Details!).RootElement;
            Assert.True(created.GetProperty("capacityRaised").GetBoolean());
            Assert.True(created.GetProperty("forceCapacity").GetBoolean());
            Assert.Equal("C8 forced override", created.GetProperty("reason").GetString());
            var updated = rows.Single(r => r.Action == AuditActions.ModuleUpdated);
            Assert.Equal(("ZY4082", AuditSubjects.Module), (updated.SubjectId, updated.SubjectType));
            var capacity = JsonDocument.Parse(updated.Details!).RootElement.GetProperty("capacity");
            Assert.Equal((1, 2), (capacity.GetProperty("before").GetInt32(), capacity.GetProperty("after").GetInt32()));
        }
        finally
        {
            await DbProbe.ExecAsync(
                factory,
                "DROP TRIGGER IF EXISTS zy_trace_modules ON modules; DROP TRIGGER IF EXISTS zy_trace_audit ON audit_events; DROP FUNCTION IF EXISTS zy_statement_trace(); DROP TABLE IF EXISTS zy_statement_trace;");
        }
    }

    /// <summary>
    /// C9: a withdrawal of a current-year row whose module count had already drifted to 0 releases nothing, says so in
    /// its receipt and logs a Warning (the ops page's drift figure and the reconcile repair it).
    /// </summary>
    [Fact]
    public async Task A_withdrawal_from_a_drifted_count_is_logged()
    {
        const string student = "S000176";
        var moduleId = await factory.CreateModuleAsync("ZY4091", Semester.Spring, capacity: 10, credits: 5);
        using (var client = await factory.LoginStudentAsync(student))
        using (var enrol = await client.EnrolAsync("ZY4091"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        await DbProbe.ExecAsync(factory, "UPDATE modules SET enrolled_count = 0 WHERE id = @m", ("m", moduleId));
        await using var host = factory.Derive(b => b.ConfigureTestServices(s => s.AddFakeLogging()));
        var logs = host.Services.GetFakeLogCollector();
        WithdrawalResult result;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<EnrolmentService>().WithdrawAsync(await factory.StudentIdAsync(student), "ZY4091", null, WithdrawOptions.Self);
        }

        Assert.True(result.Succeeded);
        Assert.False(result.Receipt!.CountDecremented);
        var warning = Assert.Single(logs.GetSnapshot(), r => r.Level == LogLevel.Warning && r.Category == typeof(EnrolmentService).FullName);
        output.WriteLine(warning.Message);
        Assert.Equal("EnrolledCountDrift", warning.Id.Name);
        Assert.Contains("ZY4091", warning.Message, StringComparison.Ordinal);
        Assert.Equal((0, 0, 10), await DbProbe.CountsAsync(factory, moduleId));
    }

    /// <summary>
    /// C11: an invalidation while a catalogue fill is in flight. The fill read the modules before the change and stores
    /// its value after the invalidation; with a versioned key it stores it under the retired generation, so the next
    /// read sees the change instead of the pre-change catalogue for another 30 s.
    /// </summary>
    [Fact]
    public async Task An_invalidation_during_a_catalogue_fill_is_not_undone_by_that_fill()
    {
        var moduleId = await factory.CreateModuleAsync("ZY4111", Semester.Spring, capacity: 10, credits: 5);
        await using var scope = factory.Services.CreateAsyncScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<CatalogueCache>();

        Task<IReadOnlyList<CatalogueModule>> fill;
        await using (var blocker = await DbProbe.HoldAsync(factory, "LOCK TABLE module_lecturers IN ACCESS EXCLUSIVE MODE"))
        {
            // The fill's first query (modules) completes; its second (module_lecturers) queues behind the table lock.
            fill = catalogue.GetAsync().AsTask();
            await DbProbe.WaitForLockWaitAsync(factory, "%module_lecturers%");

            // What an administrator's module edit does: commit, then invalidate.
            await DbProbe.ExecAsync(factory, "UPDATE modules SET title = 'Renamed during a fill' WHERE id = @m", ("m", moduleId));
            await catalogue.InvalidateAsync();
            await blocker.ReleaseAsync();
        }

        var during = await fill;
        Assert.Equal("Test module ZY4111", during.Single(m => m.Code == "ZY4111").Title);

        var after = await catalogue.GetAsync();
        Assert.Equal("Renamed during a fill", after.Single(m => m.Code == "ZY4111").Title);
    }

    /// <summary>A 2027/28 spring window open at the shared clock's instant, for the year-change tests.</summary>
    private async Task<Guid> AddOpenSpringWindowAsync(string academicYear)
    {
        var id = Guid.CreateVersion7();
        await factory.WithDbAsync(async db =>
        {
            if (!await db.EnrolmentWindows.AnyAsync(w => w.AcademicYear == academicYear && w.Semester == Semester.Spring))
            {
                db.EnrolmentWindows.Add(new EnrolmentWindow
                {
                    Id = id,
                    AcademicYear = academicYear,
                    Semester = Semester.Spring,
                    OpensAt = RushDayApiFactory.ClockStart.AddDays(-1),
                    ClosesAt = RushDayApiFactory.ClockStart.AddDays(30),
                    WithdrawalDeadlineAt = RushDayApiFactory.ClockStart.AddDays(60),
                    UpdatedAt = RushDayApiFactory.ClockStart,
                });
                await db.SaveChangesAsync();
            }
        });
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EnrolmentWindowCache>().InvalidateAsync();
        return id;
    }

    /// <summary>
    /// What <c>PUT /api/admin/settings</c> does when the academic year changes (02-api.md section 8.5): the new year and
    /// the reconciliation in one transaction, then the settings, windows and catalogue caches invalidated.
    /// </summary>
    private async Task ChangeYearAsync(string academicYear)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE academic_settings SET academic_year = {0}, updated_at = now() WHERE id = 1", academicYear);
            await StartupBackfills.ReconcileEnrolledCountAsync(db);
            await transaction.CommitAsync();
        }

        await scope.ServiceProvider.GetRequiredService<SettingsCache>().InvalidateAsync();
        await scope.ServiceProvider.GetRequiredService<EnrolmentWindowCache>().InvalidateAsync();
        await scope.ServiceProvider.GetRequiredService<CatalogueCache>().InvalidateAsync();
    }

    private async Task ReconcileAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await StartupBackfills.ReconcileEnrolledCountAsync(scope.ServiceProvider.GetRequiredService<RushDayDbContext>());
        await scope.ServiceProvider.GetRequiredService<CatalogueCache>().InvalidateAsync();
    }

    private async Task<List<(string Tx, string Table, string? Action, DateTime At)>> ReadTraceAsync()
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT tx::text, tbl, action, at FROM zy_statement_trace ORDER BY at, tbl", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string, string?, DateTime)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetDateTime(3)));
        }

        return rows;
    }
}
