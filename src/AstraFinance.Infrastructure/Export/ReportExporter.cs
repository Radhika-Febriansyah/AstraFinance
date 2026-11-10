using System.Globalization;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Money;
using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace AstraFinance.Infrastructure.Export;

/// <summary>
/// Writes a <see cref="ReportDocument"/> as PDF (PDFsharp/MigraDoc 6.2.4, MIT), Excel (ClosedXML, MIT) or CSV.
/// All three contain the same records and totals. Files are written to a temporary path first and then moved
/// into place, so a failed export never leaves a half-written file under the chosen name.
/// </summary>
public sealed class ReportExporter : IReportExporter
{
    private const string FontName = "Segoe UI";
    private static readonly Lock FontLock = new();
    private static bool _fontsConfigured;

    public async Task ExportAsync(ReportDocument report, ExportFormat format, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var temp = path + ".partial";
        try
        {
            switch (format)
            {
                case ExportFormat.Csv:
                    await WriteCsvAsync(report, temp, cancellationToken).ConfigureAwait(false);
                    break;
                case ExportFormat.Excel:
                    await Task.Run(() => WriteExcel(report, temp), cancellationToken).ConfigureAwait(false);
                    break;
                case ExportFormat.Pdf:
                    await Task.Run(() => WritePdf(report, temp), cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, null);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    // ------------------------------------------------------------------ CSV

    /// <summary>
    /// Sectioned CSV: report header lines, summary, then each table (title, header row, rows, total).
    /// Amounts are plain machine-readable numbers in the base currency (stated in the header); dates use the
    /// regional date format, like the transaction export.
    /// </summary>
    private static Task WriteCsvAsync(ReportDocument report, string path, CancellationToken ct)
    {
        var lines = new List<IReadOnlyList<CsvCell>>
        {
            new CsvCell[] { report.Title, report.PeriodText },
            new CsvCell[] { report.GeneratedText, report.Format.Currency.Code },
            Array.Empty<CsvCell>(),
        };
        foreach (var item in report.Summary)
        {
            lines.Add([item.Label, CsvValue(item.Value, report.Format)]);
        }

        foreach (var table in report.Tables)
        {
            lines.Add([]);
            lines.Add([table.Title]);
            lines.Add(table.Columns.Select(c => (CsvCell)c.Header).ToList());
            lines.AddRange(table.Rows.Select(r => (IReadOnlyList<CsvCell>)r.Select(c => CsvValue(c, report.Format)).ToList()));
            if (table.Total is { } total && table.Rows.Count > 0)
            {
                lines.Add(total.Select(c => CsvValue(c, report.Format)).ToList());
            }
        }

        foreach (var note in report.Notes)
        {
            lines.Add([]);
            lines.Add([note]);
        }

        // The first line doubles as the CSV "header"; CsvWriter writes it first.
        return CsvWriter.WriteAsync(path, lines[0].Select(c => c.Value).ToList(), lines.Skip(1), ct);
    }

    private static CsvCell CsvValue(ReportCell cell, RegionalFormat format) => cell.Kind switch
    {
        ReportCellKind.Money => new CsvCell(MoneyFormatter.FormatPlain(cell.Value, format.Currency), IsNumeric: true),
        ReportCellKind.Percent or ReportCellKind.Number => new CsvCell(cell.Value.ToString(CultureInfo.InvariantCulture), IsNumeric: true),
        ReportCellKind.Date => format.FormatDate(cell.Date),
        _ => cell.Text ?? string.Empty,
    };

    // ------------------------------------------------------------------ Excel

    private static void WriteExcel(ReportDocument report, string path)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = report.Title;
        workbook.Properties.Author = report.AppName;
        var moneyFormat = ExcelMoneyFormat(report.Format.Currency);
        decimal scale = report.Format.Currency.Scale;
        var dateFormat = report.Format.ShortDatePattern;

        // Summary sheet.
        var summary = workbook.Worksheets.Add(SheetName(report.Title, []));
        summary.Cell(1, 1).Value = report.Title;
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(1, 1).Style.Font.FontSize = 16;
        summary.Cell(2, 1).Value = report.PeriodText;
        summary.Cell(3, 1).Value = report.GeneratedText;
        var row = 5;
        foreach (var item in report.Summary)
        {
            summary.Cell(row, 1).Value = item.Label;
            SetCell(summary.Cell(row, 2), item.Value, moneyFormat, scale, dateFormat);
            row++;
        }

        row++;
        foreach (var note in report.Notes)
        {
            summary.Cell(row, 1).Value = note;
            summary.Cell(row, 1).Style.Font.Italic = true;
            row++;
        }

        summary.Column(1).Width = 42;
        summary.Column(2).Width = 22;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { summary.Name };
        foreach (var table in report.Tables)
        {
            var sheet = workbook.Worksheets.Add(SheetName(table.Title, used));
            used.Add(sheet.Name);
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var header = sheet.Cell(1, c + 1);
                header.Value = table.Columns[c].Header;
                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDEFF3");
                if (IsNumeric(table.Columns[c].Kind))
                {
                    header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
            }

            var r = 2;
            foreach (var cells in table.Rows)
            {
                for (var c = 0; c < cells.Count; c++)
                {
                    SetCell(sheet.Cell(r, c + 1), cells[c], moneyFormat, scale, dateFormat);
                }

                r++;
            }

            if (table.Rows.Count == 0 && table.EmptyText is not null)
            {
                sheet.Cell(r, 1).Value = table.EmptyText;
                sheet.Cell(r, 1).Style.Font.Italic = true;
            }
            else if (table.Total is { } total)
            {
                for (var c = 0; c < total.Count; c++)
                {
                    var cell = sheet.Cell(r, c + 1);
                    SetCell(cell, total[c], moneyFormat, scale, dateFormat);
                    cell.Style.Font.Bold = true;
                    cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                }
            }

            sheet.SheetView.FreezeRows(1);
            for (var c = 1; c <= table.Columns.Count; c++)
            {
                sheet.Column(c).Width = Math.Clamp(10 * table.Columns[c - 1].RelativeWidth + 4, 10, 48);
            }
        }

        using var stream = File.Create(path);
        workbook.SaveAs(stream);
    }

    private static void SetCell(IXLCell cell, ReportCell value, string moneyFormat, decimal scale, string dateFormat)
    {
        switch (value.Kind)
        {
            case ReportCellKind.Money:
                // Exact: minor units divided by a power of ten in decimal arithmetic.
                cell.Value = value.Value / scale;
                cell.Style.NumberFormat.Format = moneyFormat;
                break;
            case ReportCellKind.Date:
                cell.Value = value.Date.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = dateFormat;
                break;
            case ReportCellKind.Percent:
                cell.Value = value.Value / 100m;
                cell.Style.NumberFormat.Format = "0%";
                break;
            case ReportCellKind.Number:
                cell.Value = value.Value;
                break;
            default:
                cell.Value = value.Text ?? string.Empty; // always stored as text, never as a formula
                break;
        }
    }

    internal static string ExcelMoneyFormat(CurrencyInfo currency)
    {
        var number = currency.MinorDigits > 0 ? "#,##0." + new string('0', currency.MinorDigits) : "#,##0";
        var symbol = "\"" + currency.Symbol.Replace("\"", string.Empty, StringComparison.Ordinal) + "\"";
        var space = currency.SpaceAfterSymbol ? " " : string.Empty;
        var positive = currency.SymbolBeforeAmount ? symbol + space + number : number + space + symbol;
        return positive + ";-" + positive;
    }

    private static string SheetName(string title, HashSet<string> used)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var name = new string(title.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "Sheet";
        }

        name = name.Length > 31 ? name[..31] : name;
        var candidate = name;
        for (var i = 2; used.Contains(candidate); i++)
        {
            var suffix = $" ({i})";
            candidate = (name.Length + suffix.Length > 31 ? name[..(31 - suffix.Length)] : name) + suffix;
        }

        return candidate;
    }

    // ------------------------------------------------------------------ PDF

    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (!_fontsConfigured)
            {
                // Segoe UI from the Windows Fonts folder (PDFsharp's built-in lookup doesn't include it).
                GlobalFontSettings.FontResolver ??= new WindowsFontResolver();
                _fontsConfigured = true;
            }
        }
    }

    private static void WritePdf(ReportDocument report, string path)
    {
        EnsureFonts();
        var document = new Document();
        document.Info.Title = report.Title;
        document.Info.Author = report.AppName;
        document.Info.Subject = report.PeriodText;

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontName;
        normal.Font.Size = 9;
        normal.Font.Color = Color.FromRgb(0x1F, 0x24, 0x2E);

        var wide = report.Tables.Any(t => t.Columns.Count > 6);
        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        // A4; wide tables get a landscape page (explicit size: MigraDoc's orientation flag isn't applied here).
        section.PageSetup.PageWidth = wide ? Unit.FromMillimeter(297) : Unit.FromMillimeter(210);
        section.PageSetup.PageHeight = wide ? Unit.FromMillimeter(210) : Unit.FromMillimeter(297);
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        var pageWidth = wide ? Unit.FromMillimeter(297) : Unit.FromMillimeter(210);
        var usable = pageWidth.Centimeter - 3.6;

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Color.FromRgb(0x6B, 0x72, 0x80);
        footer.AddText($"{report.AppName} · {report.Title} · {report.PeriodText}");
        footer.AddTab();
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();
        footer.Format.TabStops.AddTabStop(Unit.FromCentimeter(usable), TabAlignment.Right);

        var brand = section.AddParagraph(report.AppName);
        brand.Format.Font.Size = 9;
        brand.Format.Font.Color = Color.FromRgb(0xE8, 0x6F, 0x58);
        brand.Format.Font.Bold = true;
        var title = section.AddParagraph(report.Title);
        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        title.Format.SpaceBefore = Unit.FromPoint(2);
        var period = section.AddParagraph(report.PeriodText);
        period.Format.Font.Size = 10;
        var generated = section.AddParagraph(report.GeneratedText);
        generated.Format.Font.Size = 8;
        generated.Format.Font.Color = Color.FromRgb(0x6B, 0x72, 0x80);
        generated.Format.SpaceAfter = Unit.FromPoint(12);

        // Summary
        var summary = section.AddTable();
        summary.AddColumn(Unit.FromCentimeter(Math.Min(9, usable * 0.6)));
        summary.AddColumn(Unit.FromCentimeter(5)).Format.Alignment = ParagraphAlignment.Right;
        foreach (var item in report.Summary)
        {
            var r = summary.AddRow();
            r.TopPadding = Unit.FromPoint(2);
            r.BottomPadding = Unit.FromPoint(2);
            r.Cells[0].AddParagraph(item.Label);
            var value = r.Cells[1].AddParagraph(item.Value.Display(report.Format));
            value.Format.Font.Bold = true;
        }

        foreach (var table in report.Tables)
        {
            var heading = section.AddParagraph(table.Title);
            heading.Format.Font.Size = 12;
            heading.Format.Font.Bold = true;
            heading.Format.SpaceBefore = Unit.FromPoint(16);
            heading.Format.SpaceAfter = Unit.FromPoint(6);
            heading.Format.KeepWithNext = true;

            if (table.Rows.Count == 0)
            {
                var empty = section.AddParagraph(table.EmptyText ?? string.Empty);
                empty.Format.Font.Italic = true;
                continue;
            }

            var pdfTable = section.AddTable();
            pdfTable.Borders.Width = 0;
            var totalWeight = table.Columns.Sum(c => c.RelativeWidth);
            foreach (var column in table.Columns)
            {
                var col = pdfTable.AddColumn(Unit.FromCentimeter(usable * column.RelativeWidth / totalWeight));
                col.Format.Alignment = IsNumeric(column.Kind) ? ParagraphAlignment.Right : ParagraphAlignment.Left;
            }

            var header = pdfTable.AddRow();
            header.HeadingFormat = true;
            header.Format.Font.Bold = true;
            header.Shading.Color = Color.FromRgb(0xED, 0xEF, 0xF3);
            Pad(header);
            for (var c = 0; c < table.Columns.Count; c++)
            {
                header.Cells[c].AddParagraph(table.Columns[c].Header);
            }

            foreach (var cells in table.Rows)
            {
                var r = pdfTable.AddRow();
                Pad(r);
                r.Borders.Bottom.Width = 0.4;
                r.Borders.Bottom.Color = Color.FromRgb(0xE3, 0xE6, 0xEB);
                for (var c = 0; c < cells.Count; c++)
                {
                    r.Cells[c].AddParagraph(cells[c].Display(report.Format));
                }
            }

            if (table.Total is { } total)
            {
                var r = pdfTable.AddRow();
                Pad(r);
                r.Format.Font.Bold = true;
                r.Borders.Top.Width = 0.8;
                for (var c = 0; c < total.Count; c++)
                {
                    r.Cells[c].AddParagraph(total[c].Display(report.Format));
                }
            }
        }

        foreach (var note in report.Notes)
        {
            var p = section.AddParagraph(note);
            p.Format.SpaceBefore = Unit.FromPoint(14);
            p.Format.Font.Size = 8;
            p.Format.Font.Italic = true;
            p.Format.Font.Color = Color.FromRgb(0x6B, 0x72, 0x80);
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = File.Create(path);
        renderer.PdfDocument.Save(stream);
    }

    private static void Pad(Row row)
    {
        row.TopPadding = Unit.FromPoint(3);
        row.BottomPadding = Unit.FromPoint(3);
    }

    private static bool IsNumeric(ReportCellKind kind) => kind is ReportCellKind.Money or ReportCellKind.Percent or ReportCellKind.Number;
}
