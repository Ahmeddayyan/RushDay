using System.Security.Cryptography;

namespace RushDay.Api.Security;

/// <summary>
/// The key-encryption key for the Data Protection key ring (D31): 32 bytes from <c>DataProtection:KeyEncryptionKey</c>
/// (base64). <see cref="KeyId"/> (the first 8 hex characters of its SHA-256) is written next to every encrypted key so
/// a key ring encrypted under another KEK is recognised instead of failing obscurely.
/// </summary>
public sealed class DataProtectionKeyEncryptionKey
{
    public const int KeyLength = 32;

    public const string MissingMessage = "DataProtection__KeyEncryptionKey (32 random bytes, base64) is required outside Development.";

    private readonly byte[] _key;

    public DataProtectionKeyEncryptionKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeyLength)
        {
            throw new ArgumentException($"DataProtection__KeyEncryptionKey must decode to {KeyLength} bytes; it decodes to {key.Length}.", nameof(key));
        }

        _key = [.. key];
        KeyId = Convert.ToHexStringLower(SHA256.HashData(_key))[..8];
    }

    public string KeyId { get; }

    /// <summary>A copy of the key bytes (callers may not keep or mutate the original).</summary>
    public ReadOnlySpan<byte> Key => _key;

    /// <summary>Parses the configured value; null when it is absent. Throws when present but malformed.</summary>
    public static DataProtectionKeyEncryptionKey? FromConfiguration(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("DataProtection__KeyEncryptionKey is not valid base64. " + MissingMessage, ex);
        }

        return new DataProtectionKeyEncryptionKey(bytes);
    }
}
