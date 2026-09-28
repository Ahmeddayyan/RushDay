namespace RushDay.Domain.Grades;

/// <summary>Why a grade row has or lacks a mark: only <see cref="Mark"/> carries a number.</summary>
public enum GradeOutcome
{
    Mark,
    Absent,
    Deferred,
}
