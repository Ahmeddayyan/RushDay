using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using RushDay.Infrastructure.Identity;

namespace RushDay.Api.Auth;

/// <summary>
/// Binds the <c>rushday.mfa</c> cookie (the <c>TwoFactorUserId</c> scheme, set between the password and the code) to
/// the account's security stamp at the password step (02-api.md section 2.4). A password reset, a password change, an
/// MFA reset, a lock or a disable rotates the stamp, so an outstanding challenge can no longer be completed with a
/// code. The cookie itself lives exactly <c>Auth:MfaCookieMinutes</c> from the password step (no sliding renewal).
/// </summary>
public static class MfaChallengeBinding
{
    public const string StampClaim = "rushday.mfa.stamp";

    /// <summary><c>CookieAuthenticationEvents.OnSigningIn</c> of the <c>TwoFactorUserId</c> scheme.</summary>
    public static async Task OnSigningInAsync(CookieSigningInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        // Identity stores the user id as the Name claim of this scheme. The user was loaded by the password step in the
        // same scope, so this is a change-tracker hit rather than a query.
        var userId = identity.FindFirst(ClaimTypes.Name)?.Value;
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = userId is null ? null : await users.FindByIdAsync(userId);
        identity.AddClaim(new Claim(StampClaim, user?.SecurityStamp ?? string.Empty));
    }

    /// <summary>True when the challenge cookie was issued under the user's current security stamp.</summary>
    public static async Task<bool> IsCurrentAsync(HttpContext http, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(user);

        var challenge = await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        var bound = challenge.Principal?.FindFirst(StampClaim)?.Value;
        if (string.IsNullOrEmpty(bound) || string.IsNullOrEmpty(user.SecurityStamp))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(bound), Encoding.UTF8.GetBytes(user.SecurityStamp));
    }
}
