using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Modules;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        // Deliberately no enrolled_count <= capacity CHECK (00-overview.md D11): the conditional UPDATE is the
        // single write path, and a CHECK would block withdrawals from the already-oversold CS3099.
        builder.ToTable("modules", table =>
        {
            table.HasCheckConstraint("ck_modules_enrolled_count_non_negative", "enrolled_count >= 0");
            table.HasCheckConstraint("ck_modules_capacity_positive", "capacity >= 0");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Code).HasMaxLength(16).IsRequired();
        builder.Property(m => m.Department).HasMaxLength(8).IsRequired();
        builder.Property(m => m.Title).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(2000);
        builder.Property(m => m.EnrolledCount).HasDefaultValue(0);

        // Sentinel true: an explicit false is sent to the database instead of being mistaken for "unset".
        builder.Property(m => m.IsActive).HasDefaultValue(true).HasSentinel(true);

        builder.Ignore(m => m.Level);
        builder.HasIndex(m => m.Code).IsUnique();
    }
}
