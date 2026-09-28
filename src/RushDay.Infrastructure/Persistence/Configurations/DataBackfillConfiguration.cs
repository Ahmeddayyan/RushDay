using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Infrastructure.Seeding;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class DataBackfillConfiguration : IEntityTypeConfiguration<DataBackfill>
{
    public void Configure(EntityTypeBuilder<DataBackfill> builder)
    {
        builder.ToTable("data_backfills");
        builder.HasKey(b => b.Name);
        builder.Property(b => b.Name).HasMaxLength(64);
        builder.Property(b => b.Notes).HasMaxLength(400);
    }
}
