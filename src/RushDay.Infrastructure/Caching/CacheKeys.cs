using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    /// Reads through <paramref name="cache"/> and records hit or miss: a miss is exactly a call of the factory. The
    /// factory runs inside <see cref="CacheFill"/>, so its commands are not counted as the request's own, and it must
    /// open its own context from <c>IDbContextFactory</c> rather than use the caller's scoped one (the caller's request
    /// may end while other callers still wait on the same fill).
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
                return await CacheFill.RunAsync(state, ct);
            },
            For(lifetime),
            cancellationToken: cancellationToken);

        metrics.CacheRequest(metricName, hit: !missed);
        return value;
    }

    /// <summary>
    /// <see cref="GetOrCreateAsync{T}"/> under a generation-versioned key, <c>{key}:v{generation}</c> (04 section 4):
    /// <see cref="InvalidateVersionedAsync"/> moves readers to the next generation, so a fill that was already running
    /// when the data changed (it may have read the old rows) stores its value under a key nobody reads any more.
    /// <c>HybridCache.RemoveAsync</c> alone cannot do that: it does not stop an in-flight fill, which then stores the
    /// pre-change value for a whole lifetime. The metric keeps the unversioned <paramref name="metricName"/>.
    /// </summary>
    public static ValueTask<T> GetOrCreateVersionedAsync<T>(
        this HybridCache cache,
        ICacheMetrics metrics,
        string key,
        string metricName,
        TimeSpan lifetime,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(metrics, CacheGenerations.CurrentKey(cache, key), metricName, lifetime, factory, cancellationToken);

    /// <summary>
    /// Invalidates a versioned entry: advances the generation first (every later read misses and fills afresh), then
    /// removes the retired generation's entry to free it. Call it after the change has committed.
    /// </summary>
    public static ValueTask InvalidateVersionedAsync(this HybridCache cache, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return cache.RemoveAsync(CacheGenerations.Advance(cache, key), cancellationToken);
    }
}

/// <summary>
/// The generation counters behind <see cref="CacheKeys.GetOrCreateVersionedAsync{T}"/>, one set per
/// <see cref="HybridCache"/> instance (a host), so two test hosts in one process never share a generation.
/// </summary>
public static class CacheGenerations
{
    private static readonly ConditionalWeakTable<HybridCache, ConcurrentDictionary<string, StrongBox<long>>> Counters = new();

    /// <summary>The key readers use now: <c>{key}:v{generation}</c>.</summary>
    public static string CurrentKey(HybridCache cache, string key) => Versioned(key, Volatile.Read(ref Counter(cache, key).Value));

    /// <summary>Moves <paramref name="key"/> to its next generation and returns the retired generation's key.</summary>
    public static string Advance(HybridCache cache, string key) => Versioned(key, Interlocked.Increment(ref Counter(cache, key).Value) - 1);

    private static string Versioned(string key, long generation) => key + ":v" + generation.ToString(CultureInfo.InvariantCulture);

    private static StrongBox<long> Counter(HybridCache cache, string key)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Counters.GetValue(cache, _ => new ConcurrentDictionary<string, StrongBox<long>>(StringComparer.Ordinal))
            .GetOrAdd(key, _ => new StrongBox<long>());
    }
}

/// <summary>
/// Marks the execution of a cache factory (04-performance-and-ops.md section 6.1): the API's <c>DbCommandCounter</c>
/// skips commands issued while <see cref="InProgress"/> is true, so a request that happens to fill a cache is measured
/// by its own queries only. The flag is an async-local set inside the factory's own async flow, so it never leaks
/// back to the caller.
/// </summary>
public static class CacheFill
{
    private static readonly AsyncLocal<bool> Active = new();

    public static bool InProgress => Active.Value;

    public static async ValueTask<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> factory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Active.Value = true;
        return await factory(cancellationToken);
    }
}

/// <summary>Implemented by the API's <c>RushDayMetrics</c>, which declares every instrument (04 section 6.1).</summary>
public interface ICacheMetrics
{
    void CacheRequest(string cache, bool hit);
}
