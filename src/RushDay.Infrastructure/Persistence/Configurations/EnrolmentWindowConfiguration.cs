using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Enrolments;
using RushDay.Infrastructure.Identity;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class EnrolmentWindowConfiguration : IEntityTypeConfiguration<EnrolmentWindow>
{
    public void Configure(EntityTypeBuilder<EnrolmentWindow> builder)
    {
        builder.ToTable("enrolment_windows", table =>
        {
            table.HasCheckConstraint("ck_enrolment_windows_semester", "semester IN (1, 2)");
            table.HasCheckConstraint("ck_enrolment_windows_order", "opens_at < closes_at AND closes_at <= withdrawal_deadline_at");
        });

        builder.HasKey(w => w.Id);
        builder.Property(w => w.AcademicYear).HasMaxLength(9).IsRequired();
        builder.HasIndex(w => new { w.AcademicYear, w.Semester }).IsUnique();

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(w => w.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
