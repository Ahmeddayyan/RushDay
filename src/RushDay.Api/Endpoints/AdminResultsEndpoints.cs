using Microsoft.Extensions.Options;
using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Observability;
using RushDay.Api.Options;
using RushDay.Api.Security;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Endpoints;

/// <summary>
/// The results lifecycle, <c>/api/admin/results</c> (02-api.md section 8.5, D8, D9): submission progress for a (year,
/// semester), publish at an instant (only publishable modules; up to 90 days ahead; optionally with a pinned
/// announcement), reschedule and cancel while scheduled, unpublish once live, return a module to draft, correct one
/// mark. Every mutation invalidates <c>publications:brief</c> after it commits; a publish with <c>announce</c>, and
/// every reschedule, cancel, unpublish and return to draft (which move or delete a publication's announcement), also
/// <c>announcements:university</c>.
/// </summary>
public static class AdminResultsEndpoints
{
    public static RouteGroupBuilder MapAdminResultsEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/results", ProgressAsync).WithName("AdminResults");
        var results = admin.MapGroup("/results");
        results.MapPost("/publish", PublishAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("PublishResults");
        results.MapPut("/publications/{id:guid}", RescheduleAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("ReschedulePublication");
        results.MapDelete("/publications/{id:guid}", CancelAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("CancelPublication");
        results.MapPost("/publications/{id:guid}/unpublish", UnpublishAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("UnpublishPublication");
        results.MapPost("/modules/" + StaffPatterns.ModuleCodeRoute + "/return-to-draft", ReturnToDraftAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("ReturnToDraft");
        results.MapPost("/modules/" + StaffPatterns.ModuleCodeRoute + "/marks/" + StaffPatterns.StudentNumberRoute + "/correct", CorrectAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("CorrectMark");
        return admin;
    }

    // GET /api/admin/results?semester=&academicYear=
    private static async Task<IResult> ProgressAsync([AsParameters] SemesterQuery query, AdminResultsQuery results, EnrolmentWindowService windows, TimeProvider clock, CancellationToken cancellationToken)
    {
        var semester = StaffPatterns.ParseSemester(query.Semester);
        if (semester is null)
        {
            return ProblemResults.Problem(ProblemTypes.Validation);
        }

        var year = query.AcademicYear ?? (await windows.CurrentAsync(cancellationToken)).AcademicYear;
        var now = clock.GetUtcNow();
        var data = await results.ExecuteAsync(year, semester.Value, now, cancellationToken);
        return TypedResults.Ok(AdminResultsResponse.From(data, now));
    }

    // POST /api/admin/results/publish: the results-day button.
    private static async Task<IResult> PublishAsync(
        PublishRequest request,
        CurrentUser user,
        ResultsPublicationService publications,
        PublicationCache briefs,
        AnnouncementCache announcements,
        IOptions<BrandingOptions> branding,
        RushDayMetrics metrics,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await publications.PublishAsync(
            request.AcademicYear!,
            request.Semester!.Value,
            request.PublishAt!.Value,
            request.Announce!.Value,
            request.Note,
            branding.Value.ResultsFootnote,
            userId,
            cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        metrics.ResultsPublished.Add(1);
        await briefs.InvalidateAsync(cancellationToken);
        if (result.Value!.Announced)
        {
            await announcements.InvalidateAsync(cancellationToken);
        }

        return TypedResults.Ok(PublishResponse.From(result.Value, clock.GetUtcNow()));
    }

    // PUT /api/admin/results/publications/{id}: only while scheduled.
    private static async Task<IResult> RescheduleAsync(Guid id, ReschedulePublicationRequest request, ResultsPublicationService publications, PublicationCache briefs, AnnouncementCache announcements, TimeProvider clock, CancellationToken cancellationToken)
    {
        var result = await publications.RescheduleAsync(id, request.PublishAt!.Value, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        await InvalidateAsync(briefs, announcements, cancellationToken);
        return TypedResults.Ok(PublicationInfo.From(result.Value!, clock.GetUtcNow()));
    }

    // DELETE /api/admin/results/publications/{id}: cancel, only while scheduled.
    private static async Task<IResult> CancelAsync(Guid id, ResultsPublicationService publications, PublicationCache briefs, AnnouncementCache announcements, CancellationToken cancellationToken)
    {
        var result = await publications.CancelAsync(id, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        await InvalidateAsync(briefs, announcements, cancellationToken);
        return TypedResults.Ok(RevertedPublicationResponse.From(result.Value!));
    }

    // POST /api/admin/results/publications/{id}/unpublish: only once live; students stop seeing the marks at once.
    private static async Task<IResult> UnpublishAsync(Guid id, ReasonRequest request, ResultsPublicationService publications, PublicationCache briefs, AnnouncementCache announcements, CancellationToken cancellationToken)
    {
        var result = await publications.UnpublishAsync(id, request.Reason!, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        await InvalidateAsync(briefs, announcements, cancellationToken);
        return TypedResults.Ok(RevertedPublicationResponse.From(result.Value!));
    }

    // POST /api/admin/results/modules/{code}/return-to-draft
    private static async Task<IResult> ReturnToDraftAsync(string code, ReturnToDraftRequest request, ResultsPublicationService publications, PublicationCache briefs, AnnouncementCache announcements, CancellationToken cancellationToken)
    {
        var result = await publications.ReturnToDraftAsync(code, request.AcademicYear, request.Reason!, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        await InvalidateAsync(briefs, announcements, cancellationToken);
        return TypedResults.Ok(new ReturnToDraftResponse(result.Value!.Code, MarksState.Draft, result.Value.FromScheduledPublication));
    }

    // POST /api/admin/results/modules/{code}/marks/{studentNumber}/correct
    private static async Task<IResult> CorrectAsync(
        string code,
        string studentNumber,
        CorrectMarkRequest request,
        CurrentUser user,
        ResultsPublicationService publications,
        PublicationCache briefs,
        CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await publications.CorrectAsync(code, studentNumber, request.EffectiveOutcome, request.Mark, request.Reason!, userId, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result.Error);
        }

        await briefs.InvalidateAsync(cancellationToken);
        return TypedResults.Ok(CorrectMarkResponse.From(result.Value!));
    }

    /// <summary>
    /// After a reschedule, cancel, unpublish or return to draft has committed: the brief, and the university
    /// announcements, since a publication's "results are available" announcement moves or goes with it (review S6 E2,
    /// E3). Both keys are generation-versioned, so a fill that read the rows before the commit is retired.
    /// </summary>
    private static async Task InvalidateAsync(PublicationCache briefs, AnnouncementCache announcements, CancellationToken cancellationToken)
    {
        await briefs.InvalidateAsync(cancellationToken);
        await announcements.InvalidateAsync(cancellationToken);
    }

    private static IResult Problem(PublicationError error) => error switch
    {
        PublicationError.CorrectionUnchanged => ProblemResults.Problem(
            ProblemTypes.Validation,
            "A correction must change the mark or the outcome.",
            new Dictionary<string, object?> { ["errors"] = new Dictionary<string, string[]> { ["mark"] = ["The corrected mark and outcome are the ones already recorded."] } }),
        PublicationError.NothingToPublish => ProblemResults.Problem(ProblemTypes.NothingToPublish, "No module of that semester is submitted with every mark entered."),
        PublicationError.PublishTooFarAhead => ProblemResults.Problem(ProblemTypes.PublishTooFarAhead, "Results can be scheduled at most 90 days ahead."),
        PublicationError.PublicationNotFound => ProblemResults.Problem(ProblemTypes.PublicationNotFound, "No publication has that id."),
        PublicationError.PublicationLive => ProblemResults.Problem(ProblemTypes.PublicationLive, "This publication is already live; unpublish it instead."),
        PublicationError.PublicationScheduled => ProblemResults.Problem(ProblemTypes.PublicationScheduled, "This publication is still scheduled; cancel it instead."),
        PublicationError.ModuleNotFound => ProblemResults.Problem(ProblemTypes.ModuleNotFound, "No module has that code."),
        PublicationError.ModuleNotSubmitted => ProblemResults.Problem(ProblemTypes.ModuleNotSubmitted, "These marks are still in draft with the module's lecturers."),
        PublicationError.ModuleLocked => ProblemResults.Problem(ProblemTypes.ModuleLocked, "These marks are live; unpublish the semester or correct single marks instead."),
        PublicationError.GradeNotFound => ProblemResults.Problem(ProblemTypes.GradeNotFound, "That student has no mark on this module."),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Not a refusal."),
    };
}
