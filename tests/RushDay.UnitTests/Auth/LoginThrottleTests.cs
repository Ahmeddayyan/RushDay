using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RushDay.Api.Auth;
using RushDay.Api.Options;

namespace RushDay.UnitTests.Auth;

/// <summary>The reserve-then-refund windows of the login protection (02-api.md section 5).</summary>
public sealed class LoginThrottleTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Window_refuses_beyond_its_limit_and_names_the_wait()
    {
        var clock = new FakeTimeProvider(Start);
        var window = new SlidingWindowCounter(2, TimeSpan.FromSeconds(60), 6, clock);

        Assert.True(window.TryReserve("a", out _, out _));
        Assert.True(window.TryReserve("a", out _, out _));
        Assert.False(window.TryReserve("a", out _, out var retryAfter));
        Assert.InRange(retryAfter, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));

        // Partitions are independent.
        Assert.True(window.TryReserve("b", out _, out _));
    }

    [Fact]
    public void Refunded_permits_are_free_again()
    {
        var window = new SlidingWindowCounter(1, TimeSpan.FromSeconds(60), 6, new FakeTimeProvider(Start));

        Assert.True(window.TryReserve("a", out var permit, out _));
        window.Refund(permit);

        Assert.Equal(0, window.CountOf("a"));
        Assert.True(window.TryReserve("a", out _, out _));
    }

    [Fact]
    public void Permits_leave_the_window_segment_by_segment()
    {
        var clock = new FakeTimeProvider(Start);
        var window = new SlidingWindowCounter(2, TimeSpan.FromSeconds(60), 6, clock);
        Assert.True(window.TryReserve("a", out _, out _));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(window.TryReserve("a", out _, out _));
        Assert.False(window.TryReserve("a", out _, out _));

        // The first permit (taken at 0 s) is out after 60 s; the second (30 s) is still in.
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(1, window.CountOf("a"));
        Assert.True(window.TryReserve("a", out _, out _));
    }

    [Fact]
    public void A_refund_after_the_permit_left_the_window_changes_nothing()
    {
        var clock = new FakeTimeProvider(Start);
        var window = new SlidingWindowCounter(1, TimeSpan.FromSeconds(60), 6, clock);
        Assert.True(window.TryReserve("a", out var old, out _));
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True(window.TryReserve("a", out _, out _));

        window.Refund(old);

        Assert.Equal(1, window.CountOf("a"));
    }

    [Fact]
    public async Task Concurrent_reservations_never_exceed_the_limit()
    {
        var window = new SlidingWindowCounter(5, TimeSpan.FromMinutes(10), 10, new FakeTimeProvider(Start));

        var granted = await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() => window.TryReserve("one-address", out _, out _))));

        Assert.Equal(5, granted.Count(g => g));
    }

    [Fact]
    public void Idle_partitions_are_bounded()
    {
        var clock = new FakeTimeProvider(Start);
        var window = new SlidingWindowCounter(1, TimeSpan.FromSeconds(60), 6, clock, maxPartitions: 10);

        for (var i = 0; i < 100; i++)
        {
            Assert.True(window.TryReserve("user-" + i, out _, out _));
        }

        // The least recently used partitions made room; the newest is still counted.
        Assert.Equal(1, window.CountOf("user-99"));
        Assert.Equal(0, window.CountOf("user-0"));
    }

    [Fact]
    public void A_successful_attempt_refunds_both_windows_and_a_failed_one_keeps_them()
    {
        using var throttle = Throttle(perUser: 1, perAddress: 2);

        using (var success = throttle.TryBegin("ALICE", "198.51.100.1"))
        {
            Assert.True(success.Allowed);
        }

        using (var failure = throttle.TryBegin("ALICE", "198.51.100.1"))
        {
            Assert.True(failure.Allowed);
            failure.MarkFailed();
        }

        // Alice's window (1) is now spent by the failure; the success cost nothing.
        using var refused = throttle.TryBegin("ALICE", "198.51.100.2");
        Assert.False(refused.Allowed);
        Assert.True(refused.RetryAfterSeconds >= 1);
    }

    [Fact]
    public void A_refused_username_does_not_keep_the_address_permit()
    {
        using var throttle = Throttle(perUser: 1, perAddress: 1);
        using (var failure = throttle.TryBegin("ALICE", "198.51.100.1"))
        {
            failure.MarkFailed();
        }

        // ALICE is spent, so this attempt is refused; the address permit it reserved first goes back.
        using (var refused = throttle.TryBegin("ALICE", "198.51.100.9"))
        {
            Assert.False(refused.Allowed);
        }

        using var other = throttle.TryBegin("BOB", "198.51.100.9");
        Assert.True(other.Allowed);
    }

    [Fact]
    public void Failures_count_distinct_address_keys_per_username()
    {
        using var throttle = Throttle(perUser: 100, perAddress: 100);

        Assert.Equal(1, throttle.RecordFailure("hash", "198.51.100.1"));
        Assert.Equal(1, throttle.RecordFailure("hash", "198.51.100.1"));
        Assert.Equal(2, throttle.RecordFailure("hash", "2001:db8:1:1::/64"));
        Assert.Equal(1, throttle.RecordFailure("other", "198.51.100.1"));
    }

    private static LoginThrottle Throttle(int perUser, int perAddress) =>
        new(Options.Create(new RateLimitingOptions { LoginPerUserPerMinute = perUser, LoginFailuresPerIpPer10Minutes = perAddress }), new FakeTimeProvider(Start));
}
