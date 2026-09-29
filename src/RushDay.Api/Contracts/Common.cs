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

/// <summary>
/// Instants on the wire (02-api.md section 1): written as ISO-8601 UTC with exactly three fractional digits
/// (<c>2026-09-28T09:00:00.000Z</c>); read only from a JSON string in ISO-8601 extended form (<c>yyyy-MM-ddTHH:mm:ss</c>,
/// optional fraction of up to seven digits, then <c>Z</c>, an offset, or nothing for UTC), whatever the culture, and
/// normalised to UTC. Anything else (another token type, <c>"not-a-date"</c>, a culture format such as
/// <c>"28/09/2026 09:00"</c>) is a <see cref="JsonException"/>, which minimal APIs answer with 400 <c>validation</c>.
/// </summary>
public sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public const string WriteFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly string[] ReadFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected an ISO-8601 instant string.");
        }

        var text = reader.GetString();
        if (text is not null
            && DateTimeOffset.TryParseExact(text, ReadFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
        {
            return value;
        }

        throw new JsonException("Expected an ISO-8601 instant such as 2026-09-28T09:00:00Z.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Span<char> buffer = stackalloc char[24];
        if (!value.UtcDateTime.TryFormat(buffer, out var written, WriteFormat, CultureInfo.InvariantCulture))
        {
            throw new JsonException("Could not format the instant.");
        }

        writer.WriteStringValue(buffer[..written]);
    }
}
