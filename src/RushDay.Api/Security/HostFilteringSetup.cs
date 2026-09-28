using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;

namespace RushDay.Api.Security;

/// <summary>
/// Host filtering (03-security.md section 3, T20). In Production the allowed hosts are <c>Security:AllowedHosts</c>
/// when set, else <c>RENDER_EXTERNAL_HOSTNAME</c> (Render injects it, so the Blueprint needs no manual step), else
/// <c>*</c> with a startup Warning. Development keeps <c>*</c>. The web host's own <c>HostFilteringStartupFilter</c>
/// places the middleware first in the pipeline; this only decides its host list.
/// </summary>
public static class HostFilteringSetup
{
    public const string RenderHostnameVariable = "RENDER_EXTERNAL_HOSTNAME";
    public const string DisabledWarning = "Host filtering disabled: set Security__AllowedHosts";

    public static IServiceCollection AddRushDayHostFiltering(this IServiceCollection services)
    {
        // Registered after the web host's default PostConfigure (which reads "AllowedHosts"), so this one wins.
        services.AddOptions<HostFilteringOptions>()
            .PostConfigure<IConfiguration, IHostEnvironment, IOptions<SecurityOptions>>((options, configuration, environment, security) =>
            {
                options.AllowedHosts = [.. ResolveAllowedHosts(configuration, environment, security.Value)];
            });

        return services;
    }

    /// <summary>The effective host list; <c>["*"]</c> means filtering is off.</summary>
    public static IReadOnlyList<string> ResolveAllowedHosts(IConfiguration configuration, IHostEnvironment environment, SecurityOptions security)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(security);

        if (environment.IsDevelopment())
        {
            return ["*"];
        }

        var configured = Split(security.AllowedHosts);
        if (configured.Count > 0)
        {
            return configured;
        }

        var render = Split(configuration[RenderHostnameVariable]);
        return render.Count > 0 ? render : ["*"];
    }

    public static bool IsDisabled(IReadOnlyList<string> hosts) => hosts.Count == 0 || hosts.Contains("*");

    private static List<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
