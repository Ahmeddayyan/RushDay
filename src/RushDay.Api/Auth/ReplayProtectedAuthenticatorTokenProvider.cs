using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Api.Auth;

/// <summary>
/// The TOTP provider registered as <c>TokenOptions.DefaultAuthenticatorProvider</c> (02-api.md section 2.4): RFC 6238
/// with SHA-1, 30-second steps and 6 digits over the key Identity stores (<c>GetAuthenticatorKeyAsync</c>), accepting
/// the same ±2 steps as Identity's <c>AuthenticatorTokenProvider</c> against the real clock (the code comes from the
/// user's phone, so a test's fake clock must not move it). Unlike Identity's provider it records the last accepted
/// time step per user (<c>user_tokens</c> row <c>[RushDay]</c>/<c>LastTotpStep</c>) and refuses any step at or before
/// it, so a code seen once (shoulder-surfed, phished, replayed from a proxy log) cannot be used again, by
/// <c>POST /api/auth/mfa/verify</c> or <c>POST /api/auth/mfa/enable</c> alike. The claim is one atomic upsert, so two
/// concurrent requests with the same code cannot both pass.
/// </summary>
public sealed class ReplayProtectedAuthenticatorTokenProvider(RushDayDbContext db) : IUserTwoFactorTokenProvider<ApplicationUser>
{
    public const string LoginProvider = "[RushDay]";
    public const string LastStepName = "LastTotpStep";
    public const int StepSeconds = 30;

    /// <summary>Steps accepted either side of now, as Identity's provider does (about 90 s of clock skew).</summary>
    public const int ToleranceSteps = 2;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>The current RFC 6238 time step by the real clock.</summary>
    public static long CurrentStep() => TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() / StepSeconds;

    /// <summary>The 6-digit code of <paramref name="step"/> for a base32 key.</summary>
    public static int ComputeCode(byte[] key, long step)
    {
        ArgumentNullException.ThrowIfNull(key);
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(key, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return binary % 1_000_000;
    }

    public static byte[] DecodeBase32(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var output = new List<byte>(key.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in key)
        {
            if (c is ' ' or '=' or '-')
            {
                continue;
            }

            var value = Base32Alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("The authenticator key is not base32.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user));
    }

    /// <summary>The authenticator app generates the code; the server never does (as Identity's provider).</summary>
    public Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user) =>
        Task.FromResult(string.Empty);

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (token is not { Length: 6 } || !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        byte[] secret;
        try
        {
            secret = DecodeBase32(key);
        }
        catch (FormatException)
        {
            return false;
        }

        var now = CurrentStep();
        long? matched = null;
        for (var step = now - ToleranceSteps; step <= now + ToleranceSteps; step++)
        {
            if (ComputeCode(secret, step) == code)
            {
                matched = step;
            }
        }

        return matched is { } accepted && await ClaimStepAsync(user.Id, accepted);
    }

    /// <summary>
    /// Records <paramref name="step"/> as the user's last accepted step unless an equal or later one is already
    /// recorded; true when this call recorded it.
    /// </summary>
    private async Task<bool> ClaimStepAsync(Guid userId, long step)
    {
        var value = step.ToString(CultureInfo.InvariantCulture);
        var claimed = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO user_tokens (user_id, login_provider, name, value)
            VALUES ({userId}, {LoginProvider}, {LastStepName}, {value})
            ON CONFLICT (user_id, login_provider, name) DO UPDATE SET value = EXCLUDED.value
            WHERE CAST(user_tokens.value AS bigint) < CAST(EXCLUDED.value AS bigint)
            """);
        return claimed == 1;
    }
}
