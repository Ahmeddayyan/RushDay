using System.Text.Json;
using RushDay.Api.Contracts;

namespace RushDay.UnitTests.Contracts;

/// <summary>Instants on the wire (02-api.md section 1): ISO-8601 in, ISO-8601 UTC with milliseconds out.</summary>
public sealed class UtcDateTimeOffsetJsonConverterTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new UtcDateTimeOffsetJsonConverter() } };

    [Theory]
    [InlineData("\"2026-09-28T09:00:00Z\"", "2026-09-28T09:00:00+00:00")]
    [InlineData("\"2026-09-28T10:00:00+01:00\"", "2026-09-28T09:00:00+00:00")]
    [InlineData("\"2026-09-28T09:00:00.123Z\"", "2026-09-28T09:00:00.123+00:00")]
    [InlineData("\"2026-09-28T09:00:00.1234567Z\"", "2026-09-28T09:00:00.1234567+00:00")]
    [InlineData("\"2026-09-28T09:00:00\"", "2026-09-28T09:00:00+00:00")]
    public void Reads_iso_8601_as_utc(string json, string expected)
    {
        var value = JsonSerializer.Deserialize<DateTimeOffset>(json, Json);

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
        Assert.Equal(TimeSpan.Zero, value.Offset);
    }

    [Theory]
    [InlineData("\"not-a-date\"")]
    [InlineData("\"28/09/2026 09:00\"")]
    [InlineData("\"09/28/2026\"")]
    [InlineData("\"2026-09-28\"")]
    [InlineData("\"\"")]
    [InlineData("1790154000")]
    [InlineData("true")]
    public void Anything_else_is_a_json_exception(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffset>(json, Json));
    }

    [Theory]
    [InlineData("2026-09-28T09:00:00+00:00", "\"2026-09-28T09:00:00.000Z\"")]
    [InlineData("2026-09-28T10:00:00+01:00", "\"2026-09-28T09:00:00.000Z\"")]
    [InlineData("2026-09-28T09:00:00.1239999+00:00", "\"2026-09-28T09:00:00.123Z\"")]
    public void Writes_utc_with_three_fractional_digits(string instant, string expected)
    {
        var value = DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, JsonSerializer.Serialize(value, Json));
    }
}
