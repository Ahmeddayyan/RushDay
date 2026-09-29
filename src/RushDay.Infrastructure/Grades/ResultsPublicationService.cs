using Microsoft.EntityFrameworkCore;
using Npgsql;
using RushDay.Domain.Audit;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Domain.Results;
using RushDay.Infrastructure.Announcements;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;

namespace RushDay.Infrastructure.Grades;

/// <summary>Why a results operation was refused; the API maps each to its ProblemDetails slug (02-api.md section 6).</summary>
public enum PublicationError
{
    None,
    NothingToPublish,
    PublishTooFarAhead,
    PublicationNotFound,
    PublicationLive,
    PublicationScheduled,
    ModuleNotFound,
    ModuleNotSubmitted,
    ModuleLocked,
    GradeNotFound,
}

/// <summary><c>excluded[].reason</c> of a publish.</summary>
public enum ExclusionReason
{
    NotSubmitted,
    MarksMissing,
}

/// <summary>A <c>results_publications</c> row with its creator's display name (the data behind <c>PublicationInfo</c>).</summary>
public sealed record PublicationRecord(
    Guid Id,
    string AcademicYear,
    Semester Semester,
    DateTimeOffset PublishAt,
    int GradeCount,
    int ModuleCount,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    string? Note);

/// <summary>A module of the (year, semester) that a publish left out, and why.</summary>
public sealed record ExcludedModule(string Code, MarksState Status, ExclusionReason Reason, int MarksMissing);

public sealed record PublishOutcome(PublicationRecord Publication, int Modules, int Grades, IReadOnlyList<ExcludedModule> Excluded, bool Announced);

/// <summary>What a cancel or an unpublish sent back to Submitted.</summary>
public sealed record RevertedPublication(string AcademicYear, Semester Semester, int Grades);

public sealed record ReturnedToDraft(string Code, bool FromScheduledPublication, int GradeCount);

public sealed record GradeValue(int? Mark, GradeOutcome Outcome);

public sealed record CorrectedGrade(string StudentNumber, GradeValue Before, GradeValue After, int Version, DateTimeOffset CorrectedAt);

public sealed record PublicationResult<T>(T? Value, PublicationError Error)
{
    public bool Succeeded => Error == PublicationError.None;

    public static PublicationResult<T> Success(T value) => new(value, PublicationError.None);

    public static PublicationResult<T> Fail(PublicationError error) => new(default, error);
}

/// <summary>
/// The results lifecycle of the registry (02-api.md section 8.5, 00-overview.md section 4.3, D8, D9): publish a
/// (year, semester) at a stored instant, reschedule or cancel it while scheduled, unpublish it once live, return a
/// module to draft, and correct a single submitted or published mark. There is no scheduler: a publication is an
/// instant on <c>results_publications</c> and on every grade it holds, and students' reads compare it with the clock.
/// Only publishable modules (<c>submitted</c> with nothing missing) are ever published, and only the grades of
/// <b>active</b> enrolments of that year. Every operation is one transaction that writes its audit row with the change.
/// </summary>
public sealed class ResultsPublicationService(
    RushDayDbContext db,
    EnrolmentWindowService windows,
    AnnouncementService announcements,
    AuditWriter audit,
    TimeProvider clock)
{
    /// <summary>The furthest a publication may be scheduled ahead of now.</summary>
    public static readonly TimeSpan MaxLeadTime = TimeSpan.FromDays(90);

    /// <summary>Moves every Submitted grade of the year's active enrolments on the publishable modules to Published.</summary>
    private const string PublishGradesSql = """
        UPDATE grades g
        SET status = 'Published', published_at = @publishAt, publication_id = @publication, updated_at = @now, version = g.version + 1
        FROM enrolments e
        WHERE e.student_id = g.student_id AND e.module_id = g.module_id
          AND e.academic_year = @year AND e.status = 'Active'
          AND g.module_id = ANY(@publishable) AND g.status = 'Submitted'
        RETURNING g.module_id
        """;

    /// <summary>
    /// Return to draft: the year's Submitted grades and those published under a still-scheduled publication become
    /// Draft; the CTE reports which publication each came from (<c>RETURNING</c> alone would show the new, null, value).
    /// </summary>
    private const string ReturnToDraftSql = """
        WITH target AS (
            SELECT g.id, g.publication_id
            FROM grades g
            JOIN enrolments e ON e.student_id = g.student_id AND e.module_id = g.module_id
            WHERE g.module_id = @module AND e.academic_year = @year
              AND (g.status = 'Submitted' OR (g.status = 'Published' AND g.published_at > @now))
        )
        UPDATE grades g
        SET status = 'Draft', submitted_at = NULL, published_at = NULL, publication_id = NULL, updated_at = @now, version = g.version + 1
        FROM target
        WHERE g.id = target.id
        RETURNING target.publication_id
        """;

    /// <summary>
    /// Publishes the (year, semester) at <paramref name="publishAt"/> (an earlier instant means now; more than 90 days
    /// ahead is <c>publish-too-far-ahead</c>). Only publishable modules are included; none is <c>nothing-to-publish</c>.
    /// Calling it again publishes only modules that became publishable since, under a new publication. With
    /// <paramref name="announce"/> a pinned university announcement goes out at the same instant.
    /// </summary>
    public async Task<PublicationResult<PublishOutcome>> PublishAsync(
        string academicYear,
        Semester semester,
        DateTimeOffset publishAt,
        bool announce,
        string? note,
        string resultsFootnote,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(academicYear);

        var now = clock.GetUtcNow();
        if (publishAt - now > MaxLeadTime)
        {
            return PublicationResult<PublishOutcome>.Fail(PublicationError.PublishTooFarAhead);
        }

        var instant = publishAt < now ? now : publishAt;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var modules = await db.Modules.AsNoTracking()
            .Where(m => m.Semester == semester)
            .OrderBy(m => m.Code)
            .Select(m => new { m.Id, m.Code })
            .ToListAsync(cancellationToken);
        var statuses = await MarksStatusQuery.ForModulesAsync(db, academicYear, now, [.. modules.Select(m => m.Id)], cancellationToken);

        var publishable = modules.Where(m => MarksStatusQuery.Of(statuses, m.Id).IsPublishable).Select(m => m.Id).ToArray();
        if (publishable.Length == 0)
        {
            return PublicationResult<PublishOutcome>.Fail(PublicationError.NothingToPublish);
        }

        var excluded = modules
            .Select(m => (m.Code, Status: MarksStatusQuery.Of(statuses, m.Id)))
            .Where(m => m.Status.Status == MarksState.Draft || (m.Status.Status == MarksState.Submitted && m.Status.Missing > 0))
            .Select(m => new ExcludedModule(
                m.Code,
                m.Status.Status,
                m.Status.Status == MarksState.Draft ? ExclusionReason.NotSubmitted : ExclusionReason.MarksMissing,
                m.Status.Missing))
            .ToList();

        var publication = new ResultsPublication
        {
            Id = Guid.CreateVersion7(),
            AcademicYear = academicYear,
            Semester = semester,
            PublishAt = instant,
            CreatedAt = now,
            CreatedByUserId = actorUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        db.ResultsPublications.Add(publication);
        await db.SaveChangesAsync(cancellationToken);

        // One statement; a module returned to draft (or a publish that ran first) meanwhile simply matches no rows,
        // so the counts come from what was actually published.
        var published = await RawSql.QueryAsync(
            db,
            PublishGradesSql,
            [
                new NpgsqlParameter("publishAt", instant),
                new NpgsqlParameter("publication", publication.Id),
                new NpgsqlParameter("now", now),
                new NpgsqlParameter("year", academicYear),
                new NpgsqlParameter("publishable", publishable),
            ],
            r => r.GetGuid(0),
            cancellationToken);
        if (published.Count == 0)
        {
            return PublicationResult<PublishOutcome>.Fail(PublicationError.NothingToPublish);
        }

        var moduleCount = published.Distinct().Count();
        publication.GradeCount = published.Count;
        publication.ModuleCount = moduleCount;

        if (announce)
        {
            var title = $"{semester} {academicYear} results are available";
            var body = "Sign in to see your marks." + "\n\n" + resultsFootnote;
            await announcements.CreateAsync(new AnnouncementDraft(title, body, Pinned: true, PublishedAt: instant), moduleId: null, actorUserId, cancellationToken);
        }

        var publishedIds = published.ToHashSet();
        audit.Record(
            db,
            AuditActions.ResultsPublished,
            AuditSubjects.Publication,
            publication.Id.ToString(),
            new
            {
                academicYear,
                semester = GradeNames.Of(semester),
                publishAt = GradeNames.Instant(instant),
                modules = moduleCount,
                grades = published.Count,
                excluded = excluded.Select(e => e.Code).ToArray(),
                announced = announce,
            });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var record = await ReadAsync(publication.Id, cancellationToken);
        return PublicationResult<PublishOutcome>.Success(new PublishOutcome(record!, publishedIds.Count, published.Count, excluded, announce));
    }

    /// <summary>Moves a scheduled publication (and its grades) to another instant; a live one is <c>publication-live</c>.</summary>
    public async Task<PublicationResult<PublicationRecord>> RescheduleAsync(Guid publicationId, DateTimeOffset publishAt, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        if (publishAt - now > MaxLeadTime)
        {
            return PublicationResult<PublicationRecord>.Fail(PublicationError.PublishTooFarAhead);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var publication = await LockAsync(publicationId, cancellationToken);
        if (publication is null)
        {
            return PublicationResult<PublicationRecord>.Fail(PublicationError.PublicationNotFound);
        }

        if (publication.PublishAt <= now)
        {
            return PublicationResult<PublicationRecord>.Fail(PublicationError.PublicationLive);
        }

        var instant = publishAt < now ? now : publishAt;
        var before = publication.PublishAt;
        publication.PublishAt = instant;
        await db.Database.ExecuteSqlAsync(
            $"UPDATE grades SET published_at = {instant}, updated_at = {now} WHERE publication_id = {publicationId}",
            cancellationToken);

        audit.Record(
            db,
            AuditActions.ResultsRescheduled,
            AuditSubjects.Publication,
            publicationId.ToString(),
            new { before = GradeNames.Instant(before), after = GradeNames.Instant(instant) });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return PublicationResult<PublicationRecord>.Success((await ReadAsync(publicationId, cancellationToken))!);
    }

    /// <summary>Cancels a scheduled publication: its grades go back to Submitted and the row is deleted (the audit keeps it).</summary>
    public Task<PublicationResult<RevertedPublication>> CancelAsync(Guid publicationId, CancellationToken cancellationToken = default) =>
        RevertAsync(publicationId, live: false, reason: null, cancellationToken);

    /// <summary>Unpublishes a live publication with a reason: students stop seeing its marks at once.</summary>
    public Task<PublicationResult<RevertedPublication>> UnpublishAsync(Guid publicationId, string reason, CancellationToken cancellationToken = default) =>
        RevertAsync(publicationId, live: true, reason, cancellationToken);

    /// <summary>
    /// Returns a module's marks of <paramref name="academicYear"/> (default: the settings year) to Draft, when they are
    /// Submitted or in a still-scheduled publication (whose counts drop accordingly); <c>module-not-submitted</c> for
    /// a draft or empty module, <c>module-locked</c> once any of them is live.
    /// </summary>
    public async Task<PublicationResult<ReturnedToDraft>> ReturnToDraftAsync(string moduleCode, string? academicYear, string reason, CancellationToken cancellationToken = default)
    {
        var module = await StaffModules.FindAsync(db, moduleCode, cancellationToken);
        if (module is not { } found)
        {
            return PublicationResult<ReturnedToDraft>.Fail(PublicationError.ModuleNotFound);
        }

        var year = academicYear ?? (await windows.CurrentAsync(cancellationToken)).AcademicYear;
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ModuleMarksLock.AcquireAsync(db, found.Id, cancellationToken);

        // Row locks first, so a publish, cancel or unpublish of these grades either finished before the status is
        // read or waits until this transaction ends.
        await db.Database.ExecuteSqlAsync(
            $"""
            SELECT g.id FROM grades g
            JOIN enrolments e ON e.student_id = g.student_id AND e.module_id = g.module_id
            WHERE g.module_id = {found.Id} AND e.academic_year = {year}
            FOR UPDATE OF g
            """,
            cancellationToken);

        var status = await MarksStatusQuery.ForModuleAsync(db, found.Id, year, now, cancellationToken);
        switch (status.Status)
        {
            case MarksState.NoStudents or MarksState.Draft:
                return PublicationResult<ReturnedToDraft>.Fail(PublicationError.ModuleNotSubmitted);
            case MarksState.Published:
                return PublicationResult<ReturnedToDraft>.Fail(PublicationError.ModuleLocked);
        }

        var reverted = await RawSql.QueryAsync(
            db,
            ReturnToDraftSql,
            [
                new NpgsqlParameter("module", found.Id),
                new NpgsqlParameter("year", year),
                new NpgsqlParameter("now", now),
            ],
            r => RawSql.NullableGuid(r, 0),
            cancellationToken);

        var fromPublications = reverted.Where(p => p is not null).GroupBy(p => p!.Value).ToList();
        foreach (var group in fromPublications)
        {
            var count = group.Count();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE results_publications SET grade_count = GREATEST(grade_count - {count}, 0), module_count = GREATEST(module_count - 1, 0) WHERE id = {group.Key}",
                cancellationToken);
        }

        var fromScheduled = fromPublications.Count > 0;
        audit.Record(
            db,
            AuditActions.ModuleReturnedToDraft,
            AuditSubjects.Module,
            found.Code,
            new { reason, gradeCount = reverted.Count, academicYear = year, fromScheduledPublication = fromScheduled },
            moduleId: found.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return PublicationResult<ReturnedToDraft>.Success(new ReturnedToDraft(found.Code, fromScheduled, reverted.Count));
    }

    /// <summary>
    /// Corrects one Submitted or Published grade (a Draft is the lecturers', <c>module-not-submitted</c>): mark and
    /// outcome change, <c>corrected_at</c> is stamped and status, instant and publication are kept, so a published
    /// correction is what the student sees at once, labelled "Amended". Audits <c>grade.corrected</c>.
    /// </summary>
    public async Task<PublicationResult<CorrectedGrade>> CorrectAsync(
        string moduleCode,
        string studentNumber,
        GradeOutcome outcome,
        int? mark,
        string reason,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(studentNumber);

        var module = await StaffModules.FindAsync(db, moduleCode, cancellationToken);
        if (module is not { } found)
        {
            return PublicationResult<CorrectedGrade>.Fail(PublicationError.ModuleNotFound);
        }

        var number = studentNumber.Trim().ToUpperInvariant();
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Grades
            .FromSql($"SELECT g.* FROM grades g JOIN students s ON s.id = g.student_id WHERE g.module_id = {found.Id} AND s.student_number = {number} FOR UPDATE OF g")
            .ToListAsync(cancellationToken);
        var grade = locked.SingleOrDefault();
        if (grade is null)
        {
            return PublicationResult<CorrectedGrade>.Fail(PublicationError.GradeNotFound);
        }

        if (grade.Status == GradeStatus.Draft)
        {
            return PublicationResult<CorrectedGrade>.Fail(PublicationError.ModuleNotSubmitted);
        }

        var before = new GradeValue(grade.Mark, grade.Outcome);
        var after = new GradeValue(outcome == GradeOutcome.Mark ? mark : null, outcome);
        grade.Mark = after.Mark;
        grade.Outcome = after.Outcome;
        grade.Version++;
        grade.UpdatedAt = now;
        grade.CorrectedAt = now;
        grade.EnteredByUserId = actorUserId;

        audit.Record(
            db,
            AuditActions.GradeCorrected,
            AuditSubjects.Grade,
            grade.Id.ToString(),
            new
            {
                moduleCode = found.Code,
                studentNumber = number,
                before = new { mark = before.Mark, outcome = GradeNames.Of(before.Outcome) },
                after = new { mark = after.Mark, outcome = GradeNames.Of(after.Outcome) },
                reason,
            },
            grade.StudentId,
            found.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return PublicationResult<CorrectedGrade>.Success(new CorrectedGrade(number, before, after, grade.Version, now));
    }

    /// <summary>The publications of a (year, semester), newest first, with their creators' display names.</summary>
    public async Task<IReadOnlyList<PublicationRecord>> ListAsync(string academicYear, Semester semester, CancellationToken cancellationToken = default)
    {
        var rows = await Project(db.ResultsPublications.AsNoTracking().Where(p => p.AcademicYear == academicYear && p.Semester == semester))
            .ToListAsync(cancellationToken);
        return [.. rows.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)];
    }

    /// <summary>Publications by id (the overview's next and latest).</summary>
    public async Task<IReadOnlyList<PublicationRecord>> ReadManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        ids.Count == 0 ? [] : await Project(db.ResultsPublications.AsNoTracking().Where(p => ids.Contains(p.Id))).ToListAsync(cancellationToken);

    private async Task<PublicationRecord?> ReadAsync(Guid id, CancellationToken cancellationToken) =>
        await Project(db.ResultsPublications.AsNoTracking().Where(p => p.Id == id)).SingleOrDefaultAsync(cancellationToken);

    private IQueryable<PublicationRecord> Project(IQueryable<ResultsPublication> publications) =>
        from p in publications
        from u in db.Users.AsNoTracking().Where(u => u.Id == p.CreatedByUserId).DefaultIfEmpty()
        select new PublicationRecord(p.Id, p.AcademicYear, p.Semester, p.PublishAt, p.GradeCount, p.ModuleCount, p.CreatedAt, u == null ? null : u.DisplayName, p.Note);

    private async Task<ResultsPublication?> LockAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        var rows = await db.ResultsPublications
            .FromSql($"SELECT * FROM results_publications WHERE id = {publicationId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private async Task<PublicationResult<RevertedPublication>> RevertAsync(Guid publicationId, bool live, string? reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var publication = await LockAsync(publicationId, cancellationToken);
        if (publication is null)
        {
            return PublicationResult<RevertedPublication>.Fail(PublicationError.PublicationNotFound);
        }

        var isLive = publication.PublishAt <= now;
        if (isLive != live)
        {
            // Cancel is for a scheduled publication, unpublish for a live one.
            return PublicationResult<RevertedPublication>.Fail(live ? PublicationError.PublicationScheduled : PublicationError.PublicationLive);
        }

        var grades = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE grades SET status = 'Submitted', published_at = NULL, publication_id = NULL, updated_at = {now}, version = version + 1
            WHERE publication_id = {publicationId}
            """,
            cancellationToken);
        db.ResultsPublications.Remove(publication);

        var semester = GradeNames.Of(publication.Semester);
        if (live)
        {
            audit.Record(db, AuditActions.ResultsUnpublished, AuditSubjects.Publication, publicationId.ToString(), new { academicYear = publication.AcademicYear, semester, grades, reason });
        }
        else
        {
            audit.Record(db, AuditActions.ResultsCancelled, AuditSubjects.Publication, publicationId.ToString(), new { academicYear = publication.AcademicYear, semester, grades });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return PublicationResult<RevertedPublication>.Success(new RevertedPublication(publication.AcademicYear, publication.Semester, grades));
    }
}
