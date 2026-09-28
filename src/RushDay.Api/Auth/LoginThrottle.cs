using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;
using RushDay.Infrastructure.Identity;

namespace RushDay.Api.Auth;

/// <summary>The verdict of <see cref="LoginThrottle.TryAcquire"/>.</summary>
public readonly record struct ThrottleDecision(bool Allowed, int RetryAfterSeconds)
{
    public static ThrottleDecision Allow { get; } = new(true, 0);
}

/// <summary>
/// The in-handler half of the login protection (D6, 02-api.md sections 2.3 and 5), a singleton injected into login and
/// MFA verification. It owns three limiters: a per-username sliding window (every attempt counts), a per-IP sliding
/// window of <b>failed</b> outcomes (checked before any lookup, consumed only on failure) and the CPU guard
/// <see cref="Cpu"/> around PBKDF2. It also keeps the failure map that decides when Identity lockout may count a
/// failure: only once an account's failures in 15 minutes come from at least <c>LockoutDistinctIps</c> addresses, so a
/// single address can never lock a victim (it is stopped by its own failure window first).
/// </summary>
public sealed class LoginThrottle : IDisposable
{
    public const int MaxFailureEntries = 50_000;

    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan UserWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan IpFailureWindow = TimeSpan.FromMinutes(10);

    private readonly PartitionedRateLimiter<string> _perUser;
    private readonly PartitionedRateLimiter<string> _ipFailures;
    private readonly TimeProvider _clock;
    private readonly Lazy<string> _dummyHash;
    private readonly IPasswordHasher<ApplicationUser> _hasher;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<string, DateTimeOffset>> _failures = new(StringComparer.Ordinal);
    private int _failureEntries;

    public LoginThrottle(IOptions<RateLimitingOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        _clock = clock;
        _hasher = PasswordHashing.Create();

        _perUser = PartitionedRateLimiter.Create<string, string>(username => RateLimitPartition.GetSlidingWindowLimiter(username, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, limits.LoginPerUserPerMinute),
            Window = UserWindow,
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

        _ipFailures = PartitionedRateLimiter.Create<string, string>(ip => RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, limits.LoginFailuresPerIpPer10Minutes),
            Window = IpFailureWindow,
            SegmentsPerWindow = 10,
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

        Cpu = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = Math.Max(1, limits.LoginConcurrency),
            QueueLimit = Math.Max(0, limits.LoginQueue),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });

        LockoutDistinctIps = Math.Max(1, limits.LockoutDistinctIps);

        // A hash to verify against when there is no real one, so an unknown, disabled or locked account costs one
        // PBKDF2 like a wrong password does (the failure path is constant-time).
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(DummyUser, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))));
    }

    /// <summary>The CPU guard: at most <c>LoginConcurrency</c> PBKDF2 operations at once, <c>LoginQueue</c> waiting.</summary>
    public ConcurrencyLimiter Cpu { get; }

    public int LockoutDistinctIps { get; }

    private static ApplicationUser DummyUser { get; } = new() { UserName = "dummy", DisplayName = "dummy" };

    /// <summary>First 12 hex characters of SHA-256 of the normalised username (the only form logs and audit hold).</summary>
    public static string UsernameHash(string normalizedUsername) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUsername)))[..12];

    /// <summary>
    /// Checks the per-IP failure window without consuming it, then consumes one attempt from the per-username window.
    /// Either exhausted → not allowed, with the seconds to wait.
    /// </summary>
    public ThrottleDecision TryAcquire(string normalizedUsername, string clientIp)
    {
        using (var probe = _ipFailures.AttemptAcquire(clientIp, permitCount: 0))
        {
            if (!probe.IsAcquired)
            {
                return new ThrottleDecision(false, RetryAfter(probe, IpFailureWindow / 10));
            }
        }

        using var lease = _perUser.AttemptAcquire(normalizedUsername);
        return lease.IsAcquired ? ThrottleDecision.Allow : new ThrottleDecision(false, RetryAfter(lease, UserWindow / 6));
    }

    /// <summary>
    /// Records a failed outcome: consumes the per-IP failure window and notes (usernameHash, ipHash) for 15 minutes.
    /// Returns how many distinct addresses failed for this username in the window.
    /// </summary>
    public int RecordFailure(string usernameHash, string ipHash, string clientIp)
    {
        using (_ipFailures.AttemptAcquire(clientIp))
        {
            // Consumed; an already-exhausted window simply stays exhausted.
        }

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            if (!_failures.TryGetValue(usernameHash, out var addresses))
            {
                addresses = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                _failures[usernameHash] = addresses;
            }

            if (!addresses.ContainsKey(ipHash))
            {
                _failureEntries++;
            }

            addresses[ipHash] = now;
            PruneExpired(addresses, now);

            if (_failureEntries > MaxFailureEntries)
            {
                EvictOldest(now);
            }

            return addresses.Count;
        }
    }

    /// <summary>Verifies against a throwaway hash so the failure path costs one PBKDF2 whatever the reason.</summary>
    public void VerifyDummyPassword(string password) =>
        _hasher.VerifyHashedPassword(DummyUser, _dummyHash.Value, password ?? string.Empty);

    public void Dispose()
    {
        _perUser.Dispose();
        _ipFailures.Dispose();
        Cpu.Dispose();
    }

    internal static int RetryAfter(RateLimitLease lease, TimeSpan fallback)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : fallback;
        return Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
    }

    private void PruneExpired(Dictionary<string, DateTimeOffset> addresses, DateTimeOffset now)
    {
        foreach (var (ip, seen) in addresses.ToList())
        {
            if (now - seen > FailureWindow)
            {
                addresses.Remove(ip);
                _failureEntries--;
            }
        }
    }

    private void EvictOldest(DateTimeOffset now)
    {
        foreach (var (username, addresses) in _failures.ToList())
        {
            PruneExpired(addresses, now);
            if (addresses.Count == 0)
            {
                _failures.Remove(username);
            }
        }

        var excess = _failureEntries - MaxFailureEntries;
        if (excess <= 0)
        {
            return;
        }

        var oldest = _failures
            .SelectMany(u => u.Value.Select(a => (Username: u.Key, Ip: a.Key, Seen: a.Value)))
            .OrderBy(e => e.Seen)
            .Take(excess)
            .ToList();
        foreach (var entry in oldest)
        {
            var addresses = _failures[entry.Username];
            addresses.Remove(entry.Ip);
            _failureEntries--;
            if (addresses.Count == 0)
            {
                _failures.Remove(entry.Username);
            }
        }
    }
}
