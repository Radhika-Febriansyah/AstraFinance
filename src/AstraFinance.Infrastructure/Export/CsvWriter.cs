using System.Text;

namespace AstraFinance.Infrastructure.Export;

/// <summary>A CSV cell. Numeric cells are written as-is; text cells are protected against formula injection.</summary>
public readonly record struct CsvCell(string Value, bool IsNumeric = false)
{
    public static implicit operator CsvCell(string value) => new(value ?? string.Empty);
}

/// <summary>
/// RFC 4180 CSV writer: comma-separated, CRLF line endings, fields quoted when needed, UTF-8 with
/// BOM so Excel detects the encoding. Text cells that start with = + - @ (or tab/CR) are prefixed
/// with an apostrophe so spreadsheet apps never execute them as formulas.
/// The file is written to a temporary path first and then moved into place.
/// </summary>
public static class CsvWriter
{
    public static async Task WriteAsync(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<CsvCell>> rows, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var temp = path + ".partial";
        var writer = new StreamWriter(temp, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await using (writer.ConfigureAwait(false))
        {
            writer.NewLine = "\r\n";
            await writer.WriteLineAsync(string.Join(',', headers.Select(h => Escape(new CsvCell(h))))).ConfigureAwait(false);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(string.Join(',', row.Select(Escape))).ConfigureAwait(false);
            }
        }

        File.Move(temp, path, overwrite: true);
    }

    public static string Escape(CsvCell cell)
    {
        var value = cell.Value ?? string.Empty;
        if (!cell.IsNumeric && value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        var needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0 || value.StartsWith(' ') || value.EndsWith(' ');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
