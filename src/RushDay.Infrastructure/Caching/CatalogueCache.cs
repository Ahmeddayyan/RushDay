using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Caching;

/// <summary>A lecturer assigned to a module, as <c>ModuleSummary.lecturers[]</c> shows them.</summary>
[ImmutableObject(true)]
public sealed record ModuleLecturerInfo(string StaffNumber, string FullName, string Title, ModuleLecturerRole Role, bool Left);

/// <summary>
/// One module with its lecturers: the data behind <c>ModuleSummary</c> and <c>ModuleDetail</c>. Window state is not part
/// of it; it is evaluated against the caller's clock on every read. <see cref="EnrolledCount"/> is the current
/// academic year's count (<c>modules.enrolled_count</c>, D28).
/// </summary>
[ImmutableObject(true)]
public sealed record CatalogueModule(
    Guid Id,
    string Code,
    string Title,
    string Department,
    string? Description,
    int Credits,
    Semester Semester,
    int Capacity,
    int EnrolledCount,
    bool IsActive,
    IReadOnlyList<ModuleLecturerInfo> Lecturers)
{
    /// <summary>The level is the third character of the code: CS3099 is a level-3 module.</summary>
    public int Level => Code[2] - '0';

    /// <summary><c>max(0, capacity - enrolledCount)</c>.</summary>
    public int PlacesRemaining => Math.Max(0, Capacity - EnrolledCount);

    /// <summary>Leader first, then by staff number: the order every module shape lists lecturers in.</summary>
    public static IReadOnlyList<ModuleLecturerInfo> Order(IEnumerable<ModuleLecturerInfo> lecturers) =>
        [.. lecturers.OrderBy(l => l.Role).ThenBy(l => l.StaffNumber, StringComparer.Ordinal)];
}

/// <summary>
/// <c>catalogue:all</c>, 30 s (04-performance-and-ops.md section 4, D13): every active module ordered by code with its
/// lecturers, built with two queries (modules; module_lecturers ⋈ lecturers) and viewer-agnostic, so a rush on the
/// catalogue does no database work. Invalidated by the administrator's module routes and trim (S6).
/// </summary>
public sealed class CatalogueCache(HybridCache cache, IDbContextFactory<RushDayDbContext> contexts, ICacheMetrics metrics)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    public async ValueTask<IReadOnlyList<CatalogueModule>> GetAsync(CancellationToken cancellationToken = default)
    {
        var holder = await cache.GetOrCreateAsync(
            metrics,
            CacheKeys.Catalogue,
            CacheKeys.Catalogue,
            Lifetime,
            async ct => new CatalogueHolder(await LoadAsync(ct)),
            cancellationToken);
        return holder.Modules;
    }

    public ValueTask InvalidateAsync(CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(CacheKeys.Catalogue, cancellationToken);

    /// <summary>The lecturers of the given modules (all when <paramref name="moduleIds"/> is null), grouped by module id.</summary>
    public static async Task<Dictionary<Guid, IReadOnlyList<ModuleLecturerInfo>>> LoadLecturersAsync(
        RushDayDbContext db,
        IReadOnlyCollection<Guid>? moduleIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var assignments = db.ModuleLecturers.AsNoTracking();
        if (moduleIds is not null)
        {
            assignments = assignments.Where(ml => moduleIds.Contains(ml.ModuleId));
        }

        var rows = await (
            from ml in assignments
            join l in db.Lecturers.AsNoTracking() on ml.LecturerId equals l.Id
            select new { ml.ModuleId, Lecturer = new ModuleLecturerInfo(l.StaffNumber, l.FullName, l.Title, ml.Role, l.LeftAt != null) })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ModuleId)
            .ToDictionary(g => g.Key, g => CatalogueModule.Order(g.Select(r => r.Lecturer)));
    }

    // Its own context: the fill may outlive the request that started it (04-performance-and-ops.md section 4).
    private async Task<CatalogueModule[]> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var modules = await db.Modules.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Code)
            .Select(m => new { m.Id, m.Code, m.Title, m.Department, m.Description, m.Credits, m.Semester, m.Capacity, m.EnrolledCount, m.IsActive })
            .ToListAsync(cancellationToken);

        // Every assignment in one query; the handful that belong to inactive modules are simply not looked up.
        var lecturers = await LoadLecturersAsync(db, moduleIds: null, cancellationToken);

        return
        [
            .. modules.Select(m => new CatalogueModule(
                m.Id,
                m.Code,
                m.Title,
                m.Department,
                m.Description,
                m.Credits,
                m.Semester,
                m.Capacity,
                m.EnrolledCount,
                m.IsActive,
                lecturers.TryGetValue(m.Id, out var assigned) ? assigned : [])),
        ];
    }

    [ImmutableObject(true)]
    private sealed record CatalogueHolder(IReadOnlyList<CatalogueModule> Modules);
}
