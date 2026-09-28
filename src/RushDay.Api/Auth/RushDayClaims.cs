using System.Globalization;
using System.Security.Claims;

namespace RushDay.Api.Auth;

/// <summary>The claims of 02-api.md section 2.2, issued by <see cref="RushDayClaimsPrincipalFactory"/>.</summary>
public static class RushDayClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";
    public const string DisplayName = "display_name";
    public const string StudentId = "student_id";
    public const string StudentNumber = "student_number";
    public const string LecturerId = "lecturer_id";
    public const string StaffNumber = "staff_number";

    /// <summary><c>1</c> while <c>must_change_password</c>.</summary>
    public const string PasswordChange = "pwd_change";

    /// <summary><c>1</c> while the role requires a second factor that is not enabled (and the user is not an active demo account).</summary>
    public const string MfaSetup = "mfa_setup";

    /// <summary><c>1</c> when <c>two_factor_enabled</c>.</summary>
    public const string Mfa = "mfa";

    /// <summary><c>1</c> when <c>is_demo</c>.</summary>
    public const string Demo = "demo";

    /// <summary>Sign-in instant, Unix seconds; preserved when the principal is re-issued.</summary>
    public const string IssuedAt = "iat";

    /// <summary>Last activity, Unix seconds; refreshed at most once a minute.</summary>
    public const string LastActivity = "las";

    public const string True = "1";

    public static bool IsSet(this ClaimsPrincipal principal, string type) =>
        principal.FindFirst(type)?.Value == True;

    public static Guid? GuidOf(this ClaimsPrincipal principal, string type) =>
        Guid.TryParse(principal.FindFirst(type)?.Value, out var value) ? value : null;

    public static long? UnixSecondsOf(this ClaimsPrincipal principal, string type) =>
        long.TryParse(principal.FindFirst(type)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static string UnixSeconds(DateTimeOffset instant) =>
        instant.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}
