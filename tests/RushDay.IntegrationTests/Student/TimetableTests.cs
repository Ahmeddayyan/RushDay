using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Domain.Modules;
using RushDay.Domain.Settings;
using RushDay.Infrastructure.Caching;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// <c>GET /api/me/timetable</c> (02-api.md section 8.3, 00-overview.md section 4.5): the slots of this year's active
/// enrolments on modules of <c>academic_settings.current_semester</c> only, with <c>kind</c> taken from the room.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TimetableTests(RushDayApiFactory factory)
{
    private static readonly string[] EntryProperties = ["moduleCode", "moduleTitle", "semester", "day", "startTime", "endTime", "room", "kind"];

    private static readonly string[] Weekdays = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    [Fact]
    public async Task Timetable_shows_this_years_modules_of_the_current_semester_only()
    {
        // S000073 is in the demo CS3001 cohort (autumn 2026/27) and completed four autumn 2025/26 modules.
        const string student = "S000073";
        await factory.CreateModuleAsync("ZZ1201", Semester.Spring, capacity: 10);
        using var client = await factory.LoginStudentAsync(student);
        using (var enrol = await client.EnrolAsync("ZZ1201"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        var timetable = (await client.GetJsonAsync("/api/me/timetable")).EnumerateArray().ToList();

        Assert.NotEmpty(timetable);
        Assert.All(timetable, t =>
        {
            Assert.Equal(EntryProperties.Order(), t.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal("CS3001", t.GetProperty("moduleCode").GetString());
            Assert.Equal("autumn", t.GetProperty("semester").GetString());
            Assert.Matches("^[0-2][0-9]:[0-5][0-9]$", t.GetProperty("endTime").GetString()!);
            Assert.Equal(t.GetProperty("room").GetString()!.Contains("-Lab", StringComparison.Ordinal) ? "lab" : "lecture", t.GetProperty("kind").GetString());
        });
        Assert.Contains(timetable, t => t.GetProperty("kind").GetString() == "lab");
        Assert.Contains(timetable, t => t.GetProperty("kind").GetString() == "lecture");

        // Monday first, then by start time.
        var order = timetable.Select(t => (Array.IndexOf(Weekdays, t.GetProperty("day").GetString()), t.GetProperty("startTime").GetString())).ToList();
        Assert.Equal(order.OrderBy(o => o.Item1).ThenBy(o => o.Item2, StringComparer.Ordinal), order);

        // The dashboard's week is the same list.
        var dashboard = await client.GetJsonAsync("/api/me/dashboard");
        Assert.Equal(timetable.Select(t => t.GetRawText()), dashboard.GetProperty("timetable").EnumerateArray().Select(t => t.GetRawText()));
    }

    /// <summary>When the administrator switches the current semester to spring, the spring module's slots are shown instead.</summary>
    [Fact]
    public async Task Timetable_follows_the_current_semester_setting()
    {
        const string student = "S000076";
        await factory.CreateModuleAsync("ZZ1202", Semester.Spring, capacity: 10);
        using (var client = await factory.LoginStudentAsync(student))
        using (var enrol = await client.EnrolAsync("ZZ1202"))
        {
            Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        }

        try
        {
            await SetCurrentSemesterAsync(Semester.Spring);
            await using var host = factory.Derive();
            using var client = await host.LoginStudentAsync(student);

            var timetable = (await client.GetJsonAsync("/api/me/timetable")).EnumerateArray().ToList();
            Assert.Equal(2, timetable.Count);
            Assert.All(timetable, t => Assert.Equal("ZZ1202", t.GetProperty("moduleCode").GetString()));
            Assert.Equal("spring", (await client.GetJsonAsync("/api/me/dashboard")).GetProperty("currentSemester").GetString());
        }
        finally
        {
            await SetCurrentSemesterAsync(Semester.Autumn);
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SettingsCache>().InvalidateAsync();
        }
    }

    private Task SetCurrentSemesterAsync(Semester semester) =>
        factory.WithDbAsync(db => db.AcademicSettings.Where(s => s.Id == AcademicSettings.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentSemester, semester)));
}
