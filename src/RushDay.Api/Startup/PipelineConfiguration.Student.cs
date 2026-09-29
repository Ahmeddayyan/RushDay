using RushDay.Api.Endpoints;

namespace RushDay.Api.Startup;

/// <summary>Stage S4's routes (the S2 hook <c>MapStudentSurface</c>), on the <c>/api</c> group so antiforgery and the gates apply.</summary>
public static partial class PipelineConfiguration
{
    static partial void MapStudentSurface(RouteGroupBuilder api)
    {
        api.MapMeEndpoints();
        api.MapModuleEndpoints();
        api.MapAnnouncementEndpoints();
    }
}
