using RushDay.Api.Auth;
using RushDay.Api.Endpoints;
using RushDay.Api.Hosting;
using RushDay.Api.Security;

namespace RushDay.Api.Startup;

/// <summary>
/// The middleware order and the <c>/api</c> endpoint group. Host filtering runs first of all (the web host's own
/// startup filter), then forwarded headers, so every later component sees the real client address and scheme. The
/// request log sits outside the exception handler, so it records the status the client actually received; the
/// security headers (HSTS included, outside Development) are written in an <c>OnStarting</c> callback, so error
/// responses carry them too. The command counter starts right before the endpoint.
/// </summary>
public static partial class PipelineConfiguration
{
    public static WebApplication UseRushDayPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseForwardedHeaders();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRushDaySpa();
        app.UseRouting();
        app.UseRequestTimeouts();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.UseOutputCache();
        app.UseMiddleware<EndpointCommandCounterMiddleware>();

        return app;
    }

    /// <summary>
    /// Maps every route under one <c>/api</c> group carrying the antiforgery filter and the two gates (in that order),
    /// then the SPA fallbacks. Returns the group.
    /// </summary>
    public static RouteGroupBuilder MapRushDayEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var api = app.MapGroup("/api");
        ConfigureApiGroup(api);

        api.MapIndexEndpoints();
        api.MapHealthEndpoints();
        api.MapPublicEndpoints();
        api.MapAuthEndpoints();

        MapStudentSurface(api);
        MapStaffSurface(api);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
        }

        app.MapRushDaySpaFallbacks();
        return api;
    }

    /// <summary>The filters every <c>/api</c> endpoint runs after authorization: antiforgery, then the two gates.</summary>
    public static RouteGroupBuilder ConfigureApiGroup(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.AddEndpointFilter<AntiforgeryEndpointFilter>();
        api.AddEndpointFilter<MustChangePasswordFilter>();
        api.AddEndpointFilter<MfaSetupRequiredFilter>();
        return api;
    }

    // Stages S4 and S6 map their routes by implementing these in their own endpoint files (06 ownership rules):
    // `public static partial class PipelineConfiguration { static partial void MapStudentSurface(RouteGroupBuilder api) { ... } }`.
    static partial void MapStudentSurface(RouteGroupBuilder api);

    static partial void MapStaffSurface(RouteGroupBuilder api);
}
