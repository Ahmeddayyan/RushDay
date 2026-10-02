using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Settings;
using RushDay.Infrastructure.Identity;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class AcademicSettingsConfiguration : IEntityTypeConfiguration<AcademicSettings>
{
    public void Configure(EntityTypeBuilder<AcademicSettings> builder)
    {
        builder.ToTable("academic_settings", table =>
        {
            table.HasCheckConstraint("ck_academic_settings_singleton", "id = 1");
            table.HasCheckConstraint("ck_academic_settings_current_semester", "current_semester IN (1, 2)");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.AcademicYear).HasMaxLength(9).IsRequired();
        builder.Property(s => s.InstitutionName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.InstitutionShortName).HasMaxLength(32).IsRequired();
        builder.Property(s => s.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(s => s.SupportEmail).HasMaxLength(256);
        builder.Property(s => s.SupportUrl).HasMaxLength(400);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
