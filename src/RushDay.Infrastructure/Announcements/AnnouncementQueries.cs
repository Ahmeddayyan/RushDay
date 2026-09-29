using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Announcements;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Announcements;

/// <summary>An announcement as every read shows it: the data behind <c>AnnouncementView</c> (author = the creator's display name).</summary>
[ImmutableObject(true)]
public sealed record AnnouncementRecord(
    Guid Id,
    AnnouncementScope Scope,
    Guid? ModuleId,
    string? ModuleCode,
    string Title,
    string Body,
    bool Pinned,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Visible now: published, not expired (deleted rows are never read).</summary>
    public bool IsVisibleAt(DateTimeOffset now) => PublishedAt <= now && (ExpiresAt is null || ExpiresAt > now);
}

/// <summary>The shared announcement query shapes (02-api.md section 8.2, 04-performance-and-ops.md section 3).</summary>
public static class AnnouncementQueries
{
    /// <summary>The most any list shows (<c>GET /api/announcements</c>).</summary>
    public const int ListLimit = 50;

    /// <summary>The dashboard shows the latest five.</summary>
    public const int DashboardLimit = 5;

    /// <summary>Not deleted, <c>published_at &lt;= @now</c> and not expired.</summary>
    public static IQueryable<Announcement> VisibleAt(RushDayDbContext db, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Announcements.AsNoTracking()
            .Where(a => a.DeletedAt == null && a.PublishedAt <= now && (a.ExpiresAt == null || a.ExpiresAt > now));
    }

    /// <summary>Pinned first, then newest first, projected with the module code and the author's display name (one command).</summary>
    public static IQueryable<AnnouncementRecord> Project(RushDayDbContext db, IQueryable<Announcement> announcements)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(announcements);

        return from a in announcements
               join u in db.Users.AsNoTracking() on a.CreatedByUserId equals u.Id
               from m in db.Modules.AsNoTracking().Where(m => m.Id == a.ModuleId).DefaultIfEmpty()
               orderby a.Pinned descending, a.PublishedAt descending, a.Id descending
               select new AnnouncementRecord(
                   a.Id,
                   a.Scope,
                   a.ModuleId,
                   m == null ? null : m.Code,
                   a.Title,
                   a.Body,
                   a.Pinned,
                   a.PublishedAt,
                   a.ExpiresAt,
                   u.DisplayName,
                   a.CreatedAt,
                   a.UpdatedAt);
    }

    /// <summary>The list order of every announcement read: pinned first, then <c>publishedAt</c> descending.</summary>
    public static IEnumerable<AnnouncementRecord> InListOrder(IEnumerable<AnnouncementRecord> announcements)
    {
        ArgumentNullException.ThrowIfNull(announcements);
        return announcements.OrderByDescending(a => a.Pinned).ThenByDescending(a => a.PublishedAt).ThenByDescending(a => a.Id);
    }
}
