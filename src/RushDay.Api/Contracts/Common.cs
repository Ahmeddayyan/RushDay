using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RushDay.Domain.Modules;
using RushDay.Infrastructure.Caching;

namespace RushDay.Api.Contracts;

/// <summary><c>WindowInfo.state</c>: <c>notYetOpen</c>, <c>open</c> or <c>closed</c> at request time.</summary>
public enum WindowState
{
    NotYetOpen,
    Open,
    Closed,
}

/// <summary><c>PublicationBrief.state</c>: <c>scheduled</c> while <c>publish_at &gt; now</c>, else <c>live</c>.</summary>
public enum PublicationState
{
    Scheduled,
    Live,
}

/// <summary><c>WindowInfo</c> of 02-api.md section 7.</summary>
public sealed record WindowInfo(
    Guid Id,
    string AcademicYear,
    Semester Semester,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    DateTimeOffset WithdrawalDeadlineAt,
    WindowState State)
{
    public static WindowInfo From(EnrolmentWindowSnapshot window, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(window);
        var state = now < window.OpensAt ? WindowState.NotYetOpen : window.IsOpenAt(now) ? WindowState.Open : WindowState.Closed;
        return new WindowInfo(window.Id, window.AcademicYear, window.Semester, window.OpensAt, window.ClosesAt, window.WithdrawalDeadlineAt, state);
    }
}

/// <summary><c>PublicationBrief</c> of 02-api.md section 7.</summary>
public sealed record PublicationBrief(string AcademicYear, Semester Semester, DateTimeOffset PublishAt, PublicationState State)
{
    public static PublicationBrief? From(PublicationSnapshot? publication, DateTimeOffset now) =>
        publication is null
            ? null
            : new PublicationBrief(publication.AcademicYear, publication.Semester, publication.PublishAt, publication.IsLiveAt(now) ? PublicationState.Live : PublicationState.Scheduled);
}

/// <summary><c>Paged&lt;T&gt;</c>: <c>page</c> starts at 1.</summary>
public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>Writes every instant as ISO-8601 UTC with <c>Z</c> (00-overview.md section 7), reads any offset.</summary>
public sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString() ?? throw new JsonException("Expected an ISO-8601 instant."), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.UtcDateTime);
    }
}
