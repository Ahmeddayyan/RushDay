using Microsoft.EntityFrameworkCore;
using RushDay.Api.Contracts;
using RushDay.Api.Security;
using RushDay.Api.Startup;
using RushDay.Domain.Audit;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Endpoints;

/// <summary>
/// The audit log, <c>/api/admin/audit</c> (02-api.md section 8.5, 03-security.md section 7): a filtered page, newest
/// first, and the CSV export. The export is a bulk read of personal data, so it is audited itself: the
/// <c>audit.exported</c> row (filters, row count, truncation) is committed before the first byte of CSV, and the
/// rows are streamed from a REPEATABLE READ snapshot taken before that row existed, so the count it records is the
/// count it sent. Capped at 50,000 rows for a bounded range of at most 31 days, 10,000 otherwise, with
/// <c>X-RushDay-Truncated: true</c> and a final comment line when the cap is hit.
/// </summary>
public static class AdminAuditEndpoints
{
    public const string TruncatedHeader = "X-RushDay-Truncated";
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static RouteGroupBuilder MapAdminAuditEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/audit", ListAsync).WithName("AdminAudit");
        admin.MapGet("/audit/export.csv", ExportAsync)
            .RequireRateLimiting(RateLimitPolicies.Write)
            .WithRequestTimeout(ServiceRegistration.ExportTimeoutPolicy)
            .WithName("AdminAuditExport");
        return admin;
    }

    private static async Task<IResult> ListAsync([AsParameters] AuditParameters parameters, AuditQuery query, CancellationToken cancellationToken)
    {
        var page = PageRequest.Of(parameters.Page, parameters.PageSize, DefaultPageSize, MaxPageSize);
        if (page.IsTooDeep)
        {
            return StaffPatterns.PageTooDeep();
        }

        var rows = await query.ListAsync(parameters.ToFilter(), page, cancellationToken);
        return TypedResults.Ok(new Paged<AuditEventView>([.. rows.Items.Select(AuditEventView.From)], rows.Page, rows.PageSize, rows.Total));
    }

    private static async Task ExportAsync(
        [AsParameters] AuditParameters parameters,
        HttpContext http,
        IDbContextFactory<RushDayDbContext> contexts,
        RushDayDbContext db,
        AuditWriter audit,
        CancellationToken cancellationToken)
    {
        var filter = parameters.ToFilter();

        await using var snapshot = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AuditQuery.BeginSnapshotAsync(snapshot, cancellationToken);
        var plan = await AuditQuery.PlanAsync(snapshot, filter, cancellationToken);

        // Committed on the request's own connection before anything is streamed.
        audit.Record(db, AuditActions.AuditExported, AuditSubjects.System, subjectId: null, new { filters = parameters.Describe(), rowCount = plan.RowCount, truncated = plan.Truncated });
        await db.SaveChangesAsync(cancellationToken);

        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = AuditCsvWriter.ContentType;
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{parameters.FileName()}\"";
        if (plan.Truncated)
        {
            http.Response.Headers[TruncatedHeader] = "true";
        }

        await AuditCsvWriter.WriteAsync(http.Response.Body, AuditQuery.StreamAsync(snapshot, filter, plan.Cap, cancellationToken), plan.Truncated ? plan.Cap : null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
