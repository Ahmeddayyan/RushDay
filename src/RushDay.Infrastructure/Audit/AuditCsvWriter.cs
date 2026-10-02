using System.Globalization;
using System.Text;
using RushDay.Infrastructure.Queries;

namespace RushDay.Infrastructure.Audit;

/// <summary>
/// CSV for the audit export and other staff downloads (02-api.md section 8.5, 03-security.md section 5): RFC 4180
/// lines ending in CRLF, every field quoted with inner quotes doubled, and a cell that begins with <c>=</c>, <c>+</c>,
/// <c>-</c>, <c>@</c>, a tab or a carriage return prefixed with a single quote, so a spreadsheet opens it as text and
/// never evaluates it as a formula (the OWASP CSV-injection set).
/// </summary>
public static class AuditCsvWriter
{
    public const string ContentType = "text/csv; charset=utf-8";

    /// <summary>The export's columns, in order.</summary>
    public static IReadOnlyList<string> Columns { get; } =
    [
        "occurredAt", "actorUsername", "actorRole", "action", "subjectType", "subjectId", "studentNumber", "moduleCode", "details", "requestId",
    ];

    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>One quoted, injection-safe cell (an empty quoted cell for null).</summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var safe = Array.IndexOf(FormulaTriggers, value[0]) >= 0 ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>One CSV line of cells, terminated by CRLF.</summary>
    public static string Line(IEnumerable<string?> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        return string.Join(',', cells.Select(Cell)) + "\r\n";
    }

    public static string Header() => Line(Columns);

    public static string Row(AuditEventRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Line(
        [
            row.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            row.ActorUsername,
            row.ActorRole,
            row.Action,
            row.SubjectType,
            row.SubjectId,
            row.StudentNumber,
            row.ModuleCode,
            row.Details,
            row.RequestId,
        ]);
    }

    /// <summary>The last line of a capped export (a comment line, not a record).</summary>
    public static string TruncationLine(int cap) =>
        string.Create(CultureInfo.InvariantCulture, $"# truncated at {cap} rows; narrow the date range\r\n");

    /// <summary>Writes the header, the rows and, when <paramref name="truncatedAt"/> is set, the truncation line.</summary>
    public static async Task WriteAsync(Stream output, IAsyncEnumerable<AuditEventRow> rows, int? truncatedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(rows);

        var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16 * 1024, leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            await writer.WriteAsync(Header().AsMemory(), cancellationToken);
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                await writer.WriteAsync(Row(row).AsMemory(), cancellationToken);
            }

            if (truncatedAt is { } cap)
            {
                await writer.WriteAsync(TruncationLine(cap).AsMemory(), cancellationToken);
            }

            await writer.FlushAsync(cancellationToken);
        }
    }
}
