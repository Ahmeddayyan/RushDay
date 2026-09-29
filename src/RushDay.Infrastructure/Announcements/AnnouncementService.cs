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
/// Announcements (00-overview.md section 4.4, 02-api.md sections 8.2, 8.4, 8.5). Reads for every role; create, update
/// and soft delete for the lecturer and administrator routes (S6), each audited in the same save and invalidating
/// <c>announcements:university</c> when a university announcement changes.
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

    /// <summary>Creates a university announcement (<paramref name="moduleId"/> null) or one on a module; audits <c>announcement.created</c>.</summary>
    public async Task<AnnouncementRecord> CreateAsync(AnnouncementDraft draft, Guid? moduleId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var now = clock.GetUtcNow();
        var announcement = new Announcement
        {
            Id = Guid.CreateVersion7(),
            Scope = moduleId is null ? AnnouncementScope.University : AnnouncementScope.Module,
            ModuleId = moduleId,
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

        var moduleCode = await ModuleCodeAsync(moduleId, cancellationToken);
        Audit(AuditActions.AnnouncementCreated, announcement, moduleCode);
        await db.SaveChangesAsync(cancellationToken);
        await InvalidateIfUniversityAsync(announcement.Scope, cancellationToken);

        return await ReadAsync(announcement.Id, cancellationToken);
    }

    /// <summary>
    /// Updates an announcement; audits <c>announcement.updated</c>. With <paramref name="moduleScope"/> the row is
    /// resolved only as <c>id = @id AND scope = 'Module' AND module_id = @moduleScope AND deleted_at IS NULL</c> (the
    /// lecturer routes, 02-api.md section 8.4); without it any scope that is not deleted (administrators). Null when
    /// no such row exists (404 <c>announcement-not-found</c>).
    /// </summary>
    public async Task<AnnouncementRecord?> UpdateAsync(Guid id, AnnouncementDraft draft, Guid? moduleScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var announcement = await FindEditableAsync(id, moduleScope, cancellationToken);
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

    /// <summary>Soft-deletes an announcement (resolved as in <see cref="UpdateAsync"/>); audits <c>announcement.deleted</c>. False when not found.</summary>
    public async Task<bool> DeleteAsync(Guid id, Guid? moduleScope, CancellationToken cancellationToken = default)
    {
        var announcement = await FindEditableAsync(id, moduleScope, cancellationToken);
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

    private Task<Announcement?> FindEditableAsync(Guid id, Guid? moduleScope, CancellationToken cancellationToken)
    {
        var rows = db.Announcements.Where(a => a.Id == id && a.DeletedAt == null);
        if (moduleScope is { } moduleId)
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

    private async Task InvalidateIfUniversityAsync(AnnouncementScope scope, CancellationToken cancellationToken)
    {
        if (scope == AnnouncementScope.University)
        {
            await universityCache.InvalidateAsync(cancellationToken);
        }
    }
}
