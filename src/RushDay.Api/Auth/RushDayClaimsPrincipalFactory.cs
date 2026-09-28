using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Api.Auth;

/// <summary>
/// Issues the claims of 02-api.md section 2.2. It always returns a principal: disabled, locked-out and demo-disabled
/// users are rejected by login and by <see cref="RushDaySecurityStampValidator"/>, never here.
/// </summary>
/// <remarks>
/// <c>iat</c> is taken from the request's current principal when it belongs to the same user, so a re-issue within a
/// session (<c>RefreshSignInAsync</c> after a password change or enabling MFA) never restarts the absolute lifetime;
/// the security-stamp re-issue is covered separately by <see cref="RushDayCookieEvents.CopyLifetimeClaims"/>.
/// </remarks>
public sealed class RushDayClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> identityOptions,
    RushDayDbContext db,
    IOptions<AuthOptions> auth,
    IOptions<DemoOptions> demo,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider clock)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, identityOptions)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        // sub, name, the security stamp (and email when set) come from Identity.
        var identity = await base.GenerateClaimsAsync(user);

        var roles = await UserManager.GetRolesAsync(user);
        var role = roles.Count > 0 ? roles[0] : null;
        if (role is not null)
        {
            identity.AddClaim(new Claim(RushDayClaims.Role, role));
        }

        identity.AddClaim(new Claim(RushDayClaims.DisplayName, user.DisplayName));

        if (user.StudentId is { } studentId)
        {
            var studentNumber = await db.Students.AsNoTracking().Where(s => s.Id == studentId).Select(s => s.StudentNumber).SingleOrDefaultAsync();
            identity.AddClaim(new Claim(RushDayClaims.StudentId, studentId.ToString()));
            if (studentNumber is not null)
            {
                identity.AddClaim(new Claim(RushDayClaims.StudentNumber, studentNumber));
            }
        }

        if (user.LecturerId is { } lecturerId)
        {
            var staffNumber = await db.Lecturers.AsNoTracking().Where(l => l.Id == lecturerId).Select(l => l.StaffNumber).SingleOrDefaultAsync();
            identity.AddClaim(new Claim(RushDayClaims.LecturerId, lecturerId.ToString()));
            if (staffNumber is not null)
            {
                identity.AddClaim(new Claim(RushDayClaims.StaffNumber, staffNumber));
            }
        }

        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(RushDayClaims.PasswordChange, RushDayClaims.True));
        }

        var exempt = user.IsDemo && demo.Value.Enabled;
        if (role is not null && auth.Value.MfaRoles.Contains(role, StringComparer.Ordinal) && !user.TwoFactorEnabled && !exempt)
        {
            identity.AddClaim(new Claim(RushDayClaims.MfaSetup, RushDayClaims.True));
        }

        if (user.TwoFactorEnabled)
        {
            identity.AddClaim(new Claim(RushDayClaims.Mfa, RushDayClaims.True));
        }

        if (user.IsDemo)
        {
            identity.AddClaim(new Claim(RushDayClaims.Demo, RushDayClaims.True));
        }

        var now = RushDayClaims.UnixSeconds(clock.GetUtcNow());
        var current = httpContextAccessor.HttpContext?.User;
        var sameUser = current?.FindFirst(RushDayClaims.Subject)?.Value == user.Id.ToString();
        var issuedAt = sameUser ? current!.FindFirst(RushDayClaims.IssuedAt)?.Value : null;
        identity.AddClaim(new Claim(RushDayClaims.IssuedAt, issuedAt ?? now));
        identity.AddClaim(new Claim(RushDayClaims.LastActivity, now));

        return identity;
    }
}
