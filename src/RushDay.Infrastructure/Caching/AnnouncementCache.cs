using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using RushDay.Domain.Announcements;
using RushDay.Infrastructure.Announcements;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Caching;

/// <summary>
/// <c>announcements:university</c>, 30 s (04-performance-and-ops.md section 4): the university-scope announcements that
/// are not deleted and had not expired when the entry was filled. Like <c>publications:brief</c>, the entry holds the
/// rows, not the answer: which are visible is decided against the caller's clock on every read, so a scheduled
/// announcement appears at its instant exactly. Invalidated by the announcement mutations (<c>AnnouncementService</c>)
/// and by results publication with <c>announce</c> (S6).
/// </summary>
public sealed class AnnouncementCache(HybridCache cache, IDbContextFactory<RushDayDbContext> contexts, ICacheMetrics metrics, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    /// <summary>The university announcements visible at <paramref name="now"/>, pinned first, then newest first.</summary>
    public async ValueTask<IReadOnlyList<AnnouncementRecord>> GetVisibleAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var holder = await cache.GetOrCreateAsync(
            metrics,
            CacheKeys.UniversityAnnouncements,
            CacheKeys.UniversityAnnouncements,
            Lifetime,
            async ct => new AnnouncementsHolder(await LoadAsync(ct)),
            cancellationToken);

        return [.. holder.Announcements.Where(a => a.IsVisibleAt(now))];
    }

    public ValueTask InvalidateAsync(CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(CacheKeys.UniversityAnnouncements, cancellationToken);

    // Its own context: the fill may outlive the request that started it (04-performance-and-ops.md section 4).
    private async Task<AnnouncementRecord[]> LoadAsync(CancellationToken cancellationToken)
    {
        var filledAt = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var university = db.Announcements.AsNoTracking()
            .Where(a => a.Scope == AnnouncementScope.University && a.DeletedAt == null && (a.ExpiresAt == null || a.ExpiresAt > filledAt));
        return await AnnouncementQueries.Project(db, university).ToArrayAsync(cancellationToken);
    }

    [ImmutableObject(true)]
    private sealed record AnnouncementsHolder(IReadOnlyList<AnnouncementRecord> Announcements);
}
