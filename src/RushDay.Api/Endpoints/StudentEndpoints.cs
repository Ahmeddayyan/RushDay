using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RushDay.Api.Contracts;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Api.Endpoints;

public static class StudentEndpoints
{
    public static IEndpointRouteBuilder MapStudentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/students").WithTags("Students");

        group.MapGet("/{studentNumber}/dashboard", GetDashboardAsync).WithName("GetStudentDashboard");
        group.MapPost("/{studentNumber}/enrolments", EnrolAsync).WithName("EnrolStudent");

        return app;
    }

    /// <summary>
    /// The page every student opens at 09:00 on results day. v0 baseline: straightforward code,
    /// one query per enrolled module. We expected it to collapse first; docs/load-results says what really happened.
    /// </summary>
    private static async Task<Results<Ok<DashboardResponse>, NotFound>> GetDashboardAsync(
        string studentNumber,
        RushDayDbContext db,
        CancellationToken cancellationToken)
    {
        var student = await db.Students.AsNoTracking()
            .SingleOrDefaultAsync(s => s.StudentNumber == studentNumber, cancellationToken);
        if (student is null)
        {
            return TypedResults.NotFound();
        }

        var enrolments = await db.Enrolments.AsNoTracking()
            .Where(e => e.StudentId == student.Id)
            .ToListAsync(cancellationToken);

        var modules = new List<EnrolledModule>(enrolments.Count);
        var timetable = new List<TimetableEntry>();
        foreach (var enrolment in enrolments)
        {
            var module = await db.Modules.AsNoTracking()
                .SingleAsync(m => m.Id == enrolment.ModuleId, cancellationToken);
            modules.Add(new EnrolledModule(module.Code, module.Title, module.Credits, module.Semester.ToString()));

            var slots = await db.TimetableSlots.AsNoTracking()
                .Where(t => t.ModuleId == module.Id)
                .ToListAsync(cancellationToken);
            timetable.AddRange(slots.Select(s => new TimetableEntry(
                module.Code,
                s.Day.ToString(),
                s.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                s.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                s.Room)));
        }

        var grades = await db.Grades.AsNoTracking()
            .Where(g => g.StudentId == student.Id)
            .ToListAsync(cancellationToken);

        var results = new List<GradeResult>(grades.Count);
        var weighted = new List<(int Mark, int Credits)>(grades.Count);
        foreach (var grade in grades)
        {
            var module = await db.Modules.AsNoTracking()
                .SingleAsync(m => m.Id == grade.ModuleId, cancellationToken);
            results.Add(new GradeResult(module.Code, module.Title, grade.Mark, grade.PublishedAt));
            weighted.Add((grade.Mark, module.Credits));
        }

        var average = Classification.WeightedAverage(weighted);

        return TypedResults.Ok(new DashboardResponse(
            student.StudentNumber,
            student.FullName,
            student.Programme,
            student.YearOfStudy,
            modules,
            timetable,
            results,
            average,
            average is null ? null : Classification.FromAverage(average.Value)));
    }

    /// <summary>
    /// Enrol a student on a module. v0 baseline: read counts, decide, then write. Nothing stops two
    /// requests from both passing the capacity check at the same instant.
    /// </summary>
    private static async Task<Results<Created<EnrolmentResponse>, NotFound<ProblemDetails>, Conflict<ProblemDetails>, UnprocessableEntity<ProblemDetails>>> EnrolAsync(
        string studentNumber,
        EnrolRequest request,
        RushDayDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var student = await db.Students.AsNoTracking()
            .SingleOrDefaultAsync(s => s.StudentNumber == studentNumber, cancellationToken);
        if (student is null)
        {
            return TypedResults.NotFound(Problem(StatusCodes.Status404NotFound, "Student not found", $"No student with number '{studentNumber}'."));
        }

        var module = await db.Modules.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Code == request.ModuleCode, cancellationToken);
        if (module is null)
        {
            return TypedResults.NotFound(Problem(StatusCodes.Status404NotFound, "Module not found", $"No module with code '{request.ModuleCode}'."));
        }

        var alreadyEnrolled = await db.Enrolments
            .AnyAsync(e => e.StudentId == student.Id && e.ModuleId == module.Id, cancellationToken);

        var enrolledCount = await db.Enrolments
            .CountAsync(e => e.ModuleId == module.Id, cancellationToken);

        var creditsInSemester = await db.Enrolments
            .Where(e => e.StudentId == student.Id)
            .Join(db.Modules.Where(m => m.Semester == module.Semester),
                  e => e.ModuleId,
                  m => m.Id,
                  (_, m) => m.Credits)
            .SumAsync(cancellationToken);

        var decision = EnrolmentRules.Evaluate(module, enrolledCount, creditsInSemester, alreadyEnrolled);
        switch (decision)
        {
            case EnrolmentDecision.AlreadyEnrolled:
                return TypedResults.Conflict(Problem(StatusCodes.Status409Conflict, "Already enrolled", $"{studentNumber} is already enrolled on {module.Code}."));
            case EnrolmentDecision.ModuleFull:
                return TypedResults.Conflict(Problem(StatusCodes.Status409Conflict, "Module full", $"{module.Code} has no places remaining."));
            case EnrolmentDecision.CreditLimitExceeded:
                return TypedResults.UnprocessableEntity(Problem(StatusCodes.Status422UnprocessableEntity, "Credit limit exceeded", $"Enrolling would exceed {EnrolmentRules.MaxCreditsPerSemester} credits for the semester."));
            case EnrolmentDecision.Accepted:
                break;
        }

        var enrolment = new Enrolment
        {
            Id = Guid.CreateVersion7(),
            StudentId = student.Id,
            ModuleId = module.Id,
            EnrolledAt = clock.GetUtcNow(),
        };
        db.Enrolments.Add(enrolment);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/students/{studentNumber}/enrolments/{enrolment.Id}",
            new EnrolmentResponse(enrolment.Id, studentNumber, module.Code, enrolment.EnrolledAt));
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
