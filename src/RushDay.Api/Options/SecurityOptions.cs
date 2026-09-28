namespace RushDay.Api.Options;

/// <summary>Proxy trust, host filtering and HSTS (03-security.md section 3), bound from "Security".</summary>
public sealed record SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Honour X-Forwarded-For/Proto from any peer. True on Render (its proxy is the only peer), in Development and in
    /// tests; false by default so the same image on an unknown host cannot be told a client's address by the client.
    /// </summary>
    public bool TrustForwardedHeaders { get; init; }

    /// <summary>Semicolon-separated host names; when unset in Production, <c>RENDER_EXTERNAL_HOSTNAME</c> is used.</summary>
    public string? AllowedHosts { get; init; }

    public bool HstsIncludeSubDomains { get; init; }

    public bool HstsPreload { get; init; }
}
