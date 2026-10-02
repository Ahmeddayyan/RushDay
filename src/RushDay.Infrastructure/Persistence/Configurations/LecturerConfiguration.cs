using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Lecturers;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class LecturerConfiguration : IEntityTypeConfiguration<Lecturer>
{
    public void Configure(EntityTypeBuilder<Lecturer> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.StaffNumber).HasMaxLength(16).IsRequired();
        builder.Property(l => l.FullName).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Title).HasMaxLength(16).IsRequired();
        builder.Property(l => l.Department).HasMaxLength(8).IsRequired();
        builder.Property(l => l.Email).HasMaxLength(256);
        builder.HasIndex(l => l.StaffNumber).IsUnique();
    }
}
