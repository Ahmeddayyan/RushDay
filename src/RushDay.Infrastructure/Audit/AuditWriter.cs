using System.Text.Json;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Audit;

/// <summary>
/// Adds an <c>audit_events</c> row to the caller's <see cref="RushDayDbContext"/> (03-security.md section 7); the
/// caller's <c>SaveChangesAsync</c> or transaction commits it atomically with the change it records. Actor, request id
/// and address hash come from <see cref="IAuditContext"/>. The table is append-only at the database, and this class
/// has no method that would try to change a row.
/// </summary>
public sealed class AuditWriter(IAuditContext context, TimeProvider clock)
{
    private const int MaxUsernameLength = 64;
    private const int MaxRoleLength = 16;

    private static readonly JsonSerializerOptions DetailsJson = new(JsonSerializerDefaults.Web);

    public AuditEvent Record(
        RushDayDbContext db,
        string action,
        string subjectType,
        string? subjectId,
        object? details,
        Guid? studentId = null,
        Guid? moduleId = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        // AuditActions.SubjectOf is the single mapping; a mismatch is a programming error, not data.
        var expectedSubject = AuditActions.SubjectOf(action);
        if (!string.Equals(expectedSubject, subjectType, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Audit action '{action}' belongs to subject '{expectedSubject}', not '{subjectType}'.", nameof(subjectType));
        }

        var auditEvent = new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = clock.GetUtcNow(),
            ActorUserId = context.ActorUserId,
            ActorUsername = Truncate(context.ActorUsername, MaxUsernameLength),
            ActorRole = Truncate(context.ActorRole, MaxRoleLength),
            Action = action,
            SubjectType = subjectType,
            SubjectId = subjectId,
            StudentId = studentId,
            ModuleId = moduleId,
            Details = details is null ? null : JsonSerializer.Serialize(details, DetailsJson),
            RequestId = context.RequestId,
            IpHash = context.IpHash,
        };

        db.AuditEvents.Add(auditEvent);
        return auditEvent;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
