using Microsoft.Extensions.Diagnostics.HealthChecks;
using RushDay.Api.Contracts;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Endpoints;

/// <summary>
/// <c>GET /api/admin/overview</c> (02-api.md section 8.5): counts, the current year's windows, the next and latest
/// publications, submission progress per semester, the ten latest audit rows, and the database state from the same
/// DbContext check as <c>/api/health/ready</c>, run in-process rather than through the rate-limited route.
/// </summary>
public static class AdminOverviewEndpoints
{
    public static RouteGroupBuilder MapAdminOverviewEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/overview", OverviewAsync).WithName("AdminOverview");
        return admin;
    }

    private static async Task<IResult> OverviewAsync(
        OverviewQuery query,
        EnrolmentWindowService windows,
        PublicationCache publications,
        ResultsPublicationService results,
        HealthCheckService health,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var calendar = await windows.CurrentAsync(cancellationToken);
        var yearWindows = await windows.ForYearAsync(calendar.AcademicYear, cancellationToken);
        var briefs = await publications.GetBriefAsync(now, cancellationToken);

        var data = await query.ExecuteAsync(calendar.AcademicYear, now, cancellationToken);
        var ids = new[] { briefs.Next?.Id, briefs.Latest?.Id }.OfType<Guid>().ToArray();
        var rows = (await results.ReadManyAsync(ids, cancellationToken)).ToDictionary(p => p.Id);

        var report = await health.CheckHealthAsync(r => r.Name == "database", cancellationToken);

        return TypedResults.Ok(new OverviewResponse(
            OverviewCountsView.From(data.Counts),
            calendar.AcademicYear,
            [.. yearWindows.Select(w => WindowInfo.From(w, now))],
            Info(briefs.Next),
            Info(briefs.Latest),
            [.. data.SubmissionProgress.Select(SubmissionProgressView.From)],
            [.. data.RecentAudit.Select(AuditEventView.From)],
            report.Status == HealthStatus.Healthy ? DatabaseState.Ok : DatabaseState.Degraded));

        PublicationInfo? Info(PublicationSnapshot? brief) =>
            brief is not null && rows.TryGetValue(brief.Id, out var row) ? PublicationInfo.From(row, now) : null;
    }
}
