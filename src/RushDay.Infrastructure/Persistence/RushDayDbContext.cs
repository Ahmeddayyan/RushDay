using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Announcements;
using RushDay.Domain.Audit;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Grades;
using RushDay.Domain.Lecturers;
using RushDay.Domain.Modules;
using RushDay.Domain.Results;
using RushDay.Domain.Settings;
using RushDay.Domain.Students;
using RushDay.Infrastructure.Identity;
using RushDay.Infrastructure.Seeding;

namespace RushDay.Infrastructure.Persistence;

/// <summary>
/// One context for the domain tables, the Identity store (tables renamed to snake_case: users, roles, ...)
/// and the Data Protection key ring, so cookies survive redeploys and one migration covers everything.
/// </summary>
public sealed class RushDayDbContext(DbContextOptions<RushDayDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Lecturer> Lecturers => Set<Lecturer>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<ModuleLecturer> ModuleLecturers => Set<ModuleLecturer>();
    public DbSet<TimetableSlot> TimetableSlots => Set<TimetableSlot>();
    public DbSet<Enrolment> Enrolments => Set<Enrolment>();
    public DbSet<EnrolmentWindow> EnrolmentWindows => Set<EnrolmentWindow>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<ResultsPublication> ResultsPublications => Set<ResultsPublication>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AcademicSettings> AcademicSettings => Set<AcademicSettings>();
    public DbSet<DataBackfill> DataBackfills => Set<DataBackfill>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Identity's default shape, renamed. users goes first so every foreign key that points at it, including
        // Identity's own, is named fk_<table>_users_<column>; ApplicationUserConfiguration adds its columns and check.
        modelBuilder.Entity<ApplicationUser>().ToTable("users");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RushDayDbContext).Assembly);
    }
}
