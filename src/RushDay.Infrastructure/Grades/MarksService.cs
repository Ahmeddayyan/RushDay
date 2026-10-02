using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;

namespace RushDay.Infrastructure.Grades;

/// <summary>Why a marks save or submit was refused; the API maps each to its ProblemDetails slug (02-api.md section 6).</summary>
public enum MarksError
{
    None,
    NotYourModule,
    NotModuleLeader,
    ModuleLocked,
    NotEnrolledStudents,
    StaleMark,
    NothingToSubmit,
    AlreadySubmitted,
    MarksIncomplete,
}

/// <summary>One row of <c>PUT /api/lecturer/modules/{code}/marks</c>; <see cref="Mark"/> is null iff the outcome is not a mark.</summary>
public sealed record MarkEntry(string StudentNumber, int? Mark, GradeOutcome Outcome, int? Version);

/// <summary>A refusal; <see cref="StudentNumbers"/> fills <c>studentNumbers[]</c> or <c>missing[]</c>.</summary>
public sealed record MarksFailure(MarksError Error, IReadOnlyList<string> StudentNumbers);

/// <summary>The submitted rows exactly as stored after the save, in request order, and how many were written.</summary>
public sealed record SavedMarks(Guid ModuleId, IReadOnlyList<MarksRowData> Rows, int Written);

public sealed record SubmittedMarks(string Code, DateTimeOffset SubmittedAt, int GradeCount);

public sealed record MarksResult<T>(T? Value, MarksFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static MarksResult<T> Success(T value) => new(value, null);

    public static MarksResult<T> Fail(MarksError error, IReadOnlyList<string>? studentNumbers = null) => new(default, new MarksFailure(error, studentNumbers ?? []));
}

/// <summary>
/// Serialises the mutations of one module's marks (save, submit, return to draft, publish): a PostgreSQL advisory lock
/// held to the end of the transaction, keyed on the module, so a status check and the writes it guards are one step.
/// An enrolment on the module takes the same lock <b>shared</b> (<see cref="AcquireSharedAsync"/>) around its
/// "marks still in draft" check (review S6 E1), so a submit, return to draft or publish either finished before that
/// check or waits for the enrolment to commit; enrolments do not wait for one another, so a rush is not serialised by
/// it. It never touches the module row. A transaction takes at most one of these locks, except a publish, which takes
/// its modules' locks in module-id order.
/// </summary>
public static class ModuleMarksLock
{
    /// <summary>The first key of the two-key advisory lock: "RDMK".</summary>
    public const int LockClass = 0x52444D4B;

    public static Task AcquireAsync(RushDayDbContext db, Guid moduleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var key = moduleId.ToString("D");
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockClass}, hashtext({key}))", cancellationToken);
    }

    /// <summary>The shared form, for an enrolment's check: it conflicts only with <see cref="AcquireAsync"/>.</summary>
    public static Task AcquireSharedAsync(RushDayDbContext db, Guid moduleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var key = moduleId.ToString("D");
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({LockClass}, hashtext({key}))", cancellationToken);
    }

    /// <summary>Several modules' locks (a publish), always in module-id order so two publishes never wait for each other in a cycle.</summary>
    public static async Task AcquireManyAsync(RushDayDbContext db, IEnumerable<Guid> moduleIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(moduleIds);
        foreach (var moduleId in moduleIds.Distinct().Order())
        {
            await AcquireAsync(db, moduleId, cancellationToken);
        }
    }
}

/// <summary>
/// Marks entry and submission for a module's lecturers (02-api.md section 8.4). Every call resolves the module through
/// the caller's <c>module_lecturers</c> row, so a lecturer can reach only a module they teach whatever the route
/// says. <see cref="SaveAsync"/> is all-or-nothing per request with optimistic versions; <see cref="SubmitAsync"/> is
/// the leader's alone and moves exactly the active enrolments' drafts to Submitted. Each writes its audit rows in
/// the same transaction.
/// </summary>
public sealed class MarksService(
    RushDayDbContext db,
    EnrolmentWindowService windows,
    AuditWriter audit,
    TimeProvider clock)
{
    /// <summary>The most rows one request may carry (the SPA sends larger saves as sequential chunks).</summary>
    public const int MaxRowsPerRequest = 500;

    /// <summary>
    /// Saves draft marks. The module must be <c>draft</c> or <c>noStudents</c> (else <c>module-locked</c>); every
    /// student must hold an active enrolment of the current year (else <c>not-enrolled-students</c>); every existing
    /// row's version must match (else <c>stale-mark</c>, nothing saved). New rows are Draft at version 1; changed rows
    /// get version + 1; unchanged rows are not written. Audits <c>grade.entered</c> and <c>grade.changed</c>.
    /// </summary>
    public async Task<MarksResult<SavedMarks>> SaveAsync(Guid lecturerId, string moduleCode, IReadOnlyList<MarkEntry> rows, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var module = await StaffModules.TaughtAsync(db, lecturerId, moduleCode, cancellationToken);
        if (module is null)
        {
            return MarksResult<SavedMarks>.Fail(MarksError.NotYourModule);
        }

        var calendar = await windows.CurrentAsync(cancellationToken);
        var year = calendar.AcademicYear;
        var now = clock.GetUtcNow();
        var entries = rows.Select(r => r with { StudentNumber = r.StudentNumber.Trim().ToUpperInvariant() }).ToList();
        var numbers = entries.Select(e => e.StudentNumber).Distinct(StringComparer.Ordinal).ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ModuleMarksLock.AcquireAsync(db, module.Id, cancellationToken);

        var status = await MarksStatusQuery.ForModuleAsync(db, module.Id, year, now, cancellationToken);
        if (!status.IsEditable)
        {
            return MarksResult<SavedMarks>.Fail(MarksError.ModuleLocked);
        }

        var enrolled = await (
            from e in db.Enrolments.AsNoTracking()
            join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
            where e.ModuleId == module.Id && e.Status == EnrolmentStatus.Active && e.AcademicYear == year && numbers.Contains(s.StudentNumber)
            select new { s.Id, s.StudentNumber, s.FullName })
            .ToDictionaryAsync(s => s.StudentNumber, StringComparer.Ordinal, cancellationToken);

        var notEnrolled = numbers.Where(n => !enrolled.ContainsKey(n)).ToList();
        if (notEnrolled.Count > 0)
        {
            return MarksResult<SavedMarks>.Fail(MarksError.NotEnrolledStudents, notEnrolled);
        }

        var studentIds = enrolled.Values.Select(s => s.Id).ToArray();
        var grades = await db.Grades
            .Where(g => g.ModuleId == module.Id && studentIds.Contains(g.StudentId))
            .ToDictionaryAsync(g => g.StudentId, cancellationToken);

        // Optimistic concurrency: the version the client saw must be the stored one (null for a row it saw empty).
        var stale = entries
            .Where(e =>
            {
                var stored = grades.GetValueOrDefault(enrolled[e.StudentNumber].Id);
                return stored is null ? e.Version is not null : stored.Version != e.Version || stored.Status != GradeStatus.Draft;
            })
            .Select(e => e.StudentNumber)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (stale.Count > 0)
        {
            return MarksResult<SavedMarks>.Fail(MarksError.StaleMark, stale);
        }

        var written = 0;
        foreach (var entry in entries)
        {
            var student = enrolled[entry.StudentNumber];
            var mark = entry.Outcome == GradeOutcome.Mark ? entry.Mark : null;
            if (!grades.TryGetValue(student.Id, out var grade))
            {
                grade = new Grade
                {
                    Id = Guid.CreateVersion7(),
                    StudentId = student.Id,
                    ModuleId = module.Id,
                    Mark = mark,
                    Outcome = entry.Outcome,
                    Status = GradeStatus.Draft,
                    Version = 1,
                    UpdatedAt = now,
                    EnteredByUserId = actorUserId,
                };
                db.Grades.Add(grade);
                grades[student.Id] = grade;
                written++;
                audit.Record(
                    db,
                    AuditActions.GradeEntered,
                    AuditSubjects.Grade,
                    grade.Id.ToString(),
                    new { moduleCode = module.Code, studentNumber = student.StudentNumber, mark, outcome = GradeNames.Of(entry.Outcome) },
                    student.Id,
                    module.Id);
                continue;
            }

            if (grade.Mark == mark && grade.Outcome == entry.Outcome)
            {
                continue;
            }

            var before = new { mark = grade.Mark, outcome = GradeNames.Of(grade.Outcome) };
            grade.Mark = mark;
            grade.Outcome = entry.Outcome;
            grade.Version++;
            grade.UpdatedAt = now;
            grade.EnteredByUserId = actorUserId;
            written++;
            audit.Record(
                db,
                AuditActions.GradeChanged,
                AuditSubjects.Grade,
                grade.Id.ToString(),
                new { moduleCode = module.Code, studentNumber = student.StudentNumber, before, after = new { mark, outcome = GradeNames.Of(entry.Outcome) }, version = grade.Version },
                student.Id,
                module.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var enteredByIds = grades.Values.Where(g => g.EnteredByUserId is not null).Select(g => g.EnteredByUserId!.Value).Distinct().ToArray();
        var names = await db.Users.AsNoTracking()
            .Where(u => enteredByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var stored = entries
            .Select(e => e.StudentNumber)
            .Distinct(StringComparer.Ordinal)
            .Select(number =>
            {
                var student = enrolled[number];
                var grade = grades[student.Id];
                return new MarksRowData(
                    student.StudentNumber,
                    student.FullName,
                    EnrolmentStatus.Active,
                    grade.Outcome,
                    grade.Mark,
                    grade.Status,
                    grade.Version,
                    grade.UpdatedAt,
                    grade.EnteredByUserId is { } by ? names.GetValueOrDefault(by) : null,
                    grade.CorrectedAt);
            })
            .ToList();

        return MarksResult<SavedMarks>.Success(new SavedMarks(module.Id, stored, written));
    }

    /// <summary>
    /// The leader submits the module (else <c>not-module-leader</c>): at least one active enrolment this year (else
    /// <c>nothing-to-submit</c>), nothing submitted, scheduled or published yet (<c>already-submitted</c>,
    /// <c>module-locked</c>), and a grade row for every active enrolment (else <c>marks-incomplete</c> with
    /// <c>missing</c>). Exactly those grades become Submitted; a withdrawn student's draft stays Draft. Audits
    /// <c>module.marks_submitted</c>.
    /// </summary>
    public async Task<MarksResult<SubmittedMarks>> SubmitAsync(Guid lecturerId, string moduleCode, CancellationToken cancellationToken = default)
    {
        var module = await StaffModules.TaughtAsync(db, lecturerId, moduleCode, cancellationToken);
        if (module is null)
        {
            return MarksResult<SubmittedMarks>.Fail(MarksError.NotYourModule);
        }

        if (module.Role != ModuleLecturerRole.Leader)
        {
            return MarksResult<SubmittedMarks>.Fail(MarksError.NotModuleLeader);
        }

        var calendar = await windows.CurrentAsync(cancellationToken);
        var year = calendar.AcademicYear;
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ModuleMarksLock.AcquireAsync(db, module.Id, cancellationToken);

        var status = await MarksStatusQuery.ForModuleAsync(db, module.Id, year, now, cancellationToken);
        switch (status.Status)
        {
            case MarksState.NoStudents:
                return MarksResult<SubmittedMarks>.Fail(MarksError.NothingToSubmit);
            case MarksState.Submitted:
                return MarksResult<SubmittedMarks>.Fail(MarksError.AlreadySubmitted);
            case MarksState.Scheduled or MarksState.Published:
                return MarksResult<SubmittedMarks>.Fail(MarksError.ModuleLocked);
        }

        if (status.Missing > 0)
        {
            var missing = await (
                from e in db.Enrolments.AsNoTracking()
                join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                where e.ModuleId == module.Id && e.Status == EnrolmentStatus.Active && e.AcademicYear == year
                      && !db.Grades.Any(g => g.StudentId == e.StudentId && g.ModuleId == e.ModuleId)
                orderby s.StudentNumber
                select s.StudentNumber)
                .ToListAsync(cancellationToken);
            return MarksResult<SubmittedMarks>.Fail(MarksError.MarksIncomplete, missing);
        }

        var gradeCount = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE grades g SET status = 'Submitted', submitted_at = {now}, updated_at = {now}, version = g.version + 1
            FROM enrolments e
            WHERE e.student_id = g.student_id AND e.module_id = g.module_id
              AND g.module_id = {module.Id} AND e.academic_year = {year} AND e.status = 'Active' AND g.status = 'Draft'
            """,
            cancellationToken);

        audit.Record(db, AuditActions.ModuleMarksSubmitted, AuditSubjects.Module, module.Code, new { gradeCount, academicYear = year }, moduleId: module.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MarksResult<SubmittedMarks>.Success(new SubmittedMarks(module.Code, now, gradeCount));
    }
}

/// <summary>The camelCase wire names of grade enums, for audit details (which carry strings, not enum numbers).</summary>
public static class GradeNames
{
    public static string Of(GradeOutcome outcome) => outcome switch
    {
        GradeOutcome.Mark => "mark",
        GradeOutcome.Absent => "absent",
        GradeOutcome.Deferred => "deferred",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown outcome."),
    };

    public static string Of(Semester semester) => semester switch
    {
        Semester.Autumn => "autumn",
        Semester.Spring => "spring",
        _ => throw new ArgumentOutOfRangeException(nameof(semester), semester, "Unknown semester."),
    };

    /// <summary>An instant as the API writes it (<c>2026-09-28T09:00:00.000Z</c>).</summary>
    public static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
