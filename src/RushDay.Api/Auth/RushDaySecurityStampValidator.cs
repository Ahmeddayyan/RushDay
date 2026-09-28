using System.Security.Claims;
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
public sealed class RushDaySecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<ApplicationUser> signInManager,
    ILoggerFactory logger,
    IOptions<DemoOptions> demo,
    TimeProvider clock)
    : SecurityStampValidator<ApplicationUser>(options, signInManager, logger)
{
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
