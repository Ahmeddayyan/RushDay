using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Lecturers;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Identity;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class ModuleLecturerConfiguration : IEntityTypeConfiguration<ModuleLecturer>
{
    public void Configure(EntityTypeBuilder<ModuleLecturer> builder)
    {
        builder.ToTable("module_lecturers", table => table.HasCheckConstraint(
            "ck_module_lecturers_role",
            "role IN ('Leader', 'Teacher')"));

        builder.HasKey(ml => new { ml.ModuleId, ml.LecturerId });
        builder.Property(ml => ml.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.HasIndex(ml => ml.LecturerId);

        // Exactly one leader per module is a service rule; the database makes "at most one" impossible to break
        // (migration 20261002120000_ResultsGovernance, review S6 E10).
        builder.HasIndex(ml => ml.ModuleId)
            .IsUnique()
            .HasFilter("role = 'Leader'")
            .HasDatabaseName("ix_module_lecturers_module_id_leader");

        builder.HasOne<Module>().WithMany().HasForeignKey(ml => ml.ModuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Lecturer>().WithMany().HasForeignKey(ml => ml.LecturerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(ml => ml.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
