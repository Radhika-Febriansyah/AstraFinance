namespace AstraFinance.Core.Enums;

/// <summary>Kinds of user accounts. Values are persisted; never renumber.</summary>
public enum AccountType
{
    Cash = 0,
    BankAccount = 1,
    EWallet = 2,
    SavingsAccount = 3,
    Custom = 4,
}

/// <summary>Transaction types. Values are persisted; never renumber.</summary>
public enum TransactionType
{
    /// <summary>Money entering an account.</summary>
    Income = 0,

    /// <summary>Money leaving an account.</summary>
    Expense = 1,

    /// <summary>Money moved between two of the user's accounts. Never income or expense.</summary>
    Transfer = 2,
}

/// <summary>Whether a category classifies income or expenses.</summary>
public enum CategoryKind
{
    Income = 0,
    Expense = 1,
}

public enum BudgetPeriod
{
    Monthly = 0,
}

public enum BudgetStatus
{
    Normal = 0,
    Warning = 1,
    Exceeded = 2,
}

public enum SavingsGoalStatus
{
    Active = 0,
    Completed = 1,
    Archived = 2,
}

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}

/// <summary>Digit grouping / decimal separator conventions.</summary>
public enum NumberFormatStyle
{
    /// <summary>1.234.567,89 (Indonesian/European).</summary>
    DotGroupCommaDecimal = 0,

    /// <summary>1,234,567.89 (English).</summary>
    CommaGroupDotDecimal = 1,

    /// <summary>1 234 567,89.</summary>
    SpaceGroupCommaDecimal = 2,
}

/// <summary>Short date display conventions.</summary>
public enum DateFormatStyle
{
    /// <summary>31/12/2026.</summary>
    DayMonthYear = 0,

    /// <summary>12/31/2026.</summary>
    MonthDayYear = 1,

    /// <summary>2026-12-31.</summary>
    YearMonthDay = 2,
}
