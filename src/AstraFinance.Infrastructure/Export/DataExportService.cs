using System.Globalization;
using System.Text.Json;
using AstraFinance.Core.Interfaces;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Export;

/// <summary>
/// Full data export as one readable JSON document. Amounts are integers in minor units of the base currency
/// (stated in the file). Deleted transactions are included with <c>isDeleted: true</c>, so nothing is left out.
/// Security settings (PIN hash, attempt counters) and app settings are never exported.
/// </summary>
public sealed class DataExportService : IDataExportService
{
    public const string FormatName = "astra-finance-export";
    public const int FormatVersion = 1;

    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IRegionalFormatService _format;
    private readonly IClock _clock;

    public DataExportService(IDbContextFactory<AstraDbContext> factory, IRegionalFormatService format, IClock clock)
    {
        _factory = factory;
        _format = format;
        _clock = clock;
    }

    public async Task<int> ExportJsonAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.SortOrder).ToListAsync(cancellationToken).ConfigureAwait(false);
            var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ToListAsync(cancellationToken).ConfigureAwait(false);
            var transactions = await db.Transactions.IgnoreQueryFilters().AsNoTracking().OrderBy(t => t.Date).ThenBy(t => t.CreatedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
            var adjustments = await db.BalanceAdjustments.IgnoreQueryFilters().AsNoTracking().OrderBy(a => a.Date).ToListAsync(cancellationToken).ConfigureAwait(false);
            var budgets = await db.Budgets.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            var goals = await db.SavingsGoals.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            var allocations = await db.GoalAllocations.AsNoTracking().OrderBy(a => a.Date).ToListAsync(cancellationToken).ConfigureAwait(false);
            var bills = await db.Bills.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            var payments = await db.BillPayments.AsNoTracking().OrderBy(p => p.DueDate).ToListAsync(cancellationToken).ConfigureAwait(false);

            var temp = path + ".partial";
            try
            {
                var stream = File.Create(temp);
                await using (stream.ConfigureAwait(false))
                {
                    var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                    await using (w.ConfigureAwait(false))
                    {
                        var currency = _format.Current.Currency;
                        w.WriteStartObject();
                        w.WriteString("format", FormatName);
                        w.WriteNumber("formatVersion", FormatVersion);
                        w.WriteString("exportedAtUtc", _clock.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                        w.WriteStartObject("currency");
                        w.WriteString("code", currency.Code);
                        w.WriteNumber("minorDigits", currency.MinorDigits);
                        w.WriteEndObject();
                        w.WriteString("amounts", "Integers in minor units of the currency (for IDR: whole Rupiah).");

                        Array(w, "accounts", accounts, a =>
                        {
                            w.WriteString("id", a.Id);
                            w.WriteString("name", a.Name);
                            w.WriteString("type", a.Type.ToString());
                            w.WriteNumber("openingBalance", a.OpeningBalance);
                            w.WriteBoolean("isActive", a.IsActive);
                            w.WriteString("icon", a.IconKey);
                            w.WriteString("color", a.ColorHex);
                            w.WriteString("createdAtUtc", Utc(a.CreatedAtUtc));
                        });
                        Array(w, "categories", categories, c =>
                        {
                            w.WriteString("id", c.Id);
                            w.WriteString("name", c.Name);
                            Nullable(w, "systemKey", c.SystemKey);
                            w.WriteString("kind", c.Kind.ToString());
                            w.WriteBoolean("isActive", c.IsActive);
                            w.WriteString("icon", c.IconKey);
                            w.WriteString("color", c.ColorHex);
                        });
                        Array(w, "transactions", transactions, t =>
                        {
                            w.WriteString("id", t.Id);
                            w.WriteString("type", t.Type.ToString());
                            w.WriteString("date", Day(t.Date));
                            w.WriteNumber("amount", t.Amount);
                            w.WriteString("description", t.Description);
                            Nullable(w, "notes", t.Notes);
                            NullableId(w, "categoryId", t.CategoryId);
                            w.WriteString("accountId", t.AccountId);
                            NullableId(w, "destinationAccountId", t.DestinationAccountId);
                            NullableId(w, "transferId", t.ParentTransactionId);
                            w.WriteBoolean("isDeleted", t.IsDeleted);
                            w.WriteString("createdAtUtc", Utc(t.CreatedAtUtc));
                            w.WriteString("updatedAtUtc", Utc(t.UpdatedAtUtc));
                        });
                        Array(w, "balanceAdjustments", adjustments, a =>
                        {
                            w.WriteString("id", a.Id);
                            w.WriteString("accountId", a.AccountId);
                            w.WriteString("date", Day(a.Date));
                            w.WriteNumber("amount", a.Amount);
                            w.WriteNumber("balanceBefore", a.BalanceBefore);
                            w.WriteString("reason", a.Reason);
                            w.WriteBoolean("isDeleted", a.IsDeleted);
                        });
                        Array(w, "budgets", budgets, b =>
                        {
                            w.WriteString("id", b.Id);
                            w.WriteString("categoryId", b.CategoryId);
                            w.WriteNumber("amount", b.Amount);
                            w.WriteString("period", b.Period.ToString());
                            w.WriteString("startDate", Day(b.StartDate));
                            if (b.WarningThresholdPercent is { } threshold)
                            {
                                w.WriteNumber("warningThresholdPercent", threshold);
                            }
                            else
                            {
                                w.WriteNull("warningThresholdPercent");
                            }

                            Nullable(w, "notes", b.Notes);
                        });
                        Array(w, "savingsGoals", goals, g =>
                        {
                            w.WriteString("id", g.Id);
                            w.WriteString("name", g.Name);
                            w.WriteNumber("targetAmount", g.TargetAmount);
                            Nullable(w, "targetDate", g.TargetDate is { } d ? Day(d) : null);
                            w.WriteString("status", g.Status.ToString());
                        });
                        Array(w, "goalAllocations", allocations, a =>
                        {
                            w.WriteString("id", a.Id);
                            w.WriteString("goalId", a.GoalId);
                            w.WriteString("accountId", a.AccountId);
                            w.WriteString("date", Day(a.Date));
                            w.WriteNumber("amount", a.Amount);
                            Nullable(w, "note", a.Note);
                        });
                        Array(w, "bills", bills, b =>
                        {
                            w.WriteString("id", b.Id);
                            w.WriteString("name", b.Name);
                            w.WriteNumber("amount", b.Amount);
                            w.WriteString("categoryId", b.CategoryId);
                            w.WriteString("paymentAccountId", b.PaymentAccountId);
                            w.WriteString("firstDueDate", Day(b.FirstDueDate));
                            w.WriteString("recurrence", b.Recurrence.ToString());
                            w.WriteBoolean("isActive", b.IsActive);
                            Nullable(w, "notes", b.Notes);
                        });
                        Array(w, "billPayments", payments, p =>
                        {
                            w.WriteString("id", p.Id);
                            w.WriteString("billId", p.BillId);
                            w.WriteString("dueDate", Day(p.DueDate));
                            NullableId(w, "transactionId", p.TransactionId);
                            w.WriteBoolean("skipped", p.TransactionId is null);
                        });
                        w.WriteEndObject();
                    }
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

            return accounts.Count + categories.Count + transactions.Count + adjustments.Count + budgets.Count
                   + goals.Count + allocations.Count + bills.Count + payments.Count;
        }
    }

    private static void Array<T>(Utf8JsonWriter w, string name, IEnumerable<T> items, Action<T> write)
    {
        w.WriteStartArray(name);
        foreach (var item in items)
        {
            w.WriteStartObject();
            write(item);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void Nullable(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
        {
            w.WriteNull(name);
        }
        else
        {
            w.WriteString(name, value);
        }
    }

    private static void NullableId(Utf8JsonWriter w, string name, Guid? value)
    {
        if (value is { } id)
        {
            w.WriteString(name, id);
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}
