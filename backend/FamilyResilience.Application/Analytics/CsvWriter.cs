using System.Globalization;
using System.Text;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// RFC 4180 CSV in UTF-8 without a byte-order mark, lines ending in CRLF. A cell that a spreadsheet would read as a
/// formula (starting with =, +, -, @, tab or carriage return, and not a number) gets a leading apostrophe.
/// </summary>
public static class CsvWriter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteAsync(
        Stream stream, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        await using var writer = new StreamWriter(stream, Utf8, bufferSize: 64 * 1024, leaveOpen: true) { NewLine = "\r\n" };
        await writer.WriteLineAsync(Line(header).AsMemory(), cancellationToken);
        foreach (var row in rows)
        {
            await writer.WriteLineAsync(Line(row).AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    public static string Escape(string? cell)
    {
        if (string.IsNullOrEmpty(cell))
        {
            return string.Empty;
        }

        if (cell[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            && !decimal.TryParse(cell, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            cell = "'" + cell;
        }

        return cell.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : cell;
    }

    private static string Line(IEnumerable<string?> cells) => string.Join(',', cells.Select(Escape));
}
