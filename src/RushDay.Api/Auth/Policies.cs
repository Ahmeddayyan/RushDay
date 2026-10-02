using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using RushDay.Api.Security;
using RushDay.Domain.Users;

namespace RushDay.Api.Auth;

/// <summary>
/// The authorization policies of 02-api.md section 4. There is no Staff policy; the fallback policy (authenticated)
/// covers everything that is not explicitly anonymous. An <c>Admin</c> carries neither <c>student_id</c> nor
/// <c>lecturer_id</c>, so it never passes <see cref="StudentOnly"/> or <see cref="LecturerOnly"/>.
/// </summary>
public static class Policies
{
    public const string StudentOnly = nameof(StudentOnly);
    public const string LecturerOnly = nameof(LecturerOnly);
    public const string AdminOnly = nameof(AdminOnly);
    public const string TeachesModule = nameof(TeachesModule);

    public static void Register(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy(StudentOnly, p => p.RequireAuthenticatedUser().RequireRole(RushDayRoles.Student).RequireClaim(RushDayClaims.StudentId));
        options.AddPolicy(LecturerOnly, p => p.RequireAuthenticatedUser().RequireRole(RushDayRoles.Lecturer).RequireClaim(RushDayClaims.LecturerId));
        options.AddPolicy(AdminOnly, p => p.RequireAuthenticatedUser().RequireRole(RushDayRoles.Admin));
        options.AddPolicy(TeachesModule, p => p.RequireAuthenticatedUser().AddRequirements(new TeachesModuleRequirement()));
    }
}

/// <summary>
/// Turns authorization failures into ProblemDetails instead of redirects: a lecturer who fails only
/// <see cref="TeachesModuleRequirement"/> gets 403 <c>not-your-module</c>; any other failure (an administrator or a
/// student on a lecturer route included) goes through the cookie events: 401 <c>unauthenticated</c>, 403 <c>forbidden</c>.
/// </summary>
public sealed class RushDayAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(authorizeResult);

        var failed = authorizeResult.AuthorizationFailure?.FailedRequirements.ToList();
        if (authorizeResult.Forbidden && failed is { Count: > 0 } && failed.All(r => r is TeachesModuleRequirement))
        {
            return ProblemResults.WriteAsync(context, StatusCodes.Status403Forbidden, ProblemTypes.NotYourModule);
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
