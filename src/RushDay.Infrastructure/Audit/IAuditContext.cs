namespace RushDay.Infrastructure.Audit;

/// <summary>
/// Who is acting and from where, for <see cref="AuditWriter"/> (03-security.md section 7). The API implements it from
/// the request (<c>HttpAuditContext</c>); every member is null for startup steps and background work.
/// </summary>
public interface IAuditContext
{
    Guid? ActorUserId { get; }

    string? ActorUsername { get; }

    string? ActorRole { get; }

    /// <summary><c>Activity.Current?.Id ?? HttpContext.TraceIdentifier</c>.</summary>
    string? RequestId { get; }

    /// <summary>The keyed daily hash of the client address (D32); never the address itself.</summary>
    string? IpHash { get; }
}

/// <summary>The context of work that no person started: every field null.</summary>
public sealed class SystemAuditContext : IAuditContext
{
    public static SystemAuditContext Instance { get; } = new();

    public Guid? ActorUserId => null;

    public string? ActorUsername => null;

    public string? ActorRole => null;

    public string? RequestId => null;

    public string? IpHash => null;
}
