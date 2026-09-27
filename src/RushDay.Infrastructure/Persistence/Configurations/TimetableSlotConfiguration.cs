using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Modules;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class TimetableSlotConfiguration : IEntityTypeConfiguration<TimetableSlot>
{
    public void Configure(EntityTypeBuilder<TimetableSlot> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Room).HasMaxLength(32).IsRequired();
        builder.HasIndex(t => t.ModuleId);
        builder.HasOne<Module>().WithMany().HasForeignKey(t => t.ModuleId).OnDelete(DeleteBehavior.Cascade);
    }
}
