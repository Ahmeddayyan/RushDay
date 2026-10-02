namespace RushDay.Infrastructure.Audit;

/// <summary>
/// Who is acting and from where, for <see cref="AuditWriter"/> (03-security.md section 7) and for the demo-actor rule
/// of <c>AccountService</c> (02-api.md section 8.5). The API implements it from the request (<c>HttpAuditContext</c>);
/// every member is null (or false) for startup steps and background work.
/// </summary>
public interface IAuditContext
{
    Guid? ActorUserId { get; }

    string? ActorUsername { get; }

    string? ActorRole { get; }

    /// <summary>
    /// True when the actor signed in with an <c>is_demo</c> account (the <c>demo</c> claim): its password is public, so
    /// it may not change real accounts, and what it creates is demo data.
    /// </summary>
    bool ActorIsDemo { get; }

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

    public bool ActorIsDemo => false;

    public string? RequestId => null;

    public string? IpHash => null;
}
