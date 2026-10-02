namespace RushDay.Api.Options;

/// <summary>
/// The key-encryption key for the Data Protection key ring (D31), bound from "DataProtection". Required outside
/// Development: 32 random bytes, base64. Losing it invalidates every session and antiforgery token and nothing else.
/// </summary>
public sealed record DataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string? KeyEncryptionKey { get; init; }
}
