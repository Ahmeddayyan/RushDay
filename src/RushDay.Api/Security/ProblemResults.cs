using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace RushDay.Api.Security;

/// <summary>
/// Builds and writes RFC 7807 problems with <c>type = urn:rushday:&lt;slug&gt;</c> (02-api.md section 6). Both paths go
/// through <see cref="IProblemDetailsService"/>, so <see cref="ProblemDetailsCustomizer"/> adds <c>traceId</c>.
/// </summary>
public static class ProblemResults
{
    /// <summary>The problem for a slug of the closed catalogue; <paramref name="status"/> defaults to the slug's own.</summary>
    public static ProblemDetails Create(string slug, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null, int? status = null)
    {
        if (!ProblemTypes.StatusBySlug.TryGetValue(slug, out var slugStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(slug), slug, "Not a slug of the ProblemTypes catalogue.");
        }

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.Urn(slug),
            Title = ProblemTypes.TitleOf(slug),
            Status = status ?? slugStatus,
            Detail = detail ?? DefaultDetail(slug),
        };

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return problem;
    }

    /// <summary>An <see cref="IResult"/> for endpoint handlers and filters.</summary>
    public static ProblemHttpResult Problem(string slug, string? detail = null, IReadOnlyDictionary<string, object?>? extensions = null) =>
        TypedResults.Problem(Create(slug, detail, extensions));

    /// <summary>A result that also sets <c>Retry-After</c> in whole seconds.</summary>
    public static IResult ProblemWithRetryAfter(string slug, int retryAfterSeconds, string? detail = null) =>
        new RetryAfterResult(Problem(slug, detail), retryAfterSeconds);

    /// <summary>Writes a problem directly to the response (cookie events, limiter rejections, the exception handler).</summary>
    public static async Task WriteAsync(HttpContext httpContext, int status, string slug, string? detail = null, int? retryAfterSeconds = null, IReadOnlyDictionary<string, object?>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problem = Create(slug, detail, extensions, status);
        httpContext.Response.StatusCode = status;
        if (retryAfterSeconds is { } seconds)
        {
            httpContext.Response.Headers.RetryAfter = Math.Max(1, seconds).ToString(CultureInfo.InvariantCulture);
        }

        var service = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (service is not null && await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }))
        {
            return;
        }

        // The default writer declines when Accept excludes JSON (a browser navigation); the API still answers JSON.
        ProblemDetailsCustomizer.Apply(httpContext, problem);
        var json = httpContext.RequestServices.GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()?.Value.SerializerOptions ?? JsonSerializerOptions.Web;
        httpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, problem, json, httpContext.RequestAborted);
    }

    private static string DefaultDetail(string slug) => slug switch
    {
        ProblemTypes.Validation => "One or more fields are invalid.",
        ProblemTypes.Antiforgery => "The request is missing a valid X-CSRF-TOKEN header. Fetch a fresh token and try again.",
        ProblemTypes.InvalidCurrentPassword => "Your current password is incorrect.",
        ProblemTypes.WeakPassword => "The new password does not meet the password policy.",
        ProblemTypes.InvalidMfaCode => "That code didn't work. Check the time on your phone and try the newest code.",
        ProblemTypes.Unauthenticated => "Sign in to continue.",
        ProblemTypes.InvalidCredentials => "Incorrect username or password.",
        ProblemTypes.Forbidden => "You do not have access to this resource.",
        ProblemTypes.NotYourModule => "You are not assigned to this module.",
        ProblemTypes.PasswordChangeRequired => "Change your password to continue.",
        ProblemTypes.MfaSetupRequired => "Set up two-step verification to continue.",
        ProblemTypes.NotFound => "No resource matches this request.",
        ProblemTypes.MethodNotAllowed => "This method is not allowed here.",
        ProblemTypes.PayloadTooLarge => "The request body is too large.",
        ProblemTypes.UnsupportedMediaType => "Send the request body as application/json.",
        ProblemTypes.DemoAccount => "Demo accounts are read-only.",
        ProblemTypes.MfaAlreadyEnabled => "Two-step verification is already on for this account.",
        ProblemTypes.RateLimited => "Too many requests. Try again after the time in Retry-After.",
        ProblemTypes.InternalError => "Something went wrong on our side. Quote the traceId when you report it.",
        ProblemTypes.ServerBusy => "The portal is very busy right now. Try again after the time in Retry-After.",
        ProblemTypes.Timeout => "The request took too long and was stopped. Try again in a moment.",
        _ => ProblemTypes.TitleOf(slug) + ".",
    };

    private sealed class RetryAfterResult(IResult inner, int retryAfterSeconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = Math.Max(1, retryAfterSeconds).ToString(CultureInfo.InvariantCulture);
            return inner.ExecuteAsync(httpContext);
        }
    }
}
