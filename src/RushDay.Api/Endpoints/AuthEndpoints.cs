using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Observability;
using RushDay.Api.Options;
using RushDay.Api.Security;
using RushDay.Api.Startup;
using RushDay.Domain.Audit;
using RushDay.Domain.Users;
using RushDay.Infrastructure.Accounts;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Api.Endpoints;

/// <summary>
/// Sessions and the second factor (02-api.md sections 2.3 and 2.4): csrf, login, me, logout, change-password and the
/// <c>/api/auth/mfa</c> group. There is no registration route and <c>MapIdentityApi</c> is never called (D1).
/// </summary>
public static class AuthEndpoints
{
    public const string LoggerCategory = "RushDay.Auth";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapGet("/csrf", (HttpContext http, IAntiforgery antiforgery) =>
                TypedResults.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(http).RequestToken!)))
            .AllowAnonymous()
            .WithName("Csrf");

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithRequestTimeout(ServiceRegistration.LoginTimeoutPolicy)
            .WithName("Login");

        auth.MapGet("/me", (HttpContext http, IAntiforgery antiforgery) =>
                TypedResults.Ok(Me.From(http.User, antiforgery.GetAndStoreTokens(http).RequestToken!)))
            .WithMetadata(GateExemptMetadata.Instance)
            .WithName("Me");

        auth.MapPost("/logout", LogoutAsync)
            .WithMetadata(GateExemptMetadata.Instance)
            .RequireRateLimiting(RateLimitPolicies.Write)
            .WithName("Logout");

        auth.MapPost("/change-password", ChangePasswordAsync)
            .WithMetadata(GateExemptMetadata.Instance)
            .RequireRateLimiting(RateLimitPolicies.PasswordChange)
            .WithName("ChangePassword");

        var mfa = auth.MapGroup("/mfa").WithMetadata(MfaSetupRouteMetadata.Instance);

        mfa.MapPost("/setup", MfaSetupAsync)
            .RequireRateLimiting(RateLimitPolicies.Write)
            .WithName("MfaSetup");

        mfa.MapPost("/enable", MfaEnableAsync)
            .RequireRateLimiting(RateLimitPolicies.Write)
            .WithName("MfaEnable");

        mfa.MapPost("/verify", MfaVerifyAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithRequestTimeout(ServiceRegistration.LoginTimeoutPolicy)
            .WithName("MfaVerify");

        return api;
    }

    /// <summary>Base32 in groups of four, as authenticator apps show it.</summary>
    public static string FormatSharedKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var builder = new StringBuilder(key.Length + (key.Length / 4));
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return builder.ToString().ToUpperInvariant();
    }

    public static string OtpauthUri(string issuer, string username, string key)
    {
        var encoder = UrlEncoder.Default;
        return $"otpauth://totp/{encoder.Encode(issuer)}:{encoder.Encode(username)}?secret={key}&issuer={encoder.Encode(issuer)}&digits=6";
    }

    // POST /api/auth/login, in the order of 02-api.md section 2.3.
    private static async Task<IResult> LoginAsync(LoginRequest request, [AsParameters] AuthServices s)
    {
        var http = s.Http;
        var clientKey = RateLimitPolicies.ClientKey(http);
        var normalized = s.Users.NormalizeName(request.Username) ?? request.Username.ToUpperInvariant();
        var usernameHash = LoginThrottle.UsernameHash(normalized);
        var ipHash = s.IpHasher.Hash(RateLimitPolicies.ClientIp(http));

        // 2. Per-address and per-username failure windows, before anything else is touched. A permit is reserved now
        // and refunded on success, so only failures stay counted and concurrent attempts cannot overrun either window.
        using var attempt = s.Throttle.TryBegin(normalized, clientKey);
        if (!attempt.Allowed)
        {
            s.Metrics.LoadShed(RushDayMetrics.ShedPolicies.Login);
            s.Log.LogInformation("Login outcome {Outcome} for {UsernameHash} from {IpHash}", "rate_limited", usernameHash, ipHash);
            return ProblemResults.ProblemWithRetryAfter(ProblemTypes.RateLimited, attempt.RetryAfterSeconds);
        }

        var user = await s.Users.FindByNameAsync(request.Username);

        // 3. Unknown, disabled and demo-disabled accounts cost one PBKDF2 like a wrong password, and answer the same.
        var unusable = user is null || user.DisabledAt is not null || (user.IsDemo && !s.Demo.Value.Enabled);
        SignInResult? result = null;

        // 4. The CPU guard covers every PBKDF2 operation, the dummy one included, and nothing after it.
        using (var lease = await s.Throttle.Cpu.AcquireAsync(1, http.RequestAborted))
        {
            if (!lease.IsAcquired)
            {
                s.Metrics.LoadShed(RushDayMetrics.ShedPolicies.Login);
                return ProblemResults.ProblemWithRetryAfter(ProblemTypes.RateLimited, 2);
            }

            if (unusable)
            {
                s.Throttle.VerifyDummyPassword(request.Password);
            }
            else
            {
                result = await s.SignIn.PasswordSignInAsync(user!, request.Password, isPersistent: false, lockoutOnFailure: false);
                if (result.IsLockedOut)
                {
                    // A locked account must not answer faster than a wrong password.
                    s.Throttle.VerifyDummyPassword(request.Password);
                }
            }
        }

        // An account that cannot sign in gains nothing from lockout bookkeeping, and would audit a lockout per attempt.
        if (result is null)
        {
            attempt.MarkFailed();
            await RecordFailureAsync(s, user, usernameHash, ipHash, clientKey, countTowardLockout: false);
            return InvalidCredentials(s, RushDayMetrics.LoginOutcomes.Failed, usernameHash, ipHash);
        }

        if (result.IsLockedOut)
        {
            attempt.MarkFailed();
            await RecordFailureAsync(s, user, usernameHash, ipHash, clientKey, countTowardLockout: false);
            return InvalidCredentials(s, RushDayMetrics.LoginOutcomes.LockedOut, usernameHash, ipHash);
        }

        if (result.RequiresTwoFactor)
        {
            // Identity has set the rushday.mfa cookie (bound to the security stamp); no claims are issued until the
            // code is verified. The password was right, so the attempt's permits are refunded.
            s.Metrics.Login(RushDayMetrics.LoginOutcomes.MfaRequired);
            return TypedResults.Ok(new MfaChallenge(true, s.Antiforgery.GetAndStoreTokens(http).RequestToken!));
        }

        if (result.Succeeded)
        {
            return await CompleteSignInAsync(s, user!);
        }

        // 5. Wrong password.
        attempt.MarkFailed();
        await RecordFailureAsync(s, user, usernameHash, ipHash, clientKey, countTowardLockout: true);
        return InvalidCredentials(s, RushDayMetrics.LoginOutcomes.Failed, usernameHash, ipHash);
    }

    // POST /api/auth/mfa/verify: anonymous route plus the rushday.mfa cookie.
    private static async Task<IResult> MfaVerifyAsync(MfaCodeRequest request, [AsParameters] AuthServices s)
    {
        var http = s.Http;
        var clientKey = RateLimitPolicies.ClientKey(http);
        var ipHash = s.IpHasher.Hash(RateLimitPolicies.ClientIp(http));

        var user = await s.SignIn.GetTwoFactorAuthenticationUserAsync();
        var normalized = user?.NormalizedUserName ?? string.Empty;
        var usernameHash = LoginThrottle.UsernameHash(normalized);

        using var attempt = s.Throttle.TryBegin(normalized, clientKey);
        if (!attempt.Allowed)
        {
            s.Metrics.LoadShed(RushDayMetrics.ShedPolicies.Login);
            return ProblemResults.ProblemWithRetryAfter(ProblemTypes.RateLimited, attempt.RetryAfterSeconds);
        }

        // A missing or expired challenge, a disabled account, or a challenge issued under an older security stamp (the
        // password was reset or changed, the factor reset, the account locked or disabled since the password step).
        if (user is null || user.DisabledAt is not null || !await MfaChallengeBinding.IsCurrentAsync(http, user))
        {
            attempt.MarkFailed();
            if (user is not null)
            {
                await http.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            }

            s.Throttle.RecordFailure(usernameHash, clientKey);
            return InvalidCredentials(s, RushDayMetrics.LoginOutcomes.Failed, usernameHash, ipHash);
        }

        // Identity counts a wrong code as an access failure, so lockout applies after five; the provider refuses a code
        // whose time step was already used.
        var result = await s.SignIn.TwoFactorAuthenticatorSignInAsync(request.Code, isPersistent: false, rememberClient: false);
        if (result.Succeeded)
        {
            return await CompleteSignInAsync(s, user);
        }

        attempt.MarkFailed();
        s.Throttle.RecordFailure(usernameHash, clientKey);
        if (result.IsLockedOut)
        {
            return InvalidCredentials(s, RushDayMetrics.LoginOutcomes.LockedOut, usernameHash, ipHash);
        }

        return InvalidCredentials(s, RushDayMetrics.LoginOutcomes.Failed, usernameHash, ipHash);
    }

    /// <summary>Step 6, shared by login and MFA verification: reset failures, stamp the login, re-issue the token.</summary>
    private static async Task<IResult> CompleteSignInAsync(AuthServices s, ApplicationUser user)
    {
        await s.Users.ResetAccessFailedCountAsync(user);
        var now = s.Clock.GetUtcNow();
        await s.Db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(set => set.SetProperty(u => u.LastLoginAt, now), s.Http.RequestAborted);

        var principal = await s.SignIn.CreateUserPrincipalAsync(user);
        s.Http.User = principal;
        var tokens = s.Antiforgery.GetAndStoreTokens(s.Http);

        s.Metrics.Login(RushDayMetrics.LoginOutcomes.Success);
        return TypedResults.Ok(Me.From(principal, tokens.RequestToken!));
    }

    /// <summary>
    /// Step 5: the failure map always (the windows already hold this attempt's permits); Identity's access-failed count
    /// only for a real, non-demo account whose recent failures come from at least <c>LockoutDistinctIps</c> addresses
    /// (IPv6 counted by /64).
    /// </summary>
    private static async Task RecordFailureAsync(AuthServices s, ApplicationUser? user, string usernameHash, string ipHash, string clientKey, bool countTowardLockout)
    {
        var distinctAddresses = s.Throttle.RecordFailure(usernameHash, clientKey);
        if (!countTowardLockout || user is null || user.IsDemo || distinctAddresses < s.Throttle.LockoutDistinctIps)
        {
            return;
        }

        await using var transaction = await s.Db.Database.BeginTransactionAsync(s.Http.RequestAborted);
        var failedCount = user.AccessFailedCount + 1;
        await s.Users.AccessFailedAsync(user);
        if (await s.Users.IsLockedOutAsync(user))
        {
            s.Audit.Record(s.Db, AuditActions.AuthLockedOut, AuditSubjects.Account, user.Id.ToString(), new { usernameHash, failedCount, ipHash }, studentId: user.StudentId);
            await s.Db.SaveChangesAsync(s.Http.RequestAborted);
            s.Metrics.Lockout();
            s.Log.LogWarning("Login outcome {Outcome} for {UsernameHash} from {IpHash}", "locked_out", usernameHash, ipHash);
        }

        await transaction.CommitAsync(s.Http.RequestAborted);
    }

    private static IResult InvalidCredentials(AuthServices s, string outcome, string usernameHash, string ipHash)
    {
        s.Metrics.Login(outcome);
        s.Log.LogInformation("Login outcome {Outcome} for {UsernameHash} from {IpHash}", outcome, usernameHash, ipHash);
        return ProblemResults.Problem(ProblemTypes.InvalidCredentials);
    }

    // POST /api/auth/logout: rotating the stamp ends every session of the account at its next validation.
    private static async Task<IResult> LogoutAsync(HttpContext http, UserManager<ApplicationUser> users)
    {
        if (await users.GetUserAsync(http.User) is { } user)
        {
            await users.UpdateSecurityStampAsync(user);
        }

        await http.SignOutAsync(IdentityConstants.ApplicationScheme);
        await http.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        return TypedResults.NoContent();
    }

    // POST /api/auth/change-password (password-change limiter and the CPU guard).
    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        AccountService accounts,
        LoginThrottle throttle,
        RushDayMetrics metrics,
        ILoggerFactory loggers)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null)
        {
            return ProblemResults.Problem(ProblemTypes.Unauthenticated);
        }

        AccountResult<ApplicationUser> result;
        using (var lease = await throttle.Cpu.AcquireAsync(1, http.RequestAborted))
        {
            if (!lease.IsAcquired)
            {
                metrics.LoadShed(RushDayMetrics.ShedPolicies.Login);
                return ProblemResults.ProblemWithRetryAfter(ProblemTypes.RateLimited, 2);
            }

            result = await accounts.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword, http.RequestAborted);
        }

        switch (result.Error)
        {
            case AccountError.None:
                // The stamp rotated: this session is re-issued, every other one dies at its next validation, and any
                // outstanding second-factor challenge of this browser is dropped.
                await signIn.RefreshSignInAsync(user);
                await http.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
                return TypedResults.NoContent();
            case AccountError.DemoAccount:
                return ProblemResults.Problem(ProblemTypes.DemoAccount, "Demo accounts are read-only.");
            case AccountError.InvalidCurrentPassword:
                if (result.LockedOut)
                {
                    metrics.Lockout();
                    loggers.CreateLogger(LoggerCategory).LogWarning(
                        "Login outcome {Outcome} for {UsernameHash} from {IpHash}",
                        "locked_out",
                        LoginThrottle.UsernameHash(user.NormalizedUserName ?? string.Empty),
                        http.RequestServices.GetRequiredService<IpHasher>().Hash(RateLimitPolicies.ClientIp(http)));
                }

                return ProblemResults.Problem(ProblemTypes.InvalidCurrentPassword);
            default:
                return WeakPassword(result.Codes);
        }
    }

    // POST /api/auth/mfa/setup: a fresh authenticator key, shown once. Staff only: students never enrol a factor (D27).
    private static async Task<IResult> MfaSetupAsync(
        HttpContext http,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        RushDayDbContext db,
        AuditWriter audit,
        IOptions<BrandingOptions> branding)
    {
        if (IsStudent(http))
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var user = await users.GetUserAsync(http.User);
        if (user is null)
        {
            return ProblemResults.Problem(ProblemTypes.Unauthenticated);
        }

        if (user.IsDemo)
        {
            return ProblemResults.Problem(ProblemTypes.DemoAccount, "Demo accounts are read-only.");
        }

        if (user.TwoFactorEnabled)
        {
            return ProblemResults.Problem(ProblemTypes.MfaAlreadyEnabled);
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            audit.Record(db, AuditActions.AccountMfaSetupStarted, AuditSubjects.Account, user.Id.ToString(), new { username = user.UserName }, studentId: user.StudentId);
            await db.SaveChangesAsync(http.RequestAborted);
            await transaction.CommitAsync(http.RequestAborted);
        }

        // Resetting the key rotated the security stamp; keep this session alive.
        await signIn.RefreshSignInAsync(user);

        var key = await users.GetAuthenticatorKeyAsync(user) ?? string.Empty;
        return TypedResults.Ok(new MfaSetupResponse(FormatSharedKey(key), OtpauthUri(branding.Value.InstitutionShortName, user.UserName ?? string.Empty, key)));
    }

    // POST /api/auth/mfa/enable: the first code proves the app holds the key.
    private static async Task<IResult> MfaEnableAsync(
        MfaCodeRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IAntiforgery antiforgery,
        RushDayDbContext db,
        AuditWriter audit)
    {
        if (IsStudent(http))
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var user = await users.GetUserAsync(http.User);
        if (user is null)
        {
            return ProblemResults.Problem(ProblemTypes.Unauthenticated);
        }

        if (user.IsDemo)
        {
            return ProblemResults.Problem(ProblemTypes.DemoAccount, "Demo accounts are read-only.");
        }

        if (user.TwoFactorEnabled)
        {
            return ProblemResults.Problem(ProblemTypes.MfaAlreadyEnabled);
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.Code))
        {
            return ProblemResults.Problem(ProblemTypes.InvalidMfaCode);
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted))
        {
            await users.SetTwoFactorEnabledAsync(user, true);
            audit.Record(db, AuditActions.AccountMfaEnabled, AuditSubjects.Account, user.Id.ToString(), new { username = user.UserName }, studentId: user.StudentId);
            await db.SaveChangesAsync(http.RequestAborted);
            await transaction.CommitAsync(http.RequestAborted);
        }

        // Fresh claims: mfa=1 and no mfa_setup.
        await signIn.RefreshSignInAsync(user);
        var principal = await signIn.CreateUserPrincipalAsync(user);
        http.User = principal;
        return TypedResults.Ok(Me.From(principal, antiforgery.GetAndStoreTokens(http).RequestToken!));
    }

    private static bool IsStudent(HttpContext http) =>
        string.Equals(http.User.FindFirst(RushDayClaims.Role)?.Value, RushDayRoles.Student, StringComparison.Ordinal);

    private static IResult WeakPassword(IReadOnlyList<string> codes) =>
        ProblemResults.Problem(
            ProblemTypes.WeakPassword,
            extensions: new Dictionary<string, object?> { ["errors"] = new Dictionary<string, string[]> { ["newPassword"] = [.. codes] } });

    /// <summary>The services login and MFA verification share (bound with <c>[AsParameters]</c>).</summary>
    internal sealed record AuthServices(
        HttpContext Http,
        UserManager<ApplicationUser> Users,
        SignInManager<ApplicationUser> SignIn,
        LoginThrottle Throttle,
        IAntiforgery Antiforgery,
        IOptions<DemoOptions> Demo,
        RushDayMetrics Metrics,
        IpHasher IpHasher,
        AuditWriter Audit,
        RushDayDbContext Db,
        TimeProvider Clock,
        ILoggerFactory Loggers)
    {
        public ILogger Log => Loggers.CreateLogger(LoggerCategory);
    }
}
