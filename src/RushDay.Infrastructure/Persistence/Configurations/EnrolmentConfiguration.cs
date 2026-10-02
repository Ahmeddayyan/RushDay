using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Domain.Students;
using RushDay.Infrastructure.Identity;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class EnrolmentConfiguration : IEntityTypeConfiguration<Enrolment>
{
    public void Configure(EntityTypeBuilder<Enrolment> builder)
    {
        builder.ToTable("enrolments", table =>
        {
            table.HasCheckConstraint("ck_enrolments_status", "status IN ('Active', 'Withdrawn')");
            table.HasCheckConstraint("ck_enrolments_source", "source IN ('Seed', 'Self', 'Admin')");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).IsRequired().HasDefaultValue(EnrolmentStatus.Active);

        // The 'Seed' default is kept so an insert by the v0 container during Render's deploy overlap still succeeds.
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(16).IsRequired().HasDefaultValue(EnrolmentSource.Seed);
        builder.Property(e => e.AcademicYear).HasMaxLength(9).IsRequired();

        // The row toggles status instead of being deleted, so (student, module) stays unique across history.
        builder.HasIndex(e => new { e.StudentId, e.ModuleId }).IsUnique();
        builder.HasIndex(e => new { e.ModuleId, e.Status });

        // The dashboard and credit-budget path: one student's active rows in the current year.
        builder.HasIndex(e => new { e.StudentId, e.AcademicYear, e.Status }).HasDatabaseName("ix_enrolments_student_id_academic_year_status");

        builder.HasOne<Student>().WithMany().HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Module>().WithMany().HasForeignKey(e => e.ModuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
