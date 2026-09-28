namespace RushDay.Domain.Announcements;

/// <summary>Plain-text notice, university-wide or for one module. Soft-deleted rows stay for the audit log.</summary>
public sealed class Announcement
{
    public Guid Id { get; init; }
    public AnnouncementScope Scope { get; init; }
    public Guid? ModuleId { get; init; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public bool Pinned { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
