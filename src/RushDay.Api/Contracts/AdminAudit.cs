using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Contracts;

/// <summary><c>AuditEventView</c> of 02-api.md section 7; <c>details</c> is the stored JSON object.</summary>
public sealed record AuditEventView(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? ActorUsername,
    string? ActorRole,
    string Action,
    string SubjectType,
    string? SubjectId,
    string? StudentNumber,
    string? ModuleCode,
    JsonElement? Details,
    string? RequestId)
{
    public static AuditEventView From(AuditEventRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        JsonElement? details = null;
        if (!string.IsNullOrEmpty(row.Details))
        {
            using var document = JsonDocument.Parse(row.Details);
            details = document.RootElement.Clone();
        }

        return new AuditEventView(row.Id, row.OccurredAt, row.ActorUsername, row.ActorRole, row.Action, row.SubjectType, row.SubjectId, row.StudentNumber, row.ModuleCode, details, row.RequestId);
    }
}

/// <summary>The filters of <c>GET /api/admin/audit</c> and of its CSV export.</summary>
public sealed record AuditParameters
{
    [StringLength(StaffPatterns.SearchMaxLength)]
    [NoNul]
    public string? Actor { get; init; }

    [RegularExpression(StaffPatterns.StudentNumber)]
    public string? StudentNumber { get; init; }

    [RegularExpression(StaffPatterns.ModuleCode)]
    public string? ModuleCode { get; init; }

    [StringLength(StaffPatterns.SearchMaxLength)]
    [NoNul]
    public string? Action { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    /// <summary>The filter with both bounds normalised to UTC (the database stores and compares UTC instants).</summary>
    public AuditFilter ToFilter() => new(Actor, StudentNumber, ModuleCode, Action, From?.ToUniversalTime(), To?.ToUniversalTime());

    /// <summary><c>audit-&lt;from&gt;-&lt;to&gt;.csv</c>, each bound as <c>yyyyMMdd</c> or <c>all</c>.</summary>
    public string FileName() => $"audit-{Day(From)}-{Day(To)}.csv";

    /// <summary>The filters as the <c>audit.exported</c> row records them.</summary>
    public object Describe() => new
    {
        actor = Actor,
        studentNumber = StudentNumber?.ToUpperInvariant(),
        moduleCode = ModuleCode?.ToUpperInvariant(),
        action = Action,
        from = From?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        to = To?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
    };

    private static string Day(DateTimeOffset? instant) =>
        instant is { } value ? value.ToUniversalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture) : "all";
}
