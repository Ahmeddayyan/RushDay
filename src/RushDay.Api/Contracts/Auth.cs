using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using RushDay.Api.Auth;

namespace RushDay.Api.Contracts;

/// <summary><c>POST /api/auth/login</c>.</summary>
public sealed record LoginRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;
}

/// <summary><c>POST /api/auth/mfa/enable</c> and <c>POST /api/auth/mfa/verify</c>.</summary>
public sealed record MfaCodeRequest
{
    /// <summary>Six ASCII digits: <c>\d</c> would also admit Arabic-Indic or full-width digits (joint item J4).</summary>
    [Required]
    [RegularExpression("^[0-9]{6}$")]
    public string Code { get; init; } = string.Empty;
}

/// <summary><c>POST /api/auth/change-password</c>.</summary>
public sealed record ChangePasswordRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary><c>GET /api/auth/csrf</c>: the request token, delivered only in JSON (D5).</summary>
public sealed record CsrfResponse(string CsrfToken);

/// <summary><c>Me</c> of 02-api.md section 7, built from the principal's claims.</summary>
public sealed record Me(
    Guid Id,
    string Username,
    string DisplayName,
    string Role,
    string? StudentNumber,
    string? StaffNumber,
    bool MustChangePassword,
    bool MfaEnabled,
    bool MfaSetupRequired,
    bool IsDemo,
    string CsrfToken)
{
    public static Me From(ClaimsPrincipal principal, string csrfToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return new Me(
            principal.GuidOf(RushDayClaims.Subject) ?? Guid.Empty,
            principal.FindFirst(RushDayClaims.Name)?.Value ?? string.Empty,
            principal.FindFirst(RushDayClaims.DisplayName)?.Value ?? string.Empty,
            principal.FindFirst(RushDayClaims.Role)?.Value ?? string.Empty,
            principal.FindFirst(RushDayClaims.StudentNumber)?.Value,
            principal.FindFirst(RushDayClaims.StaffNumber)?.Value,
            principal.IsSet(RushDayClaims.PasswordChange),
            principal.IsSet(RushDayClaims.Mfa),
            principal.IsSet(RushDayClaims.MfaSetup),
            principal.IsSet(RushDayClaims.Demo),
            csrfToken);
    }
}

/// <summary><c>MfaChallenge</c>: the password was right and a code is needed; no claims are issued yet.</summary>
public sealed record MfaChallenge(bool MfaRequired, string CsrfToken);

/// <summary><c>POST /api/auth/mfa/setup</c>.</summary>
public sealed record MfaSetupResponse(string SharedKey, string OtpauthUri);
