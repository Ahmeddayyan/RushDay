using RushDay.Api.Security;

namespace RushDay.Api.Auth;

/// <summary>
/// Second gate on the <c>/api</c> group (D27): a principal with <c>mfa_setup=1</c> (an administrator without a TOTP
/// factor, outside demo mode) gets 403 <c>mfa-setup-required</c> everywhere except anonymous-capable endpoints, the
/// three exempt routes and <c>/api/auth/mfa/*</c>, until <c>POST /api/auth/mfa/enable</c> succeeds.
/// </summary>
public sealed class MfaSetupRequiredFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        if (!MustChangePasswordFilter.IsExempt(httpContext)
            && httpContext.GetEndpoint()?.Metadata.GetMetadata<MfaSetupRouteMetadata>() is null
            && httpContext.User.IsSet(RushDayClaims.MfaSetup))
        {
            return ValueTask.FromResult<object?>(ProblemResults.Problem(ProblemTypes.MfaSetupRequired));
        }

        return next(context);
    }
}
