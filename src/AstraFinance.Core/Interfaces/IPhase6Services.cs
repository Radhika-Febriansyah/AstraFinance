using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Rules;

namespace AstraFinance.Core.Interfaces;

/// <summary>Input for creating or editing a monthly budget.</summary>
/// <param name="CategoryId">Expense category the budget limits.</param>
/// <param name="Amount">Monthly limit in minor units (positive).</param>
/// <param name="StartMonth">First month the budget applies to (any day in that month).</param>
/// <param name="WarningThresholdPercent">Per-budget warning threshold (1–100), or null for the global default.</param>
public sealed record BudgetDraft(Guid CategoryId, long Amount, DateOnly StartMonth, int? WarningThresholdPercent, string? Notes);

/// <summary>A budget evaluated for one month (Expense transactions only).</summary>
public sealed record BudgetStatusItem(Budget Budget, Category Category, BudgetEvaluation Evaluation);

/// <summary>Totals across all budgets for a month.</summary>
public sealed record BudgetOverview(DateRange Month, IReadOnlyList<BudgetStatusItem> Items, long TotalLimit, long TotalSpent)
{
    public long TotalRemaining => checked(TotalLimit - TotalSpent);
}

public interface IBudgetService
{
    Task<Budget> CreateAsync(BudgetDraft draft, CancellationToken cancellationToken = default);

    Task<Budget> UpdateAsync(Guid id, BudgetDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Removes a budget. Budgets are plans, not financial records: no transaction is affected.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Budget?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All budgets regardless of start month.</summary>
    Task<IReadOnlyList<Budget>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Evaluates every budget that applies in the month containing <paramref name="anyDayInMonth"/>.</summary>
    Task<BudgetOverview> GetOverviewAsync(DateOnly anyDayInMonth, CancellationToken cancellationToken = default);

    /// <summary>Global warning threshold used when a budget has none (default 80 %).</summary>
    Task<int> GetDefaultWarningThresholdAsync(CancellationToken cancellationToken = default);

    Task SetDefaultWarningThresholdAsync(int percent, CancellationToken cancellationToken = default);
}

/// <summary>Editable savings-goal fields.</summary>
public sealed record SavingsGoalDraft(string Name, long TargetAmount, DateOnly? TargetDate, string IconKey, string ColorHex);

/// <summary>Allocated money for a goal from one account.</summary>
public sealed record GoalAccountShare(Guid AccountId, long Amount);

/// <summary>A goal with its progress.</summary>
public sealed record SavingsGoalSummary(SavingsGoal Goal, long Allocated, IReadOnlyList<GoalAccountShare> ByAccount)
{
    public long Remaining => Math.Max(0, Goal.TargetAmount - Allocated);

    public long ProgressPercent => Money.MoneyMath.PercentOf(Allocated, Goal.TargetAmount);

    public bool IsReached => Allocated >= Goal.TargetAmount;
}

/// <summary>How much of an account's balance is earmarked for goals and how much is still free.</summary>
public sealed record AccountAllocationInfo(Guid AccountId, long Balance, long Allocated)
{
    public long Available => AllocationRules.Available(Balance, Allocated);

    /// <summary>True when spending reduced the balance below what is allocated to goals.</summary>
    public bool IsOverAllocated => Allocated > Balance;
}

/// <summary>
/// Savings goals earmark existing money. Allocations never change account balances; they reduce the
/// unallocated amount of the source account. Allocations can't exceed that available amount.
/// </summary>
public interface ISavingsGoalService
{
    Task<SavingsGoal> CreateAsync(SavingsGoalDraft draft, CancellationToken cancellationToken = default);

    Task<SavingsGoal> UpdateAsync(Guid id, SavingsGoalDraft draft, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavingsGoalSummary>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<SavingsGoalSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Earmarks <paramref name="amount"/> from an account for the goal.</summary>
    Task<GoalAllocation> AllocateAsync(Guid goalId, Guid accountId, long amount, DateOnly effectiveDate, string? note, CancellationToken cancellationToken = default);

    /// <summary>Releases (un-earmarks) money this goal holds from an account.</summary>
    Task<GoalAllocation> ReleaseAsync(Guid goalId, Guid accountId, long amount, DateOnly effectiveDate, string? note, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GoalAllocation>> GetHistoryAsync(Guid goalId, CancellationToken cancellationToken = default);

    Task SetStatusAsync(Guid goalId, SavingsGoalStatus status, CancellationToken cancellationToken = default);

    /// <summary>Archives a goal, recording releases for everything it still holds (history is kept).</summary>
    Task ArchiveAsync(Guid goalId, DateOnly effectiveDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountAllocationInfo>> GetAccountAllocationsAsync(CancellationToken cancellationToken = default);
}
