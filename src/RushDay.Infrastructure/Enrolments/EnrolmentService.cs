using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;

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

/// <summary>A completed withdrawal; <see cref="CountDecremented"/> is false for an earlier year's row (it held no place this year).</summary>
public sealed record WithdrawalReceipt(Guid EnrolmentId, Guid ModuleId, string ModuleCode, string AcademicYear, DateTimeOffset WithdrawnAt, bool CountDecremented);

public sealed record WithdrawalResult(WithdrawalReceipt? Receipt, EnrolmentFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static WithdrawalResult Success(WithdrawalReceipt receipt) => new(receipt, null);

    public static WithdrawalResult Fail(EnrolmentFailure failure) => new(null, failure);
}

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
/// (student, module) index and the conditional reactivation make duplicate rows impossible. Lock order is always
/// student → enrolment → module, in enrolment and withdrawal alike, so the two never deadlock. Each call runs in its
/// own transaction, or joins the caller's under a savepoint (the administrator's trim and leave loops).
/// </summary>
public sealed class EnrolmentService(
    RushDayDbContext db,
    EnrolmentWindowService windows,
    AuditWriter audit,
    IEnrolmentMetrics metrics,
    TimeProvider clock)
{
    /// <summary>Step 8 without <c>ForceCapacity</c>: the single write path of <c>enrolled_count</c> upward.</summary>
    public const string ClaimPlaceSql =
        "UPDATE modules SET enrolled_count = enrolled_count + 1, updated_at = @now WHERE id = @module AND enrolled_count < capacity " +
        "RETURNING capacity, enrolled_count, false AS raised, capacity AS previous_capacity";

    /// <summary>Step 8 with <c>ForceCapacity</c>: raises capacity only when the module is full (02-api.md section 8.5).</summary>
    public const string ForceClaimPlaceSql =
        "WITH before AS (SELECT capacity AS c FROM modules WHERE id = @module FOR UPDATE) " +
        "UPDATE modules m SET enrolled_count = m.enrolled_count + 1, " +
        "capacity = CASE WHEN m.enrolled_count >= m.capacity THEN m.enrolled_count + 1 ELSE m.capacity END, updated_at = @now " +
        "FROM before WHERE m.id = @module " +
        "RETURNING m.capacity, m.enrolled_count, m.capacity <> before.c AS raised, before.c AS previous_capacity";

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

    /// <summary>Withdraws a student's active enrolment on a module (04 section 2.2).</summary>
    public async Task<WithdrawalResult> WithdrawAsync(Guid studentId, string moduleCode, Guid? actorUserId, WithdrawOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var code = NormaliseCode(moduleCode);
        var calendar = await windows.CurrentAsync(cancellationToken);
        var module = await db.Modules.AsNoTracking()
            .Where(m => m.Code == code)
            .Select(m => new { m.Id, m.Code, m.Semester })
            .SingleOrDefaultAsync(cancellationToken);
        if (module is null)
        {
            // Nobody is enrolled on a module that does not exist.
            return WithdrawalResult.Fail(new EnrolmentFailure(EnrolmentError.NotEnrolled));
        }

        // Cached reads happen before the transaction, so a cache fill never runs while a row lock is held.
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

            // 2. The active row, of any year.
            var enrolment = await db.Enrolments.AsNoTracking()
                .Where(e => e.StudentId == studentId && e.ModuleId == module.Id && e.Status == EnrolmentStatus.Active)
                .Select(e => new { e.Id, e.AcademicYear })
                .SingleOrDefaultAsync(cancellationToken);
            if (enrolment is null)
            {
                return await scope.FailAsync(WithdrawalResult.Fail(new EnrolmentFailure(EnrolmentError.NotEnrolled)), cancellationToken);
            }

            var isCurrentYear = string.Equals(enrolment.AcademicYear, calendar.AcademicYear, StringComparison.Ordinal);

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

            // 5. Only a row of the current year holds one of this year's places (D28).
            if (isCurrentYear)
            {
                await db.Modules
                    .Where(m => m.Id == module.Id && m.EnrolledCount > 0)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(m => m.EnrolledCount, m => m.EnrolledCount - 1)
                              .SetProperty(m => m.UpdatedAt, now),
                        cancellationToken);
            }

            // 6. The audit row commits with the change.
            tracked.Add(options.Override
                ? audit.Record(db, AuditActions.EnrolmentAdminWithdrawn, AuditSubjects.Enrolment, enrolment.Id.ToString(), AdminWithdrawnDetails(module.Code, enrolment.AcademicYear, options), studentId, module.Id)
                : audit.Record(db, AuditActions.EnrolmentWithdrawn, AuditSubjects.Enrolment, enrolment.Id.ToString(), new { moduleCode = module.Code, academicYear = enrolment.AcademicYear }, studentId, module.Id));
            await db.SaveChangesAsync(cancellationToken);

            await scope.CompleteAsync(cancellationToken);
            return WithdrawalResult.Success(new WithdrawalReceipt(enrolment.Id, module.Id, module.Code, enrolment.AcademicYear, now, isCurrentYear));
        }
        catch
        {
            Detach(tracked);
            throw;
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

        // The calendar and the window come from caches, read before the transaction so no fill runs under a lock.
        var calendar = await windows.CurrentAsync(cancellationToken);
        var currentYear = calendar.AcademicYear;
        var window = await windows.FindAsync(currentYear, module.Semester, cancellationToken);
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

            // 2. The window of (current year, module semester); the override ignores it.
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
            // either way it is stamped with the current academic year (D28).
            var source = options.Override ? EnrolmentSource.Admin : EnrolmentSource.Self;
            Guid? createdBy = options.Override ? actorUserId : null;
            Guid enrolmentId;
            var reactivated = existing is not null;
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

            // 7. A self-enrolment's audit row goes in with the insert (one batch). The override's row names
            // capacityRaised, which only step 8 knows, so it is written after step 8 in the same transaction.
            if (!options.Override)
            {
                tracked.Add(audit.Record(
                    db,
                    AuditActions.EnrolmentCreated,
                    AuditSubjects.Enrolment,
                    enrolmentId.ToString(),
                    new { moduleCode = module.Code, academicYear = currentYear, source = "self", reactivated },
                    studentId,
                    module.Id));
            }

            if (tracked.Count > 0)
            {
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception) when (IsUniqueViolation(exception))
                {
                    // Two requests for the same (student, module) that both saw no row: the unique index decides.
                    Detach(tracked);
                    return await scope.FailAsync(EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.AlreadyEnrolled)), cancellationToken);
                }
            }

            // 8. Claim the place last, so the hot module row is locked only for this statement and the commit.
            var claim = await ClaimPlaceAsync(module.Id, now, options.ForceCapacity, cancellationToken);
            if (claim is null)
            {
                Detach(tracked);
                return await scope.FailAsync(EnrolmentResult.Fail(new EnrolmentFailure(EnrolmentError.ModuleFull)), cancellationToken);
            }

            if (options.Override)
            {
                tracked.Add(audit.Record(
                    db,
                    AuditActions.EnrolmentAdminCreated,
                    AuditSubjects.Enrolment,
                    enrolmentId.ToString(),
                    new { moduleCode = module.Code, academicYear = currentYear, reason = options.Reason, @override = true, forceCapacity = options.ForceCapacity, capacityRaised = claim.Raised },
                    studentId,
                    module.Id));
                if (claim.Raised)
                {
                    tracked.Add(audit.Record(
                        db,
                        AuditActions.ModuleUpdated,
                        AuditSubjects.Module,
                        module.Code,
                        new { capacity = new { before = claim.PreviousCapacity, after = claim.Capacity }, reason = options.Reason },
                        moduleId: module.Id));
                }

                await db.SaveChangesAsync(cancellationToken);
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
    /// <c>SELECT ... FROM students WHERE id = @student FOR UPDATE</c>: null when the student does not exist. Run as an
    /// uncomposed raw query, so EF sends it exactly as written.
    /// </summary>
    private async Task<StudentLock?> LockStudentAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQuery<bool>($"SELECT (left_at IS NOT NULL) AS \"Value\" FROM students WHERE id = {studentId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : new StudentLock(rows[0]);
    }

    /// <summary>
    /// Step 8 as an <see cref="NpgsqlCommand"/> on the transaction's connection: EF's <c>SqlQuery</c> would wrap the
    /// data-modifying statement in a subquery, which PostgreSQL rejects, and <c>ExecuteSql</c> returns only a count.
    /// </summary>
    private async Task<PlaceClaim?> ClaimPlaceAsync(Guid moduleId, DateTimeOffset now, bool forceCapacity, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = forceCapacity ? ForceClaimPlaceSql : ClaimPlaceSql;
        command.Parameters.Add(new NpgsqlParameter("module", moduleId));
        command.Parameters.Add(new NpgsqlParameter("now", now));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PlaceClaim(reader.GetInt32(0), reader.GetInt32(1), reader.GetBoolean(2), reader.GetInt32(3));
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

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

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

        private async Task RollbackAsync(CancellationToken cancellationToken)
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
    }
}
