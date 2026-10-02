namespace RushDay.Api.Auth;

/// <summary>
/// A partitioned sliding-window counter whose permits can be handed back (<see cref="Refund"/>), which the limiters of
/// <c>System.Threading.RateLimiting</c> cannot do. <see cref="LoginThrottle"/> uses it for "failed outcomes only"
/// windows without a check-then-consume race: every attempt reserves a permit before any work, and a success refunds
/// it, so concurrent attempts can never overrun the limit (02-api.md section 5). Time comes from the injected
/// <see cref="TimeProvider"/>; the window is split into equal segments, like <c>SlidingWindowRateLimiter</c>.
/// Memory is bounded: idle partitions are pruned, and beyond <c>maxPartitions</c> the least recently used go first.
/// </summary>
public sealed class SlidingWindowCounter
{
    private readonly int _limit;
    private readonly int _segments;
    private readonly long _segmentTicks;
    private readonly int _maxPartitions;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Partition> _partitions = new(StringComparer.Ordinal);
    private long _lastPruneSegment;
    private long _uses;

    public SlidingWindowCounter(int limit, TimeSpan window, int segments, TimeProvider clock, int maxPartitions = 100_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(window.Ticks, segments);
        ArgumentNullException.ThrowIfNull(clock);

        _limit = limit;
        _segments = segments;
        _segmentTicks = window.Ticks / segments;
        _maxPartitions = Math.Max(1, maxPartitions);
        _clock = clock;
    }

    public int Limit => _limit;

    /// <summary>Permits held in the current window for <paramref name="key"/>.</summary>
    public int CountOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var segment = CurrentSegment();
        lock (_gate)
        {
            return _partitions.TryGetValue(key, out var partition) ? partition.Count(segment, _segments) : 0;
        }
    }

    /// <summary>
    /// Takes one permit for <paramref name="key"/> when the window has room; otherwise returns false with the time until
    /// the oldest permit leaves the window.
    /// </summary>
    public bool TryReserve(string key, out WindowPermit permit, out TimeSpan retryAfter)
    {
        ArgumentNullException.ThrowIfNull(key);
        var segment = CurrentSegment();
        lock (_gate)
        {
            PruneIfDue(segment);
            if (!_partitions.TryGetValue(key, out var partition))
            {
                if (_partitions.Count >= _maxPartitions)
                {
                    MakeRoom(segment);
                }

                partition = new Partition(_segments);
                _partitions[key] = partition;
            }

            partition.LastUsed = ++_uses;
            if (partition.Count(segment, _segments) >= _limit)
            {
                var oldest = partition.OldestSegment(segment, _segments) ?? segment;
                var freedAt = (oldest + _segments) * _segmentTicks;
                retryAfter = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerSecond, freedAt - _clock.GetUtcNow().UtcTicks));
                permit = default;
                return false;
            }

            partition.Add(segment, _segments, 1);
            permit = new WindowPermit(key, segment);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    /// <summary>Hands a reserved permit back (a success); a no-op once its segment has left the window.</summary>
    public void Refund(WindowPermit permit)
    {
        if (permit.Key is null)
        {
            return;
        }

        var segment = CurrentSegment();
        lock (_gate)
        {
            if (segment - permit.Segment < _segments && _partitions.TryGetValue(permit.Key, out var partition))
            {
                partition.Add(permit.Segment, _segments, -1);
            }
        }
    }

    private long CurrentSegment() => _clock.GetUtcNow().UtcTicks / _segmentTicks;

    private void PruneIfDue(long segment)
    {
        if (segment - _lastPruneSegment < _segments)
        {
            return;
        }

        PruneIdle(segment);
    }

    private void PruneIdle(long segment)
    {
        _lastPruneSegment = segment;
        foreach (var (key, partition) in _partitions.ToList())
        {
            if (partition.Count(segment, _segments) == 0)
            {
                _partitions.Remove(key);
            }
        }
    }

    /// <summary>Full: drop idle partitions, and if that is not enough the least recently used tenth (one sort per batch).</summary>
    private void MakeRoom(long segment)
    {
        PruneIdle(segment);
        if (_partitions.Count < _maxPartitions)
        {
            return;
        }

        var batch = Math.Max(1, _maxPartitions / 10);
        foreach (var key in _partitions.OrderBy(p => p.Value.LastUsed).Take(batch).Select(p => p.Key).ToList())
        {
            _partitions.Remove(key);
        }
    }

    /// <summary>A ring of per-segment counts; slot <c>s % segments</c> belongs to segment <c>s</c> while it is in the window.</summary>
    private sealed class Partition(int segments)
    {
        // "Never used": far enough in the past to be outside any window, and far enough from MinValue not to overflow.
        private readonly long[] _segmentOf = [.. Enumerable.Repeat(long.MinValue / 2, segments)];
        private readonly int[] _counts = new int[segments];

        public long LastUsed { get; set; }

        public int Count(long current, int window)
        {
            var total = 0;
            for (var i = 0; i < _counts.Length; i++)
            {
                if (current - _segmentOf[i] < window)
                {
                    total += _counts[i];
                }
            }

            return total;
        }

        public long? OldestSegment(long current, int window)
        {
            long? oldest = null;
            for (var i = 0; i < _counts.Length; i++)
            {
                if (_counts[i] > 0 && current - _segmentOf[i] < window && (oldest is null || _segmentOf[i] < oldest))
                {
                    oldest = _segmentOf[i];
                }
            }

            return oldest;
        }

        public void Add(long segment, int window, int delta)
        {
            var slot = (int)(segment % window);
            if (_segmentOf[slot] != segment)
            {
                if (delta < 0)
                {
                    return;
                }

                _segmentOf[slot] = segment;
                _counts[slot] = 0;
            }

            _counts[slot] = Math.Max(0, _counts[slot] + delta);
        }
    }
}

/// <summary>A permit taken from a <see cref="SlidingWindowCounter"/>; <c>default</c> holds nothing.</summary>
public readonly record struct WindowPermit(string? Key, long Segment);
