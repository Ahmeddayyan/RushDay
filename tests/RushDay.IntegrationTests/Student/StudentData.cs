using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RushDay.Domain.Announcements;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// Data helpers for the student-surface tests: rows are written straight through <see cref="RushDayDbContext"/> (the
/// administrator's routes arrive in S6), and the caches that would hide them are invalidated on the host that serves
/// the requests.
/// </summary>
public static class StudentData
{
    public const string CurrentYear = "2026/27";
    public const string PreviousYear = "2025/26";

    public static async Task<T> WithDbAsync<T>(this WebApplicationFactory<Program> factory, Func<RushDayDbContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<RushDayDbContext>());
    }

    public static Task WithDbAsync(this WebApplicationFactory<Program> factory, Func<RushDayDbContext, Task> action) =>
        factory.WithDbAsync(async db =>
        {
            await action(db);
            return true;
        });

    public static Task<HttpClient> LoginStudentAsync(this WebApplicationFactory<Program> factory, string studentNumber) =>
        factory.LoginAsync(studentNumber, DemoAccounts.StudentPassword);

    public static Task<HttpResponseMessage> EnrolAsync(this HttpClient client, string moduleCode) =>
        client.PostAsJsonAsync("/api/me/enrolments", new { moduleCode });

    public static Task<HttpResponseMessage> WithdrawAsync(this HttpClient client, string moduleCode) =>
        client.DeleteAsync("/api/me/enrolments/" + moduleCode);

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string path)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var response = await client.GetAsync(path);
        var body = await response.ReadJsonAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} answered {(int)response.StatusCode}: {body}");
        return body;
    }

    /// <summary>Creates an active (or inactive) module with one lecture and one lab slot, and drops <c>catalogue:all</c>.</summary>
    public static async Task<Guid> CreateModuleAsync(
        this WebApplicationFactory<Program> factory,
        string code,
        Semester semester,
        int capacity,
        int credits = 15,
        bool isActive = true)
    {
        var id = Guid.CreateVersion7();
        await factory.WithDbAsync(async db =>
        {
            db.Modules.Add(new Module
            {
                Id = id,
                Code = code,
                Department = code[..2],
                Title = "Test module " + code,
                Description = "Created by a test.",
                Credits = credits,
                Capacity = capacity,
                Semester = semester,
                IsActive = isActive,
            });
            db.TimetableSlots.Add(new TimetableSlot { Id = Guid.CreateVersion7(), ModuleId = id, Day = DayOfWeek.Tuesday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), Room = "Turing-101" });
            db.TimetableSlots.Add(new TimetableSlot { Id = Guid.CreateVersion7(), ModuleId = id, Day = DayOfWeek.Thursday, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(16, 0), Room = "Hopper-Lab3" });
            await db.SaveChangesAsync();
        });
        await factory.InvalidateCatalogueAsync();
        return id;
    }

    public static async Task InvalidateCatalogueAsync(this WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogueCache>().InvalidateAsync();
    }

    public static async Task InvalidateAnnouncementsAsync(this WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AnnouncementCache>().InvalidateAsync();
    }

    public static Task<Guid> StudentIdAsync(this WebApplicationFactory<Program> factory, string studentNumber) =>
        factory.WithDbAsync(db => db.Students.AsNoTracking().Where(s => s.StudentNumber == studentNumber).Select(s => s.Id).SingleAsync());

    public static Task<Guid> ModuleIdAsync(this WebApplicationFactory<Program> factory, string code) =>
        factory.WithDbAsync(db => db.Modules.AsNoTracking().Where(m => m.Code == code).Select(m => m.Id).SingleAsync());

    /// <summary><c>(enrolled_count, active enrolments of the current year)</c> of a module.</summary>
    public static Task<(int EnrolledCount, int ActiveCount)> CountsAsync(this WebApplicationFactory<Program> factory, string code) =>
        factory.WithDbAsync(async db =>
        {
            var module = await db.Modules.AsNoTracking().Where(m => m.Code == code).Select(m => new { m.Id, m.EnrolledCount }).SingleAsync();
            var active = await db.Enrolments.AsNoTracking().CountAsync(e => e.ModuleId == module.Id && e.Status == EnrolmentStatus.Active && e.AcademicYear == CurrentYear);
            return (module.EnrolledCount, active);
        });

    public static Task<Enrolment?> EnrolmentAsync(this WebApplicationFactory<Program> factory, string studentNumber, string code) =>
        factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == studentNumber).Select(s => s.Id).SingleAsync();
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            return await db.Enrolments.AsNoTracking().SingleOrDefaultAsync(e => e.StudentId == studentId && e.ModuleId == moduleId);
        });

    /// <summary>Inserts an enrolment row directly (for earlier-year and withdrawn rows), keeping <c>enrolled_count</c> as it is.</summary>
    public static Task InsertEnrolmentAsync(this WebApplicationFactory<Program> factory, string studentNumber, string code, string academicYear, EnrolmentStatus status = EnrolmentStatus.Active) =>
        factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == studentNumber).Select(s => s.Id).SingleAsync();
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            db.Enrolments.Add(new Enrolment
            {
                Id = Guid.CreateVersion7(),
                StudentId = studentId,
                ModuleId = moduleId,
                EnrolledAt = new DateTimeOffset(2025, 9, 20, 9, 0, 0, TimeSpan.Zero),
                Status = status,
                Source = EnrolmentSource.Admin,
                AcademicYear = academicYear,
                WithdrawnAt = status == EnrolmentStatus.Withdrawn ? new DateTimeOffset(2025, 10, 1, 9, 0, 0, TimeSpan.Zero) : null,
            });
            await db.SaveChangesAsync();
        });

    /// <summary>Inserts (or replaces) the grade of (student, module) with the given status, outcome and mark.</summary>
    public static Task PutGradeAsync(
        this WebApplicationFactory<Program> factory,
        string studentNumber,
        string code,
        GradeStatus status,
        int? mark,
        GradeOutcome outcome = GradeOutcome.Mark,
        DateTimeOffset? publishedAt = null) =>
        factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == studentNumber).Select(s => s.Id).SingleAsync();
            var moduleId = await db.Modules.Where(m => m.Code == code).Select(m => m.Id).SingleAsync();
            await db.Grades.Where(g => g.StudentId == studentId && g.ModuleId == moduleId).ExecuteDeleteAsync();
            db.Grades.Add(new Grade
            {
                Id = Guid.CreateVersion7(),
                StudentId = studentId,
                ModuleId = moduleId,
                Mark = mark,
                Outcome = outcome,
                Status = status,
                PublishedAt = publishedAt,
                SubmittedAt = status == GradeStatus.Draft ? null : RushDayApiFactory.ClockStart,
                UpdatedAt = RushDayApiFactory.ClockStart,
                Version = 1,
            });
            await db.SaveChangesAsync();
        });

    /// <summary>Codes of MA modules of a semester the student holds no enrolment on (so no result exists either), with room to spare.</summary>
    public static Task<List<string>> FreeModulesAsync(this WebApplicationFactory<Program> factory, string studentNumber, Semester semester, int count) =>
        factory.WithDbAsync(async db =>
        {
            var studentId = await db.Students.Where(s => s.StudentNumber == studentNumber).Select(s => s.Id).SingleAsync();
            return await db.Modules.AsNoTracking()
                .Where(m => m.IsActive && m.Semester == semester && m.Code.StartsWith("MA") && m.Capacity > m.EnrolledCount + 10)
                .Where(m => !db.Enrolments.Any(e => e.StudentId == studentId && e.ModuleId == m.Id))
                .OrderBy(m => m.Code)
                .Select(m => m.Code)
                .Take(count)
                .ToListAsync();
        });

    /// <summary>Adds an announcement straight to the table and drops <c>announcements:university</c>.</summary>
    public static async Task<Guid> AddAnnouncementAsync(
        this WebApplicationFactory<Program> factory,
        string title,
        string? moduleCode = null,
        bool pinned = false,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? expiresAt = null,
        bool deleted = false)
    {
        var id = Guid.CreateVersion7();
        await factory.WithDbAsync(async db =>
        {
            var author = await db.Users.Where(u => u.NormalizedUserName == DemoAccounts.AdminUsername.ToUpperInvariant()).Select(u => u.Id).SingleAsync();
            Guid? moduleId = moduleCode is null ? null : await db.Modules.Where(m => m.Code == moduleCode).Select(m => m.Id).SingleAsync();
            var at = publishedAt ?? RushDayApiFactory.ClockStart.AddHours(-1);
            db.Announcements.Add(new Announcement
            {
                Id = id,
                Scope = moduleId is null ? AnnouncementScope.University : AnnouncementScope.Module,
                ModuleId = moduleId,
                Title = title,
                Body = "Body of " + title,
                Pinned = pinned,
                PublishedAt = at,
                ExpiresAt = expiresAt,
                CreatedByUserId = author,
                CreatedAt = at,
                UpdatedAt = at,
                DeletedAt = deleted ? at : null,
            });
            await db.SaveChangesAsync();
        });
        await factory.InvalidateAnnouncementsAsync();
        return id;
    }

    public static IReadOnlyList<string> Strings(this JsonElement array, string property) =>
        [.. array.EnumerateArray().Select(e => e.GetProperty(property).GetString()!)];
}
