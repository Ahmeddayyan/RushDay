using Microsoft.EntityFrameworkCore;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;

namespace RushDay.Infrastructure.Modules;

/// <summary>Why a module mutation was refused.</summary>
public enum ModuleAdminError
{
    None,
    ModuleNotFound,
    ModuleCodeTaken,
    LecturerNotFound,
    CapacityBelowEnrolled,
    SemesterChangeWithEnrolments,
    InvalidLecturerAssignment,
}

/// <summary>A module's editable fields.</summary>
public sealed record ModuleChange(string Title, string? Description, int Credits, int Capacity, Semester Semester, bool IsActive);

public sealed record LecturerAssignment(string StaffNumber, ModuleLecturerRole Role);

/// <summary>An outcome; <see cref="EnrolledCount"/> fills the extension of the two capacity and semester refusals.</summary>
public sealed record ModuleAdminResult<T>(T? Value, ModuleAdminError Error, int? EnrolledCount = null)
{
    public bool Succeeded => Error == ModuleAdminError.None;

    public static ModuleAdminResult<T> Success(T value) => new(value, ModuleAdminError.None);

    public static ModuleAdminResult<T> Fail(ModuleAdminError error, int? enrolledCount = null) => new(default, error, enrolledCount);
}

/// <summary>The lecturers whose <c>lecturer-modules:{id}</c> entry must be dropped after an assignment change.</summary>
public sealed record LecturersSet(string Code, IReadOnlyList<Guid> AffectedLecturerIds);

public sealed record TrimOutcome(string Code, int Capacity, int Before, int After, IReadOnlyList<string> Withdrawn);

/// <summary>One module of <c>GET /api/admin/modules</c>: the catalogue shape plus this year's marks status.</summary>
public sealed record AdminModuleData(CatalogueModule Module, MarksStatusData Marks);

/// <summary>
/// The registry's module administration (02-api.md section 8.5): the list (inactive modules on request) with this
/// year's marks status, create, update with the capacity rule (only a request that <b>lowers</b> capacity below
/// <c>enrolled_count</c> is refused; an unchanged capacity is accepted even on an oversold module) and the semester
/// rule (not while students hold places), lecturer assignment (exactly one leader, no duplicates, nobody who has
/// left), and trim to capacity. Every mutation audits in its transaction; the caller invalidates <c>catalogue:all</c>
/// and, for assignments, the affected lecturers' <c>lecturer-modules</c> entries.
/// </summary>
public sealed class ModuleAdminService(
    RushDayDbContext db,
    EnrolmentService enrolments,
    EnrolmentWindowService windows,
    AuditWriter audit,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<AdminModuleData>> ListAsync(bool includeInactive, string academicYear, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var modules = db.Modules.AsNoTracking();
        if (!includeInactive)
        {
            modules = modules.Where(m => m.IsActive);
        }

        var rows = await StaffModules.CatalogueModulesAsync(db, modules, cancellationToken);
        var statuses = await MarksStatusQuery.ForModulesAsync(db, academicYear, now, moduleIds: null, cancellationToken);
        return [.. rows.Select(m => new AdminModuleData(m, MarksStatusQuery.Of(statuses, m.Id)))];
    }

    public async Task<ModuleAdminResult<string>> CreateAsync(string code, ModuleChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var normalised = StaffModules.NormaliseCode(code);

        if (await db.Modules.AnyAsync(m => m.Code == normalised, cancellationToken))
        {
            return ModuleAdminResult<string>.Fail(ModuleAdminError.ModuleCodeTaken);
        }

        var module = new Module
        {
            Id = Guid.CreateVersion7(),
            Code = normalised,
            Department = normalised[..2],
            Title = change.Title,
            Description = change.Description,
            Credits = change.Credits,
            Capacity = change.Capacity,
            Semester = change.Semester,
            IsActive = true,
            UpdatedAt = clock.GetUtcNow(),
        };
        db.Modules.Add(module);
        var entry = audit.Record(db, AuditActions.ModuleCreated, AuditSubjects.Module, module.Code, new { before = (object?)null, after = Snapshot(module) }, moduleId: module.Id);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(module).State = EntityState.Detached;
            db.Entry(entry).State = EntityState.Detached;
            return ModuleAdminResult<string>.Fail(ModuleAdminError.ModuleCodeTaken);
        }

        return ModuleAdminResult<string>.Success(module.Code);
    }

    public async Task<ModuleAdminResult<string>> UpdateAsync(string code, ModuleChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var normalised = StaffModules.NormaliseCode(code);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The row lock makes enrolled_count exact for the two rules: an enrolment claiming a place waits for us.
        var rows = await db.Modules.FromSql($"SELECT * FROM modules WHERE code = {normalised} FOR UPDATE").ToListAsync(cancellationToken);
        var module = rows.SingleOrDefault();
        if (module is null)
        {
            return ModuleAdminResult<string>.Fail(ModuleAdminError.ModuleNotFound);
        }

        if (change.Capacity < module.Capacity && change.Capacity < module.EnrolledCount)
        {
            return ModuleAdminResult<string>.Fail(ModuleAdminError.CapacityBelowEnrolled, module.EnrolledCount);
        }

        if (change.Semester != module.Semester && module.EnrolledCount > 0)
        {
            return ModuleAdminResult<string>.Fail(ModuleAdminError.SemesterChangeWithEnrolments, module.EnrolledCount);
        }

        var before = Snapshot(module);
        module.Title = change.Title;
        module.Description = change.Description;
        module.Credits = change.Credits;
        module.Capacity = change.Capacity;
        module.Semester = change.Semester;
        module.IsActive = change.IsActive;
        module.UpdatedAt = clock.GetUtcNow();

        audit.Record(db, AuditActions.ModuleUpdated, AuditSubjects.Module, module.Code, new { before, after = Snapshot(module) }, moduleId: module.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ModuleAdminResult<string>.Success(module.Code);
    }

    /// <summary>Replaces the module's assignments; <c>role</c> changes keep the original <c>assigned_at</c>.</summary>
    public async Task<ModuleAdminResult<LecturersSet>> SetLecturersAsync(string code, IReadOnlyList<LecturerAssignment> assignments, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        var module = await StaffModules.FindAsync(db, code, cancellationToken);
        if (module is not { } found)
        {
            return ModuleAdminResult<LecturersSet>.Fail(ModuleAdminError.ModuleNotFound);
        }

        var requested = assignments.Select(a => a with { StaffNumber = a.StaffNumber.Trim().ToUpperInvariant() }).ToList();
        var numbers = requested.Select(a => a.StaffNumber).Distinct(StringComparer.Ordinal).ToArray();
        var lecturers = await db.Lecturers.AsNoTracking()
            .Where(l => numbers.Contains(l.StaffNumber))
            .Select(l => new { l.Id, l.StaffNumber, l.LeftAt })
            .ToDictionaryAsync(l => l.StaffNumber, StringComparer.Ordinal, cancellationToken);
        if (numbers.Any(n => !lecturers.ContainsKey(n)))
        {
            return ModuleAdminResult<LecturersSet>.Fail(ModuleAdminError.LecturerNotFound);
        }

        if (numbers.Length != requested.Count
            || requested.Count(a => a.Role == ModuleLecturerRole.Leader) != 1
            || lecturers.Values.Any(l => l.LeftAt is not null))
        {
            return ModuleAdminResult<LecturersSet>.Fail(ModuleAdminError.InvalidLecturerAssignment);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var current = await db.ModuleLecturers.Where(ml => ml.ModuleId == found.Id).ToListAsync(cancellationToken);
        var staffById = await db.Lecturers.AsNoTracking()
            .Where(l => current.Select(c => c.LecturerId).Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.StaffNumber, cancellationToken);
        var before = Ordered(current.Select(c => (staffById.GetValueOrDefault(c.LecturerId) ?? c.LecturerId.ToString(), c.Role)));

        var now = clock.GetUtcNow();
        var wanted = requested.ToDictionary(a => lecturers[a.StaffNumber].Id, a => a.Role);
        foreach (var existing in current)
        {
            if (!wanted.TryGetValue(existing.LecturerId, out var role))
            {
                db.ModuleLecturers.Remove(existing);
            }
            else if (existing.Role != role)
            {
                existing.Role = role;
            }
        }

        foreach (var (lecturerId, role) in wanted)
        {
            if (current.All(c => c.LecturerId != lecturerId))
            {
                db.ModuleLecturers.Add(new ModuleLecturer
                {
                    ModuleId = found.Id,
                    LecturerId = lecturerId,
                    Role = role,
                    AssignedAt = now,
                    AssignedByUserId = actorUserId,
                });
            }
        }

        var after = Ordered(requested.Select(a => (a.StaffNumber, a.Role)));
        audit.Record(db, AuditActions.ModuleLecturersSet, AuditSubjects.Module, found.Code, new { before, after }, moduleId: found.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var affected = current.Select(c => c.LecturerId).Concat(wanted.Keys).Distinct().ToList();
        return ModuleAdminResult<LecturersSet>.Success(new LecturersSet(found.Code, affected));
    }

    /// <summary>
    /// Withdraws this year's active enrolments, latest <c>enrolled_at</c> first, until <c>enrolled_count</c> equals
    /// capacity (<see cref="WithdrawForTrimAsync"/>, inside this transaction), then audits <c>module.trimmed</c>.
    /// </summary>
    public async Task<ModuleAdminResult<TrimOutcome>> TrimAsync(string code, string reason, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var module = await StaffModules.FindAsync(db, code, cancellationToken);
        if (module is not { } found)
        {
            return ModuleAdminResult<TrimOutcome>.Fail(ModuleAdminError.ModuleNotFound);
        }

        var calendar = await windows.CurrentAsync(cancellationToken);
        var year = calendar.AcademicYear;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var start = await CountsAsync(found.Id, cancellationToken);
        var excess = Math.Max(0, start.EnrolledCount - start.Capacity);
        var latest = await (
            from e in db.Enrolments.AsNoTracking()
            join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
            where e.ModuleId == found.Id && e.Status == EnrolmentStatus.Active && e.AcademicYear == year
            orderby e.EnrolledAt descending, s.StudentNumber descending
            select new TrimCandidate(e.StudentId, s.StudentNumber))
            .Take(excess)
            .ToListAsync(cancellationToken);

        var withdrawn = await WithdrawForTrimAsync(found.Code, latest, reason, actorUserId, cancellationToken);
        var end = await CountsAsync(found.Id, cancellationToken);

        audit.Record(db, AuditActions.ModuleTrimmed, AuditSubjects.Module, found.Code, new { reason, withdrawn }, moduleId: found.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ModuleAdminResult<TrimOutcome>.Success(new TrimOutcome(found.Code, end.Capacity, start.EnrolledCount, end.EnrolledCount, withdrawn));
    }

    /// <summary>
    /// The one place trim withdraws enrolments (an administrator's override withdrawal per row, each audited
    /// <c>enrolment.admin_withdrawn</c> with <c>trim: true</c>), inside the caller's transaction; returns the student
    /// numbers withdrawn, in the order given.
    /// </summary>
    private async Task<IReadOnlyList<string>> WithdrawForTrimAsync(string moduleCode, IReadOnlyList<TrimCandidate> candidates, string reason, Guid actorUserId, CancellationToken cancellationToken)
    {
        var withdrawn = new List<string>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var result = await enrolments.WithdrawAsync(
                candidate.StudentId,
                moduleCode,
                actorUserId,
                new WithdrawOptions(Override: true, Reason: reason, Trim: true),
                cancellationToken);
            if (result.Succeeded)
            {
                withdrawn.Add(candidate.StudentNumber);
            }
        }

        return withdrawn;
    }

    private sealed record TrimCandidate(Guid StudentId, string StudentNumber);

    private async Task<(int Capacity, int EnrolledCount)> CountsAsync(Guid moduleId, CancellationToken cancellationToken)
    {
        var row = await db.Modules.AsNoTracking().Where(m => m.Id == moduleId).Select(m => new { m.Capacity, m.EnrolledCount }).SingleAsync(cancellationToken);
        return (row.Capacity, row.EnrolledCount);
    }

    private static string[] Ordered(IEnumerable<(string StaffNumber, ModuleLecturerRole Role)> assignments) =>
        [.. assignments.OrderBy(a => a.Role).ThenBy(a => a.StaffNumber, StringComparer.Ordinal).Select(a => a.StaffNumber)];

    private static object Snapshot(Module module) => new
    {
        title = module.Title,
        description = module.Description,
        credits = module.Credits,
        capacity = module.Capacity,
        semester = GradeNames.Of(module.Semester),
        isActive = module.IsActive,
    };
}
