using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Ops;
using RushDay.Infrastructure.Persistence;
using RushDay.IntegrationTests.Student;

namespace RushDay.IntegrationTests.Staff;

/// <summary>
/// Joint items J1 (trim and leave withdraw through <c>WithdrawManyAsync</c>, in the enrolment lock order) and J2 (the
/// reconcile route's pre-lock no longer blocks enrolment inserts). Every interleaving is built deterministically on
/// <c>pg_stat_activity</c>, as in <see cref="BulkWithdrawalTests"/>. Modules <c>YK####</c>; students created here as
/// <c>S943###</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StaffConcurrencyTests(RushDayApiFactory factory)
{
    private const string Reason = StaffData.Reason;

    /// <summary>
    /// A trim holds the rows it withdrew and waits for the module row (held here); a student it is about to withdraw
    /// asks to withdraw themselves. The trim holds every student of the module from its first statement, so the
    /// student's request queues on its own row and finds the enrolment gone (404). Before J1 the trim's loop of
    /// single withdrawals held the module row from its first row while waiting for the next student, who held their
    /// row while waiting for the module: 40P01, one side answered 503.
    /// </summary>
    [Fact]
    public async Task A_trim_racing_a_students_own_withdrawal_never_deadlocks()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YK1001", Semester.Spring, capacity: 10);
        var first = await ResultsGovernanceTests.NewStudentAsync(admin, "S943001");
        var second = await ResultsGovernanceTests.NewStudentAsync(admin, "S943002");
        var third = await ResultsGovernanceTests.NewStudentAsync(admin, "S943003");
        foreach (var number in new[] { first, second, third })
        {
            await admin.OverrideEnrolAsync(number, "YK1001");
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        using var student = await LoginAsync(admin, second);
        var moduleId = await factory.ModuleIdAsync("YK1001");
        await factory.WithDbAsync(db => db.Modules.Where(m => m.Id == moduleId).ExecuteUpdateAsync(s => s.SetProperty(m => m.Capacity, 1)));

        Task<(HttpStatusCode Status, string Body)> trim;
        Task<(HttpStatusCode Status, string Body)> own;
        await using (var blocker = await DbProbe.HoldAsync(factory, "SELECT 1 FROM modules WHERE id = @m FOR NO KEY UPDATE", ("m", moduleId)))
        {
            trim = SendAsync(admin, HttpMethod.Post, "/api/admin/modules/YK1001/trim-to-capacity", new { reason = Reason });
            await DbProbe.WaitForLockWaitAsync(factory, "%modules%");

            own = SendAsync(student, HttpMethod.Delete, "/api/me/enrolments/YK1001", null);
            await DbProbe.WaitForLockWaitAsync(factory, "%", count: 2);
            await blocker.ReleaseAsync();
        }

        var (trimStatus, trimBody) = await trim;
        var (ownStatus, ownBody) = await own;
        Assert.True(trimStatus == HttpStatusCode.OK, $"Trim answered {(int)trimStatus}: {trimBody}");
        Assert.True(ownStatus == HttpStatusCode.NotFound && ownBody.Contains("urn:rushday:not-enrolled", StringComparison.Ordinal), $"The student's withdrawal answered {(int)ownStatus}: {ownBody}");
        Assert.Contains(second, trimBody, StringComparison.Ordinal);
        Assert.Equal((1, 1, 1), await DbProbe.CountsAsync(factory, moduleId));
        Assert.Equal(EnrolmentStatus.Active, (await factory.EnrolmentAsync(first, "YK1001"))!.Status);
    }

    /// <summary>
    /// A student's own withdrawal holds its student row and waits for the module row (held here); the registry marks
    /// the same student as left meanwhile. The leave queues on the student row, then withdraws what is still active in
    /// one bulk statement: no deadlock, no server error, counts equal the rows. (The leave took the student row first
    /// before J1 too; what J1 changes for it is one statement instead of a loop of savepoints.)
    /// </summary>
    [Fact]
    public async Task A_leave_racing_the_students_own_withdrawal_never_deadlocks()
    {
        using var real = await factory.RealAdminAsync();
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YK1002", Semester.Spring, capacity: 10);
        await admin.CreateModuleAsync("YK1003", Semester.Spring, capacity: 10);
        var number = await ResultsGovernanceTests.NewStudentAsync(admin, "S943011");
        await admin.OverrideEnrolAsync(number, "YK1002");
        await admin.OverrideEnrolAsync(number, "YK1003");

        // A real login (a real administrator's leave may disable it), with the forced change done.
        await real.Client.PostJsonAsync("/api/admin/accounts", new { username = "s943011", displayName = "Leaving Student", role = "Student", studentNumber = number, temporaryPassword = TestAccounts.Password }, HttpStatusCode.Created);
        await factory.WithDbAsync(db => db.Users.Where(u => u.UserName == "s943011").ExecuteUpdateAsync(s => s.SetProperty(u => u.MustChangePassword, false)));
        using var student = await factory.LoginAsync("s943011", TestAccounts.Password);

        var moduleId = await factory.ModuleIdAsync("YK1002");
        var otherId = await factory.ModuleIdAsync("YK1003");
        Task<(HttpStatusCode Status, string Body)> own;
        Task<(HttpStatusCode Status, string Body)> leave;
        await using (var blocker = await DbProbe.HoldAsync(factory, "SELECT 1 FROM modules WHERE id = @m FOR NO KEY UPDATE", ("m", moduleId)))
        {
            own = SendAsync(student, HttpMethod.Delete, "/api/me/enrolments/YK1002", null);
            await DbProbe.WaitForLockWaitAsync(factory, "%modules%");

            leave = SendAsync(real.Client, HttpMethod.Post, $"/api/admin/students/{number}/leave", new { reason = Reason });
            await DbProbe.WaitForLockWaitAsync(factory, "%", count: 2);
            await blocker.ReleaseAsync();
        }

        var (ownStatus, ownBody) = await own;
        var (leaveStatus, leaveBody) = await leave;
        Assert.True(ownStatus == HttpStatusCode.NoContent, $"The student's withdrawal answered {(int)ownStatus}: {ownBody}");
        Assert.True(leaveStatus == HttpStatusCode.OK, $"The leave answered {(int)leaveStatus}: {leaveBody}");
        Assert.Contains("\"withdrawn\":1", leaveBody, StringComparison.Ordinal);
        Assert.Equal((0, 0, 10), await DbProbe.CountsAsync(factory, moduleId));
        Assert.Equal((0, 0, 10), await DbProbe.CountsAsync(factory, otherId));
        Assert.NotNull((await factory.ReadUserAsync("s943011")).DisabledAt);
    }

    /// <summary>
    /// J2: the reconcile route locks every module row in the mode an enrolment's claim takes (FOR NO KEY UPDATE), so
    /// while it runs an enrolment insert's foreign-key check (FOR KEY SHARE on the module) goes through. Before J2 it
    /// took FOR UPDATE, which blocks that check: the insert waited out the reconciliation (here, a lock timeout).
    /// </summary>
    [Fact]
    public async Task Reconcile_does_not_block_enrolment_inserts()
    {
        using var admin = await factory.DemoAdminAsync();
        await admin.CreateModuleAsync("YK2001", Semester.Spring, capacity: 10);
        var number = await ResultsGovernanceTests.NewStudentAsync(admin, "S943021");
        var moduleId = await factory.ModuleIdAsync("YK2001");
        var studentId = await factory.StudentIdAsync(number);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        await using (var reconcile = await db.Database.BeginTransactionAsync())
        {
            await ReconcileService.ReconcileInTransactionAsync(db);

            string? failure = null;
            try
            {
                await using var connection = new NpgsqlConnection(factory.ConnectionString);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await using var insert = new NpgsqlCommand(
                    "SET LOCAL lock_timeout = '3s'; INSERT INTO enrolments (id, student_id, module_id, enrolled_at, status, source, academic_year) VALUES (gen_random_uuid(), @s, @m, now(), 'Withdrawn', 'Admin', @y)",
                    connection,
                    transaction);
                insert.Parameters.AddWithValue("s", studentId);
                insert.Parameters.AddWithValue("m", moduleId);
                insert.Parameters.AddWithValue("y", StudentData.CurrentYear);
                await insert.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch (PostgresException exception)
            {
                failure = exception.SqlState + " " + exception.MessageText;
            }

            await reconcile.RollbackAsync();
            Assert.Null(failure);
        }

        Assert.Equal(EnrolmentStatus.Withdrawn, (await factory.EnrolmentAsync(number, "YK2001"))!.Status);
    }

    private async Task<HttpClient> LoginAsync(HttpClient admin, string number)
    {
        await admin.PostJsonAsync("/api/admin/accounts", new { username = number, displayName = "Racing Student", role = "Student", studentNumber = number, temporaryPassword = TestAccounts.Password }, HttpStatusCode.Created);
        return await factory.LoginAsync(number, TestAccounts.Password);
    }

    private static Task<(HttpStatusCode Status, string Body)> SendAsync(HttpClient client, HttpMethod method, string path, object? body) =>
        Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        });
}
