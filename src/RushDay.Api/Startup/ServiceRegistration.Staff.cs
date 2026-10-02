using RushDay.Infrastructure.Enrolments;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Lecturers;
using RushDay.Infrastructure.Modules;
using RushDay.Infrastructure.Ops;
using RushDay.Infrastructure.Queries;
using RushDay.Infrastructure.Settings;
using RushDay.Infrastructure.Students;

namespace RushDay.Api.Startup;

/// <summary>Stage S6's services (the S2 hook <c>AddStaffSurface</c>): the staff queries and services, all scoped.</summary>
public static partial class ServiceRegistration
{
    static partial void AddStaffSurface(IServiceCollection services)
    {
        services.AddScoped<MarksService>();
        services.AddScoped<ResultsPublicationService>();
        services.AddScoped<ModuleAdminService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<EnrolmentWindowAdminService>();
        services.AddScoped<StudentAdminService>();
        services.AddScoped<LecturerAdminService>();
        services.AddScoped<ReconcileService>();
        services.AddScoped<DemoResetService>();

        services.AddScoped<RosterQuery>();
        services.AddScoped<MarksSheetQuery>();
        services.AddScoped<LecturerModulesQuery>();
        services.AddScoped<AdminStudentQuery>();
        services.AddScoped<AdminAccountQuery>();
        services.AddScoped<AdminResultsQuery>();
        services.AddScoped<AuditQuery>();
        services.AddScoped<OverviewQuery>();
    }
}
