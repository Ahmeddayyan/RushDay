using RushDay.Api.Auth;
using RushDay.Api.Contracts;
using RushDay.Api.Security;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Modules;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Endpoints;

/// <summary>
/// Modules, <c>/api/admin/modules</c> (02-api.md section 8.5, D33): the registry's list with this year's marks status,
/// create, update (capacity and semester guards), lecturer assignment, trim to capacity, and the read-only roster and
/// marks sheet of any module (the lecturer shapes without the <c>module_lecturers</c> join, for any academic year).
/// Mutations invalidate <c>catalogue:all</c>; an assignment change also every affected <c>lecturer-modules</c> entry.
/// </summary>
public static class AdminModuleEndpoints
{
    public static RouteGroupBuilder MapAdminModuleEndpoints(this RouteGroupBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        admin.MapGet("/modules", ListAsync).WithName("AdminModules");
        admin.MapPost("/modules", CreateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("CreateModule");

        var module = admin.MapGroup("/modules/" + StaffPatterns.ModuleCodeRoute);
        module.MapPut("/", UpdateAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("UpdateModule");
        module.MapGet("/roster", RosterAsync).WithName("AdminModuleRoster");
        module.MapGet("/marks", MarksAsync).WithName("AdminModuleMarks");
        module.MapPost("/trim-to-capacity", TrimAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("TrimToCapacity");
        module.MapPut("/lecturers", SetLecturersAsync).RequireRateLimiting(RateLimitPolicies.Write).WithName("SetModuleLecturers");
        return admin;
    }

    private static async Task<IResult> ListAsync([AsParameters] ModuleListParameters parameters, ModuleAdminService modules, EnrolmentWindowService windows, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var calendar = await windows.CurrentAsync(cancellationToken);
        var allWindows = await windows.AllAsync(cancellationToken);
        var rows = await modules.ListAsync(parameters.IncludeInactive ?? false, calendar.AcademicYear, now, cancellationToken);
        return TypedResults.Ok(rows.Select(m => new AdminModuleView(
            ModuleSummary.From(m.Module, EnrolmentWindowService.Find(allWindows, calendar.AcademicYear, m.Module.Semester), now),
            m.Module.Description,
            MarksStatus.From(m.Marks))).ToList());
    }

    private static async Task<IResult> CreateAsync(
        CreateModuleRequest request,
        ModuleAdminService modules,
        ModuleDetailQuery detail,
        CatalogueCache catalogue,
        EnrolmentWindowService windows,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var result = await modules.CreateAsync(request.Code!, request.ToChange(), cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        await catalogue.InvalidateAsync(cancellationToken);
        return TypedResults.Created((string?)null, await DetailAsync(result.Value!, detail, windows, clock, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        string code,
        UpdateModuleRequest request,
        ModuleAdminService modules,
        ModuleDetailQuery detail,
        CatalogueCache catalogue,
        EnrolmentWindowService windows,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var result = await modules.UpdateAsync(code, request.ToChange(), cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        await catalogue.InvalidateAsync(cancellationToken);
        return TypedResults.Ok(await DetailAsync(result.Value!, detail, windows, clock, cancellationToken));
    }

    // GET /api/admin/modules/{code}/roster: any module, any year (default: the settings year), read-only.
    private static async Task<IResult> RosterAsync(string code, [AsParameters] AdminModuleReadParameters parameters, RosterQuery query, RushDayDbContext db, EnrolmentWindowService windows, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await StaffModules.FindAsync(db, code, cancellationToken) is not { } module)
        {
            return ModuleNotFound();
        }

        var now = clock.GetUtcNow();
        var year = parameters.AcademicYear ?? (await windows.CurrentAsync(cancellationToken)).AcademicYear;
        var page = PageRequest.Of(parameters.Page, parameters.PageSize, LecturerEndpoints.RosterPageSize, LecturerEndpoints.RosterMaxPageSize);
        if (page.IsTooDeep)
        {
            return StaffPatterns.PageTooDeep();
        }

        var rows = await query.ExecuteAsync(module.Id, year, lecturerId: null, parameters.Q, page, cancellationToken);

        var catalogueModule = await StaffModules.CatalogueModuleAsync(db, module.Id, cancellationToken);
        var calendar = await windows.CurrentAsync(cancellationToken);
        var window = await windows.FindAsync(calendar.AcademicYear, catalogueModule.Semester, cancellationToken);
        return TypedResults.Ok(RosterResponse.From(ModuleSummary.From(catalogueModule, window, now), rows));
    }

    // GET /api/admin/modules/{code}/marks: the sheet with myRole null; corrections go through the results route.
    private static async Task<IResult> MarksAsync(string code, [AsParameters] AdminModuleReadParameters parameters, MarksSheetQuery query, RushDayDbContext db, EnrolmentWindowService windows, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await StaffModules.FindAsync(db, code, cancellationToken) is not { } module)
        {
            return ModuleNotFound();
        }

        var year = parameters.AcademicYear ?? (await windows.CurrentAsync(cancellationToken)).AcademicYear;
        var page = PageRequest.Of(parameters.Page, parameters.PageSize, LecturerEndpoints.MarksPageSize, LecturerEndpoints.MarksMaxPageSize);
        if (page.IsTooDeep)
        {
            return StaffPatterns.PageTooDeep();
        }

        var sheet = await query.ExecuteAsync(module.Id, module.Code, module.Title, year, lecturerId: null, myRole: null, parameters.Q, page, clock.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(MarksSheet.From(sheet));
    }

    private static async Task<IResult> TrimAsync(string code, ReasonRequest request, CurrentUser user, ModuleAdminService modules, CatalogueCache catalogue, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await modules.TrimAsync(code, request.Reason!, userId, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        await catalogue.InvalidateAsync(cancellationToken);
        return TypedResults.Ok(TrimResponse.From(result.Value!));
    }

    private static async Task<IResult> SetLecturersAsync(
        string code,
        SetLecturersRequest request,
        CurrentUser user,
        ModuleAdminService modules,
        ModuleDetailQuery detail,
        CatalogueCache catalogue,
        LecturerModuleCache lecturerModules,
        EnrolmentWindowService windows,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
        {
            return ProblemResults.Problem(ProblemTypes.Forbidden);
        }

        var result = await modules.SetLecturersAsync(code, request.ToAssignments(), userId, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(result);
        }

        foreach (var lecturerId in result.Value!.AffectedLecturerIds)
        {
            await lecturerModules.InvalidateAsync(lecturerId, cancellationToken);
        }

        await catalogue.InvalidateAsync(cancellationToken);
        return TypedResults.Ok(await DetailAsync(result.Value.Code, detail, windows, clock, cancellationToken));
    }

    private static async Task<ModuleDetail> DetailAsync(string code, ModuleDetailQuery detail, EnrolmentWindowService windows, TimeProvider clock, CancellationToken cancellationToken)
    {
        var data = await detail.ExecuteAsync(code, cancellationToken) ?? throw new InvalidOperationException($"Module {code} vanished after its own update.");
        var calendar = await windows.CurrentAsync(cancellationToken);
        var window = await windows.FindAsync(calendar.AcademicYear, data.Module.Semester, cancellationToken);
        return ModuleDetail.From(data, window, clock.GetUtcNow());
    }

    private static IResult ModuleNotFound() => ProblemResults.Problem(ProblemTypes.ModuleNotFound, "No module has that code.");

    private static IResult Problem<T>(ModuleAdminResult<T> result) => result.Error switch
    {
        ModuleAdminError.ModuleNotFound => ModuleNotFound(),
        ModuleAdminError.ModuleCodeTaken => ProblemResults.Problem(ProblemTypes.ModuleCodeTaken, "A module already has that code."),
        ModuleAdminError.LecturerNotFound => ProblemResults.Problem(ProblemTypes.LecturerNotFound, "No lecturer has one of those staff numbers."),
        ModuleAdminError.CapacityBelowEnrolled => ProblemResults.Problem(
            ProblemTypes.CapacityBelowEnrolled,
            "Capacity cannot be lowered below the number of students enrolled this year.",
            new Dictionary<string, object?> { ["enrolledCount"] = result.EnrolledCount }),
        ModuleAdminError.SemesterChangeWithEnrolments => ProblemResults.Problem(
            ProblemTypes.SemesterChangeWithEnrolments,
            "The semester cannot change while students are enrolled: it would move their credits and timetables.",
            new Dictionary<string, object?> { ["enrolledCount"] = result.EnrolledCount }),
        ModuleAdminError.InvalidLecturerAssignment => ProblemResults.Problem(
            ProblemTypes.InvalidLecturerAssignment,
            "A module needs exactly one leader, each lecturer at most once, and nobody who has left."),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.Error, "Not a refusal."),
    };
}
