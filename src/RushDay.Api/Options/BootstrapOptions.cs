namespace RushDay.Api.Options;

/// <summary>The first administrator of a customer deployment (01-domain-and-data.md section 6 step 5), bound from "Bootstrap".</summary>
public sealed record BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    /// <summary>A customer who evaluated with demo mode first sets another name, because <c>admin</c> is then a disabled demo account.</summary>
    public string AdminUsername { get; init; } = "admin";

    /// <summary>Consumed only when no usable administrator exists; validated by the password policy; removed from Render after first use.</summary>
    public string? AdminPassword { get; init; }
}
