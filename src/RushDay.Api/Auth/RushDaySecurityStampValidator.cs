using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;
using RushDay.Infrastructure.Identity;

namespace RushDay.Api.Auth;

/// <summary>
/// Identity's security-stamp validator (one primary-key read per active session every
/// <c>Auth:SecurityStampIntervalMinutes</c>) that also rejects a user who is disabled, locked out or a demo account
/// while demo mode is off, even when the stamp is unchanged (T12): an automatic lockout does not rotate the stamp, yet
/// must still end existing sessions.
/// </summary>
/// <remarks>
/// Identity's own validator measures the interval from the ticket's <c>IssuedUtc</c>, which the cookie handler resets
/// on every renewal. <see cref="RushDayCookieEvents"/> renews the ticket whenever <c>las</c> is over a minute old, so a
/// session used at least every five minutes would never be re-checked. The interval therefore runs from the
/// <c>svt</c> claim (<see cref="RushDayClaims.StampValidatedAt"/>), which only a successful check moves.
/// </remarks>
public sealed class RushDaySecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<ApplicationUser> signInManager,
    ILoggerFactory logger,
    IOptions<DemoOptions> demo,
    TimeProvider clock)
    : SecurityStampValidator<ApplicationUser>(options, signInManager, logger)
{
    /// <summary>True when the principal's last stamp check is older than <paramref name="interval"/> (or unknown).</summary>
    public static bool IsDue(ClaimsPrincipal? principal, DateTimeOffset now, TimeSpan interval)
    {
        var checkedAt = principal?.UnixSecondsOf(RushDayClaims.StampValidatedAt) ?? principal?.UnixSecondsOf(RushDayClaims.IssuedAt);
        return checkedAt is null || now.ToUnixTimeSeconds() - checkedAt.Value > interval.TotalSeconds;
    }

    public override async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsDue(context.Principal, clock.GetUtcNow(), Options.ValidationInterval))
        {
            return;
        }

        var user = await VerifySecurityStamp(context.Principal);
        if (user is null)
        {
            context.RejectPrincipal();
            await SignInManager.SignOutAsync();
            return;
        }

        // Re-issues the principal from the store (OnRefreshingPrincipal keeps iat and las; the factory stamps svt = now)
        // and renews the cookie.
        await SecurityStampVerified(user, context);
    }

    protected override async Task<ApplicationUser?> VerifySecurityStamp(ClaimsPrincipal? principal)
    {
        var user = await base.VerifySecurityStamp(principal);
        if (user is null)
        {
            return null;
        }

        if (user.DisabledAt is not null
            || (user.LockoutEnd is { } lockoutEnd && lockoutEnd > clock.GetUtcNow())
            || (user.IsDemo && !demo.Value.Enabled))
        {
            return null;
        }

        return user;
    }
}
