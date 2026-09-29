using System.Security.Cryptography;
using System.Text;

namespace RushDay.Infrastructure.Audit;

/// <summary>The pseudonymous forms that logs and audit details hold instead of identifiers (03-security.md section 6).</summary>
public static class AuditHashes
{
    /// <summary>First 12 hex characters of SHA-256 of the normalised (upper-case) username.</summary>
    public static string UsernameHash(string normalizedUsername)
    {
        ArgumentNullException.ThrowIfNull(normalizedUsername);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUsername)))[..12];
    }
}
