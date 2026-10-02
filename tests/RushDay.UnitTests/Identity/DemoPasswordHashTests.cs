using System.Buffers.Binary;
using Microsoft.AspNetCore.Identity;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Seeding;

namespace RushDay.UnitTests.Identity;

/// <summary>
/// The demo backfill hashes each role's password once with a throwaway <see cref="ApplicationUser"/> and stores
/// the same hash on thousands of rows (01-domain-and-data.md section 6 step 8). Identity's V3 hasher ignores the
/// user, so the hash must verify for any user through a hasher built with the same iteration count that S2's DI
/// registration uses (<see cref="PasswordHashing.IterationCount"/>); otherwise every first login would rewrite it.
/// </summary>
public sealed class DemoPasswordHashTests
{
    [Theory]
    [InlineData(DemoAccounts.StudentPassword, DemoAccounts.StudentUsername)]
    [InlineData(DemoAccounts.LecturerPassword, DemoAccounts.LecturerUsername)]
    [InlineData(DemoAccounts.AdminPassword, DemoAccounts.AdminUsername)]
    public void A_hash_made_with_a_throwaway_user_verifies_for_a_real_user_through_a_fresh_hasher(string password, string username)
    {
        var hash = PasswordHashing.Create().HashPassword(new ApplicationUser(), password);

        var verifying = PasswordHashing.Create();
        var user = new ApplicationUser { UserName = username, NormalizedUserName = username.ToUpperInvariant() };

        Assert.Equal(PasswordVerificationResult.Success, verifying.VerifyHashedPassword(user, hash, password));
        Assert.Equal(PasswordVerificationResult.Failed, verifying.VerifyHashedPassword(user, hash, password + "x"));
    }

    [Fact]
    public void The_iteration_count_is_the_owasp_figure()
    {
        Assert.Equal(210_000, PasswordHashing.IterationCount);
    }

    [Fact]
    public void The_hash_is_the_identity_v3_format_with_210000_iterations()
    {
        var hash = PasswordHashing.Create().HashPassword(new ApplicationUser(), DemoAccounts.StudentPassword);

        var (version, prf, iterations, saltLength) = DecodeV3Header(hash);

        Assert.Equal(0x01, version);
        Assert.Equal((uint)KeyDerivationPrf.HMACSHA512, prf);
        Assert.Equal(PasswordHashing.IterationCount, (int)iterations);
        Assert.Equal(16u, saltLength);
    }

    [Fact]
    public void A_hash_with_fewer_iterations_verifies_as_rehash_needed_so_the_backfill_rewrites_it()
    {
        var weaker = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), DemoAccounts.StudentPassword);
        Assert.Equal(100_000, (int)DecodeV3Header(weaker).Iterations);

        var result = PasswordHashing.Create().VerifyHashedPassword(new ApplicationUser(), weaker, DemoAccounts.StudentPassword);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
    }

    /// <summary>V3 layout: 0x01, then big-endian uint32 PRF, iteration count and salt length, then salt and subkey.</summary>
    private static (byte Version, uint Prf, uint Iterations, uint SaltLength) DecodeV3Header(string hash)
    {
        var bytes = Convert.FromBase64String(hash);
        return (
            bytes[0],
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9, 4)));
    }

    private enum KeyDerivationPrf : uint
    {
        HMACSHA1 = 0,
        HMACSHA256 = 1,
        HMACSHA512 = 2,
    }
}
