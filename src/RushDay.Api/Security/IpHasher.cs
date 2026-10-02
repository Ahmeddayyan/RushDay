using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;

namespace RushDay.Api.Security;

/// <summary>
/// Pseudonymises client addresses (D32): <c>ip_hash = hex(HMAC-SHA256(dailyKey, clientIp))[..32]</c> with
/// <c>dailyKey = HMAC-SHA256(KEK, "rushday.ip-hash:" + yyyy-MM-dd)</c>, never stored. Development (no KEK) uses a
/// per-process random key. Used for <c>audit_events.ip_hash</c>, lockout audit details and failed-login log lines.
/// </summary>
public sealed class IpHasher
{
    public const int HashLength = 32;

    private readonly byte[] _rootKey;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<DateOnly, byte[]> _dailyKeys = new();

    public IpHasher(IOptions<DataProtectionOptions> dataProtection, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(dataProtection);
        _clock = clock;

        var kek = DataProtectionKeyEncryptionKey.FromConfiguration(dataProtection.Value.KeyEncryptionKey);
        _rootKey = kek is null ? RandomNumberGenerator.GetBytes(DataProtectionKeyEncryptionKey.KeyLength) : kek.Key.ToArray();
    }

    public string Hash(IPAddress? address) => Hash(address?.ToString());

    public string Hash(string? clientIp)
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var dailyKey = _dailyKeys.GetOrAdd(today, DeriveDailyKey);
        if (_dailyKeys.Count > 3)
        {
            foreach (var old in _dailyKeys.Keys.Where(d => d < today.AddDays(-1)))
            {
                _dailyKeys.TryRemove(old, out _);
            }
        }

        var mac = HMACSHA256.HashData(dailyKey, Encoding.UTF8.GetBytes(clientIp ?? "unknown"));
        return Convert.ToHexStringLower(mac)[..HashLength];
    }

    private byte[] DeriveDailyKey(DateOnly date) =>
        HMACSHA256.HashData(_rootKey, Encoding.UTF8.GetBytes("rushday.ip-hash:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
}
