using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;
using RushDay.Api.Options;

namespace RushDay.Api.Security;

/// <summary>
/// Host filtering (03-security.md section 3, T20). Outside Development the allowed hosts are <c>Security:AllowedHosts</c>
/// plus <c>RENDER_EXTERNAL_HOSTNAME</c> whenever Render sets it (so the Blueprint needs no manual step and a custom
/// domain never breaks Render's health check); with neither, <c>*</c> with a startup Warning. Development keeps <c>*</c>.
/// The web host's own <c>HostFilteringStartupFilter</c> places the middleware first in the pipeline; this only decides
/// its host list. Its 400 for a refused host carries no security headers (it answers before any of them run).
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

        // Render's own hostname is always allowed when Render sets it: its deploy health check calls that name, so a
        // custom domain in Security:AllowedHosts must not lock the platform out.
        var hosts = Split(security.AllowedHosts);
        foreach (var render in Split(configuration[RenderHostnameVariable]))
        {
            if (!hosts.Contains(render, StringComparer.OrdinalIgnoreCase))
            {
                hosts.Add(render);
            }
        }

        return hosts.Count > 0 ? hosts : ["*"];
    }

    public static bool IsDisabled(IReadOnlyList<string> hosts) => hosts.Count == 0 || hosts.Contains("*");

    private static List<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
