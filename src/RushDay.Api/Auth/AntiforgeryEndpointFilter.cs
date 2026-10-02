using Microsoft.AspNetCore.Antiforgery;
using RushDay.Api.Security;

namespace RushDay.Api.Auth;

/// <summary>
/// Validates the <c>X-CSRF-TOKEN</c> header on every POST, PUT, PATCH and DELETE under <c>/api</c> (02-api.md section
/// 3, D5). No exemptions: login and MFA verification are protected too (login CSRF). Failure → 400
/// <c>urn:rushday:antiforgery</c>.
/// </summary>
public sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return ProblemResults.Problem(ProblemTypes.Antiforgery);
            }
        }

        return await next(context);
    }
}
