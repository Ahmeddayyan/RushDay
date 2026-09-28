using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace RushDay.Infrastructure.Identity;

/// <summary>
/// The one PBKDF2 iteration count (01-domain-and-data.md section 8). The backfills hash with <see cref="Create"/>
/// and S2's Identity registration uses <see cref="IterationCount"/>, so a demo hash never verifies as
/// <c>SuccessRehashNeeded</c>, which would make <c>PasswordSignInAsync</c> rewrite a per-user hash on first login.
/// </summary>
public static class PasswordHashing
{
    /// <summary>OWASP's current figure for PBKDF2-HMAC-SHA512 (Identity's V3 format).</summary>
    public const int IterationCount = 210_000;

    public static PasswordHasher<ApplicationUser> Create() =>
        new(Options.Create(new PasswordHasherOptions { IterationCount = IterationCount }));
}
