using RushDay.Api.Contracts;

namespace RushDay.Api.Endpoints;

/// <summary><c>GET /api</c>: what this service is, with the owner's story verbatim (00-overview.md section 1).</summary>
public static class IndexEndpoints
{
    /// <summary>The owner's story. Verbatim wherever the project describes itself; <c>scripts/check-story.ps1</c> greps this file.</summary>
    public const string Story = "I built RushDay because my own university's portal fell over every results day and enrolment window. I wanted to understand why that happens and to build a portal that doesn't crash under the same load.";

    public const string GithubUrl = "https://github.com/Ahmeddayyan/RushDay";

    public static RouteGroupBuilder MapIndexEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(string.Empty, (IHostEnvironment environment, IConfiguration configuration) => TypedResults.Ok(new ApiIndex(
                Name: "RushDay",
                Story: Story,
                Commit: configuration["RENDER_GIT_COMMIT"] is { Length: > 0 } commit ? commit : "local",
                Environment: environment.EnvironmentName,
                Links: new ApiIndexLinks(
                    Health: "/api/health/live",
                    Ready: "/api/health/ready",
                    Status: "/api/public/status",
                    Login: "/api/auth/login",
                    Github: GithubUrl,
                    Openapi: environment.IsDevelopment() ? "/api/openapi/v1.json" : null))))
            .AllowAnonymous()
            .WithName("ApiIndex")
            .WithTags("Public");

        return api;
    }
}
