using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Caching;

/// <summary>A <c>results_publications</c> row reduced to what the public brief needs.</summary>
[ImmutableObject(true)]
public sealed record PublicationSnapshot(Guid Id, string AcademicYear, Semester Semester, DateTimeOffset PublishAt)
{
    public bool IsLiveAt(DateTimeOffset now) => PublishAt <= now;
}

/// <summary>The next scheduled and the latest live publication at one instant.</summary>
public sealed record PublicationBriefs(PublicationSnapshot? Next, PublicationSnapshot? Latest);

/// <summary>
/// <c>publications:brief</c>, 60 s. The cached value is the list of publication instants, not the answer: which one is
/// "next" and which "latest" is decided against the caller's clock on every read, so a publication becomes live at
/// its instant exactly rather than up to a minute later. Invalidated by publish, reschedule, cancel, unpublish and
/// return-to-draft (S6).
/// </summary>
public sealed class PublicationCache(HybridCache cache, IDbContextFactory<RushDayDbContext> contexts, ICacheMetrics metrics)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public async ValueTask<PublicationBriefs> GetBriefAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var holder = await cache.GetOrCreateAsync(
            metrics,
            CacheKeys.PublicationsBrief,
            CacheKeys.PublicationsBrief,
            Lifetime,
            async ct => new PublicationsHolder(await LoadAsync(ct)),
            cancellationToken);

        return Brief(holder.Publications, now);
    }

    public ValueTask InvalidateAsync(CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(CacheKeys.PublicationsBrief, cancellationToken);

    /// <summary>Next = earliest <c>publish_at &gt; now</c>; latest = latest <c>publish_at &lt;= now</c>.</summary>
    public static PublicationBriefs Brief(IEnumerable<PublicationSnapshot> publications, DateTimeOffset now)
    {
        PublicationSnapshot? next = null;
        PublicationSnapshot? latest = null;
        foreach (var publication in publications)
        {
            if (publication.PublishAt > now)
            {
                if (next is null || publication.PublishAt < next.PublishAt)
                {
                    next = publication;
                }
            }
            else if (latest is null || publication.PublishAt > latest.PublishAt)
            {
                latest = publication;
            }
        }

        return new PublicationBriefs(next, latest);
    }

    // Its own context: the fill may outlive the request that started it (04-performance-and-ops.md section 4).
    private async Task<PublicationSnapshot[]> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.ResultsPublications.AsNoTracking()
            .Select(p => new PublicationSnapshot(p.Id, p.AcademicYear, p.Semester, p.PublishAt))
            .ToArrayAsync(cancellationToken);
    }

    [ImmutableObject(true)]
    private sealed record PublicationsHolder(IReadOnlyList<PublicationSnapshot> Publications);
}
