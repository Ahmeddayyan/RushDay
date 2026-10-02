using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Npgsql;
using RushDay.Api.Observability;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// <see cref="EnrolmentService.WithdrawManyAsync"/>, the administrator's bulk withdrawal inside one administrative
/// transaction (trim to capacity, a student leaving; 04-performance-and-ops.md section 2.2, review S4 finding C2). It
/// keeps the single lock order across rows (every student first, in id order; each module once, last), so a student's
/// own withdrawal racing it never deadlocks, and it takes no savepoints, so 70 rows do not overflow the subtransaction
/// cache. Modules <c>ZY5xxx</c>; students <c>S000201</c>–<c>S000299</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BulkWithdrawalTests(RushDayApiFactory factory, ITestOutputHelper output)
{
    private static readonly WithdrawOptions Trim = new(Override: true, Reason: "Trimmed to capacity by a test.", Trim: true);

    /// <summary>
    /// A student's own withdrawal holds its student row and waits for the module row (held here by the test); the bulk
    /// withdrawal then queues on that student. Released, the student's withdrawal completes and the bulk finds that row
    /// already withdrawn. (Before the fix, a trim that had withdrawn another row held the module row while waiting for
    /// this student: 40P01.)
    /// </summary>
    [Fact]
    public async Task A_bulk_withdrawal_queued_behind_a_students_own_withdrawal_completes()
    {
        var moduleId = await factory.CreateModuleAsync("ZY5001", Semester.Spring, capacity: 10, credits: 5);
        var a = await EnrolAsync("S000201", "ZY5001");
        var b = await EnrolAsync("S000202", "ZY5001");
        using var client = await factory.LoginStudentAsync("S000202");

        Task<HttpStatusCode> own;
        Task<BulkWithdrawalResult> bulk;
        await using (var blocker = await DbProbe.HoldAsync(factory, "SELECT 1 FROM modules WHERE id = @m FOR NO KEY UPDATE", ("m", moduleId)))
        {
            own = Task.Run(async () =>
            {
                using var response = await client.WithdrawAsync("ZY5001");
                return response.StatusCode;
            });
            await DbProbe.WaitForLockWaitAsync(factory, "%UPDATE modules%");

            bulk = BulkInTransactionAsync([new WithdrawalTarget(a, moduleId), new WithdrawalTarget(b, moduleId)], Trim);
            await DbProbe.WaitForLockWaitAsync(factory, "%FROM students WHERE id = ANY%");
            await blocker.ReleaseAsync();
        }

        Assert.Equal(HttpStatusCode.NoContent, await own);
        var result = await bulk;
        Assert.Equal([await EnrolmentIdOf(a, moduleId)], result.Withdrawn.Select(r => r.EnrolmentId).ToList());
        Assert.Equal(1, result.NotEnrolled);
        Assert.Equal((0, 0, 10), await DbProbe.CountsAsync(factory, moduleId));
    }

    /// <summary>
    /// The other order: the bulk withdrawal holds every target student and waits for the module row; the student's own
    /// withdrawal queues on its student row, then finds its enrolment already withdrawn (404 not-enrolled).
    /// </summary>
    [Fact]
    public async Task A_students_own_withdrawal_queued_behind_a_bulk_withdrawal_finds_nothing_to_withdraw()
    {
        var moduleId = await factory.CreateModuleAsync("ZY5002", Semester.Spring, capacity: 10, credits: 5);
        var a = await EnrolAsync("S000203", "ZY5002");
        var b = await EnrolAsync("S000204", "ZY5002");
        using var client = await factory.LoginStudentAsync("S000204");

        Task<BulkWithdrawalResult> bulk;
        Task<(HttpStatusCode Status, string Body)> own;
        await using (var blocker = await DbProbe.HoldAsync(factory, "SELECT 1 FROM modules WHERE id = @m FOR NO KEY UPDATE", ("m", moduleId)))
        {
            bulk = BulkInTransactionAsync([new WithdrawalTarget(a, moduleId), new WithdrawalTarget(b, moduleId)], Trim);
            await DbProbe.WaitForLockWaitAsync(factory, "%FROM modules WHERE id = ANY%");

            own = Task.Run(async () =>
            {
                using var response = await client.WithdrawAsync("ZY5002");
                return (response.StatusCode, await response.Content.ReadAsStringAsync());
            });
            await DbProbe.WaitForLockWaitAsync(factory, "%FROM students WHERE id%");
            await blocker.ReleaseAsync();
        }

        var result = await bulk;
        Assert.Equal(2, result.Withdrawn.Count);
        Assert.All(result.Withdrawn, r => Assert.True(r.CountDecremented));
        var (status, body) = await own;
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("urn:rushday:not-enrolled", body, StringComparison.Ordinal);
        Assert.Equal((0, 0, 10), await DbProbe.CountsAsync(factory, moduleId));
    }

    /// <summary>
    /// Five rounds of a bulk withdrawal of twenty students racing each of those students' own withdrawal and a
    /// re-enrolment on a second module: no deadlock, no server error, and the counts equal the active rows after every
    /// round.
    /// </summary>
    [Fact]
    public async Task Bulk_withdrawals_racing_students_own_requests_never_deadlock()
    {
        var trimmed = await factory.CreateModuleAsync("ZY5003", Semester.Spring, capacity: 50, credits: 5);
        var other = await factory.CreateModuleAsync("ZY5008", Semester.Spring, capacity: 50, credits: 5);
        var numbers = Enumerable.Range(205, 20).Select(n => "S" + n.ToString("D6", CultureInfo.InvariantCulture)).ToList();
        var ids = new List<Guid>();
        foreach (var number in numbers)
        {
            ids.Add(await factory.StudentIdAsync(number));
        }

        var clients = new List<HttpClient>();
        foreach (var number in numbers)
        {
            clients.Add(await factory.LoginStudentAsync(number));
        }

        var tally = new ConcurrentDictionary<string, int>();
        try
        {
            for (var round = 0; round < 5; round++)
            {
                foreach (var id in ids)
                {
                    await EnrolAsync(id, "ZY5003");
                }

                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var bulk = Task.Run(async () =>
                {
                    await start.Task;
                    try
                    {
                        var result = await BulkInTransactionAsync([.. ids.Select(id => new WithdrawalTarget(id, trimmed))], Trim);
                        return "bulk withdrew " + result.Withdrawn.Count;
                    }
                    catch (Exception exception) when (exception is NpgsqlException or DbUpdateException or InvalidOperationException)
                    {
                        return "bulk threw " + DbProbe.Describe(exception);
                    }
                });
                var own = clients.Select((client, index) => Task.Run(async () =>
                {
                    await start.Task;
                    if (index % 2 == 0)
                    {
                        using var withdraw = await client.WithdrawAsync("ZY5003");
                        Count("own withdraw " + (int)withdraw.StatusCode);
                    }

                    using var enrol = await client.EnrolAsync("ZY5008");
                    Count("other enrol " + (int)enrol.StatusCode);
                    using var leave = await client.WithdrawAsync("ZY5008");
                    Count("other withdraw " + (int)leave.StatusCode);
                })).ToList();
                start.SetResult();
                var bulkOutcome = await bulk;
                Count(bulkOutcome.StartsWith("bulk threw", StringComparison.Ordinal) ? bulkOutcome : "bulk ok");
                await Task.WhenAll(own);

                var t = await DbProbe.CountsAsync(factory, trimmed);
                var o = await DbProbe.CountsAsync(factory, other);
                Assert.Equal((0, 0), (t.EnrolledCount, t.Active));
                Assert.Equal((0, 0), (o.EnrolledCount, o.Active));
            }
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }

        output.WriteLine(string.Join(", ", tally.OrderBy(k => k.Key).Select(k => $"{k.Key} x{k.Value}")));
        Assert.Equal(5, tally.GetValueOrDefault("bulk ok"));
        Assert.DoesNotContain(tally.Keys, k => k.StartsWith("bulk threw", StringComparison.Ordinal) || k.EndsWith(" 500", StringComparison.Ordinal) || k.EndsWith(" 503", StringComparison.Ordinal));

        void Count(string key) => tally.AddOrUpdate(key, 1, (_, v) => v + 1);
    }

    /// <summary>
    /// Seventy rows in one administrative transaction: one statement withdraws them, one inserts the audit rows, one
    /// decrements the module, and the backend has no subtransaction at all (a savepoint per row overflowed the
    /// 64-entry cache, which slows every snapshot on the server).
    /// </summary>
    [Fact]
    public async Task Seventy_rows_in_one_transaction_take_no_subtransaction()
    {
        var moduleId = await factory.CreateModuleAsync("ZY5004", Semester.Spring, capacity: 100, credits: 5);
        var ids = new List<Guid>();
        foreach (var n in Enumerable.Range(225, 70))
        {
            var id = await factory.StudentIdAsync("S" + n.ToString("D6", CultureInfo.InvariantCulture));
            await EnrolAsync(id, "ZY5004");
            ids.Add(id);
        }

        Assert.Equal((70, 70, 100), await DbProbe.CountsAsync(factory, moduleId));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<EnrolmentService>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var pid = (await db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").ToListAsync())[0];

        var result = await service.WithdrawManyAsync([.. ids.Select(id => new WithdrawalTarget(id, moduleId))], null, Trim);

        var (subtransactions, overflowed) = await SubtransactionsAsync(pid);
        output.WriteLine($"backend {pid} after withdrawing {result.Withdrawn.Count} rows: subxact_count={subtransactions} overflowed={overflowed}");
        Assert.Equal((0, false), (subtransactions, overflowed));
        await transaction.CommitAsync();

        Assert.Equal(70, result.Withdrawn.Count);
        Assert.Equal(0, result.NotEnrolled);
        Assert.All(result.Withdrawn, r => Assert.True(r.CountDecremented));
        Assert.Equal((0, 0, 100), await DbProbe.CountsAsync(factory, moduleId));

        var audit = await factory.WithDbAsync(db2 => db2.AuditEvents.AsNoTracking()
            .Where(a => a.ModuleId == moduleId && a.Action == AuditActions.EnrolmentAdminWithdrawn).ToListAsync());
        Assert.Equal(70, audit.Count);
        Assert.All(audit, a =>
        {
            var details = JsonDocument.Parse(a.Details!).RootElement;
            Assert.True(details.GetProperty("trim").GetBoolean());
            Assert.True(details.GetProperty("override").GetBoolean());
            Assert.Equal("ZY5004", details.GetProperty("moduleCode").GetString());
        });
    }

    /// <summary>
    /// A student leaving (one student, several modules), in a transaction of its own: current-year rows release their
    /// places, an earlier year's row releases none, every row gets its audit row flagged <c>left</c>, and no cached value
    /// is read (there is nothing to fill under the caller's locks).
    /// </summary>
    [Fact]
    public async Task A_leaver_withdraws_every_row_and_releases_only_this_years_places()
    {
        const string student = "S000295";
        var first = await factory.CreateModuleAsync("ZY5005", Semester.Spring, capacity: 10, credits: 5);
        var second = await factory.CreateModuleAsync("ZY5006", Semester.Spring, capacity: 10, credits: 5);
        var earlier = await factory.CreateModuleAsync("ZY5007", Semester.Spring, capacity: 10, credits: 5);
        var studentId = await EnrolAsync(student, "ZY5005");
        await EnrolAsync(student, "ZY5006");
        await factory.InsertEnrolmentAsync(student, "ZY5007", StudentData.PreviousYear);

        var metrics = factory.Services.GetRequiredService<RushDayMetrics>();
        using var cacheReads = new MetricCollector<long>(metrics.CacheRequests);
        BulkWithdrawalResult result;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<EnrolmentService>();
            result = await service.WithdrawManyAsync(
                [new WithdrawalTarget(studentId, first), new WithdrawalTarget(studentId, second), new WithdrawalTarget(studentId, earlier), new WithdrawalTarget(studentId, first)],
                null,
                new WithdrawOptions(Override: true, Reason: "The student left.", Left: true));
        }

        Assert.Empty(cacheReads.GetMeasurementSnapshot());
        Assert.Equal(3, result.Withdrawn.Count);
        Assert.Equal(0, result.NotEnrolled);
        Assert.Equal(
            [("ZY5005", StudentData.CurrentYear, true), ("ZY5006", StudentData.CurrentYear, true), ("ZY5007", StudentData.PreviousYear, false)],
            result.Withdrawn.Select(r => (r.ModuleCode, r.AcademicYear, r.CountDecremented)).OrderBy(r => r.ModuleCode).ToList());
        Assert.Equal((0, 0, 10), await DbProbe.CountsAsync(factory, first));
        Assert.Equal((0, 0, 10), await DbProbe.CountsAsync(factory, second));
        foreach (var code in new[] { "ZY5005", "ZY5006", "ZY5007" })
        {
            Assert.Equal(EnrolmentStatus.Withdrawn, (await factory.EnrolmentAsync(student, code))!.Status);
        }

        var audit = await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Action == AuditActions.EnrolmentAdminWithdrawn).ToListAsync());
        Assert.Equal(3, audit.Count);
        Assert.All(audit, a => Assert.True(JsonDocument.Parse(a.Details!).RootElement.GetProperty("left").GetBoolean()));

        // A bulk withdrawal is an override by definition.
        await using var check = factory.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<ArgumentException>(() => check.ServiceProvider.GetRequiredService<EnrolmentService>()
            .WithdrawManyAsync([new WithdrawalTarget(studentId, first)], null, WithdrawOptions.Self));
    }

    /// <summary>Runs a bulk withdrawal inside an administrative transaction of its own scope, as S6's routes do, and commits.</summary>
    private Task<BulkWithdrawalResult> BulkInTransactionAsync(IReadOnlyCollection<WithdrawalTarget> targets, WithdrawOptions options) => Task.Run(async () =>
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await scope.ServiceProvider.GetRequiredService<EnrolmentService>().WithdrawManyAsync(targets, null, options);
        await transaction.CommitAsync();
        return result;
    });

    /// <summary>An administrator's enrolment (window and credits ignored), so the tests do not depend on the students' other modules.</summary>
    private async Task<Guid> EnrolAsync(string studentNumber, string code)
    {
        var id = await factory.StudentIdAsync(studentNumber);
        await EnrolAsync(id, code);
        return id;
    }

    private async Task EnrolAsync(Guid studentId, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<EnrolmentService>().EnrolAsync(studentId, code, null, new EnrolOptions(Override: true, Reason: "Enrolled by a test."));
        Assert.True(result.Succeeded, result.Failure?.Error.ToString());
    }

    private Task<Guid> EnrolmentIdOf(Guid studentId, Guid moduleId) =>
        factory.WithDbAsync(db => db.Enrolments.AsNoTracking().Where(e => e.StudentId == studentId && e.ModuleId == moduleId).Select(e => e.Id).SingleAsync());

    private async Task<(int Count, bool Overflowed)> SubtransactionsAsync(int pid)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT s.subxact_count, s.subxact_overflowed FROM pg_stat_get_backend_idset() b, LATERAL pg_stat_get_backend_subxact(b) s WHERE pg_stat_get_backend_pid(b) = @pid",
            connection);
        command.Parameters.AddWithValue("pid", pid);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetBoolean(1));
    }
}
