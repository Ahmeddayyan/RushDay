using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Students;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.StudentNumber).HasMaxLength(16).IsRequired();
        builder.Property(s => s.FullName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Programme).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(256);
        builder.HasIndex(s => s.StudentNumber).IsUnique();
    }
}
