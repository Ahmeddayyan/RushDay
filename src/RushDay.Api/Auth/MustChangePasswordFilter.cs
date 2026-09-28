using Microsoft.AspNetCore.Authorization;
using RushDay.Api.Security;

namespace RushDay.Api.Auth;

/// <summary>
/// Marks the three routes a gated user may still call: <c>GET /api/auth/me</c>, <c>POST /api/auth/logout</c> and
/// <c>POST /api/auth/change-password</c> (02-api.md section 2.3).
/// </summary>
public sealed class GateExemptMetadata
{
    public static GateExemptMetadata Instance { get; } = new();
}

/// <summary>Marks the <c>/api/auth/mfa</c> group, which a user gated by <c>mfa_setup</c> may reach.</summary>
public sealed class MfaSetupRouteMetadata
{
    public static MfaSetupRouteMetadata Instance { get; } = new();
}

/// <summary>
/// First gate on the <c>/api</c> group: a principal with <c>pwd_change=1</c> gets 403 <c>password-change-required</c>
/// everywhere except anonymous-capable endpoints and the three exempt routes, so a provisioned or reset account must
/// change its password before it can do anything else.
/// </summary>
public sealed class MustChangePasswordFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        if (!IsExempt(httpContext) && httpContext.User.IsSet(RushDayClaims.PasswordChange))
        {
            return ValueTask.FromResult<object?>(ProblemResults.Problem(ProblemTypes.PasswordChangeRequired));
        }

        return next(context);
    }

    internal static bool IsExempt(HttpContext httpContext)
    {
        var metadata = httpContext.GetEndpoint()?.Metadata;
        return metadata is null
            || metadata.GetMetadata<IAllowAnonymous>() is not null
            || metadata.GetMetadata<GateExemptMetadata>() is not null;
    }
}
