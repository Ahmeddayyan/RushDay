using Microsoft.Extensions.Options;
using RushDay.Api.Auth;
using RushDay.Api.Endpoints;
using RushDay.Api.Options;

namespace RushDay.Api.Startup;

/// <summary>
/// Stage S6's routes (the S2 hook <c>MapStaffSurface</c>): <c>/lecturer</c> behind <c>LecturerOnly</c> and
/// <c>/admin</c> behind <c>AdminOnly</c>, both on the <c>/api</c> group so antiforgery and the two gates apply. The
/// demo reset is mapped only when <c>Demo:Enabled</c>.
/// </summary>
public static partial class PipelineConfiguration
{
    static partial void MapStaffSurface(RouteGroupBuilder api)
    {
        api.MapLecturerEndpoints();

        var demoEnabled = ((IEndpointRouteBuilder)api).ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value.Enabled;
        var admin = api.MapGroup("/admin").RequireAuthorization(Policies.AdminOnly).WithTags("Admin");
        admin.MapAdminOverviewEndpoints();
        admin.MapAdminSettingsEndpoints();
        admin.MapAdminWindowEndpoints();
        admin.MapAdminResultsEndpoints();
        admin.MapAdminStudentEndpoints();
        admin.MapAdminModuleEndpoints();
        admin.MapAdminLecturerEndpoints();
        admin.MapAdminAccountEndpoints();
        admin.MapAdminAnnouncementEndpoints();
        admin.MapAdminAuditEndpoints();
        admin.MapAdminOpsEndpoints(demoEnabled);
    }
}
