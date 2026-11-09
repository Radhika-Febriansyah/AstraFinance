using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Rules;
using AstraFinance.Core.Validation;
using AstraFinance.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace AstraFinance.Infrastructure.Services;

/// <summary>
/// Recurring bills and subscriptions. A bill never changes a balance by reaching its due date: only a confirmed
/// payment creates an Expense. Each occurrence can be handled once (paid or skipped), enforced by a unique
/// (bill, due date) index; the next open occurrence is the first one after the latest handled occurrence.
/// </summary>
public sealed class BillService : IBillService
{
    public const int MaxNameLength = 100;
    public const int MaxNotesLength = 2000;

    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly INotificationSettingsService _notificationSettings;
    private readonly IClock _clock;

    public BillService(IDbContextFactory<AstraDbContext> factory, INotificationSettingsService notificationSettings, IClock clock)
    {
        _factory = factory;
        _notificationSettings = notificationSettings;
        _clock = clock;
    }

    public async Task<Bill> CreateAsync(BillDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await ValidateAsync(db, draft, existing: null, cancellationToken).ConfigureAwait(false);
            var now = _clock.UtcNow.UtcDateTime;
            var bill = new Bill { Id = Guid.NewGuid(), CreatedAtUtc = now, IsActive = true };
            Apply(bill, draft, now);
            db.Bills.Add(bill);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return bill;
        }
    }

    public async Task<Bill> UpdateAsync(Guid id, BillDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false)
                       ?? throw new FinanceValidationException("Id", ValidationCodes.BillNotFound);
            await ValidateAsync(db, draft, bill, cancellationToken).ConfigureAwait(false);
            Apply(bill, draft, _clock.UtcNow.UtcDateTime);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return bill;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false)
                       ?? throw new FinanceValidationException("Id", ValidationCodes.BillNotFound);
            if (await db.BillPayments.AnyAsync(p => p.BillId == id, cancellationToken).ConfigureAwait(false))
            {
                // Payment history must stay explainable: such bills can only be paused.
                throw new FinanceValidationException("Id", ValidationCodes.BillHasHistory);
            }

            db.Bills.Remove(bill);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id, cancellationToken).ConfigureAwait(false)
                       ?? throw new FinanceValidationException("Id", ValidationCodes.BillNotFound);
            if (bill.IsActive != isActive)
            {
                bill.IsActive = isActive;
                bill.UpdatedAtUtc = _clock.UtcNow.UtcDateTime;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<BillSummary?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken = default)
    {
        var list = await LoadSummariesAsync(today, includePaused: true, onlyId: id, cancellationToken).ConfigureAwait(false);
        return list.Count == 0 ? null : list[0];
    }

    public Task<IReadOnlyList<BillSummary>> ListAsync(DateOnly today, bool includePaused = false, CancellationToken cancellationToken = default) =>
        LoadSummariesAsync(today, includePaused, onlyId: null, cancellationToken);

    public async Task<Transaction> PayAsync(BillPaymentDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var dbTx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (dbTx.ConfigureAwait(false))
            {
                // Repeated confirmation of the same request (double click, retry): return what was saved.
                var already = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == draft.RequestId, cancellationToken).ConfigureAwait(false);
                if (already is not null && await db.BillPayments.AnyAsync(p => p.TransactionId == already.Id, cancellationToken).ConfigureAwait(false))
                {
                    return already;
                }

                var (bill, payment) = await LoadOpenOccurrenceAsync(db, draft.BillId, draft.DueDate, cancellationToken).ConfigureAwait(false);
                var now = _clock.UtcNow.UtcDateTime;
                var transaction = await TransactionService.AddValidatedAsync(
                    db,
                    new TransactionDraft
                    {
                        Id = draft.RequestId,
                        Type = TransactionType.Expense,
                        Amount = draft.Amount,
                        Description = bill.Name,
                        Notes = draft.Note,
                        CategoryId = bill.CategoryId,
                        AccountId = draft.AccountId,
                        Date = draft.PaidDate,
                    },
                    now,
                    cancellationToken).ConfigureAwait(false);

                Record(db, payment, bill.Id, draft.DueDate, transaction.Id, now);
                await SaveHandledAsync(db, cancellationToken).ConfigureAwait(false);
                await dbTx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return transaction;
            }
        }
    }

    public async Task SkipAsync(Guid billId, DateOnly dueDate, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var dbTx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (dbTx.ConfigureAwait(false))
            {
                var (bill, payment) = await LoadOpenOccurrenceAsync(db, billId, dueDate, cancellationToken).ConfigureAwait(false);
                Record(db, payment, bill.Id, dueDate, transactionId: null, _clock.UtcNow.UtcDateTime);
                await SaveHandledAsync(db, cancellationToken).ConfigureAwait(false);
                await dbTx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task SkipMissedAsync(Guid billId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var dbTx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (dbTx.ConfigureAwait(false))
            {
                var bill = await db.Bills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId, cancellationToken).ConfigureAwait(false)
                           ?? throw new FinanceValidationException("Id", ValidationCodes.BillNotFound);
                var states = await LoadStatesAsync(db, [billId], cancellationToken).ConfigureAwait(false);
                var next = NextDue(bill, states.GetValueOrDefault(billId) ?? []);
                var lastMissed = BillSchedule.LatestOnOrBefore(bill.FirstDueDate, bill.Recurrence, today.AddDays(-1));
                if (lastMissed is null || lastMissed.Value < next)
                {
                    throw new FinanceValidationException("DueDate", ValidationCodes.BillOccurrenceNotOpen);
                }

                var (_, payment) = await LoadOpenOccurrenceAsync(db, billId, lastMissed.Value, cancellationToken, skipOrderCheck: true).ConfigureAwait(false);
                Record(db, payment, billId, lastMissed.Value, transactionId: null, _clock.UtcNow.UtcDateTime);
                await SaveHandledAsync(db, cancellationToken).ConfigureAwait(false);
                await dbTx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task UndoSkipAsync(Guid billId, DateOnly dueDate, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var dbTx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (dbTx.ConfigureAwait(false))
            {
                var states = (await LoadStatesAsync(db, [billId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(billId) ?? [];
                var latest = states.Where(s => s.IsHandled).MaxBy(s => s.Payment.DueDate);
                if (latest is null || latest.Payment.DueDate != dueDate || latest.Payment.TransactionId is not null)
                {
                    throw new FinanceValidationException("DueDate", ValidationCodes.BillOccurrenceNotOpen);
                }

                var payment = await db.BillPayments.FirstAsync(p => p.Id == latest.Payment.Id, cancellationToken).ConfigureAwait(false);
                db.BillPayments.Remove(payment);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await dbTx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<BillHistoryEntry>> GetHistoryAsync(Guid billId, CancellationToken cancellationToken = default)
    {
        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var states = (await LoadStatesAsync(db, [billId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(billId) ?? [];
            var accountIds = states.Where(s => s.Transaction is not null).Select(s => s.Transaction!.AccountId).Distinct().ToList();
            var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken).ConfigureAwait(false);
            return states
                .OrderByDescending(s => s.Payment.DueDate)
                .Select(s => new BillHistoryEntry(
                    s.Payment.Id,
                    s.Payment.DueDate,
                    s.Payment.PaidAtUtc,
                    s.Transaction,
                    s.Transaction is null ? null : accounts.GetValueOrDefault(s.Transaction.AccountId),
                    IsSkipped: s.Payment.TransactionId is null,
                    IsVoided: !s.IsHandled))
                .ToList();
        }
    }

    /// <summary>The first occurrence after the latest handled one.</summary>
    internal static DateOnly NextDue(Bill bill, IReadOnlyList<PaymentState> states)
    {
        var latest = states.Where(s => s.IsHandled).Select(s => (DateOnly?)s.Payment.DueDate).Max();
        return latest is null ? bill.FirstDueDate : BillSchedule.NextAfter(bill.FirstDueDate, bill.Recurrence, latest.Value);
    }

    internal static BillStatus Evaluate(Bill bill, DateOnly nextDue, DateOnly today, int reminderDays)
    {
        if (!bill.IsActive)
        {
            return BillStatus.Paused;
        }

        var days = nextDue.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => BillStatus.Overdue,
            0 => BillStatus.DueToday,
            _ when days <= reminderDays => BillStatus.DueSoon,
            _ => BillStatus.Upcoming,
        };
    }

    private async Task<IReadOnlyList<BillSummary>> LoadSummariesAsync(DateOnly today, bool includePaused, Guid? onlyId, CancellationToken ct)
    {
        var settings = await _notificationSettings.GetAsync(ct).ConfigureAwait(false);
        var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var query = db.Bills.AsNoTracking();
            if (onlyId is { } id)
            {
                query = query.Where(b => b.Id == id);
            }
            else if (!includePaused)
            {
                query = query.Where(b => b.IsActive);
            }

            var bills = await query.ToListAsync(ct).ConfigureAwait(false);
            if (bills.Count == 0)
            {
                return [];
            }

            var billIds = bills.Select(b => b.Id).ToList();
            var categoryIds = bills.Select(b => b.CategoryId).Distinct().ToList();
            var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
            var states = await LoadStatesAsync(db, billIds, ct).ConfigureAwait(false);
            var accountIds = bills.Select(b => b.PaymentAccountId)
                .Concat(states.Values.SelectMany(v => v).Where(s => s.Transaction is not null).Select(s => s.Transaction!.AccountId))
                .Distinct()
                .ToList();
            var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct).ConfigureAwait(false);

            var result = new List<BillSummary>(bills.Count);
            foreach (var bill in bills)
            {
                var billStates = states.GetValueOrDefault(bill.Id) ?? [];
                var next = NextDue(bill, billStates);
                var status = Evaluate(bill, next, today, settings.BillReminderDays);
                var missed = status == BillStatus.Overdue ? BillSchedule.CountBetween(bill.FirstDueDate, bill.Recurrence, next, today) : 0;
                var last = billStates.Where(s => s.IsHandled).MaxBy(s => s.Payment.DueDate);
                BillHistoryEntry? lastEntry = last is null
                    ? null
                    : new BillHistoryEntry(
                        last.Payment.Id,
                        last.Payment.DueDate,
                        last.Payment.PaidAtUtc,
                        last.Transaction,
                        last.Transaction is null ? null : accounts.GetValueOrDefault(last.Transaction.AccountId),
                        IsSkipped: last.Payment.TransactionId is null,
                        IsVoided: false);
                result.Add(new BillSummary(
                    bill,
                    categories.GetValueOrDefault(bill.CategoryId),
                    accounts.GetValueOrDefault(bill.PaymentAccountId) ?? string.Empty,
                    next,
                    status,
                    missed,
                    lastEntry,
                    billStates.Count > 0));
            }

            return result
                .OrderBy(s => s.Bill.IsActive ? 0 : 1)
                .ThenBy(s => s.NextDue)
                .ThenBy(s => s.Bill.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    internal sealed record PaymentState(BillPayment Payment, Transaction? Transaction)
    {
        /// <summary>Skipped, or paid with an Expense that still exists.</summary>
        public bool IsHandled => Payment.TransactionId is null || Transaction is { IsDeleted: false };
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<PaymentState>>> LoadStatesAsync(AstraDbContext db, List<Guid> billIds, CancellationToken ct)
    {
        var payments = await db.BillPayments.AsNoTracking().Where(p => billIds.Contains(p.BillId))
            .ToListAsync(ct).ConfigureAwait(false);
        var transactionIds = payments.Where(p => p.TransactionId is not null).Select(p => p.TransactionId!.Value).ToList();
        var transactions = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(t => transactionIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        return payments
            .GroupBy(p => p.BillId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PaymentState>)g
                    .Select(p => new PaymentState(p, p.TransactionId is { } tid ? transactions.GetValueOrDefault(tid) : null))
                    .ToList());
    }

    /// <summary>
    /// Loads an active bill and checks that <paramref name="dueDate"/> is its next open occurrence. Returns the
    /// existing (voided) record for that occurrence, if any, so it can be reused.
    /// </summary>
    private static async Task<(Bill Bill, BillPayment? Existing)> LoadOpenOccurrenceAsync(
        AstraDbContext db, Guid billId, DateOnly dueDate, CancellationToken ct, bool skipOrderCheck = false)
    {
        var bill = await db.Bills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId, ct).ConfigureAwait(false)
                   ?? throw new FinanceValidationException("Id", ValidationCodes.BillNotFound);
        if (!bill.IsActive)
        {
            throw new FinanceValidationException("Id", ValidationCodes.BillPaused);
        }

        var states = (await LoadStatesAsync(db, [billId], ct).ConfigureAwait(false)).GetValueOrDefault(billId) ?? [];
        var existing = states.FirstOrDefault(s => s.Payment.DueDate == dueDate);
        if (existing is { IsHandled: true })
        {
            throw new FinanceValidationException("DueDate", ValidationCodes.BillAlreadyHandled);
        }

        if (!skipOrderCheck && NextDue(bill, states) != dueDate)
        {
            // Occurrences are handled in order; anything else is a stale form or a mistake.
            throw new FinanceValidationException("DueDate", ValidationCodes.BillOccurrenceNotOpen);
        }

        var tracked = existing is null
            ? null
            : await db.BillPayments.FirstAsync(p => p.Id == existing.Payment.Id, ct).ConfigureAwait(false);
        return (bill, tracked);
    }

    private static void Record(AstraDbContext db, BillPayment? existing, Guid billId, DateOnly dueDate, Guid? transactionId, DateTime now)
    {
        if (existing is not null)
        {
            // The occurrence's earlier payment was voided (its Expense deleted): reuse the record.
            existing.TransactionId = transactionId;
            existing.PaidAtUtc = now;
            return;
        }

        db.BillPayments.Add(new BillPayment
        {
            Id = Guid.NewGuid(),
            BillId = billId,
            DueDate = dueDate,
            TransactionId = transactionId,
            PaidAtUtc = now,
        });
    }

    private static async Task SaveHandledAsync(AstraDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Unique (bill, due date): another confirmation of the same occurrence won the race.
            throw new FinanceValidationException("DueDate", ValidationCodes.BillAlreadyHandled);
        }
    }

    private static void Apply(Bill bill, BillDraft draft, DateTime now)
    {
        bill.Name = draft.Name.Trim();
        bill.Amount = draft.Amount;
        bill.CategoryId = draft.CategoryId;
        bill.PaymentAccountId = draft.PaymentAccountId;
        bill.FirstDueDate = draft.FirstDueDate;
        bill.Recurrence = draft.Recurrence;
        bill.Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim();
        bill.UpdatedAtUtc = now;
    }

    private static async Task ValidateAsync(AstraDbContext db, BillDraft draft, Bill? existing, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        var name = draft.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors.Add(new("Name", ValidationCodes.Required));
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add(new("Name", ValidationCodes.TooLong));
        }

        if (draft.Amount <= 0)
        {
            errors.Add(new("Amount", ValidationCodes.AmountMustBePositive));
        }
        else if (draft.Amount > MoneyMath.MaxAmount)
        {
            errors.Add(new("Amount", ValidationCodes.AmountTooLarge));
        }

        if (draft.FirstDueDate == default)
        {
            errors.Add(new("FirstDueDate", ValidationCodes.Required));
        }

        if (!Enum.IsDefined(draft.Recurrence))
        {
            errors.Add(new("Recurrence", ValidationCodes.Required));
        }

        if (draft.Notes?.Trim().Length > MaxNotesLength)
        {
            errors.Add(new("Notes", ValidationCodes.TooLong));
        }

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == draft.CategoryId, ct).ConfigureAwait(false);
        if (category is null)
        {
            errors.Add(new("CategoryId", ValidationCodes.CategoryNotFound));
        }
        else if (category.Kind != CategoryKind.Expense)
        {
            errors.Add(new("CategoryId", ValidationCodes.CategoryKindMismatch));
        }
        else if (!category.IsActive && existing?.CategoryId != category.Id)
        {
            errors.Add(new("CategoryId", ValidationCodes.CategoryInactive));
        }

        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == draft.PaymentAccountId, ct).ConfigureAwait(false);
        if (account is null)
        {
            errors.Add(new("PaymentAccountId", ValidationCodes.AccountNotFound));
        }
        else if (!account.IsActive && existing?.PaymentAccountId != account.Id)
        {
            errors.Add(new("PaymentAccountId", ValidationCodes.AccountInactive));
        }

        if (errors.Count > 0)
        {
            throw new FinanceValidationException(errors);
        }
    }
}
