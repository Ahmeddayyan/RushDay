using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Lecturers;
using RushDay.Domain.Students;
using RushDay.Infrastructure.Identity;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("users", table => table.HasCheckConstraint(
            "ck_users_one_principal",
            "NOT (student_id IS NOT NULL AND lecturer_id IS NOT NULL)"));

        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.MustChangePassword).HasDefaultValue(false);
        builder.Property(u => u.IsDemo).HasDefaultValue(false);

        builder.HasIndex(u => u.StudentId).IsUnique();
        builder.HasIndex(u => u.LecturerId).IsUnique();

        // Users are never deleted, only disabled; the principal rows must not disappear under a login.
        builder.HasOne<Student>().WithMany().HasForeignKey(u => u.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lecturer>().WithMany().HasForeignKey(u => u.LecturerId).OnDelete(DeleteBehavior.Restrict);
    }
}
