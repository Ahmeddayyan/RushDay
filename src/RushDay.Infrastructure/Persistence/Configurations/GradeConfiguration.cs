using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Grades;
using RushDay.Domain.Modules;
using RushDay.Domain.Students;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class GradeConfiguration : IEntityTypeConfiguration<Grade>
{
    public void Configure(EntityTypeBuilder<Grade> builder)
    {
        builder.HasKey(g => g.Id);
        builder.HasIndex(g => new { g.StudentId, g.ModuleId }).IsUnique();
        builder.HasOne<Student>().WithMany().HasForeignKey(g => g.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Module>().WithMany().HasForeignKey(g => g.ModuleId).OnDelete(DeleteBehavior.Cascade);
    }
}
