using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using RushDay.Domain.Modules;
using RushDay.Domain.Settings;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Caching;

/// <summary>The <c>academic_settings</c> row as cached (immutable, so HybridCache hands out one instance).</summary>
[ImmutableObject(true)]
public sealed record SettingsSnapshot(
    string AcademicYear,
    Semester CurrentSemester,
    string InstitutionName,
    string InstitutionShortName,
    string TimeZone,
    string? SupportEmail,
    string? SupportUrl,
    DateTimeOffset UpdatedAt);

/// <summary>
/// <c>settings</c>, 60 s. Invalidated by <c>PUT /api/admin/settings</c> (S6). Null only on a database whose backfills
/// have not created the row yet. The key is generation-versioned (<c>settings:v{n}</c>, <see cref="CacheKeys.GetOrCreateVersionedAsync{T}"/>), so
/// a fill that read the old row before a year change cannot outlive the invalidation.
/// </summary>
public sealed class SettingsCache(HybridCache cache, IDbContextFactory<RushDayDbContext> contexts, ICacheMetrics metrics)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public async ValueTask<SettingsSnapshot?> GetAsync(CancellationToken cancellationToken = default)
    {
        var holder = await cache.GetOrCreateVersionedAsync(
            metrics,
            CacheKeys.Settings,
            CacheKeys.Settings,
            Lifetime,
            async ct => new SettingsHolder(await LoadAsync(ct)),
            cancellationToken);
        return holder.Value;
    }

    public ValueTask InvalidateAsync(CancellationToken cancellationToken = default) =>
        cache.InvalidateVersionedAsync(CacheKeys.Settings, cancellationToken);

    // Its own context: the fill may outlive the request that started it (04-performance-and-ops.md section 4).
    private async Task<SettingsSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.AcademicSettings.AsNoTracking()
            .Where(s => s.Id == AcademicSettings.SingletonId)
            .Select(s => new SettingsSnapshot(
                s.AcademicYear,
                s.CurrentSemester,
                s.InstitutionName,
                s.InstitutionShortName,
                s.TimeZone,
                s.SupportEmail,
                s.SupportUrl,
                s.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Wraps a possibly-null row so "no row yet" is cached like any other value.</summary>
    [ImmutableObject(true)]
    private sealed record SettingsHolder(SettingsSnapshot? Value);
}
