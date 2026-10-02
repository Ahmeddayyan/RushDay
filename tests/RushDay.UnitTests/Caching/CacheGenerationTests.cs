using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Infrastructure.Caching;

namespace RushDay.UnitTests.Caching;

/// <summary>
/// Generation-versioned cache keys (04-performance-and-ops.md section 4; review S4 findings C3, C11 and D5), the one
/// mechanism behind <c>settings</c>, <c>catalogue:all</c> and <c>announcements:university</c>: a fill that was already
/// running when its entry was invalidated stores its (possibly pre-change) value under a retired key, so the next reader
/// misses and fills afresh instead of being served that value for a whole lifetime.
/// </summary>
public sealed class CacheGenerationTests : IDisposable
{
    private const string Key = "unit:versioned";

    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ServiceProvider _services = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();

    private readonly RecordingMetrics _metrics = new();

    private HybridCache Cache => _services.GetRequiredService<HybridCache>();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task An_invalidation_during_a_fill_retires_the_value_that_fill_stores()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fill = Cache.GetOrCreateVersionedAsync(_metrics, Key, Key, Lifetime, async _ =>
        {
            entered.SetResult();
            await release.Task;
            return "read before the change";
        }, CancellationToken.None).AsTask();

        await entered.Task;
        await Cache.InvalidateVersionedAsync(Key);   // the change has committed; its route invalidates
        release.SetResult();                         // the fill that read the old rows completes afterwards

        Assert.Equal("read before the change", await fill);
        var next = await Cache.GetOrCreateVersionedAsync(_metrics, Key, Key, Lifetime, _ => ValueTask.FromResult("read after the change"), CancellationToken.None);
        Assert.Equal("read after the change", next);
        Assert.Equal([(Key, false), (Key, false)], _metrics.Requests);
    }

    /// <summary>Why the generation exists: <c>RemoveAsync</c> alone does not stop an in-flight fill from storing its value.</summary>
    [Fact]
    public async Task A_plain_remove_during_a_fill_is_undone_by_that_fill()
    {
        const string plainKey = "unit:plain";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = CacheKeys.For(Lifetime);
        var fill = Cache.GetOrCreateAsync(plainKey, async _ =>
        {
            entered.SetResult();
            await release.Task;
            return "read before the change";
        }, options).AsTask();

        await entered.Task;
        await Cache.RemoveAsync(plainKey);
        release.SetResult();
        await fill;

        Assert.Equal("read before the change", await Cache.GetOrCreateAsync(plainKey, _ => ValueTask.FromResult("read after the change"), options));
    }

    [Fact]
    public async Task Reads_hit_until_invalidated_and_the_metric_keeps_the_plain_name()
    {
        var fills = 0;
        ValueTask<int> Factory(CancellationToken _) => ValueTask.FromResult(Interlocked.Increment(ref fills));

        Assert.Equal(1, await Cache.GetOrCreateVersionedAsync(_metrics, Key, "unit", Lifetime, Factory, CancellationToken.None));
        Assert.Equal(1, await Cache.GetOrCreateVersionedAsync(_metrics, Key, "unit", Lifetime, Factory, CancellationToken.None));
        await Cache.InvalidateVersionedAsync(Key);
        Assert.Equal(2, await Cache.GetOrCreateVersionedAsync(_metrics, Key, "unit", Lifetime, Factory, CancellationToken.None));

        Assert.Equal([("unit", false), ("unit", true), ("unit", false)], _metrics.Requests);
    }

    [Fact]
    public void Generations_belong_to_one_cache_instance()
    {
        using var other = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider();
        var otherCache = other.GetRequiredService<HybridCache>();

        Assert.Equal(Key + ":v0", CacheGenerations.CurrentKey(Cache, Key));
        Assert.Equal(Key + ":v0", CacheGenerations.Advance(Cache, Key));
        Assert.Equal(Key + ":v1", CacheGenerations.CurrentKey(Cache, Key));
        Assert.Equal(Key + ":v0", CacheGenerations.CurrentKey(otherCache, Key));
    }

    private sealed class RecordingMetrics : ICacheMetrics
    {
        public List<(string Cache, bool Hit)> Requests { get; } = [];

        public void CacheRequest(string cache, bool hit)
        {
            lock (Requests)
            {
                Requests.Add((cache, hit));
            }
        }
    }
}
