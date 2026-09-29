using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Identity;

namespace RushDay.Api.Auth;

/// <summary>
/// One sign-in attempt's reservation in <see cref="LoginThrottle"/>'s two failure windows. Disposing it refunds both
/// permits unless <see cref="MarkFailed"/> was called, so only failed outcomes stay counted, and a request that ends
/// early (an exception, the CPU guard shedding it) costs the caller nothing.
/// </summary>
public sealed class LoginAttempt : IDisposable
{
    private readonly LoginThrottle? _throttle;
    private readonly WindowPermit _address;
    private readonly WindowPermit _username;
    private bool _failed;
    private bool _settled;

    internal LoginAttempt(LoginThrottle throttle, WindowPermit address, WindowPermit username)
    {
        _throttle = throttle;
        _address = address;
        _username = username;
        Allowed = true;
    }

    private LoginAttempt(int retryAfterSeconds)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    public bool Allowed { get; }

    public int RetryAfterSeconds { get; }

    /// <summary>The attempt failed: its permits stay in both windows.</summary>
    public void MarkFailed() => _failed = true;

    public void Dispose()
    {
        if (_settled || _throttle is null)
        {
            return;
        }

        _settled = true;
        if (!_failed)
        {
            _throttle.Refund(_address, _username);
        }
    }

    internal static LoginAttempt Rejected(int retryAfterSeconds) => new(retryAfterSeconds);
}

/// <summary>
/// The in-handler half of the login protection (D6, 02-api.md sections 2.3 and 5), a singleton injected into login and
/// MFA verification. It owns two "failed outcomes only" windows, per client address (<c>LoginFailuresPerIpPer10Minutes</c>)
/// and per normalised username (<c>LoginPerUserPerMinute</c>), and the CPU guard <see cref="Cpu"/> around PBKDF2.
/// Both windows work by reserve-then-refund (<see cref="TryBegin"/>, <see cref="LoginAttempt"/>): a permit is taken
/// before the password is checked and handed back on success, so concurrent attempts cannot overrun a window and a
/// user's own successful sign-ins never spend it. It also keeps the failure map that decides when Identity lockout
/// may count a failure: only once an account's failures in 15 minutes come from at least <c>LockoutDistinctIps</c>
/// addresses (IPv6 by /64, <see cref="Security.RateLimitPolicies.ClientKey(HttpContext)"/>), so a single address can
/// never lock a victim (it is stopped by its own failure window first).
/// </summary>
public sealed class LoginThrottle : IDisposable
{
    public const int MaxFailureEntries = 50_000;

    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan UserWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan IpFailureWindow = TimeSpan.FromMinutes(10);

    private readonly SlidingWindowCounter _perUser;
    private readonly SlidingWindowCounter _ipFailures;
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

        _perUser = new SlidingWindowCounter(Math.Max(1, limits.LoginPerUserPerMinute), UserWindow, 6, clock);
        _ipFailures = new SlidingWindowCounter(Math.Max(1, limits.LoginFailuresPerIpPer10Minutes), IpFailureWindow, 10, clock);

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
    public static string UsernameHash(string normalizedUsername) => AuditHashes.UsernameHash(normalizedUsername);

    /// <summary>
    /// Reserves one permit in the per-address failure window, then one in the per-username window. Either exhausted →
    /// not allowed, with the seconds to wait, and nothing stays reserved. Dispose the result when the attempt ends.
    /// </summary>
    public LoginAttempt TryBegin(string normalizedUsername, string clientKey)
    {
        ArgumentNullException.ThrowIfNull(normalizedUsername);
        ArgumentNullException.ThrowIfNull(clientKey);

        if (!_ipFailures.TryReserve(clientKey, out var address, out var addressRetry))
        {
            return LoginAttempt.Rejected(Seconds(addressRetry));
        }

        if (!_perUser.TryReserve(normalizedUsername, out var username, out var usernameRetry))
        {
            _ipFailures.Refund(address);
            return LoginAttempt.Rejected(Seconds(usernameRetry));
        }

        return new LoginAttempt(this, address, username);
    }

    /// <summary>
    /// Notes a failed outcome for (usernameHash, address key) for 15 minutes and returns how many distinct addresses
    /// failed for this username in that time. The windows were already charged by <see cref="TryBegin"/>.
    /// </summary>
    public int RecordFailure(string usernameHash, string clientKey)
    {
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            if (!_failures.TryGetValue(usernameHash, out var addresses))
            {
                addresses = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                _failures[usernameHash] = addresses;
            }

            if (!addresses.ContainsKey(clientKey))
            {
                _failureEntries++;
            }

            addresses[clientKey] = now;
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

    public void Dispose() => Cpu.Dispose();

    internal static int RetryAfter(RateLimitLease lease, TimeSpan fallback)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : fallback;
        return Seconds(retryAfter);
    }

    internal void Refund(WindowPermit address, WindowPermit username)
    {
        _perUser.Refund(username);
        _ipFailures.Refund(address);
    }

    private static int Seconds(TimeSpan retryAfter) => Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

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
