using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Results;
using RushDay.Infrastructure.Identity;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class ResultsPublicationConfiguration : IEntityTypeConfiguration<ResultsPublication>
{
    public void Configure(EntityTypeBuilder<ResultsPublication> builder)
    {
        builder.ToTable("results_publications", table => table.HasCheckConstraint(
            "ck_results_publications_semester",
            "semester IN (1, 2)"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.AcademicYear).HasMaxLength(9).IsRequired();
        builder.Property(p => p.Note).HasMaxLength(400);

        builder.HasIndex(p => new { p.AcademicYear, p.Semester, p.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_results_publications_semester_created_at");

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
