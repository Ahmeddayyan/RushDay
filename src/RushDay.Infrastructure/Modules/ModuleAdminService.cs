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
/// rule (never once the module has had an enrolment or a grade in any year: it would move students' credits,
/// timetables and published results between semesters, review S6 E7), lecturer assignment (exactly one leader, no
/// duplicates, nobody who has left; serialised on the module row and backed by a unique index, E10), and trim to
/// capacity (counted from the real enrolments under lock and withdrawn through
/// <see cref="EnrolmentService.WithdrawManyAsync"/>, E4 and J1). Every mutation audits in its transaction; the caller
/// invalidates <c>catalogue:all</c> and, for assignments, the affected lecturers' <c>lecturer-modules</c> entries.
/// </summary>
public sealed class ModuleAdminService(
    RushDayDbContext db,
    EnrolmentService enrolments,
    EnrolmentWindowService windows,
    AuditWriter audit,
    TimeProvider clock)
{
    /// <summary>Every student holding one of the year's places on the module, locked in id order (the enrolment lock order).</summary>
    private const string LockModuleStudentsSql = """
        SELECT s.id FROM students s
        WHERE s.id IN (SELECT e.student_id FROM enrolments e WHERE e.module_id = @module AND e.status = 'Active' AND e.academic_year = @year)
        ORDER BY s.id
        FOR NO KEY UPDATE
        """;

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

        // FOR UPDATE, not NO KEY UPDATE: it also holds off the foreign-key check (FOR KEY SHARE) of any enrolment insert,
        // so neither rule below can be overtaken by a new enrolment row before this commits.
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

        if (change.Semester != module.Semester)
        {
            // Any enrolment of any year (active or withdrawn) or any grade pins the semester: results are grouped by
            // (enrolment year, module semester), so moving it would move earlier years' published marks (review S6 E7).
            var ever = await db.Enrolments.CountAsync(e => e.ModuleId == module.Id, cancellationToken);
            if (ever > 0 || await db.Grades.AnyAsync(g => g.ModuleId == module.Id, cancellationToken))
            {
                return ModuleAdminResult<string>.Fail(ModuleAdminError.SemesterChangeWithEnrolments, ever);
            }
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

    /// <summary>
    /// Replaces the module's assignments; <c>role</c> changes keep the original <c>assigned_at</c>. The module row is
    /// locked first (<c>FOR NO KEY UPDATE</c>), so two assignments of one module run one after the other and the second
    /// reads what the first committed (review S6 E10: both used to read the old set and leave two leaders). Rows that
    /// stop being leader are saved before the new leader is, so the unique index on the leader never sees two.
    /// </summary>
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
        await db.Database.ExecuteSqlAsync($"SELECT id FROM modules WHERE id = {found.Id} FOR NO KEY UPDATE", cancellationToken);

        var current = await db.ModuleLecturers.Where(ml => ml.ModuleId == found.Id).ToListAsync(cancellationToken);
        var staffById = await db.Lecturers.AsNoTracking()
            .Where(l => current.Select(c => c.LecturerId).Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.StaffNumber, cancellationToken);
        var before = Ordered(current.Select(c => (staffById.GetValueOrDefault(c.LecturerId) ?? c.LecturerId.ToString(), c.Role)));

        var now = clock.GetUtcNow();
        var wanted = requested.ToDictionary(a => lecturers[a.StaffNumber].Id, a => a.Role);

        // 1. Removals and demotions first.
        foreach (var existing in current)
        {
            if (!wanted.TryGetValue(existing.LecturerId, out var role))
            {
                db.ModuleLecturers.Remove(existing);
            }
            else if (existing.Role != role && role == ModuleLecturerRole.Teacher)
            {
                existing.Role = role;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // 2. Then promotions and additions.
        foreach (var existing in current.Where(c => wanted.TryGetValue(c.LecturerId, out var role) && c.Role != role))
        {
            existing.Role = wanted[existing.LecturerId];
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
    /// Trims an over-capacity module back to capacity (02-api.md section 8.5; review S6 E4, joint item J1). Every student
    /// holding one of this year's places is locked first (id order, the enrolment lock order), so no withdrawal can
    /// change the count meanwhile; the excess is <b>the real number of active enrolments</b> of the year minus capacity,
    /// never the stored <c>enrolled_count</c> (a drifted counter must not withdraw anyone from a module that is not
    /// over capacity); the latest <c>enrolled_at</c> go first, withdrawn by one
    /// <see cref="EnrolmentService.WithdrawManyAsync"/> call (each audited <c>enrolment.admin_withdrawn</c> with
    /// <c>trim: true</c>); then, with the module row locked last, <c>enrolled_count</c> is set to the real count, which
    /// also repairs a drifted counter. <c>before</c> and <c>after</c> are the stored count before and after.
    /// </summary>
    public async Task<ModuleAdminResult<TrimOutcome>> TrimAsync(string code, string reason, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var module = await StaffModules.FindAsync(db, code, cancellationToken);
        if (module is not { } found)
        {
            return ModuleAdminResult<TrimOutcome>.Fail(ModuleAdminError.ModuleNotFound);
        }

        var cachedYear = (await windows.CurrentAsync(cancellationToken)).AcademicYear;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var start = await CountsAsync(found.Id, cancellationToken);

        // 1. Students, then the year under a share lock: the order of an enrolment or a withdrawal.
        await LockStudentsAsync(found.Id, cachedYear, cancellationToken);
        var years = await db.Database.SqlQueryRaw<string>(EnrolmentService.LockAcademicYearSql).ToListAsync(cancellationToken);
        var year = years.Count == 0 ? cachedYear : years[0];
        if (!string.Equals(year, cachedYear, StringComparison.Ordinal))
        {
            await LockStudentsAsync(found.Id, year, cancellationToken);
        }

        // 2. The real count, and the latest enrolments beyond capacity.
        var active = ActiveEnrolments(found.Id, year);
        var excess = Math.Max(0, await active.CountAsync(cancellationToken) - start.Capacity);
        var latest = await (
            from e in active
            join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
            orderby e.EnrolledAt descending, s.StudentNumber descending
            select new { e.StudentId, s.StudentNumber })
            .Take(excess)
            .ToListAsync(cancellationToken);

        // 3. One bulk withdrawal (it locks each module row last, in id order).
        var result = await enrolments.WithdrawManyAsync(
            [.. latest.Select(c => new WithdrawalTarget(c.StudentId, found.Id))],
            actorUserId,
            new WithdrawOptions(Override: true, Reason: reason, Trim: true),
            cancellationToken);
        var withdrawnIds = result.Withdrawn.Select(r => r.EnrolmentId).ToHashSet();
        var withdrawnStudents = await db.Enrolments.AsNoTracking()
            .Where(e => withdrawnIds.Contains(e.Id))
            .Select(e => e.StudentId)
            .ToListAsync(cancellationToken);
        var withdrawn = latest.Where(c => withdrawnStudents.Contains(c.StudentId)).Select(c => c.StudentNumber).ToList();

        // 4. The module row (already held when anything was withdrawn), and the stored count set to the real one.
        await db.Database.ExecuteSqlAsync($"SELECT id FROM modules WHERE id = {found.Id} FOR NO KEY UPDATE", cancellationToken);
        var real = await ActiveEnrolments(found.Id, year).CountAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE modules SET enrolled_count = {real}, updated_at = {clock.GetUtcNow()} WHERE id = {found.Id} AND enrolled_count <> {real}",
            cancellationToken);
        var end = await CountsAsync(found.Id, cancellationToken);

        audit.Record(db, AuditActions.ModuleTrimmed, AuditSubjects.Module, found.Code, new { reason, withdrawn, enrolledCount = new { before = start.EnrolledCount, after = end.EnrolledCount } }, moduleId: found.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ModuleAdminResult<TrimOutcome>.Success(new TrimOutcome(found.Code, end.Capacity, start.EnrolledCount, end.EnrolledCount, withdrawn));
    }

    private IQueryable<Enrolment> ActiveEnrolments(Guid moduleId, string academicYear) =>
        db.Enrolments.AsNoTracking().Where(e => e.ModuleId == moduleId && e.Status == EnrolmentStatus.Active && e.AcademicYear == academicYear);

    private Task LockStudentsAsync(Guid moduleId, string academicYear, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            LockModuleStudentsSql,
            [new NpgsqlParameter("module", moduleId), new NpgsqlParameter("year", academicYear)],
            cancellationToken);

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
