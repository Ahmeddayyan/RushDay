using System.Security.Claims;

namespace RushDay.Api.Auth;

/// <summary>
/// The caller, read from claims only (02-api.md section 2.2): ownership checks use these values and never trust
/// route values for identity. Scoped; empty for anonymous requests.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public Guid? UserId => Principal.GuidOf(RushDayClaims.Subject);

    public string? Username => Principal.FindFirst(RushDayClaims.Name)?.Value;

    public string? Role => Principal.FindFirst(RushDayClaims.Role)?.Value;

    public string? DisplayName => Principal.FindFirst(RushDayClaims.DisplayName)?.Value;

    public Guid? StudentId => Principal.GuidOf(RushDayClaims.StudentId);

    public string? StudentNumber => Principal.FindFirst(RushDayClaims.StudentNumber)?.Value;

    public Guid? LecturerId => Principal.GuidOf(RushDayClaims.LecturerId);

    public string? StaffNumber => Principal.FindFirst(RushDayClaims.StaffNumber)?.Value;

    public bool MustChangePassword => Principal.IsSet(RushDayClaims.PasswordChange);

    public bool MfaSetupRequired => Principal.IsSet(RushDayClaims.MfaSetup);

    public bool MfaEnabled => Principal.IsSet(RushDayClaims.Mfa);

    public bool IsDemo => Principal.IsSet(RushDayClaims.Demo);
}
