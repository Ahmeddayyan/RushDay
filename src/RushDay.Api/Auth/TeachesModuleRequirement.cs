using Microsoft.AspNetCore.Authorization;

namespace RushDay.Api.Auth;

/// <summary>
/// The caller teaches the module named by the route value <c>code</c> (02-api.md section 4). Used only under
/// <c>/api/lecturer</c>; a failure answers 403 <c>not-your-module</c>, never 404.
/// </summary>
public sealed class TeachesModuleRequirement : IAuthorizationRequirement
{
    public const string RouteValue = "code";
}
