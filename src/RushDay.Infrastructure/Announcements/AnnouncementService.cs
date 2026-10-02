using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Announcements;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Announcements;

/// <summary>Who is reading <c>GET /api/announcements</c>: exactly one of the principals, or an administrator.</summary>
public sealed record AnnouncementViewer(Guid? StudentId, Guid? LecturerId, bool IsAdmin)
{
    public static AnnouncementViewer Student(Guid studentId) => new(studentId, null, false);

    public static AnnouncementViewer Lecturer(Guid lecturerId) => new(null, lecturerId, false);

    public static AnnouncementViewer Admin { get; } = new(null, null, true);

    /// <summary>A signed-in user with no principal and no admin role sees university announcements only.</summary>
    public static AnnouncementViewer UniversityOnly { get; } = new(null, null, false);
}

/// <summary>The editable fields of an announcement; <see cref="PublishedAt"/> defaults to now.</summary>
public sealed record AnnouncementDraft(string Title, string Body, bool Pinned = false, DateTimeOffset? PublishedAt = null, DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Whom an announcement write acts for (review S4 D6, joint item J3), stated at every call site instead of being
/// inferred from a null module id: <see cref="Administrator"/> (a new announcement is university-wide; an edit or a
/// delete may reach an announcement of any scope) or <see cref="Module"/> (a module's lecturers: created on that module,
/// and an edit or a delete reaches only that module's rows). There is no other value; lecturer calls get theirs only
/// from the module resolved through <c>module_lecturers</c>.
/// </summary>
public sealed class AnnouncementWriteScope
{
    private AnnouncementWriteScope(Guid? moduleId) => ModuleId = moduleId;

    /// <summary>The administrator routes and the results publication.</summary>
    public static AnnouncementWriteScope Administrator { get; } = new(null);

    /// <summary>The module's id when the write is a module's lecturers'; null only for <see cref="Administrator"/>.</summary>
    public Guid? ModuleId { get; }

    public bool IsAdministrator => ModuleId is null;

    /// <summary>A module's lecturers, on the module they teach.</summary>
    public static AnnouncementWriteScope Module(Guid moduleId) =>
        moduleId == Guid.Empty ? throw new ArgumentException("A module scope needs the module's id.", nameof(moduleId)) : new(moduleId);
}

/// <summary>
/// Announcements (00-overview.md section 4.4, 02-api.md sections 8.2, 8.4, 8.5). Reads for every role; create, update,
/// move and soft delete for the lecturer and administrator routes (S6) and the results publication, each audited in
/// the same save. A change to a university announcement invalidates <c>announcements:university</c> after its save
/// when the service owns the transaction; inside a caller's transaction (a publish, a reschedule, a cancel) the
/// caller invalidates after its commit, so no fill can store the pre-commit list under the new generation.
/// </summary>
public sealed class AnnouncementService(
    RushDayDbContext db,
    AnnouncementCache universityCache,
    AuditWriter audit,
    TimeProvider clock)
{
    /// <summary>
    /// <c>GET /api/announcements</c>: visible now; a student sees university announcements plus those of modules with an
    /// active enrolment in <paramref name="currentYear"/>, a lecturer university plus assigned modules, an
    /// administrator everything; pinned first, then newest first; at most 50. The university part is served from
    /// <c>announcements:university</c>; the module part is two queries (the caller's module ids; their announcements).
    /// </summary>
    public async Task<IReadOnlyList<AnnouncementRecord>> ListVisibleAsync(AnnouncementViewer viewer, string currentYear, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        var university = await universityCache.GetVisibleAsync(now, cancellationToken);

        var modules = AnnouncementQueries.VisibleAt(db, now).Where(a => a.Scope == AnnouncementScope.Module);
        if (!viewer.IsAdmin)
        {
            Guid[] moduleIds = [];
            if (viewer.StudentId is { } studentId)
            {
                moduleIds = await db.Enrolments.AsNoTracking()
                    .Where(e => e.StudentId == studentId && e.Status == EnrolmentStatus.Active && e.AcademicYear == currentYear)
                    .Select(e => e.ModuleId)
                    .ToArrayAsync(cancellationToken);
            }
            else if (viewer.LecturerId is { } lecturerId)
            {
                moduleIds = await db.ModuleLecturers.AsNoTracking()
                    .Where(ml => ml.LecturerId == lecturerId)
                    .Select(ml => ml.ModuleId)
                    .ToArrayAsync(cancellationToken);
            }

            if (moduleIds.Length == 0)
            {
                return [.. university.Take(AnnouncementQueries.ListLimit)];
            }

            modules = modules.Where(a => a.ModuleId != null && moduleIds.Contains(a.ModuleId.Value));
        }

        var moduleAnnouncements = await AnnouncementQueries.Project(db, modules)
            .Take(AnnouncementQueries.ListLimit)
            .ToListAsync(cancellationToken);

        return [.. AnnouncementQueries.InListOrder(university.Concat(moduleAnnouncements)).Take(AnnouncementQueries.ListLimit)];
    }

    /// <summary>A module's announcements for its lecturers: including future and expired, never deleted.</summary>
    public async Task<IReadOnlyList<AnnouncementRecord>> ListForModuleAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        var rows = db.Announcements.AsNoTracking().Where(a => a.Scope == AnnouncementScope.Module && a.ModuleId == moduleId && a.DeletedAt == null);
        return await AnnouncementQueries.Project(db, rows).ToListAsync(cancellationToken);
    }

    /// <summary>Every announcement of every scope for administrators: including future and expired, never deleted.</summary>
    public async Task<IReadOnlyList<AnnouncementRecord>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = db.Announcements.AsNoTracking().Where(a => a.DeletedAt == null);
        return await AnnouncementQueries.Project(db, rows).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates an announcement for <paramref name="scope"/>: a university one for <see cref="AnnouncementWriteScope.Administrator"/>,
    /// else one on the scope's module; audits <c>announcement.created</c>.
    /// </summary>
    public async Task<AnnouncementRecord> CreateAsync(AnnouncementDraft draft, AnnouncementWriteScope scope, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(scope);

        var now = clock.GetUtcNow();
        var announcement = new Announcement
        {
            Id = Guid.CreateVersion7(),
            Scope = scope.IsAdministrator ? AnnouncementScope.University : AnnouncementScope.Module,
            ModuleId = scope.ModuleId,
            Title = draft.Title,
            Body = draft.Body,
            Pinned = draft.Pinned,
            PublishedAt = draft.PublishedAt ?? now,
            ExpiresAt = draft.ExpiresAt,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Announcements.Add(announcement);

        var moduleCode = await ModuleCodeAsync(scope.ModuleId, cancellationToken);
        Audit(AuditActions.AnnouncementCreated, announcement, moduleCode);
        await db.SaveChangesAsync(cancellationToken);
        await InvalidateIfUniversityAsync(announcement.Scope, cancellationToken);

        return await ReadAsync(announcement.Id, cancellationToken);
    }

    /// <summary>
    /// Updates an announcement; audits <c>announcement.updated</c>. For a module scope the row is resolved only as
    /// <c>id = @id AND scope = 'Module' AND module_id = @module AND deleted_at IS NULL</c> (the lecturer routes, 02-api.md
    /// section 8.4); for the administrator any scope that is not deleted. Null when no such row exists (404
    /// <c>announcement-not-found</c>).
    /// </summary>
    public async Task<AnnouncementRecord?> UpdateAsync(Guid id, AnnouncementDraft draft, AnnouncementWriteScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var announcement = await FindEditableAsync(id, scope, cancellationToken);
        if (announcement is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        announcement.Title = draft.Title;
        announcement.Body = draft.Body;
        announcement.Pinned = draft.Pinned;
        announcement.PublishedAt = draft.PublishedAt ?? announcement.PublishedAt;
        announcement.ExpiresAt = draft.ExpiresAt;
        announcement.UpdatedAt = now;

        Audit(AuditActions.AnnouncementUpdated, announcement, await ModuleCodeAsync(announcement.ModuleId, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await InvalidateIfUniversityAsync(announcement.Scope, cancellationToken);

        return await ReadAsync(announcement.Id, cancellationToken);
    }

    /// <summary>
    /// Moves an announcement to another instant (resolved as in <see cref="UpdateAsync"/>), everything else unchanged;
    /// audits <c>announcement.updated</c>. A rescheduled publication moves its "results are available" announcement
    /// with it (review S6 E2). False when not found (or already deleted).
    /// </summary>
    public async Task<bool> MoveAsync(Guid id, DateTimeOffset publishedAt, AnnouncementWriteScope scope, CancellationToken cancellationToken = default)
    {
        var announcement = await FindEditableAsync(id, scope, cancellationToken);
        if (announcement is null)
        {
            return false;
        }

        announcement.PublishedAt = publishedAt;
        announcement.UpdatedAt = clock.GetUtcNow();

        Audit(AuditActions.AnnouncementUpdated, announcement, await ModuleCodeAsync(announcement.ModuleId, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await InvalidateIfUniversityAsync(announcement.Scope, cancellationToken);
        return true;
    }

    /// <summary>Soft-deletes an announcement (resolved as in <see cref="UpdateAsync"/>); audits <c>announcement.deleted</c>. False when not found.</summary>
    public async Task<bool> DeleteAsync(Guid id, AnnouncementWriteScope scope, CancellationToken cancellationToken = default)
    {
        var announcement = await FindEditableAsync(id, scope, cancellationToken);
        if (announcement is null)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        announcement.DeletedAt = now;
        announcement.UpdatedAt = now;

        Audit(AuditActions.AnnouncementDeleted, announcement, await ModuleCodeAsync(announcement.ModuleId, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await InvalidateIfUniversityAsync(announcement.Scope, cancellationToken);
        return true;
    }

    private Task<Announcement?> FindEditableAsync(Guid id, AnnouncementWriteScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var rows = db.Announcements.Where(a => a.Id == id && a.DeletedAt == null);
        if (scope.ModuleId is { } moduleId)
        {
            rows = rows.Where(a => a.Scope == AnnouncementScope.Module && a.ModuleId == moduleId);
        }

        return rows.SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<AnnouncementRecord> ReadAsync(Guid id, CancellationToken cancellationToken) =>
        await AnnouncementQueries.Project(db, db.Announcements.AsNoTracking().Where(a => a.Id == id)).SingleAsync(cancellationToken);

    private async Task<string?> ModuleCodeAsync(Guid? moduleId, CancellationToken cancellationToken) =>
        moduleId is { } id
            ? await db.Modules.AsNoTracking().Where(m => m.Id == id).Select(m => m.Code).SingleOrDefaultAsync(cancellationToken)
            : null;

    private void Audit(string action, Announcement announcement, string? moduleCode) =>
        audit.Record(
            db,
            action,
            AuditSubjects.Announcement,
            announcement.Id.ToString(),
            new { scope = announcement.Scope == AnnouncementScope.University ? "university" : "module", moduleCode, title = announcement.Title },
            moduleId: announcement.ModuleId);

    /// <summary>
    /// Only when the save was the service's own: inside a caller's transaction the change is not committed yet, so a
    /// fill started now would read the old rows into the new generation; that caller invalidates after its commit.
    /// </summary>
    private async Task InvalidateIfUniversityAsync(AnnouncementScope scope, CancellationToken cancellationToken)
    {
        if (scope == AnnouncementScope.University && db.Database.CurrentTransaction is null)
        {
            await universityCache.InvalidateAsync(cancellationToken);
        }
    }
}
