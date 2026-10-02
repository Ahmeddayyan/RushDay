using RushDay.Domain.Lecturers;
using RushDay.Domain.Modules;

namespace RushDay.Infrastructure.Seeding;

/// <summary>
/// Deterministic teaching staff (backfill step 6): 40 lecturers, ten per department, and one Leader per module
/// by round robin within the department, plus a Teacher on every level-3 module. L00001 (Dr Aisha Khan) leads
/// CS3099 so the demo lecturer owns the interesting module.
/// </summary>
internal static class LecturerSeed
{
    public const int LecturerCount = 40;
    public const int LecturersPerDepartment = 10;
    public const int RandomSeed = 4242;
    public const string DemoLecturerStaffNumber = "L00001";
    public const string DemoLecturerFullName = "Aisha Khan";
    public const string DemoLecturerTitle = "Dr";

    private const int TeacherOffset = 5;

    public static List<Lecturer> BuildLecturers()
    {
        var random = new Random(RandomSeed);
        var lecturers = new List<Lecturer>(LecturerCount);

        for (var number = 1; number <= LecturerCount; number++)
        {
            var department = DatabaseSeeder.Departments[(number - 1) / LecturersPerDepartment];
            var firstName = DatabaseSeeder.FirstNames[random.Next(DatabaseSeeder.FirstNames.Length)];
            var lastName = DatabaseSeeder.LastNames[random.Next(DatabaseSeeder.LastNames.Length)];

            lecturers.Add(new Lecturer
            {
                Id = Guid.CreateVersion7(),
                StaffNumber = $"L{number:D5}",
                FullName = $"{firstName} {lastName}",
                Title = number % 4 == 0 ? "Prof" : "Dr",
                Department = department,
            });
        }

        var demoLecturer = lecturers[0];
        demoLecturer.FullName = DemoLecturerFullName;
        demoLecturer.Title = DemoLecturerTitle;

        return lecturers;
    }

    public static List<ModuleLecturer> BuildAssignments(IReadOnlyList<Module> modules, IReadOnlyList<Lecturer> lecturers, DateTimeOffset assignedAt)
    {
        var staffByDepartment = lecturers
            .GroupBy(l => l.Department)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.StaffNumber, StringComparer.Ordinal).ToArray());
        var demoLecturer = lecturers.Single(l => l.StaffNumber == DemoLecturerStaffNumber);

        var assignments = new List<ModuleLecturer>(modules.Count * 2);

        foreach (var department in modules.GroupBy(m => m.Department))
        {
            if (!staffByDepartment.TryGetValue(department.Key, out var staff))
            {
                continue;
            }

            var index = 0;
            foreach (var module in department.OrderBy(m => m.Code, StringComparer.Ordinal))
            {
                var leader = staff[index % staff.Length];
                var teacher = module.Level == 3 ? staff[(index + TeacherOffset) % staff.Length] : null;

                if (module.Code == DatabaseSeeder.HotModuleCode && leader != demoLecturer)
                {
                    teacher = leader;
                    leader = demoLecturer;
                }

                assignments.Add(new ModuleLecturer
                {
                    ModuleId = module.Id,
                    LecturerId = leader.Id,
                    Role = ModuleLecturerRole.Leader,
                    AssignedAt = assignedAt,
                });

                if (teacher is not null && teacher != leader)
                {
                    assignments.Add(new ModuleLecturer
                    {
                        ModuleId = module.Id,
                        LecturerId = teacher.Id,
                        Role = ModuleLecturerRole.Teacher,
                        AssignedAt = assignedAt,
                    });
                }

                index++;
            }
        }

        return assignments;
    }
}
