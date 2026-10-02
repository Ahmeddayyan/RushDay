using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RushDay.Domain.Announcements;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Identity;

namespace RushDay.Infrastructure.Persistence.Configurations;

internal sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements", table =>
        {
            table.HasCheckConstraint("ck_announcements_scope", "scope IN ('University', 'Module')");
            table.HasCheckConstraint("ck_announcements_scope_module", "(scope = 'Module') = (module_id IS NOT NULL)");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.Title).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Body).HasMaxLength(4000).IsRequired();
        builder.Property(a => a.Pinned).HasDefaultValue(false);

        builder.HasIndex(a => new { a.Scope, a.PublishedAt }).IsDescending(false, true);
        builder.HasIndex(a => new { a.ModuleId, a.PublishedAt }).IsDescending(false, true);

        builder.HasOne<Module>().WithMany().HasForeignKey(a => a.ModuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
