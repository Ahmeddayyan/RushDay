using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using RushDay.Domain.Students;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class EnrolmentConfiguration : IEntityTypeConfiguration<Enrolment>
{
    public void Configure(EntityTypeBuilder<Enrolment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.StudentId, e.ModuleId }).IsUnique();
        builder.HasIndex(e => e.ModuleId);
        builder.HasOne<Student>().WithMany().HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Module>().WithMany().HasForeignKey(e => e.ModuleId).OnDelete(DeleteBehavior.Cascade);
    }
}
