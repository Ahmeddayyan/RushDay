using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;

namespace RushDay.Api.Auth;

/// <summary>
/// Per-role session lifetimes (02-api.md section 2.1) enforced on every authenticated request:
/// (1) reject when <c>now - iat</c> exceeds the absolute lifetime or <c>now - las</c> the sliding one;
/// (2) the security-stamp validator, due when <c>now - svt</c> exceeds the interval (never measured from the ticket's
/// issue time, which step 3 resets); (3) refresh <c>las</c> at most once a minute and renew the cookie.
/// Students and lecturers: 8 h sliding / 12 h absolute; administrators: 60 min / 8 h.
/// </summary>
public static class RushDayCookieEvents
{
    public static readonly TimeSpan ActivityRefreshInterval = TimeSpan.FromSeconds(60);

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = context.Principal;
        if (principal is null)
        {
            return;
        }

        var services = context.HttpContext.RequestServices;
        var auth = services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
        var role = principal.FindFirst(RushDayClaims.Role)?.Value;

        var issuedAt = principal.UnixSecondsOf(RushDayClaims.IssuedAt);
        var lastActivity = principal.UnixSecondsOf(RushDayClaims.LastActivity);
        if (issuedAt is null || lastActivity is null
            || now - issuedAt.Value > auth.AbsoluteLifetimeFor(role).TotalSeconds
            || now - lastActivity.Value > auth.SlidingLifetimeFor(role).TotalSeconds)
        {
            await RejectAsync(context);
            return;
        }

        await services.GetRequiredService<ISecurityStampValidator>().ValidateAsync(context);
        if (context.Principal is null)
        {
            return;
        }

        var current = context.Principal;
        var activity = current.UnixSecondsOf(RushDayClaims.LastActivity) ?? lastActivity.Value;
        if (now - activity > ActivityRefreshInterval.TotalSeconds)
        {
            context.ReplacePrincipal(WithClaim(current, RushDayClaims.LastActivity, now.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            context.ShouldRenew = true;
        }
    }

    /// <summary>
    /// <c>SecurityStampValidatorOptions.OnRefreshingPrincipal</c>: the re-issued principal keeps the original
    /// <c>iat</c> and <c>las</c>, so a stamp re-validation never restarts the absolute clock.
    /// </summary>
    public static void CopyLifetimeClaims(ClaimsPrincipal? current, ClaimsPrincipal? replacement)
    {
        if (current is null || replacement?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        foreach (var type in new[] { RushDayClaims.IssuedAt, RushDayClaims.LastActivity })
        {
            if (current.FindFirst(type) is not { } original)
            {
                continue;
            }

            foreach (var existing in identity.FindAll(type).ToList())
            {
                identity.RemoveClaim(existing);
            }

            identity.AddClaim(new Claim(type, original.Value));
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    private static ClaimsPrincipal WithClaim(ClaimsPrincipal principal, string type, string value)
    {
        var source = principal.Identity as ClaimsIdentity;
        var identity = new ClaimsIdentity(
            principal.Claims.Where(c => c.Type != type),
            source?.AuthenticationType ?? IdentityConstants.ApplicationScheme,
            source?.NameClaimType ?? RushDayClaims.Name,
            source?.RoleClaimType ?? RushDayClaims.Role);
        identity.AddClaim(new Claim(type, value));
        return new ClaimsPrincipal(identity);
    }
}
