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

        builder.HasOne<Module>().WithMany().HasForeignKey(ml => ml.ModuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Lecturer>().WithMany().HasForeignKey(ml => ml.LecturerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(ml => ml.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
