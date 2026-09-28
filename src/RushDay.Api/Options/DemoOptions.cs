namespace RushDay.Api.Options;

/// <summary>
/// The demo switch (D29), bound from "Demo". Off by default and never set by render.yaml; the public demo sets both
/// values in the Render dashboard.
/// </summary>
public sealed record DemoOptions
{
    public const string SectionName = "Demo";

    public bool Enabled { get; init; }

    /// <summary>Required in Production when <see cref="Enabled"/> is true, otherwise startup aborts.</summary>
    public bool PublicDemoAcknowledged { get; init; }
}
