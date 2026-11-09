using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Entities;

// ---------------------------------------------------------------------------------------------
// Persistence model. All money values are integer minor units of the application base currency
// (whole Rupiah for IDR). Never use float/double for money. Audit timestamps are UTC; financial
// dates are date-only (DateOnly) so they can never shift across time zones.
// ---------------------------------------------------------------------------------------------

/// <summary>Common audit metadata.</summary>
public abstract class AuditedEntity
{
    public Guid Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>A place where money is held (cash, bank, e-wallet, ...).</summary>
public sealed class Account : AuditedEntity
{
    public string Name { get; set; } = string.Empty;

    public AccountType Type { get; set; }

    /// <summary>Balance before any recorded activity, in minor units. May be zero or negative (e.g. overdraft).</summary>
    public long OpeningBalance { get; set; }

    public string IconKey { get; set; } = "Icon.Accounts";

    public string ColorHex { get; set; } = "#F4806B";

    /// <summary>Inactive accounts keep their history but accept no new activity.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}

/// <summary>Income or expense classification.</summary>
public sealed class Category : AuditedEntity
{
    /// <summary>User-visible name for custom categories (system categories are localized via <see cref="SystemKey"/>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable key for built-in categories (e.g. "Food"); null for user-created categories.</summary>
    public string? SystemKey { get; set; }

    public CategoryKind Kind { get; set; }

    public string IconKey { get; set; } = "Icon.Layers";

    public string ColorHex { get; set; } = "#F4806B";

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}

/// <summary>
/// Income, expense or transfer. A transfer is a single record (source <see cref="AccountId"/>,
/// <see cref="DestinationAccountId"/>) so it can never be counted twice. A transfer fee is a
/// separate Expense whose <see cref="ParentTransactionId"/> points to the transfer.
/// </summary>
public sealed class Transaction : AuditedEntity
{
    public TransactionType Type { get; set; }

    /// <summary>Always positive; the type determines the direction.</summary>
    public long Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Notes { get; set; }

    /// <summary>Required for income and expense, null for transfers.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Income/expense account, or the transfer source account.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Transfer destination; null for income and expense.</summary>
    public Guid? DestinationAccountId { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Links a transfer-fee expense to its transfer.</summary>
    public Guid? ParentTransactionId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }
}

/// <summary>
/// Documented balance correction. Stored as a signed delta with a mandatory reason so the
/// history is preserved; the opening balance is never silently overwritten.
/// </summary>
public sealed class BalanceAdjustment : AuditedEntity
{
    public Guid AccountId { get; set; }

    /// <summary>Signed change in minor units (non-zero).</summary>
    public long Amount { get; set; }

    /// <summary>Calculated balance immediately before the adjustment (audit).</summary>
    public long BalanceBefore { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }
}

/// <summary>Monthly spending limit for an expense category.</summary>
public sealed class Budget : AuditedEntity
{
    public Guid CategoryId { get; set; }

    public long Amount { get; set; }

    public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;

    public DateOnly StartDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Percent of the limit at which status becomes Warning; null uses the global default.</summary>
    public int? WarningThresholdPercent { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Savings target funded by allocating (earmarking) existing account money.</summary>
public sealed class SavingsGoal : AuditedEntity
{
    public string Name { get; set; } = string.Empty;

    public long TargetAmount { get; set; }

    public DateOnly? TargetDate { get; set; }

    public string IconKey { get; set; } = "Icon.SavingsGoals";

    public string ColorHex { get; set; } = "#F4806B";

    public string? ImagePath { get; set; }

    public SavingsGoalStatus Status { get; set; } = SavingsGoalStatus.Active;

    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>
/// Allocation history entry. Positive amounts earmark money from <see cref="AccountId"/> for the
/// goal, negative amounts release it. Allocations never change account balances.
/// </summary>
public sealed class GoalAllocation
{
    public Guid Id { get; set; }

    public Guid GoalId { get; set; }

    public Guid AccountId { get; set; }

    public long Amount { get; set; }

    public DateOnly Date { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Recurring bill or subscription. Reaching the due date never moves money by itself.</summary>
public sealed class Bill : AuditedEntity
{
    public string Name { get; set; } = string.Empty;

    public long Amount { get; set; }

    public Guid CategoryId { get; set; }

    public Guid PaymentAccountId { get; set; }

    /// <summary>First due date; later occurrences are derived from the recurrence.</summary>
    public DateOnly FirstDueDate { get; set; }

    public RecurrenceFrequency Recurrence { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// How one bill occurrence was handled: paid (with the Expense it created) or skipped (no expense).
/// Unique per (bill, due date) to prevent duplicate payment records.
/// </summary>
public sealed class BillPayment
{
    public Guid Id { get; set; }

    public Guid BillId { get; set; }

    public DateOnly DueDate { get; set; }

    /// <summary>
    /// The Expense transaction created when the payment was confirmed; null when the occurrence was skipped.
    /// If that transaction is later deleted, the occurrence counts as unpaid again.
    /// </summary>
    public Guid? TransactionId { get; set; }

    /// <summary>When the payment (or skip) was recorded.</summary>
    public DateTime PaidAtUtc { get; set; }
}

/// <summary>Key/value application setting stored in the database (regional, onboarding, security).</summary>
public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}
