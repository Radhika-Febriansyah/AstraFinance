using AstraFinance.Core.Enums;
using AstraFinance.Core.Money;

namespace AstraFinance.Core.Rules;

/// <summary>Aggregated, non-deleted activity for one account (all values are non-negative except adjustments).</summary>
public sealed record BalanceComponents(
    long OpeningBalance,
    long Income,
    long Expense,
    long TransfersIn,
    long TransfersOut,
    long Adjustments);

/// <summary>The single definition of an account balance.</summary>
public static class BalanceCalculator
{
    /// <summary>
    /// Opening Balance + Income − Expense + Transfers In − Transfers Out + Balance Adjustments.
    /// Each transfer is one record that contributes to exactly one "in" and one "out".
    /// </summary>
    public static long Compute(BalanceComponents c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return checked(c.OpeningBalance + c.Income - c.Expense + c.TransfersIn - c.TransfersOut + c.Adjustments);
    }
}

/// <summary>Inclusive date range.</summary>
public readonly record struct DateRange(DateOnly Start, DateOnly End)
{
    public static DateRange Month(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        return new DateRange(start, start.AddMonths(1).AddDays(-1));
    }

    public static DateRange MonthOf(DateOnly date) => Month(date.Year, date.Month);

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}

/// <summary>Evaluated state of a budget for one period.</summary>
public sealed record BudgetEvaluation(long Limit, long Spent, long Remaining, long UsagePercent, BudgetStatus Status);

/// <summary>Budget status rules. Only Expense transactions are ever passed in as "spent".</summary>
public static class BudgetEvaluator
{
    public const int DefaultWarningThresholdPercent = 80;

    public static BudgetEvaluation Evaluate(long limit, long spent, int? warningThresholdPercent = null)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Budget limit must be positive.");
        }

        if (spent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spent), "Spent cannot be negative.");
        }

        var threshold = Math.Clamp(warningThresholdPercent ?? DefaultWarningThresholdPercent, 1, 100);
        var percent = MoneyMath.PercentOf(spent, limit);
        var remaining = checked(limit - spent);

        // Compare exactly (spent * 100 >= limit * threshold) rather than via the rounded percentage.
        var status = spent > limit
            ? BudgetStatus.Exceeded
            : (Int128)spent * 100 >= (Int128)limit * threshold ? BudgetStatus.Warning : BudgetStatus.Normal;

        return new BudgetEvaluation(limit, spent, remaining, percent, status);
    }
}

/// <summary>
/// Savings goals earmark existing money. Allocations never change balances; they reduce the
/// amount still available (unallocated) in the source account.
/// </summary>
public static class AllocationRules
{
    public static long Available(long accountBalance, long allocatedFromAccount) =>
        Math.Max(0, checked(accountBalance - allocatedFromAccount));

    /// <summary>True when allocating <paramref name="amount"/> keeps allocations within the account balance.</summary>
    public static bool CanAllocate(long amount, long accountBalance, long allocatedFromAccount) =>
        amount > 0 && amount <= Available(accountBalance, allocatedFromAccount);

    /// <summary>True when releasing <paramref name="amount"/> does not exceed what this goal holds from the account.</summary>
    public static bool CanRelease(long amount, long allocatedToGoalFromAccount) =>
        amount > 0 && amount <= allocatedToGoalFromAccount;
}
