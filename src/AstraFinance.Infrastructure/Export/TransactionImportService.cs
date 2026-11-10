using System.Globalization;
using System.Text;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Localization;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;
using AstraFinance.Infrastructure.Database;
using AstraFinance.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Export;

/// <summary>
/// CSV import of income and expense transactions into one account (see <see cref="ITransactionImportService"/>).
/// Transfers are never imported: a CSV has only one side of a transfer, and guessing the other account could
/// create money that doesn't exist.
/// </summary>
public sealed class TransactionImportService : ITransactionImportService
{
    /// <summary>Largest file accepted (a few hundred thousand rows); larger files are refused instead of freezing the app.</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    private static readonly char[] Delimiters = [',', ';', '\t'];
    private static readonly string[] DateNames = ["date", "tanggal", "transaction date", "tgl", "tanggal transaksi"];
    private static readonly string[] AmountNames = ["amount", "jumlah", "nominal", "value", "nilai"];
    private static readonly string[] DescriptionNames = ["description", "deskripsi", "keterangan", "memo", "details", "uraian"];
    private static readonly string[] CategoryNames = ["category", "kategori"];
    private static readonly string[] TypeNames = ["type", "jenis", "tipe"];
    private static readonly string[] NotesNames = ["notes", "catatan"];
    private static readonly string[] AccountNames = ["account", "akun", "rekening"];

    private static readonly string[] IncomeWords = ["income", "pemasukan", "credit", "kredit", "cr", "in", "masuk"];
    private static readonly string[] ExpenseWords = ["expense", "pengeluaran", "debit", "db", "out", "keluar"];
    private static readonly string[] TransferWords = ["transfer"];

    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IRegionalFormatService _format;
    private readonly IClock _clock;

    public TransactionImportService(IDbContextFactory<AstraDbContext> factory, IRegionalFormatService format, IClock clock)
    {
        _factory = factory;
        _format = format;
        _clock = clock;
    }

    // ------------------------------------------------------------------ Reading

    public async Task<CsvTable> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new ImportFailedException(0, "FileTooLarge", new IOException("The file is too large."));
        }

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return Parse(text);
    }

    /// <summary>RFC 4180 parsing with automatic comma/semicolon/tab detection. Empty lines are dropped.</summary>
    public static CsvTable Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.TrimStart('﻿');
        var delimiter = DetectDelimiter(text);
        var records = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                records.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            records.Add(row);
        }

        var nonEmpty = records.Where(r => r.Any(f => !string.IsNullOrWhiteSpace(f))).ToList();
        if (nonEmpty.Count == 0)
        {
            return new CsvTable([], []);
        }

        return new CsvTable(nonEmpty[0].Select(h => h.Trim()).ToList(), nonEmpty.Skip(1).Select(r => (IReadOnlyList<string>)r).ToList());
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var counts = Delimiters.ToDictionary(d => d, d => firstLine.Count(ch => ch == d));
        return counts.MaxBy(p => p.Value).Key is var best && counts[best] > 0 ? best : ',';
    }

    public ImportMapping GuessMapping(IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var normalized = headers.Select(h => h.Trim().ToLowerInvariant()).ToList();

        int? Find(string[] names)
        {
            foreach (var name in names)
            {
                var index = normalized.IndexOf(name);
                if (index >= 0)
                {
                    return index;
                }
            }

            return null;
        }

        return new ImportMapping(
            Find(DateNames) ?? 0,
            Find(AmountNames) ?? Math.Min(1, Math.Max(0, headers.Count - 1)),
            Find(DescriptionNames),
            Find(CategoryNames),
            Find(TypeNames),
            Find(NotesNames),
            Find(AccountNames));
    }

    // ------------------------------------------------------------------ Preview

    public async Task<ImportPreview> PreviewAsync(CsvTable table, ImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(options);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == options.AccountId, cancellationToken).ConfigureAwait(false)
                          ?? throw new FinanceValidationException("AccountId", ValidationCodes.AccountNotFound);
            var categories = await db.Categories.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            var lookup = CategoryLookup.Build(categories);

            var rows = new List<ImportRow>();
            var m = options.Mapping;
            var regional = _format.Current;
            var plain = new RegionalFormat(regional.Currency, NumberFormatStyle.CommaGroupDotDecimal, regional.DateStyle);
            for (var i = 0; i < table.Rows.Count; i++)
            {
                var cells = table.Rows[i];
                var line = i + 2; // header is line 1
                string Cell(int? index) => index is { } x && x < cells.Count ? Unprotect(cells[x].Trim()) : string.Empty;

                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var typeText = Cell(m.TypeColumn).ToLowerInvariant();
                if (typeText.Length > 0 && TransferWords.Contains(typeText))
                {
                    rows.Add(Row(line, ImportRowStatus.Skipped, ImportMessages.Transfer, Cell(m.DescriptionColumn)));
                    continue;
                }

                var accountName = Cell(m.AccountColumn);
                if (m.AccountColumn is not null && accountName.Length > 0
                    && !string.Equals(accountName, account.Name, StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(Row(line, ImportRowStatus.Skipped, ImportMessages.OtherAccount, accountName));
                    continue;
                }

                var description = Cell(m.DescriptionColumn);
                if (!TryParseDate(Cell(m.DateColumn), options.DateStyle, out var date))
                {
                    rows.Add(Row(line, ImportRowStatus.Error, ImportMessages.BadDate, description));
                    continue;
                }

                var amountText = Cell(m.AmountColumn);
                if (!MoneyParser.TryParseSigned(amountText, options.NumberStyle == ImportNumberStyle.Plain ? plain : regional, out var signed))
                {
                    rows.Add(Row(line, ImportRowStatus.Error, ImportMessages.BadAmount, description) with { Date = date });
                    continue;
                }

                if (signed == 0)
                {
                    rows.Add(Row(line, ImportRowStatus.Error, ImportMessages.ZeroAmount, description) with { Date = date });
                    continue;
                }

                TransactionType type;
                if (IncomeWords.Contains(typeText))
                {
                    type = TransactionType.Income;
                }
                else if (ExpenseWords.Contains(typeText))
                {
                    type = TransactionType.Expense;
                }
                else
                {
                    // No (recognised) type column: the sign decides.
                    type = signed < 0 ? TransactionType.Expense : TransactionType.Income;
                }

                var amount = Math.Abs(signed);
                var notes = Cell(m.NotesColumn);
                if (description.Length > TransactionService.MaxDescriptionLength || notes.Length > TransactionService.MaxNotesLength)
                {
                    rows.Add(Row(line, ImportRowStatus.Error, ImportMessages.TooLong, description) with { Date = date, Amount = amount, Type = type });
                    continue;
                }

                var (category, fallback) = lookup.Resolve(Cell(m.CategoryColumn), type);
                rows.Add(new ImportRow(
                    line,
                    ImportRowStatus.Ready,
                    fallback ? ImportMessages.CategoryFallback : null,
                    type,
                    date,
                    amount,
                    description,
                    category?.Id,
                    category?.Name ?? string.Empty,
                    notes.Length == 0 ? null : notes));
            }

            await MarkDuplicatesAsync(db, options.AccountId, rows, cancellationToken).ConfigureAwait(false);
            return new ImportPreview(rows);
        }
    }

    private static ImportRow Row(int line, ImportRowStatus status, string code, string description) =>
        new(line, status, code, TransactionType.Expense, default, 0, description, null, string.Empty, null);

    /// <summary>Same date, type, amount and description as a transaction already in the account, or as an earlier row.</summary>
    private static async Task MarkDuplicatesAsync(AstraDbContext db, Guid accountId, List<ImportRow> rows, CancellationToken ct)
    {
        var ready = rows.Where(r => r.Status == ImportRowStatus.Ready).ToList();
        if (ready.Count == 0)
        {
            return;
        }

        var from = ready.Min(r => r.Date);
        var to = ready.Max(r => r.Date);
        var existing = (await db.Transactions.AsNoTracking()
                .Where(t => t.AccountId == accountId && t.Date >= from && t.Date <= to
                            && (t.Type == TransactionType.Income || t.Type == TransactionType.Expense))
                .Select(t => new { t.Date, t.Type, t.Amount, t.Description })
                .ToListAsync(ct).ConfigureAwait(false))
            .Select(t => Key(t.Date, t.Type, t.Amount, t.Description))
            .ToHashSet(StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Status != ImportRowStatus.Ready)
            {
                continue;
            }

            var key = Key(r.Date, r.Type, r.Amount, r.Description);
            if (existing.Contains(key))
            {
                rows[i] = r with { Status = ImportRowStatus.Duplicate, MessageCode = ImportMessages.ExistsInAccount };
            }
            else if (!seen.Add(key))
            {
                rows[i] = r with { Status = ImportRowStatus.Duplicate, MessageCode = ImportMessages.RepeatedInFile };
            }
        }
    }

    private static string Key(DateOnly date, TransactionType type, long amount, string description) =>
        string.Create(CultureInfo.InvariantCulture, $"{date:yyyy-MM-dd}|{(int)type}|{amount}|{description.Trim().ToUpperInvariant()}");

    // ------------------------------------------------------------------ Import

    public async Task<int> ImportAsync(Guid accountId, IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var toSave = rows.Where(r => r.Status is ImportRowStatus.Ready or ImportRowStatus.Duplicate).ToList();
        if (toSave.Count == 0)
        {
            return 0;
        }

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var dbTx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (dbTx.ConfigureAwait(false))
            {
                var now = _clock.UtcNow.UtcDateTime;
                foreach (var row in toSave)
                {
                    try
                    {
                        await TransactionService.AddValidatedAsync(
                            db,
                            new TransactionDraft
                            {
                                Type = row.Type,
                                Amount = row.Amount,
                                Description = row.Description,
                                Notes = row.Notes,
                                CategoryId = row.CategoryId,
                                AccountId = accountId,
                                Date = row.Date,
                            },
                            now,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (FinanceValidationException ex)
                    {
                        // All or nothing: the transaction is rolled back when it is disposed uncommitted.
                        throw new ImportFailedException(row.LineNumber, ex.Errors[0].Code, ex);
                    }
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await dbTx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return toSave.Count;
            }
        }
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>Removes the apostrophe Astra's CSV export adds in front of text that could be read as a formula.</summary>
    private static string Unprotect(string value) =>
        value.Length > 1 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@' ? value[1..] : value;

    public static bool TryParseDate(string text, DateFormatStyle style, out DateOnly date)
    {
        var normalized = text.Trim().Replace('.', '/').Replace('-', '/');
        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        string[] patterns = style switch
        {
            DateFormatStyle.MonthDayYear => ["M/d/yyyy", "MM/dd/yyyy"],
            DateFormatStyle.YearMonthDay => ["yyyy/M/d", "yyyy/MM/dd"],
            _ => ["d/M/yyyy", "dd/MM/yyyy"],
        };
        return DateOnly.TryParseExact(normalized, patterns, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Matches category names in English and Indonesian (built-in names) and custom names.</summary>
    private sealed class CategoryLookup
    {
        private readonly Dictionary<(CategoryKind, string), Category> _byName = [];
        private readonly Dictionary<CategoryKind, Category?> _fallback = [];

        public static CategoryLookup Build(IReadOnlyList<Category> categories)
        {
            var lookup = new CategoryLookup();
            var english = new LocalizationService(AppLanguage.English);
            var indonesian = new LocalizationService(AppLanguage.Indonesian);
            foreach (var c in categories.Where(c => c.IsActive))
            {
                foreach (var name in new[] { c.Name, DefaultCategories.DisplayName(c, english), DefaultCategories.DisplayName(c, indonesian) })
                {
                    lookup._byName.TryAdd((c.Kind, name.Trim().ToUpperInvariant()), c);
                }
            }

            lookup._fallback[CategoryKind.Income] = categories.FirstOrDefault(c => c.IsActive && c.SystemKey == "OtherIncome");
            lookup._fallback[CategoryKind.Expense] = categories.FirstOrDefault(c => c.IsActive && c.SystemKey == "OtherExpense");
            return lookup;
        }

        /// <summary>The matching category, or the "Other" category of that kind (with <c>fallback</c> = true).</summary>
        public (Category? Category, bool Fallback) Resolve(string name, TransactionType type)
        {
            var kind = type == TransactionType.Income ? CategoryKind.Income : CategoryKind.Expense;
            return name.Length > 0 && _byName.TryGetValue((kind, name.Trim().ToUpperInvariant()), out var match)
                ? (match, false)
                : (_fallback.GetValueOrDefault(kind), true);
        }
    }
}
