using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Modules;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Code).HasMaxLength(16).IsRequired();
        builder.Property(m => m.Title).HasMaxLength(200).IsRequired();
        builder.HasIndex(m => m.Code).IsUnique();
    }
}
