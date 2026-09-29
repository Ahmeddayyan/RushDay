using RushDay.Domain.Announcements;
using RushDay.Infrastructure.Announcements;

namespace RushDay.Api.Contracts;

/// <summary><c>AnnouncementView</c> of 02-api.md section 7 (<c>author</c> is the creator's display name).</summary>
public sealed record AnnouncementView(
    Guid Id,
    AnnouncementScope Scope,
    string? ModuleCode,
    string Title,
    string Body,
    bool Pinned,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static AnnouncementView From(AnnouncementRecord announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        return new AnnouncementView(
            announcement.Id,
            announcement.Scope,
            announcement.ModuleCode,
            announcement.Title,
            announcement.Body,
            announcement.Pinned,
            announcement.PublishedAt,
            announcement.ExpiresAt,
            announcement.Author,
            announcement.CreatedAt,
            announcement.UpdatedAt);
    }

    public static IReadOnlyList<AnnouncementView> From(IEnumerable<AnnouncementRecord> announcements)
    {
        ArgumentNullException.ThrowIfNull(announcements);
        return [.. announcements.Select(From)];
    }
}
