using Microsoft.Net.Http.Headers;
using RushDay.Api.Security;

namespace RushDay.Api.Hosting;

/// <summary>
/// Serves the React build in <c>wwwroot</c> (05-frontend.md section 4). <see cref="UseRushDaySpa"/> runs after the
/// security headers and before routing; <see cref="MapRushDaySpaFallbacks"/> runs after every <c>Map*</c>. An unknown
/// <c>/api</c> path is a JSON 404, never <c>index.html</c> and never 401; every other unmatched GET is the SPA shell.
/// </summary>
public static class SpaHosting
{
    public const string IndexFile = "index.html";

    public const string NotBuiltMessage = "Front end not built: run \"npm run build\" in src/RushDay.Web or use \"npm run dev\".";

    /// <summary>
    /// <c>/assets/*</c> (Vite's hashed output) is immutable for a year; everything else is revalidated, so a deploy
    /// shows on the next load while the shell stays cacheable.
    /// </summary>
    public static StaticFileOptions StaticFileOptions { get; } = new()
    {
        OnPrepareResponse = context =>
        {
            var headers = context.Context.Response.GetTypedHeaders();
            headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/assets")
                ? new CacheControlHeaderValue
                {
                    Public = true,
                    MaxAge = TimeSpan.FromDays(365),
                    Extensions = { new NameValueHeaderValue("immutable") },
                }
                : new CacheControlHeaderValue { NoCache = true };
        },
    };

    public static WebApplication UseRushDaySpa(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseDefaultFiles();
        app.UseStaticFiles(StaticFileOptions);
        return app;
    }

    public static WebApplication MapRushDaySpaFallbacks(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapFallback("/api/{**rest}", (HttpContext http) =>
                ProblemResults.WriteAsync(http, StatusCodes.Status404NotFound, ProblemTypes.NotFound))
            .AllowAnonymous()
            .ExcludeFromDescription();

        if (app.Environment.WebRootFileProvider.GetFileInfo(IndexFile).Exists)
        {
            // Deep links such as /student/results: without AllowAnonymous the fallback policy would answer 401 to an
            // anonymous visitor of /login.
            app.MapFallbackToFile(IndexFile, StaticFileOptions)
                .AllowAnonymous()
                .ExcludeFromDescription();
        }
        else
        {
            app.MapFallback("{**path}", () => Results.Text(NotBuiltMessage, "text/plain", statusCode: StatusCodes.Status503ServiceUnavailable))
                .AllowAnonymous()
                .ExcludeFromDescription();
        }

        return app;
    }
}
