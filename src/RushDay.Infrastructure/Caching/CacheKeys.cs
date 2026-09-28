using Microsoft.Extensions.Caching.Hybrid;

namespace RushDay.Infrastructure.Caching;

/// <summary>
/// The in-process cache entries of 04-performance-and-ops.md section 4 (HybridCache, no L2). Each wrapper records
/// <c>rushday.cache.requests{cache, result}</c> through <see cref="ICacheMetrics"/>.
/// </summary>
public static class CacheKeys
{
    public const string Catalogue = "catalogue:all";
    public const string Windows = "windows:all";
    public const string Settings = "settings";
    public const string PublicationsBrief = "publications:brief";
    public const string UniversityAnnouncements = "announcements:university";
    public const string LecturerModulesPrefix = "lecturer-modules:";

    /// <summary>The <c>cache</c> tag of the metric: the key without its per-entity suffix.</summary>
    public const string LecturerModulesName = "lecturer-modules";

    public static string LecturerModules(Guid lecturerId) => LecturerModulesPrefix + lecturerId.ToString("D");

    public static HybridCacheEntryOptions For(TimeSpan lifetime) => new()
    {
        Expiration = lifetime,
        LocalCacheExpiration = lifetime,
    };

    /// <summary>
    /// Reads through <paramref name="cache"/> and records hit or miss: a miss is exactly a call of the factory.
    /// </summary>
    public static async ValueTask<T> GetOrCreateAsync<T>(
        this HybridCache cache,
        ICacheMetrics metrics,
        string key,
        string metricName,
        TimeSpan lifetime,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken)
    {
        var missed = false;
        var value = await cache.GetOrCreateAsync(
            key,
            factory,
            async (state, ct) =>
            {
                missed = true;
                return await state(ct);
            },
            For(lifetime),
            cancellationToken: cancellationToken);

        metrics.CacheRequest(metricName, hit: !missed);
        return value;
    }
}

/// <summary>Implemented by the API's <c>RushDayMetrics</c>, which declares every instrument (04 section 6.1).</summary>
public interface ICacheMetrics
{
    void CacheRequest(string cache, bool hit);
}
