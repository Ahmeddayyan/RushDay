using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Queries;

namespace RushDay.Api.Contracts;

/// <summary><c>AdminStudentView.student</c> and <c>StudentExport.student</c> (02-api.md section 8.5).</summary>
public sealed record StudentProfile(string StudentNumber, string FullName, string Programme, int YearOfStudy, string? Email, DateTimeOffset? LeftAt)
{
    public static StudentProfile From(StudentProfileRow student)
    {
        ArgumentNullException.ThrowIfNull(student);
        return new StudentProfile(student.StudentNumber, student.FullName, student.Programme, student.YearOfStudy, student.Email, student.LeftAt);
    }
}

/// <summary><c>AdminStudentView.enrolments[]</c> and <c>StudentExport.enrolments[]</c>: every year and status.</summary>
public sealed record StudentEnrolmentRecord(
    string ModuleCode,
    string Title,
    int Credits,
    Semester Semester,
    string AcademicYear,
    EnrolmentStatus Status,
    EnrolmentSource Source,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? WithdrawnAt)
{
    public static StudentEnrolmentRecord From(StudentEnrolmentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new StudentEnrolmentRecord(row.ModuleCode, row.Title, row.Credits, row.Semester, row.AcademicYear, row.Status, row.Source, row.EnrolledAt, row.WithdrawnAt);
    }
}

/// <summary>
/// <c>AdminStudentView.grades[]</c> (02-api.md section 8.5). A student's own export carries only rows with
/// <c>visibleToStudent = true</c> (<see cref="GradeQueries.VisibleToStudents"/>), so there it is always published.
/// </summary>
public sealed record StudentGradeRecord(
    string ModuleCode,
    string ModuleTitle,
    int Credits,
    Semester Semester,
    string AcademicYear,
    GradeOutcome Outcome,
    int? Mark,
    GradeStatus Status,
    DateTimeOffset? PublishedAt,
    bool VisibleToStudent,
    int Version,
    DateTimeOffset? CorrectedAt)
{
    /// <summary>A visible grade: Published, instant passed, enrolment active.</summary>
    public static StudentGradeRecord FromVisible(VisibleGradeRow grade)
    {
        ArgumentNullException.ThrowIfNull(grade);
        return new StudentGradeRecord(
            grade.ModuleCode,
            grade.ModuleTitle,
            grade.Credits,
            grade.Semester,
            grade.AcademicYear,
            grade.Outcome,
            grade.Mark,
            GradeStatus.Published,
            grade.PublishedAt,
            VisibleToStudent: true,
            grade.Version,
            grade.CorrectedAt);
    }
}

/// <summary>
/// <c>StudentExport</c> = <c>AdminStudentView</c> minus <c>recentAudit</c> and <c>account</c> (02-api.md section 8.3):
/// the student, every enrolment, the visible grades only (no drafts), the average as the student sees it, and the
/// instant of the export.
/// </summary>
public sealed record StudentExport(
    StudentProfile Student,
    IReadOnlyList<StudentEnrolmentRecord> Enrolments,
    IReadOnlyList<StudentGradeRecord> Grades,
    double? WeightedAverage,
    string? Classification,
    DateTimeOffset ExportedAt)
{
    public static StudentExport From(StudentExportData data, DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new StudentExport(
            StudentProfile.From(data.Student),
            [.. data.Enrolments.Select(StudentEnrolmentRecord.From)],
            [.. data.Grades.Select(StudentGradeRecord.FromVisible)],
            data.WeightedAverage,
            data.Classification,
            exportedAt);
    }

    /// <summary><c>Content-Disposition: attachment; filename="rushday-{studentNumber}.json"</c>.</summary>
    public static string ContentDisposition(string studentNumber) => $"attachment; filename=\"rushday-{studentNumber}.json\"";
}
