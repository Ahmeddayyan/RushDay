using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RushDay.Api.Auth;
using RushDay.Infrastructure.Audit;

namespace RushDay.UnitTests.Auth;

/// <summary>The small pieces behind the S2 session fixes (02-api.md sections 2.1 and 2.4).</summary>
public sealed class SessionPrimitivesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, false)]
    [InlineData(300, false)]
    [InlineData(301, true)]
    public void Stamp_check_is_due_from_the_last_check_not_from_the_cookie(int secondsSinceCheck, bool due)
    {
        var principal = Principal((RushDayClaims.IssuedAt, Now.AddHours(-3)), (RushDayClaims.StampValidatedAt, Now.AddSeconds(-secondsSinceCheck)));

        Assert.Equal(due, RushDaySecurityStampValidator.IsDue(principal, Now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Without_a_check_claim_the_sign_in_instant_counts()
    {
        Assert.True(RushDaySecurityStampValidator.IsDue(Principal((RushDayClaims.IssuedAt, Now.AddMinutes(-6))), Now, TimeSpan.FromMinutes(5)));
        Assert.False(RushDaySecurityStampValidator.IsDue(Principal((RushDayClaims.IssuedAt, Now.AddMinutes(-4))), Now, TimeSpan.FromMinutes(5)));
        Assert.True(RushDaySecurityStampValidator.IsDue(new ClaimsPrincipal(new ClaimsIdentity()), Now, TimeSpan.FromMinutes(5)));
    }

    /// <summary>RFC 6238 appendix B, SHA-1, T = 59 s: 94287082, whose last six digits are the 6-digit code.</summary>
    [Fact]
    public void Totp_codes_match_the_rfc_6238_vectors()
    {
        var key = "12345678901234567890"u8.ToArray();

        Assert.Equal(287082, ReplayProtectedAuthenticatorTokenProvider.ComputeCode(key, 59 / 30));
        Assert.Equal(81804, ReplayProtectedAuthenticatorTokenProvider.ComputeCode(key, 1111111109 / 30));
        Assert.Equal(50471, ReplayProtectedAuthenticatorTokenProvider.ComputeCode(key, 1111111111 / 30));
    }

    [Fact]
    public void Base32_keys_decode_with_or_without_grouping()
    {
        // "12345678901234567890" in base32.
        const string key = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

        Assert.Equal("12345678901234567890"u8.ToArray(), ReplayProtectedAuthenticatorTokenProvider.DecodeBase32(key));
        Assert.Equal("12345678901234567890"u8.ToArray(), ReplayProtectedAuthenticatorTokenProvider.DecodeBase32("gezd gnbv gy3t qojq gezd gnbv gy3t qojq"));
        Assert.Throws<FormatException>(() => ReplayProtectedAuthenticatorTokenProvider.DecodeBase32("not base32!"));
    }

    [Fact]
    public void Username_hash_is_twelve_hex_characters_of_sha256()
    {
        Assert.Equal("e7dcee3cc63d", AuditHashes.UsernameHash("ALICE"));
        Assert.Equal(AuditHashes.UsernameHash("ALICE"), LoginThrottle.UsernameHash("ALICE"));
    }

    [Fact]
    public void Audit_context_marks_a_demo_actor_from_the_claim()
    {
        var demo = new DefaultHttpContext { User = Principal((RushDayClaims.Demo, null)) };
        var real = new DefaultHttpContext { User = Principal() };

        Assert.True(new CurrentUser(new HttpContextAccessor { HttpContext = demo }).IsDemo);
        Assert.False(new CurrentUser(new HttpContextAccessor { HttpContext = real }).IsDemo);
        Assert.False(SystemAuditContext.Instance.ActorIsDemo);
    }

    private static ClaimsPrincipal Principal(params (string Type, DateTimeOffset? At)[] claims)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var (type, at) in claims)
        {
            identity.AddClaim(new Claim(type, at is { } instant ? RushDayClaims.UnixSeconds(instant) : RushDayClaims.True));
        }

        return new ClaimsPrincipal(identity);
    }
}
