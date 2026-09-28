using System.Diagnostics;
using RushDay.Api.Security;
using RushDay.Infrastructure.Audit;

namespace RushDay.Api.Auth;

/// <summary>
/// <see cref="IAuditContext"/> for requests (03-security.md section 7): the actor from claims, the request id, and the
/// keyed daily hash of the client address (after forwarded headers). Null members outside a request.
/// </summary>
public sealed class HttpAuditContext(IHttpContextAccessor accessor, CurrentUser currentUser, IpHasher ipHasher) : IAuditContext
{
    public Guid? ActorUserId => currentUser.UserId;

    public string? ActorUsername => currentUser.Username;

    public string? ActorRole => currentUser.Role;

    public string? RequestId => accessor.HttpContext is { } http ? Activity.Current?.Id ?? http.TraceIdentifier : null;

    public string? IpHash => accessor.HttpContext is { } http ? ipHasher.Hash(http.Connection.RemoteIpAddress) : null;
}
