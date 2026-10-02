using Microsoft.EntityFrameworkCore;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Enrolments;

/// <summary>Why a window mutation was refused.</summary>
public enum WindowError
{
    None,
    WindowNotFound,
    WindowExists,
    WindowDatesInvalid,
}

/// <summary>The three instants of a window; valid iff <c>opens_at &lt; closes_at &lt;= withdrawal_deadline_at</c>.</summary>
public sealed record WindowDates(DateTimeOffset OpensAt, DateTimeOffset ClosesAt, DateTimeOffset WithdrawalDeadlineAt)
{
    /// <summary>The CHECK <c>ck_enrolment_windows_order</c>, answered as 422 <c>window-dates-invalid</c> before the database would refuse it.</summary>
    public bool IsValid => OpensAt < ClosesAt && ClosesAt <= WithdrawalDeadlineAt;
}

public sealed record WindowResult(EnrolmentWindowSnapshot? Window, WindowError Error)
{
    public bool Succeeded => Error == WindowError.None;
}

/// <summary>
/// The administrator's enrolment windows (02-api.md section 8.5): one per (academic year, semester). Reads go to the
/// table (an administrator sees a change at once); every mutation audits <c>window.created|updated|deleted</c> and
/// the caller invalidates <c>windows:all</c>.
/// </summary>
public sealed class EnrolmentWindowAdminService(RushDayDbContext db, AuditWriter audit, TimeProvider clock)
{
    /// <summary>Every window, newest academic year first, autumn before spring.</summary>
    public async Task<IReadOnlyList<EnrolmentWindowSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.EnrolmentWindows.AsNoTracking()
            .OrderByDescending(w => w.AcademicYear)
            .ThenBy(w => w.Semester)
            .Select(w => new EnrolmentWindowSnapshot(w.Id, w.AcademicYear, w.Semester, w.OpensAt, w.ClosesAt, w.WithdrawalDeadlineAt))
            .ToListAsync(cancellationToken);

    public async Task<WindowResult> CreateAsync(string academicYear, Semester semester, WindowDates dates, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(academicYear);
        ArgumentNullException.ThrowIfNull(dates);

        if (!dates.IsValid)
        {
            return new WindowResult(null, WindowError.WindowDatesInvalid);
        }

        if (await db.EnrolmentWindows.AnyAsync(w => w.AcademicYear == academicYear && w.Semester == semester, cancellationToken))
        {
            return new WindowResult(null, WindowError.WindowExists);
        }

        var window = new EnrolmentWindow
        {
            Id = Guid.CreateVersion7(),
            AcademicYear = academicYear,
            Semester = semester,
            OpensAt = dates.OpensAt,
            ClosesAt = dates.ClosesAt,
            WithdrawalDeadlineAt = dates.WithdrawalDeadlineAt,
            CreatedByUserId = actorUserId,
            UpdatedAt = clock.GetUtcNow(),
        };
        db.EnrolmentWindows.Add(window);
        var entry = Audit(AuditActions.WindowCreated, window, before: null);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two administrators created the same (year, semester) at once: the unique index decides.
            db.Entry(window).State = EntityState.Detached;
            db.Entry(entry).State = EntityState.Detached;
            return new WindowResult(null, WindowError.WindowExists);
        }

        return new WindowResult(Snapshot(window), WindowError.None);
    }

    public async Task<WindowResult> UpdateAsync(Guid id, WindowDates dates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);

        var window = await db.EnrolmentWindows.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (window is null)
        {
            return new WindowResult(null, WindowError.WindowNotFound);
        }

        if (!dates.IsValid)
        {
            return new WindowResult(null, WindowError.WindowDatesInvalid);
        }

        var before = Dates(window);
        window.OpensAt = dates.OpensAt;
        window.ClosesAt = dates.ClosesAt;
        window.WithdrawalDeadlineAt = dates.WithdrawalDeadlineAt;
        window.UpdatedAt = clock.GetUtcNow();
        Audit(AuditActions.WindowUpdated, window, before);
        await db.SaveChangesAsync(cancellationToken);

        return new WindowResult(Snapshot(window), WindowError.None);
    }

    public async Task<WindowResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var window = await db.EnrolmentWindows.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (window is null)
        {
            return new WindowResult(null, WindowError.WindowNotFound);
        }

        db.EnrolmentWindows.Remove(window);
        audit.Record(
            db,
            AuditActions.WindowDeleted,
            AuditSubjects.Window,
            window.Id.ToString(),
            new { academicYear = window.AcademicYear, semester = GradeNames.Of(window.Semester), before = Dates(window), after = (object?)null });
        await db.SaveChangesAsync(cancellationToken);

        return new WindowResult(Snapshot(window), WindowError.None);
    }

    private Domain.Audit.AuditEvent Audit(string action, EnrolmentWindow window, object? before) =>
        audit.Record(
            db,
            action,
            AuditSubjects.Window,
            window.Id.ToString(),
            new { academicYear = window.AcademicYear, semester = GradeNames.Of(window.Semester), before, after = Dates(window) });

    private static object Dates(EnrolmentWindow window) => new
    {
        opensAt = GradeNames.Instant(window.OpensAt),
        closesAt = GradeNames.Instant(window.ClosesAt),
        withdrawalDeadlineAt = GradeNames.Instant(window.WithdrawalDeadlineAt),
    };

    private static EnrolmentWindowSnapshot Snapshot(EnrolmentWindow w) =>
        new(w.Id, w.AcademicYear, w.Semester, w.OpensAt, w.ClosesAt, w.WithdrawalDeadlineAt);
}
