using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Domain.Students;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Infrastructure.Seeding;

/// <summary>
/// Generates a realistic, deterministic university dataset. The same seed always produces the same
/// students, modules, enrolments and marks, so load-test runs are comparable across machines.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>The oversubscribed final-year elective every student wants on enrolment day.</summary>
    public const string HotModuleCode = "CS3099";
    public const int HotModuleCapacity = 30;

    /// <summary>Every seeded row is a completed 2025/26 autumn enrolment with a published mark.</summary>
    public const string SeedAcademicYear = "2025/26";

    private const int ModuleCredits = 15;
    private const int AutumnCapacity = 1_500;
    private const int SpringCapacity = 300;
    private const int BatchSize = 5_000;

    internal static readonly string[] Departments = ["CS", "MA", "PH", "EE"];

    private static readonly Dictionary<string, string[]> TitlesByDepartment = new()
    {
        ["CS"] = ["Programming", "Algorithms", "Databases", "Operating Systems", "Networks", "Software Engineering", "Computer Architecture", "Human-Computer Interaction", "Security", "Distributed Systems"],
        ["MA"] = ["Calculus", "Linear Algebra", "Discrete Mathematics", "Probability", "Statistics", "Numerical Methods", "Real Analysis", "Optimisation", "Graph Theory", "Number Theory"],
        ["PH"] = ["Mechanics", "Electromagnetism", "Thermodynamics", "Quantum Physics", "Optics", "Waves", "Relativity", "Solid State Physics", "Nuclear Physics", "Astrophysics"],
        ["EE"] = ["Circuit Theory", "Digital Logic", "Signals and Systems", "Control Systems", "Power Electronics", "Embedded Systems", "Communications", "Microelectronics", "Electrical Machines", "Antennas"],
    };

    private static readonly string[] Programmes =
    [
        "BSc Computer Science", "BSc Software Engineering", "BSc Mathematics",
        "BSc Physics", "BEng Electrical Engineering", "BSc Data Science",
    ];

    internal static readonly string[] FirstNames =
    [
        "Aisha", "Ahmed", "Amelia", "Arjun", "Ben", "Chloe", "Daniel", "Dina", "Ella", "Ethan",
        "Fatima", "George", "Hannah", "Hassan", "Isla", "Jack", "Jamal", "Kate", "Leo", "Layla",
        "Maya", "Mohammed", "Noah", "Olivia", "Omar", "Priya", "Rahul", "Ruby", "Sam", "Sara",
        "Tariq", "Thomas", "Uma", "Victor", "Wei", "Yara", "Yusuf", "Zainab", "Zoe", "Zara",
    ];

    internal static readonly string[] LastNames =
    [
        "Ahmed", "Ali", "Begum", "Brown", "Chen", "Clarke", "Davies", "Evans", "Garcia", "Hayder",
        "Hussain", "Jones", "Khan", "Kim", "Kumar", "Lee", "Martin", "Miller", "Nguyen", "Okafor",
        "Patel", "Roberts", "Robinson", "Shah", "Singh", "Smith", "Taylor", "Thompson", "Walker", "Wang",
        "White", "Williams", "Wilson", "Wright", "Young", "Zhang", "Abbas", "Baker", "Carter", "Dixon",
    ];

    private static readonly string[] Buildings = ["Turing", "Lovelace", "Hopper", "Berners-Lee"];

    public static async Task SeedAsync(RushDayDbContext db, SeedOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        options ??= new SeedOptions();

        if (await db.Students.AnyAsync(cancellationToken))
        {
            return;
        }

        var random = new Random(options.RandomSeed);

        var modules = BuildModules(options);
        var slots = BuildTimetable(modules, random);
        var students = BuildStudents(options, random);
        var enrolments = BuildEnrolments(students, modules, options, random);
        var grades = BuildGrades(enrolments, options, random);

        db.ChangeTracker.AutoDetectChangesEnabled = false;

        db.Modules.AddRange(modules);
        db.TimetableSlots.AddRange(slots);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        await SaveInBatchesAsync(db, students, cancellationToken);
        await SaveInBatchesAsync(db, enrolments, cancellationToken);
        await SaveInBatchesAsync(db, grades, cancellationToken);
    }

    private static List<Module> BuildModules(SeedOptions options)
    {
        var modules = new List<Module>();

        for (var level = 1; level <= 3; level++)
        {
            foreach (var semester in new[] { Semester.Autumn, Semester.Spring })
            {
                var numberOffset = semester == Semester.Autumn ? 0 : 50;
                for (var i = 0; i < options.ModulesPerLevelPerSemester; i++)
                {
                    var department = Departments[i % Departments.Length];
                    var titles = TitlesByDepartment[department];
                    var baseTitle = titles[(i / Departments.Length) % titles.Length];
                    var suffix = i >= titles.Length ? " (Advanced)" : string.Empty;

                    modules.Add(new Module
                    {
                        Id = Guid.CreateVersion7(),
                        Code = $"{department}{level}{numberOffset + i + 1:D3}",
                        Department = department,
                        Title = $"{baseTitle} {ToRoman(level)}{suffix}",
                        Credits = ModuleCredits,
                        Capacity = semester == Semester.Autumn ? AutumnCapacity : SpringCapacity,
                        Semester = semester,
                    });
                }
            }
        }

        modules.Add(new Module
        {
            Id = Guid.CreateVersion7(),
            Code = HotModuleCode,
            Department = HotModuleCode[..2],
            Title = "Advanced Machine Learning",
            Credits = ModuleCredits,
            Capacity = HotModuleCapacity,
            Semester = Semester.Spring,
        });

        return modules;
    }

    private static List<TimetableSlot> BuildTimetable(IReadOnlyList<Module> modules, Random random)
    {
        var slots = new List<TimetableSlot>(modules.Count * 2);

        foreach (var module in modules)
        {
            var lectureStart = random.Next(9, 17);
            slots.Add(new TimetableSlot
            {
                Id = Guid.CreateVersion7(),
                ModuleId = module.Id,
                Day = (DayOfWeek)random.Next(1, 6),
                StartTime = new TimeOnly(lectureStart, 0),
                EndTime = new TimeOnly(lectureStart + 1, 0),
                Room = $"{Buildings[random.Next(Buildings.Length)]}-{random.Next(100, 400)}",
            });

            var labStart = random.Next(9, 16);
            slots.Add(new TimetableSlot
            {
                Id = Guid.CreateVersion7(),
                ModuleId = module.Id,
                Day = (DayOfWeek)random.Next(1, 6),
                StartTime = new TimeOnly(labStart, 0),
                EndTime = new TimeOnly(labStart + 2, 0),
                Room = $"{Buildings[random.Next(Buildings.Length)]}-Lab{random.Next(1, 9)}",
            });
        }

        return slots;
    }

    private static List<Student> BuildStudents(SeedOptions options, Random random)
    {
        var students = new List<Student>(options.StudentCount);
        for (var i = 1; i <= options.StudentCount; i++)
        {
            students.Add(new Student
            {
                Id = Guid.CreateVersion7(),
                StudentNumber = $"S{i:D6}",
                FullName = $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}",
                Programme = Programmes[random.Next(Programmes.Length)],
                YearOfStudy = ((i - 1) % 3) + 1,
            });
        }

        return students;
    }

    private static List<Enrolment> BuildEnrolments(IReadOnlyList<Student> students, IReadOnlyList<Module> modules, SeedOptions options, Random random)
    {
        var autumnByLevel = modules
            .Where(m => m.Semester == Semester.Autumn)
            .GroupBy(m => m.Level)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var termStart = new DateTimeOffset(2025, 9, 15, 9, 0, 0, TimeSpan.Zero);
        var enrolments = new List<Enrolment>(students.Count * options.AutumnModulesPerStudent);

        foreach (var student in students)
        {
            var pool = autumnByLevel[student.YearOfStudy];
            var chosen = pool.OrderBy(_ => random.Next()).Take(options.AutumnModulesPerStudent);

            foreach (var module in chosen)
            {
                enrolments.Add(new Enrolment
                {
                    Id = Guid.CreateVersion7(),
                    StudentId = student.Id,
                    ModuleId = module.Id,
                    EnrolledAt = termStart.AddMinutes(random.Next(0, 60 * 24 * 7)),
                    Status = EnrolmentStatus.Active,
                    Source = EnrolmentSource.Seed,
                    AcademicYear = SeedAcademicYear,
                });
            }
        }

        return enrolments;
    }

    private static List<Grade> BuildGrades(IReadOnlyList<Enrolment> enrolments, SeedOptions options, Random random)
    {
        var grades = new List<Grade>(enrolments.Count);
        foreach (var enrolment in enrolments)
        {
            grades.Add(new Grade
            {
                Id = Guid.CreateVersion7(),
                StudentId = enrolment.StudentId,
                ModuleId = enrolment.ModuleId,
                Mark = Math.Clamp((int)Math.Round(NextGaussian(random, mean: 62, standardDeviation: 12)), 0, 100),
                Outcome = GradeOutcome.Mark,
                Status = GradeStatus.Published,
                PublishedAt = options.ResultsDay,
                UpdatedAt = options.ResultsDay,
                Version = 1,
            });
        }

        return grades;
    }

    private static async Task SaveInBatchesAsync<TEntity>(RushDayDbContext db, IReadOnlyList<TEntity> entities, CancellationToken cancellationToken)
        where TEntity : class
    {
        for (var offset = 0; offset < entities.Count; offset += BatchSize)
        {
            db.Set<TEntity>().AddRange(entities.Skip(offset).Take(BatchSize));
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }

    private static double NextGaussian(Random random, double mean, double standardDeviation)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        var standardNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + (standardDeviation * standardNormal);
    }

    private static string ToRoman(int level) => level switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        _ => level.ToString(CultureInfo.InvariantCulture),
    };
}
