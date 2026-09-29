using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Caching;

/// <summary>One <c>enrolment_windows</c> row as cached. State is evaluated against the request's clock, never cached.</summary>
[ImmutableObject(true)]
public sealed record EnrolmentWindowSnapshot(
    Guid Id,
    string AcademicYear,
    Semester Semester,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    DateTimeOffset WithdrawalDeadlineAt)
{
    /// <summary>Self-enrolment is allowed iff <c>opens_at &lt;= now &lt; closes_at</c>.</summary>
    public bool IsOpenAt(DateTimeOffset now) => OpensAt <= now && now < ClosesAt;

    public bool AllowsWithdrawalAt(DateTimeOffset now) => now < WithdrawalDeadlineAt;
}

/// <summary><c>windows:all</c>, 60 s: every window of every year. Invalidated by the admin window routes (S6).</summary>
public sealed class EnrolmentWindowCache(HybridCache cache, IDbContextFactory<RushDayDbContext> contexts, ICacheMetrics metrics)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public async ValueTask<IReadOnlyList<EnrolmentWindowSnapshot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var holder = await cache.GetOrCreateAsync(
            metrics,
            CacheKeys.Windows,
            CacheKeys.Windows,
            Lifetime,
            async ct => new WindowsHolder(await LoadAsync(ct)),
            cancellationToken);
        return holder.Windows;
    }

    /// <summary>The windows of one academic year, autumn first.</summary>
    public async ValueTask<IReadOnlyList<EnrolmentWindowSnapshot>> ForYearAsync(string academicYear, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return [.. all.Where(w => w.AcademicYear == academicYear).OrderBy(w => w.Semester)];
    }

    public async ValueTask<EnrolmentWindowSnapshot?> FindAsync(string academicYear, Semester semester, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(w => w.AcademicYear == academicYear && w.Semester == semester);
    }

    /// <summary>False when no window exists for the (year, semester).</summary>
    public async ValueTask<bool> IsOpenAsync(string academicYear, Semester semester, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var window = await FindAsync(academicYear, semester, cancellationToken);
        return window?.IsOpenAt(now) ?? false;
    }

    public ValueTask InvalidateAsync(CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(CacheKeys.Windows, cancellationToken);

    // Its own context: the fill may outlive the request that started it (04-performance-and-ops.md section 4).
    private async Task<EnrolmentWindowSnapshot[]> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.EnrolmentWindows.AsNoTracking()
            .OrderByDescending(w => w.AcademicYear)
            .ThenBy(w => w.Semester)
            .Select(w => new EnrolmentWindowSnapshot(w.Id, w.AcademicYear, w.Semester, w.OpensAt, w.ClosesAt, w.WithdrawalDeadlineAt))
            .ToArrayAsync(cancellationToken);
    }

    [ImmutableObject(true)]
    private sealed record WindowsHolder(IReadOnlyList<EnrolmentWindowSnapshot> Windows);
}
