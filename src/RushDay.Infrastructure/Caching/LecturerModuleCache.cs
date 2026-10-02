using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Caching;

/// <summary>
/// <c>lecturer-modules:{lecturerId}</c>, 60 s: the module codes a lecturer is assigned to, for the
/// <c>TeachesModule</c> policy (02-api.md section 4). Invalidated by <c>PUT /api/admin/modules/{code}/lecturers</c>
/// for every lecturer added or removed and by a lecturer leaving (S6). A lecturer who has left teaches nothing (review
/// S6 E5). An unknown lecturer or code yields an empty set, so a non-member always gets 403, never 404. The key is
/// generation-versioned (joint item J5), so a fill that read the assignments before a removal committed cannot keep
/// the removed lecturer in for another lifetime.
/// </summary>
public sealed class LecturerModuleCache(HybridCache cache, IDbContextFactory<RushDayDbContext> contexts, ICacheMetrics metrics)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public async ValueTask<IReadOnlyList<string>> GetCodesAsync(Guid lecturerId, CancellationToken cancellationToken = default)
    {
        var holder = await cache.GetOrCreateVersionedAsync(
            metrics,
            CacheKeys.LecturerModules(lecturerId),
            CacheKeys.LecturerModulesName,
            Lifetime,
            async ct => new CodesHolder(await LoadAsync(lecturerId, ct)),
            cancellationToken);
        return holder.Codes;
    }

    public async ValueTask<bool> TeachesAsync(Guid lecturerId, string moduleCode, CancellationToken cancellationToken = default)
    {
        var codes = await GetCodesAsync(lecturerId, cancellationToken);
        return codes.Contains(moduleCode.ToUpperInvariant(), StringComparer.Ordinal);
    }

    public ValueTask InvalidateAsync(Guid lecturerId, CancellationToken cancellationToken = default) =>
        cache.InvalidateVersionedAsync(CacheKeys.LecturerModules(lecturerId), cancellationToken);

    // Its own context: the fill may outlive the request that started it (04-performance-and-ops.md section 4).
    private async Task<string[]> LoadAsync(Guid lecturerId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await (
                from ml in db.ModuleLecturers.AsNoTracking()
                join l in db.Lecturers.AsNoTracking() on ml.LecturerId equals l.Id
                join m in db.Modules.AsNoTracking() on ml.ModuleId equals m.Id
                where ml.LecturerId == lecturerId && l.LeftAt == null
                orderby m.Code
                select m.Code)
            .ToArrayAsync(cancellationToken);
    }

    [ImmutableObject(true)]
    private sealed record CodesHolder(IReadOnlyList<string> Codes);
}
