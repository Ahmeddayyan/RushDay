using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Domain.Results;
using RushDay.Domain.Students;
using RushDay.Infrastructure.Identity;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class GradeConfiguration : IEntityTypeConfiguration<Grade>
{
    public void Configure(EntityTypeBuilder<Grade> builder)
    {
        builder.ToTable("grades", table =>
        {
            table.HasCheckConstraint("ck_grades_status", "status IN ('Draft', 'Submitted', 'Published')");
            table.HasCheckConstraint("ck_grades_mark_range", "mark >= 0 AND mark <= 100");
            table.HasCheckConstraint("ck_grades_published_has_instant", "status <> 'Published' OR published_at IS NOT NULL");
        });

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(g => g.Version).HasDefaultValue(1);

        builder.HasIndex(g => new { g.StudentId, g.ModuleId }).IsUnique();

        // The results-day read path: visible grades for one student.
        builder.HasIndex(g => new { g.StudentId, g.Status, g.PublishedAt });
        builder.HasIndex(g => new { g.ModuleId, g.Status });

        builder.HasOne<Student>().WithMany().HasForeignKey(g => g.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Module>().WithMany().HasForeignKey(g => g.ModuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ResultsPublication>().WithMany().HasForeignKey(g => g.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(g => g.EnteredByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
