using System.Text.Json.Serialization;
using RushDay.Domain.Modules;

namespace RushDay.Api.Contracts;

/// <summary><c>GET /api</c>.</summary>
public sealed record ApiIndex(string Name, string Story, string Commit, string Environment, ApiIndexLinks Links);

/// <summary>The index links; <see cref="Openapi"/> is present only in Development (T21).</summary>
public sealed record ApiIndexLinks(
    string Health,
    string Ready,
    string Status,
    string Login,
    string Github,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Openapi);

/// <summary><c>GET /api/health/live</c>.</summary>
public sealed record LiveResponse(string Status);

/// <summary><c>GET /api/health/ready</c>.</summary>
public sealed record ReadyResponse(string Status, IReadOnlyList<ReadyCheck> Checks);

public sealed record ReadyCheck(string Name, string Status, double DurationMs);

/// <summary><c>GET /api/public/status</c> (02-api.md section 8.1).</summary>
public sealed record PublicStatus(
    DateTimeOffset ServerTime,
    InstitutionInfo Institution,
    string AcademicYear,
    Semester CurrentSemester,
    PublicationBrief? NextPublication,
    PublicationBrief? LatestPublication,
    IReadOnlyList<WindowInfo> EnrolmentWindows,
    DemoInfo? Demo);

public sealed record InstitutionInfo(
    string Name,
    string ShortName,
    string TimeZone,
    string? PrivacyNoticeUrl,
    string ResultsFootnote,
    SupportInfo? Support);

public sealed record SupportInfo(string? Email, string? Url);

/// <summary>Null unless <c>Demo:Enabled</c>.</summary>
public sealed record DemoInfo(IReadOnlyList<DemoAccountInfo> Accounts);

public sealed record DemoAccountInfo(string Role, string Username, string Password, string Hint);
