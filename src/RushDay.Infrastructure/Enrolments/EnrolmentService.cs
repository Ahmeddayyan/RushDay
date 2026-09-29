using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.Infrastructure.Enrolments;

/// <summary>Why an enrolment or withdrawal was refused; the API maps each to its ProblemDetails slug (02-api.md section 6).</summary>
public enum EnrolmentError
{
    None,
    ModuleNotFound,
    StudentNotFound,
    ModuleInactive,
    ModuleFull,
    AlreadyEnrolled,
    WindowClosed,
    CreditLimitExceeded,
    ResultsExist,
    StudentLeft,
    NotEnrolled,
    WithdrawalDeadlinePassed,
}

/// <summary>
/// <c>EnrolAsync</c> options (04-performance-and-ops.md section 2.1). <see cref="Override"/> is the administrator's
/// override: windows and the credit limit are ignored, <c>results-exist</c> and <c>student-left</c> still apply, and
/// capacity applies unless <see cref="ForceCapacity"/>, which raises capacity only when the module is full.
/// </summary>
public sealed record EnrolOptions(bool Override = false, bool ForceCapacity = false, string? Reason = null)
{
    /// <summary>A student enrolling themselves.</summary>
    public static EnrolOptions Self { get; } = new();
}

/// <summary>
/// <c>WithdrawAsync</c> options (04 section 2.2). <see cref="Override"/> is the administrator's withdrawal, which ignores
/// the deadline, the year and <c>results-exist</c>; <see cref="Left"/> and <see cref="Trim"/> only flag its audit row.
/// </summary>
public sealed record WithdrawOptions(bool Override = false, string? Reason = null, bool Left = false, bool Trim = false)
{
    /// <summary>A student withdrawing themselves.</summary>
    public static WithdrawOptions Self { get; } = new();
}

/// <summary>A refusal with the extension data its problem carries (02-api.md section 6).</summary>
public sealed record EnrolmentFailure(
    EnrolmentError Error,
    Semester? Semester = null,
    DateTimeOffset? OpensAt = null,
    DateTimeOffset? ClosesAt = null,
    int? CurrentCredits = null,
    int? ModuleCredits = null,
    int? Limit = null,
    DateTimeOffset? WithdrawalDeadlineAt = null);

/// <summary>An accepted enrolment: <see cref="PlacesRemaining"/> comes from the claiming statement's <c>RETURNING</c>.</summary>
public sealed record EnrolmentReceipt(
    Guid EnrolmentId,
    Guid ModuleId,
    string ModuleCode,
    string AcademicYear,
    DateTimeOffset EnrolledAt,
    int PlacesRemaining,
    bool Reactivated,
    bool CapacityRaised);

public sealed record EnrolmentResult(EnrolmentReceipt? Receipt, EnrolmentFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static EnrolmentResult Success(EnrolmentReceipt receipt) => new(receipt, null);

    public static EnrolmentResult Fail(EnrolmentFailure failure) => new(null, failure);
}

/// <summary>
/// A completed withdrawal. <see cref="CountDecremented"/> is true when the withdrawal released one of this year's places:
/// false for an earlier year's row (it held no place this year), and false when <c>enrolled_count</c> had already drifted
/// to 0 (logged as <see cref="EnrolmentService.CountDriftEvent"/>).
/// </summary>
public sealed record WithdrawalReceipt(Guid EnrolmentId, Guid ModuleId, string ModuleCode, string AcademicYear, DateTimeOffset WithdrawnAt, bool CountDecremented);

public sealed record WithdrawalResult(WithdrawalReceipt? Receipt, EnrolmentFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static WithdrawalResult Success(WithdrawalReceipt receipt) => new(receipt, null);

    public static WithdrawalResult Fail(EnrolmentFailure failure) => new(null, failure);
}

/// <summary>One enrolment an administrative bulk withdrawal targets: (student, module) names at most one row (D10).</summary>
public sealed record WithdrawalTarget(Guid StudentId, Guid ModuleId);

/// <summary>
/// The outcome of <see cref="EnrolmentService.WithdrawManyAsync"/>: one receipt per row withdrawn, in no particular order;
/// <see cref="NotEnrolled"/> counts targets that had no active row by the time their student was locked (a student's own
/// withdrawal got there first).
/// </summary>
public sealed record BulkWithdrawalResult(IReadOnlyList<WithdrawalReceipt> Withdrawn, int NotEnrolled);

/// <summary>
/// The enrolment metrics of 04-performance-and-ops.md section 6.1; the API implements it over <c>RushDayMetrics</c>
/// (<c>rushday.enrolments.accepted</c>, <c>.rejected{reason}</c>, <c>.duration</c>).
/// </summary>
public interface IEnrolmentMetrics
{
    void Accepted();

    /// <summary>Called only for the seven rejection reasons of 04 section 2.1.</summary>
    void Rejected(EnrolmentError reason);

    void Duration(double milliseconds);
}

/// <summary>
/// The enrolment concurrency fix (04-performance-and-ops.md section 2, D10, D11). Capacity is claimed by one atomic
/// conditional <c>UPDATE modules SET enrolled_count = enrolled_count + 1 ... WHERE ... AND enrolled_count &lt; capacity
/// RETURNING ...</c>, run last in a READ COMMITTED transaction: PostgreSQL locks the module row for the update and, when
/// another transaction updated it first, re-evaluates the <c>WHERE</c> against the committed row (EvalPlanQual), so
/// at most <c>capacity</c> transactions ever claim a place and no read-then-write path exists. The unique
/// (student, module) index and the conditional reactivation make duplicate rows impossible.
/// <para>
/// Lock order is always student (<c>FOR NO KEY UPDATE</c>) → settings row (<c>FOR SHARE</c>, the academic year) →
/// enrolment → module, in enrolment, withdrawal and <see cref="WithdrawManyAsync"/> alike, and every module row lock is
/// the <c>FOR NO KEY UPDATE</c> an <c>UPDATE</c> takes, which does not conflict with the <c>FOR KEY SHARE</c> an
/// enrolment insert's foreign-key check holds on the same row. <see cref="EnrolAsync"/> and <see cref="WithdrawAsync"/>
/// each run in their own transaction (or under a savepoint in a caller's); an administrative operation that withdraws
/// several rows in one transaction (trim, leave) uses <see cref="WithdrawManyAsync"/>, which keeps the order across rows.
/// </para>
/// </summary>
public sealed class EnrolmentService(
    RushDayDbContext db,
    EnrolmentWindowService windows,
    AuditWriter audit,
    IEnrolmentMetrics metrics,
    TimeProvider clock,
    ILogger<EnrolmentService> logger)
{
    /// <summary>Step 8 without <c>ForceCapacity</c>: the single write path of <c>enrolled_count</c> upward.</summary>
    public const string ClaimPlaceSql =
        "UPDATE modules SET enrolled_count = enrolled_count + 1, updated_at = @now WHERE id = @module AND is_active AND enrolled_count < capacity " +
        "RETURNING capacity, enrolled_count, false AS raised, capacity AS previous_capacity";

    /// <summary>
    /// Step 8 with <c>ForceCapacity</c>: raises capacity only when the module is full (02-api.md section 8.5). The row is
    /// locked <c>FOR NO KEY UPDATE</c>, the mode the <c>UPDATE</c> itself takes: <c>FOR UPDATE</c> would conflict with the
    /// <c>FOR KEY SHARE</c> that every enrolment insert's foreign-key check holds on the module row, so two forced
    /// enrolments on one module would deadlock.
    /// </summary>
    public const string ForceClaimPlaceSql = "WITH " + ForceClaimBeforeCte + " " + ForceClaimUpdate;

    /// <summary>
    /// <see cref="ForceClaimPlaceSql"/> with the override's audit rows inserted by the same statement: the
    /// <c>enrolment.admin_created</c> row, whose <c>capacityRaised</c> only the claim knows, and the <c>module.updated</c>
    /// row when capacity was raised. The hot row is therefore locked only for this statement and the commit.
    /// </summary>
    public const string ForceClaimPlaceAuditedSql =
        "WITH " + ForceClaimBeforeCte + ", claim AS (" + ForceClaimUpdate + "), " +
        "created AS (INSERT INTO audit_events (" + AuditColumns + ") " +
        "SELECT @created_id, @created_at, @actor_user_id, @actor_username, @actor_role, @created_action, @created_subject_type, @created_subject_id, @student, @module, " +
        "CAST(@created_details AS jsonb) || jsonb_build_object('capacityRaised', claim.raised), @request_id, @ip_hash FROM claim), " +
        "raised AS (INSERT INTO audit_events (" + AuditColumns + ") " +
        "SELECT @raised_id, @raised_at, @actor_user_id, @actor_username, @actor_role, @raised_action, @raised_subject_type, @raised_subject_id, CAST(NULL AS uuid), @module, " +
        "jsonb_build_object('capacity', jsonb_build_object('before', claim.previous_capacity, 'after', claim.capacity), 'reason', @reason), @request_id, @ip_hash " +
        "FROM claim WHERE claim.raised) " +
        "SELECT capacity, enrolled_count, raised, previous_capacity FROM claim";

    /// <summary>The academic year, read after the student lock and share-locked until commit (04 section 2.1 step 1).</summary>
    public const string LockAcademicYearSql = "SELECT academic_year AS \"Value\" FROM academic_settings WHERE id = 1 FOR SHARE";

    /// <summary>
    /// Logged at Warning when a withdrawal of a current-year row finds <c>enrolled_count</c> already at 0 (or below the
    /// rows withdrawn): the count had drifted. The ops page's <c>dataQuality.enrolledCountDrift</c> shows it and
    /// <c>POST /api/admin/ops/reconcile</c> repairs it.
    /// </summary>
    public static readonly EventId CountDriftEvent = new(4101, "EnrolledCountDrift");

    private const string ForceClaimBeforeCte = "before AS (SELECT capacity AS c FROM modules WHERE id = @module AND is_active FOR NO KEY UPDATE)";

    private const string ForceClaimUpdate =
        "UPDATE modules m SET enrolled_count = m.enrolled_count + 1, " +
        "capacity = CASE WHEN m.enrolled_count >= m.capacity THEN m.enrolled_count + 1 ELSE m.capacity END, updated_at = @now " +
        "FROM before WHERE m.id = @module AND m.is_active " +
        "RETURNING m.capacity, m.enrolled_count, m.capacity <> before.c AS raised, before.c AS previous_capacity";

    private const string AuditColumns =
        "id, occurred_at, actor_user_id, actor_username, actor_role, action, subject_type, subject_id, student_id, module_id, details, request_id, ip_hash";

    private const string LockStudentsSql = "SELECT id FROM students WHERE id = ANY(@students) ORDER BY id FOR NO KEY UPDATE";

    private const string WithdrawManySql =
        "UPDATE enrolments e SET status = 'Withdrawn', withdrawn_at = @now, updated_at = @now " +
        "FROM unnest(@students, @modules) AS t(student_id, module_id), modules m " +
        "WHERE e.student_id = t.student_id AND e.module_id = t.module_id AND e.status = 'Active' AND m.id = e.module_id " +
        "RETURNING e.id, e.student_id, e.module_id, m.code, e.academic_year";

    private const string LockModulesSql = "SELECT id, enrolled_count FROM modules WHERE id = ANY(@modules) ORDER BY id FOR NO KEY UPDATE";

    private const string DecrementModulesSql =
        "UPDATE modules m SET enrolled_count = greatest(m.enrolled_count - d.n, 0), updated_at = @now " +
        "FROM unnest(@modules, @counts) AS d(id, n) WHERE m.id = d.id";

    private const string EnrolmentUniqueIndex = "ix_enrolments_student_id_module_id";

    private const string SavepointName = "rushday_enrolment";

    /// <summary>Enrols a student on a module (04 section 2.1). <paramref name="actorUserId"/> is recorded as creator only on an override.</summary>
    public async Task<EnrolmentResult> EnrolAsync(Guid studentId, string moduleCode, Guid? actorUserId, EnrolOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await EnrolCoreAsync(studentId, NormaliseCode(moduleCode), actorUserId, options, cancellationToken);
            if (result.Succeeded)
            {
                metrics.Accepted();
            }
            else if (IsRejectionReason(result.Failure!.Error))
            {
                metrics.Rejected(result.Failure.Error);
            }

            return result;
        }
        finally
        {
            metrics.Duration(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Withdraws a student's active enrolment on a module (04 section 2.2): a student's own withdrawal, or one
    /// administrator's withdrawal of one row. An operation that withdraws several rows in one transaction uses
    /// <see cref="WithdrawManyAsync"/> instead.
    /// </summary>
    public async Task<WithdrawalResult> WithdrawAsync(Guid studentId, string moduleCode, Guid? actorUserId, WithdrawOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var code = NormaliseCode(moduleCode);
        var module = await db.Modules.AsNoTracking()
            .Where(m => m.Code == code)
            .Select(m => new { m.Id, m.Code, m.Semester })
            .SingleOrDefaultAsync(cancellationToken);
        if (module is null)
        {
            // Nobody is enrolled on a module that does not exist.
            return WithdrawalResult.Fail(new EnrolmentFailure(EnrolmentError.NotEnrolled));
        }

        // Cached reads happen before this call's transaction, so a cache fill never runs while its row locks are held.
        var calendar = await windows.CurrentAsync(cancellationToken);
        var allWindows = await windows.AllAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var tracked = new List<object>();

        await using var scope = await UnitOfWork.BeginAsync(db, cancellationToken);
        try
        {
            // 1. Serialise this student's own requests (and an administrator acting on them).
            var student = await LockStudentAsync(studentId, cancellationToken);
            if (student is null)
            {
                return await scope.FailAsync(WithdrawalResult.Fail(new EnrolmentFailure(EnrolmentError.StudentNotFound)), cancellationToken);
            }

            // The academic year under a share lock: it cannot change until this transaction ends (04 section 2.2).
            var currentYear = await LockAcademicYearAsync(calendar.AcademicYear, cancellationToken);

            // 2. The active row, of any year.
            var enrolment = await db.Enrolments.AsNoTracking()
                .Where(e => e.StudentId == studentId && e.ModuleId == module.Id && e.Status == EnrolmentStatus.Active)
                .Select(e => new { e.Id, e.AcademicYear })
                .SingleOrDefaultAsync(cancellationToken);
            if (enrolment is null)
            {
                return await scope.FailAsync(WithdrawalResult.Fail(new EnrolmentFailure(EnrolmentError.NotEnrolled)), cancellationToken);
            }

            var isCurrentYear = string.Equals(enrolment.AcademicYear, currentYear, StringComparison.Ordinal);

            // 3. A student may withdraw only this year's row, before the deadline, and while no mark is recorded.
            if (!options.Override)
            {
                var window = EnrolmentWindowService.Find(allWindows, enrolment.AcademicYear, module.Semester);
                if (!isCurrentYear || window is null || !window.AllowsWithdrawalAt(now))
                {
                    return await scope.FailAsync(
                        WithdrawalResult.Fail(new EnrolmentFailure(EnrolmentError.WithdrawalDeadlinePassed, WithdrawalDeadlineAt: window?.WithdrawalDeadlineAt)),
                        cancellationToken);
                }

                if (await GradeQueries.WithResults(db).AnyAsync(g => g.StudentId == studentId && g.ModuleId == module.Id, cancellationToken))
                {
                    return await scope.FailAsync(WithdrawalResult.Fail(new EnrolmentFailure(EnrolmentError.ResultsExist)), cancellationToken);
                }
            }

            // 4. The row toggles status; it is never deleted (D10).
            var withdrawn = await db.Enrolments
                .Where(e => e.Id == enrolment.Id && e.Status == EnrolmentStatus.Active)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.Status, EnrolmentStatus.Withdrawn)
                          .SetProperty(e => e.WithdrawnAt, now)
                          .SetProperty(e => e.UpdatedAt, now),
                    cancellationToken);
            if (withdrawn == 0)
            {
                return await scope.FailAsync(WithdrawalResult.Fail(new EnrolmentFailure(EnrolmentError.NotEnrolled)), cancellationToken);
            }

            // 5. Only a row of the current year holds one of this year's places (D28). A count already at 0 has drifted.
            var decremented = false;
            if (isCurrentYear)
            {
                decremented = await db.Modules
                    .Where(m => m.Id == module.Id && m.EnrolledCount > 0)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(m => m.EnrolledCount, m => m.EnrolledCount - 1)
                              .SetProperty(m => m.UpdatedAt, now),
                        cancellationToken) == 1;
                if (!decremented)
                {
                    LogDrift(module.Code, withdrawnRows: 1, countBefore: 0);
                }
            }

            // 6. The audit row commits with the change.
            tracked.Add(options.Override
                ? audit.Record(db, AuditActions.EnrolmentAdminWithdrawn, AuditSubjects.Enrolment, enrolment.Id.ToString(), AdminWithdrawnDetails(module.Code, enrolment.AcademicYear, options), studentId, module.Id)
                : audit.Record(db, AuditActions.EnrolmentWithdrawn, AuditSubjects.Enrolment, enrolment.Id.ToString(), new { moduleCode = module.Code, academicYear = enrolment.AcademicYear }, studentId, module.Id));
            await db.SaveChangesAsync(cancellationToken);

            await scope.CompleteAsync(cancellationToken);
            return WithdrawalResult.Success(new WithdrawalReceipt(enrolment.Id, module.Id, module.Code, enrolment.AcademicYear, now, decremented));
        }
        catch
        {
            Detach(tracked);
            throw;
        }
    }

    /// <summary>
    /// The administrator's withdrawal of several rows inside one administrative operation (trim to capacity, a student
    /// leaving; 04 section 2.2), in the caller's transaction when there is one (else its own), with no savepoints:
    /// every target student is locked first, in id order (<c>FOR NO KEY UPDATE</c>); the academic year is read under
    /// a share lock; every targeted active row is withdrawn by one statement; one <c>enrolment.admin_withdrawn</c>
    /// audit row per withdrawal is written; and each affected module is decremented once, last, its row locked in
    /// module-id order. That is the lock order of a single enrolment or withdrawal, so a student's own request racing
    /// the operation waits or finds its row gone but never deadlocks, and no cached value is read inside the caller's
    /// transaction. The caller must not have locked module rows before calling, and should commit soon after: the
    /// module rows stay locked until the caller's transaction ends. A target whose row is no longer active is counted
    /// in <see cref="BulkWithdrawalResult.NotEnrolled"/>. Deadlines, years and <c>results-exist</c> are ignored (it is
    /// an override); <paramref name="options"/> must have <see cref="WithdrawOptions.Override"/> set, and its
    /// <see cref="WithdrawOptions.Reason"/>, <see cref="WithdrawOptions.Left"/> and <see cref="WithdrawOptions.Trim"/>
    /// go into every audit row.
    /// </summary>
    public async Task<BulkWithdrawalResult> WithdrawManyAsync(IReadOnlyCollection<WithdrawalTarget> targets, Guid? actorUserId, WithdrawOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Override)
        {
            throw new ArgumentException("A bulk withdrawal is an administrator's override; set WithdrawOptions.Override.", nameof(options));
        }

        var pairs = targets.Distinct().ToArray();
        if (pairs.Length == 0)
        {
            return new BulkWithdrawalResult([], 0);
        }

        var now = clock.GetUtcNow();
        var tracked = new List<object>();
        var autoSavepoints = db.Database.AutoSavepointsEnabled;
        db.Database.AutoSavepointsEnabled = false;
        var own = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        try
        {
            // 1. Every target student, in id order.
            await using (var lockStudents = Command(LockStudentsSql))
            {
                lockStudents.Parameters.Add(new NpgsqlParameter("students", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = pairs.Select(p => p.StudentId).Distinct().ToArray() });
                await lockStudents.ExecuteNonQueryAsync(cancellationToken);
            }

            // 2. The academic year, share-locked until the caller commits.
            var currentYear = await LockAcademicYearAsync(StartupBackfills.CurrentAcademicYear, cancellationToken);

            // 3. Every targeted row that is still active, in one statement.
            var rows = new List<(Guid EnrolmentId, Guid StudentId, Guid ModuleId, string ModuleCode, string AcademicYear)>();
            await using (var withdraw = Command(WithdrawManySql))
            {
                withdraw.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
                withdraw.Parameters.Add(new NpgsqlParameter("students", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = pairs.Select(p => p.StudentId).ToArray() });
                withdraw.Parameters.Add(new NpgsqlParameter("modules", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = pairs.Select(p => p.ModuleId).ToArray() });
                await using var reader = await withdraw.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4)));
                }
            }

            // 4. One audit row per withdrawal, before any module row is locked.
            foreach (var row in rows)
            {
                tracked.Add(audit.Record(db, AuditActions.EnrolmentAdminWithdrawn, AuditSubjects.Enrolment, row.EnrolmentId.ToString(), AdminWithdrawnDetails(row.ModuleCode, row.AcademicYear, options), row.StudentId, row.ModuleId));
            }

            if (tracked.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                Detach(tracked);
            }

            // 5. Each module that lost current-year rows, once, last, in id order (D28: earlier years hold no place).
            var released = rows
                .Where(r => string.Equals(r.AcademicYear, currentYear, StringComparison.Ordinal))
                .GroupBy(r => r.ModuleId)
                .ToDictionary(g => g.Key, g => g.Count());
            var decrementedByModule = new Dictionary<Guid, int>();
            if (released.Count > 0)
            {
                var countsBefore = new Dictionary<Guid, int>();
                await using (var lockModules = Command(LockModulesSql))
                {
                    lockModules.Parameters.Add(new NpgsqlParameter("modules", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = released.Keys.ToArray() });
                    await using var reader = await lockModules.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        countsBefore[reader.GetGuid(0)] = reader.GetInt32(1);
                    }
                }

                await using (var decrement = Command(DecrementModulesSql))
                {
                    decrement.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
                    decrement.Parameters.Add(new NpgsqlParameter("modules", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = released.Keys.ToArray() });
                    decrement.Parameters.Add(new NpgsqlParameter("counts", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = released.Values.ToArray() });
                    await decrement.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (var (moduleId, count) in released)
                {
                    var before = countsBefore.GetValueOrDefault(moduleId);
                    decrementedByModule[moduleId] = Math.Min(before, count);
                    if (before < count)
                    {
                        LogDrift(rows.First(r => r.ModuleId == moduleId).ModuleCode, count, before);
                    }
                }
            }

            if (own is not null)
            {
                await own.CommitAsync(cancellationToken);
            }

            var receipts = new List<WithdrawalReceipt>(rows.Count);
            foreach (var row in rows)
            {
                var releasedPlace = decrementedByModule.TryGetValue(row.ModuleId, out var left) && left > 0
                    && string.Equals(row.AcademicYear, currentYear, StringComparison.Ordinal);
                if (releasedPlace)
                {
                    decrementedByModule[row.ModuleId] = left - 1;
                }

                receipts.Add(new WithdrawalReceipt(row.EnrolmentId, row.ModuleId, row.ModuleCode, row.AcademicYear, now, releasedPlace));
            }

            return new BulkWithdrawalResult(receipts, pairs.Length - rows.Count);
        }
        catch
        {
            Detach(tracked);
            throw;
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }

            db.Database.AutoSavepointsEnabled = autoSavepoints;
        }
    }

    private async Task<EnrolmentResult> EnrolCoreAsync(Guid studentId, string code, Guid? actorUserId, EnrolOptions options, CancellationToken cancellationToken)
    {
        // Fast path before any transaction: 470 losers of a 500-student rush are answered here without a lock.
        var module = await db.Modules.AsNoTracking().SingleOrDefaultAsync(m => m.Code == code, cancellationToken);
        if (module is null)
        {
            return EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.ModuleNotFound));
        }

        if (!module.IsActive)
        {
            return EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.ModuleInactive));
        }

        if (module.EnrolledCount >= module.Capacity && !options.ForceCapacity)
        {
            return EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.ModuleFull));
        }

        // The calendar and the windows come from caches, read before the transaction so no fill runs under a lock.
        var calendar = await windows.CurrentAsync(cancellationToken);
        var allWindows = await windows.AllAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var tracked = new List<object>();

        await using var scope = await UnitOfWork.BeginAsync(db, cancellationToken);
        try
        {
            // 1. Serialises this student's own requests; a student who has left cannot be enrolled by anyone.
            var student = await LockStudentAsync(studentId, cancellationToken);
            if (student is null)
            {
                return await scope.FailAsync(EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.StudentNotFound)), cancellationToken);
            }

            if (student.Left)
            {
                return await scope.FailAsync(EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.StudentLeft)), cancellationToken);
            }

            // The academic year under a share lock, after the student lock: the cached year may predate a year change,
            // and a year change cannot commit while this transaction holds the row, so the year stamped on the row and
            // the count claimed below always belong to the same year (04 section 2.1 step 1).
            var currentYear = await LockAcademicYearAsync(calendar.AcademicYear, cancellationToken);

            // 2. The window of (current year, module semester); the override ignores it.
            var window = EnrolmentWindowService.Find(allWindows, currentYear, module.Semester);
            var windowOpen = options.Override || (window?.IsOpenAt(now) ?? false);

            // 3. The existing row, and whether a completed module would be retaken (never in v1, override included).
            var existing = await db.Enrolments.AsNoTracking()
                .Where(e => e.StudentId == studentId && e.ModuleId == module.Id)
                .Select(e => new { e.Id, e.Status, e.AcademicYear })
                .SingleOrDefaultAsync(cancellationToken);
            var current = existing is { Status: EnrolmentStatus.Active } && string.Equals(existing.AcademicYear, currentYear, StringComparison.Ordinal);
            var hasResult = await GradeQueries.WithResults(db).AnyAsync(g => g.StudentId == studentId && g.ModuleId == module.Id, cancellationToken);
            if (!current && hasResult)
            {
                return await scope.FailAsync(EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.ResultsExist)), cancellationToken);
            }

            // 4. This year's credits in the module's semester.
            var credits = await (
                from e in db.Enrolments.AsNoTracking()
                join m in db.Modules.AsNoTracking() on e.ModuleId equals m.Id
                where e.StudentId == studentId && e.Status == EnrolmentStatus.Active && e.AcademicYear == currentYear && m.Semester == module.Semester
                select m.Credits).SumAsync(cancellationToken);

            // 5. The pure policy; step 8 below is the authority on capacity.
            var decision = EnrolmentRules.Evaluate(module, module.EnrolledCount, credits, alreadyEnrolled: current, windowOpen, ignoreCreditLimit: options.Override);
            if (decision == EnrolmentDecision.ModuleFull && options.ForceCapacity)
            {
                decision = EnrolmentDecision.Accepted;
            }

            if (decision != EnrolmentDecision.Accepted)
            {
                return await scope.FailAsync(EnrolmentResult.Fail(Refusal(decision, module, window, credits)), cancellationToken);
            }

            // 6. Reactivate the existing row (withdrawn, or active in an earlier year without a result) or insert one;
            // either way it is stamped with the current academic year (D28). A row from an earlier year takes this
            // year's marks from scratch: its Draft grade (the only kind results-exist lets through) is deleted with it.
            var source = options.Override ? EnrolmentSource.Admin : EnrolmentSource.Self;
            Guid? createdBy = options.Override ? actorUserId : null;
            Guid enrolmentId;
            var reactivated = existing is not null;
            var discardedDraft = false;
            if (existing is not null)
            {
                var updated = await db.Enrolments
                    .Where(e => e.Id == existing.Id && (e.Status == EnrolmentStatus.Withdrawn || e.AcademicYear != currentYear))
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(e => e.Status, EnrolmentStatus.Active)
                              .SetProperty(e => e.EnrolledAt, now)
                              .SetProperty(e => e.WithdrawnAt, (DateTimeOffset?)null)
                              .SetProperty(e => e.Source, source)
                              .SetProperty(e => e.AcademicYear, currentYear)
                              .SetProperty(e => e.CreatedByUserId, createdBy)
                              .SetProperty(e => e.UpdatedAt, now),
                        cancellationToken);
                if (updated == 0)
                {
                    // Lost a race for the same row.
                    return await scope.FailAsync(EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.AlreadyEnrolled)), cancellationToken);
                }

                if (!string.Equals(existing.AcademicYear, currentYear, StringComparison.Ordinal))
                {
                    discardedDraft = await db.Grades
                        .Where(g => g.StudentId == studentId && g.ModuleId == module.Id && g.Status == GradeStatus.Draft)
                        .ExecuteDeleteAsync(cancellationToken) > 0;
                }

                enrolmentId = existing.Id;
            }
            else
            {
                var enrolment = new Enrolment
                {
                    Id = Guid.CreateVersion7(),
                    StudentId = studentId,
                    ModuleId = module.Id,
                    EnrolledAt = now,
                    Status = EnrolmentStatus.Active,
                    Source = source,
                    AcademicYear = currentYear,
                    CreatedByUserId = createdBy,
                };
                db.Enrolments.Add(enrolment);
                tracked.Add(enrolment);
                enrolmentId = enrolment.Id;
            }

            // 7. The audit row goes in with the insert (one batch), before the claim. Only a forced override's row
            // depends on the claim (capacityRaised), so the claiming statement inserts that one itself (step 8).
            var details = CreatedDetails(module.Code, currentYear, options, reactivated, discardedDraft);
            if (!options.ForceCapacity)
            {
                if (options.Override)
                {
                    details["capacityRaised"] = false;
                }

                tracked.Add(audit.Record(
                    db,
                    options.Override ? AuditActions.EnrolmentAdminCreated : AuditActions.EnrolmentCreated,
                    AuditSubjects.Enrolment,
                    enrolmentId.ToString(),
                    details,
                    studentId,
                    module.Id));
            }

            if (tracked.Count > 0)
            {
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception) when (IsEnrolmentUniqueViolation(exception))
                {
                    // Two requests for the same (student, module) that both saw no row: the unique index decides.
                    Detach(tracked);
                    return await scope.FailAsync(EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.AlreadyEnrolled)), cancellationToken);
                }
            }

            // 8. Claim the place last, so the hot module row is locked only for this statement and the commit.
            var claim = options.ForceCapacity
                ? await ForceClaimPlaceAsync(module, studentId, enrolmentId, details, options.Reason, now, cancellationToken)
                : await ClaimPlaceAsync(module.Id, now, cancellationToken);
            if (claim is null)
            {
                Detach(tracked);
                await scope.RollbackAsync(cancellationToken);

                // No row: full, or deactivated after the fast path (the claim re-checks is_active). Asked after the
                // rollback, so it costs a claim loser no lock time.
                var stillActive = await db.Modules.AsNoTracking().Where(m => m.Id == module.Id).Select(m => m.IsActive).SingleOrDefaultAsync(cancellationToken);
                return EnrolmentResult.Fail(new EnrolmentFailure(stillActive ? EnrolmentError.ModuleFull : EnrolmentError.ModuleInactive));
            }

            await scope.CompleteAsync(cancellationToken);
            return EnrolmentResult.Success(new EnrolmentReceipt(
                enrolmentId,
                module.Id,
                module.Code,
                currentYear,
                now,
                Math.Max(0, claim.Capacity - claim.EnrolledCount),
                reactivated,
                claim.Raised));
        }
        catch
        {
            Detach(tracked);
            throw;
        }
    }

    /// <summary>
    /// <c>SELECT ... FROM students WHERE id = @student FOR NO KEY UPDATE</c>: null when the student does not exist. The
    /// mode serialises this student's enrolment requests without blocking the <c>FOR KEY SHARE</c> of foreign-key checks
    /// (a lecturer saving this student's grade, an account insert). Run as an uncomposed raw query, so EF sends it
    /// exactly as written.
    /// </summary>
    private async Task<StudentLock?> LockStudentAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQuery<bool>($"SELECT (left_at IS NOT NULL) AS \"Value\" FROM students WHERE id = {studentId} FOR NO KEY UPDATE")
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : new StudentLock(rows[0]);
    }

    /// <summary><see cref="LockAcademicYearSql"/>; <paramref name="fallback"/> only before the backfills have created the row.</summary>
    private async Task<string> LockAcademicYearAsync(string fallback, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<string>(LockAcademicYearSql).ToListAsync(cancellationToken);
        return rows.Count == 0 ? fallback : rows[0];
    }

    /// <summary>
    /// Step 8 as an <see cref="NpgsqlCommand"/> on the transaction's connection: EF's <c>SqlQuery</c> would wrap the
    /// data-modifying statement in a subquery, which PostgreSQL rejects, and <c>ExecuteSql</c> returns only a count.
    /// </summary>
    private async Task<PlaceClaim?> ClaimPlaceAsync(Guid moduleId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = Command(ClaimPlaceSql);
        command.Parameters.Add(new NpgsqlParameter("module", moduleId));
        command.Parameters.Add(new NpgsqlParameter("now", now));
        return await ReadClaimAsync(command, cancellationToken);
    }

    /// <summary>
    /// The forced claim with its audit rows (<see cref="ForceClaimPlaceAuditedSql"/>). The rows are built by
    /// <see cref="AuditWriter"/> (actor, request, address hash) and detached at once: the statement inserts them.
    /// </summary>
    private async Task<PlaceClaim?> ForceClaimPlaceAsync(Module module, Guid studentId, Guid enrolmentId, Dictionary<string, object?> details, string? reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var created = audit.Record(db, AuditActions.EnrolmentAdminCreated, AuditSubjects.Enrolment, enrolmentId.ToString(), details, studentId, module.Id);
        var raised = audit.Record(db, AuditActions.ModuleUpdated, AuditSubjects.Module, module.Code, null, moduleId: module.Id);
        db.Entry(created).State = EntityState.Detached;
        db.Entry(raised).State = EntityState.Detached;

        await using var command = Command(ForceClaimPlaceAuditedSql);
        var p = command.Parameters;
        p.Add(Typed("module", module.Id, NpgsqlDbType.Uuid));
        p.Add(Typed("now", now, NpgsqlDbType.TimestampTz));
        p.Add(Typed("student", studentId, NpgsqlDbType.Uuid));
        p.Add(Typed("actor_user_id", created.ActorUserId, NpgsqlDbType.Uuid));
        p.Add(Typed("actor_username", created.ActorUsername, NpgsqlDbType.Varchar));
        p.Add(Typed("actor_role", created.ActorRole, NpgsqlDbType.Varchar));
        p.Add(Typed("request_id", created.RequestId, NpgsqlDbType.Varchar));
        p.Add(Typed("ip_hash", created.IpHash, NpgsqlDbType.Varchar));
        p.Add(Typed("created_id", created.Id, NpgsqlDbType.Uuid));
        p.Add(Typed("created_at", created.OccurredAt, NpgsqlDbType.TimestampTz));
        p.Add(Typed("created_action", created.Action, NpgsqlDbType.Varchar));
        p.Add(Typed("created_subject_type", created.SubjectType, NpgsqlDbType.Varchar));
        p.Add(Typed("created_subject_id", created.SubjectId, NpgsqlDbType.Varchar));
        p.Add(Typed("created_details", created.Details, NpgsqlDbType.Text));
        p.Add(Typed("raised_id", raised.Id, NpgsqlDbType.Uuid));
        p.Add(Typed("raised_at", raised.OccurredAt, NpgsqlDbType.TimestampTz));
        p.Add(Typed("raised_action", raised.Action, NpgsqlDbType.Varchar));
        p.Add(Typed("raised_subject_type", raised.SubjectType, NpgsqlDbType.Varchar));
        p.Add(Typed("raised_subject_id", raised.SubjectId, NpgsqlDbType.Varchar));
        p.Add(Typed("reason", reason, NpgsqlDbType.Text));
        return await ReadClaimAsync(command, cancellationToken);
    }

    private static async Task<PlaceClaim?> ReadClaimAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PlaceClaim(reader.GetInt32(0), reader.GetInt32(1), reader.GetBoolean(2), reader.GetInt32(3));
    }

    /// <summary>A raw command on the current transaction's connection (the statements EF cannot send as written).</summary>
    private NpgsqlCommand Command(string sql)
    {
        var command = (NpgsqlCommand)db.Database.GetDbConnection().CreateCommand();
        command.Transaction = (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        return command;
    }

    private static NpgsqlParameter Typed(string name, object? value, NpgsqlDbType type) => new(name, type) { Value = value ?? DBNull.Value };

    private void LogDrift(string moduleCode, int withdrawnRows, int countBefore) =>
        logger.LogWarning(
            CountDriftEvent,
            "enrolled_count of {ModuleCode} was {CountBefore} when {WithdrawnRows} current-year enrolment(s) were withdrawn: the count had drifted from the active rows. POST /api/admin/ops/reconcile repairs it.",
            moduleCode,
            countBefore,
            withdrawnRows);

    private static Dictionary<string, object?> CreatedDetails(string moduleCode, string academicYear, EnrolOptions options, bool reactivated, bool discardedDraft)
    {
        var details = options.Override
            ? new Dictionary<string, object?>
            {
                ["moduleCode"] = moduleCode,
                ["academicYear"] = academicYear,
                ["reason"] = options.Reason,
                ["override"] = true,
                ["forceCapacity"] = options.ForceCapacity,
            }
            : new Dictionary<string, object?>
            {
                ["moduleCode"] = moduleCode,
                ["academicYear"] = academicYear,
                ["source"] = "self",
                ["reactivated"] = reactivated,
            };
        if (discardedDraft)
        {
            details["discardedDraft"] = true;
        }

        return details;
    }

    private static EnrolmentFailure Refusal(EnrolmentDecision decision, Module module, EnrolmentWindowSnapshot? window, int credits) => decision switch
    {
        EnrolmentDecision.AlreadyEnrolled => new EnrolmentFailure(EnrolmentError.AlreadyEnrolled),
        EnrolmentDecision.WindowClosed => new EnrolmentFailure(EnrolmentError.WindowClosed, module.Semester, window?.OpensAt, window?.ClosesAt),
        EnrolmentDecision.CreditLimitExceeded => new EnrolmentFailure(
            EnrolmentError.CreditLimitExceeded,
            module.Semester,
            CurrentCredits: credits,
            ModuleCredits: module.Credits,
            Limit: EnrolmentRules.MaxCreditsPerSemester),
        EnrolmentDecision.ModuleFull => new EnrolmentFailure(EnrolmentError.ModuleFull),
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Not a refusal."),
    };

    private static Dictionary<string, object?> AdminWithdrawnDetails(string moduleCode, string academicYear, WithdrawOptions options)
    {
        var details = new Dictionary<string, object?>
        {
            ["moduleCode"] = moduleCode,
            ["academicYear"] = academicYear,
            ["reason"] = options.Reason,
            ["override"] = true,
        };
        if (options.Left)
        {
            details["left"] = true;
        }

        if (options.Trim)
        {
            details["trim"] = true;
        }

        return details;
    }

    private static bool IsRejectionReason(EnrolmentError error) => error is EnrolmentError.ModuleFull
        or EnrolmentError.AlreadyEnrolled
        or EnrolmentError.WindowClosed
        or EnrolmentError.CreditLimitExceeded
        or EnrolmentError.ResultsExist
        or EnrolmentError.StudentLeft
        or EnrolmentError.ModuleInactive;

    /// <summary>
    /// A 23505 on the (student, module) index only: a unique violation of any other row in the same batch (the audit
    /// row) is a failure, not "already enrolled".
    /// </summary>
    private static bool IsEnrolmentUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: EnrolmentUniqueIndex };

    private static string NormaliseCode(string moduleCode)
    {
        ArgumentNullException.ThrowIfNull(moduleCode);
        return moduleCode.Trim().ToUpperInvariant();
    }

    private void Detach(List<object> entities)
    {
        foreach (var entity in entities)
        {
            db.Entry(entity).State = EntityState.Detached;
        }

        entities.Clear();
    }

    private sealed record StudentLock(bool Left);

    private sealed record PlaceClaim(int Capacity, int EnrolledCount, bool Raised, int PreviousCapacity);

    /// <summary>
    /// The transaction of one enrolment or withdrawal: its own READ COMMITTED transaction, or a savepoint inside the
    /// caller's (so a refusal or a unique violation undoes only this call's work). EF's automatic savepoints are off
    /// for the duration: they would add two round trips to every save on the hot path, and a failure here rolls back
    /// the whole unit anyway.
    /// </summary>
    private sealed class UnitOfWork : IAsyncDisposable
    {
        private readonly RushDayDbContext _db;
        private readonly IDbContextTransaction? _own;
        private readonly IDbContextTransaction? _ambient;
        private readonly bool _autoSavepoints;
        private bool _finished;

        private UnitOfWork(RushDayDbContext db, IDbContextTransaction? own, IDbContextTransaction? ambient, bool autoSavepoints)
        {
            _db = db;
            _own = own;
            _ambient = ambient;
            _autoSavepoints = autoSavepoints;
        }

        public static async Task<UnitOfWork> BeginAsync(RushDayDbContext db, CancellationToken cancellationToken)
        {
            var autoSavepoints = db.Database.AutoSavepointsEnabled;
            db.Database.AutoSavepointsEnabled = false;
            var ambient = db.Database.CurrentTransaction;
            if (ambient is not null)
            {
                await ambient.CreateSavepointAsync(SavepointName, cancellationToken);
                return new UnitOfWork(db, null, ambient, autoSavepoints);
            }

            var own = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            return new UnitOfWork(db, own, null, autoSavepoints);
        }

        public async Task<T> FailAsync<T>(T result, CancellationToken cancellationToken)
        {
            await RollbackAsync(cancellationToken);
            return result;
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            if (_own is not null)
            {
                await _own.CommitAsync(cancellationToken);
            }
            else
            {
                await _ambient!.ReleaseSavepointAsync(SavepointName, cancellationToken);
            }

            _finished = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            _finished = true;
            if (_own is not null)
            {
                await _own.RollbackAsync(cancellationToken);
            }
            else
            {
                await _ambient!.RollbackToSavepointAsync(SavepointName, cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_finished && _ambient is not null)
                {
                    // An exception escaped: undo this unit's work inside the caller's transaction. If even that fails
                    // (a broken connection), the caller's transaction is lost anyway and the original exception wins.
                    try
                    {
                        await _ambient.RollbackToSavepointAsync(SavepointName, CancellationToken.None);
                    }
                    catch (Exception exception) when (exception is DbException or InvalidOperationException)
                    {
                        // Ignored on purpose, see above.
                    }
                }
            }
            finally
            {
                // A disposed transaction that was never committed rolls back by itself.
                if (_own is not null)
                {
                    await _own.DisposeAsync();
                }

                _db.Database.AutoSavepointsEnabled = _autoSavepoints;
            }
        }
    }
}
