using Microsoft.AspNetCore.Authorization;
using RushDay.Infrastructure.Caching;

namespace RushDay.Api.Auth;

/// <summary>
/// Passes when the caller has a <c>lecturer_id</c> claim and <see cref="LecturerModuleCache"/> lists the route's module
/// code for that lecturer. Nobody else passes. An unknown code still evaluates the cache (an empty match), so a
/// non-member gets 403 <c>not-your-module</c> whether or not the module exists.
/// </summary>
public sealed class TeachesModuleHandler(LecturerModuleCache lecturerModules) : AuthorizationHandler<TeachesModuleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TeachesModuleRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        var code = httpContext?.GetRouteValue(TeachesModuleRequirement.RouteValue) as string;
        var lecturerId = context.User.GuidOf(RushDayClaims.LecturerId);

        if (lecturerId is { } id && code is not null
            && await lecturerModules.TeachesAsync(id, code, httpContext?.RequestAborted ?? CancellationToken.None))
        {
            context.Succeed(requirement);
        }

        // Otherwise the requirement stays pending (no Fail()), so the result lists it among the failed requirements
        // and RushDayAuthorizationResultHandler can tell "not your module" from "not a lecturer".
    }
}
