namespace RushDay.Domain.Audit;

/// <summary>
/// Append-only record of who changed what. The application never updates or deletes these rows, and the
/// database refuses to (trigger trg_audit_events_immutable). Actor fields are null for startup steps; the
/// denormalised student and module ids exist for filtering.
/// </summary>
public sealed class AuditEvent
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ActorUsername { get; init; }
    public string? ActorRole { get; init; }
    public required string Action { get; init; }
    public required string SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public Guid? StudentId { get; init; }
    public Guid? ModuleId { get; init; }

    /// <summary>Small JSON document (before/after values, reason, decision flags); stored as jsonb.</summary>
    public string? Details { get; init; }

    public string? RequestId { get; init; }
    public string? IpHash { get; init; }

    /// <summary>Should: sha256 chain over the previous row for tamper evidence. Always null in v1.</summary>
    public string? ChainHash { get; init; }
}
