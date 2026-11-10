using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Interfaces;

/// <summary>Input for creating or editing a recurring bill or subscription.</summary>
/// <param name="Amount">Usual amount in minor units (positive). A payment may differ (variable bills).</param>
/// <param name="FirstDueDate">First due date; later occurrences follow the recurrence.</param>
public sealed record BillDraft(
    string Name,
    long Amount,
    Guid CategoryId,
    Guid PaymentAccountId,
    DateOnly FirstDueDate,
    RecurrenceFrequency Recurrence,
    string? Notes);

/// <summary>Confirmation of one bill occurrence's payment.</summary>
/// <param name="RequestId">Id given to the created Expense; repeating a request with the same id writes nothing twice.</param>
/// <param name="DueDate">The occurrence being paid; must be the bill's next open occurrence.</param>
/// <param name="PaidDate">Date of the Expense transaction.</param>
public sealed record BillPaymentDraft(
    Guid RequestId,
    Guid BillId,
    DateOnly DueDate,
    long Amount,
    Guid AccountId,
    DateOnly PaidDate,
    string? Note);

/// <summary>A bill with its next open occurrence evaluated against a given day.</summary>
/// <param name="NextDue">The first occurrence after the latest handled (paid or skipped) one.</param>
/// <param name="MissedCount">Occurrences from <paramref name="NextDue"/> up to (not including) today; 0 unless overdue.</param>
/// <param name="LastHandled">The most recent handled occurrence, if any.</param>
/// <param name="HasHistory">True when a payment or skip was ever recorded (the bill can then only be paused, not deleted).</param>
public sealed record BillSummary(
    Bill Bill,
    Category? Category,
    string AccountName,
    DateOnly NextDue,
    BillStatus Status,
    int MissedCount,
    BillHistoryEntry? LastHandled,
    bool HasHistory);

/// <summary>One recorded occurrence. <paramref name="Transaction"/> is null for a skip, or when the payment was voided.</summary>
/// <param name="IsVoided">The payment's Expense was deleted, so the occurrence counts as unpaid again.</param>
public sealed record BillHistoryEntry(
    Guid Id,
    DateOnly DueDate,
    DateTime RecordedAtUtc,
    Transaction? Transaction,
    string? AccountName,
    bool IsSkipped,
    bool IsVoided);

public interface IBillService
{
    Task<Bill> CreateAsync(BillDraft draft, CancellationToken cancellationToken = default);

    Task<Bill> UpdateAsync(Guid id, BillDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Deletes a bill that has no payment history. Bills with history can only be paused.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Pauses or resumes a bill. Paused bills have no due status and send no reminders.</summary>
    Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    Task<BillSummary?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Active bills ordered by next due date (paused bills last, when included).</summary>
    Task<IReadOnlyList<BillSummary>> ListAsync(DateOnly today, bool includePaused = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms a payment: creates one Expense (through the normal transaction validation) and records the
    /// occurrence as paid, atomically. Reaching a due date never changes a balance on its own.
    /// </summary>
    Task<Transaction> PayAsync(BillPaymentDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Marks the next open occurrence as handled without recording an expense.</summary>
    Task SkipAsync(Guid billId, DateOnly dueDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Skips every missed occurrence: marks the latest occurrence on or before <paramref name="today"/> as skipped,
    /// so the bill moves on to its next future occurrence. No expense is recorded.
    /// </summary>
    Task SkipMissedAsync(Guid billId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Undoes a skip, provided it is still the bill's latest handled occurrence.</summary>
    Task UndoSkipAsync(Guid billId, DateOnly dueDate, CancellationToken cancellationToken = default);

    /// <summary>Recorded occurrences, newest first.</summary>
    Task<IReadOnlyList<BillHistoryEntry>> GetHistoryAsync(Guid billId, CancellationToken cancellationToken = default);
}

/// <summary>Notification preferences, stored with the financial data (so they are backed up with it).</summary>
/// <param name="Enabled">Master switch for reminders and warnings.</param>
/// <param name="BillReminderDays">Days before a due date at which a bill is "due soon" and a reminder is shown (0–14).</param>
/// <param name="WindowsNotifications">Also show reminders as Windows notifications (in addition to the in-app list).</param>
/// <param name="BackupReminders">Remind when no backup was made for <paramref name="BackupReminderDays"/> days (Phase 9).</param>
public sealed record NotificationSettings(
    bool Enabled,
    bool BillReminders,
    int BillReminderDays,
    bool BudgetWarnings,
    bool WindowsNotifications,
    bool BackupReminders = true,
    int BackupReminderDays = 14)
{
    public const int MaxReminderDays = 14;

    public static IReadOnlyList<int> BackupReminderChoices { get; } = [7, 14, 30];

    public static NotificationSettings Default { get; } = new(true, true, 3, true, true);
}

public interface INotificationSettingsService
{
    Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default);
}

public enum ReminderKind
{
    BillDueSoon = 0,
    BillDueToday = 1,
    BillOverdue = 2,
    BudgetWarning = 3,
    BudgetExceeded = 4,
    /// <summary>No backup for the configured number of days. <see cref="Reminder.Percent"/> holds the days since the last backup (-1 = never).</summary>
    BackupOverdue = 5,
}

/// <summary>A reminder that should be shown now. <paramref name="Date"/> is the due date for bills, the month for budgets.</summary>
/// <param name="Category">For budget warnings: the budget category (its display name is localized by the caller).</param>
public sealed record Reminder(ReminderKind Kind, Guid EntityId, string Name, long Amount, DateOnly Date, long Percent, Category? Category = null);

/// <summary>
/// Works out which bill reminders and budget warnings have not been shown yet and remembers what was shown,
/// so each appears once per bill occurrence and stage, and once per budget, month and level.
/// </summary>
public interface IReminderService
{
    Task<IReadOnlyList<Reminder>> CollectDueAsync(DateOnly today, CancellationToken cancellationToken = default);
}
