using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;

namespace AstraFinance.Core.Services;

/// <summary>Built-in category definition. Display names are localized via "Category.{Key}".</summary>
public sealed record DefaultCategory(string Key, CategoryKind Kind, string IconKey, string ColorHex);

public static class DefaultCategories
{
    /// <summary>Key of the expense category used for transfer fees by default.</summary>
    public const string FeesKey = "Fees";

    public static IReadOnlyList<DefaultCategory> All { get; } =
    [
        new("Food", CategoryKind.Expense, "Icon.Utensils", "#F4806B"),
        new("Transportation", CategoryKind.Expense, "Icon.Car", "#6AA6F2"),
        new("Entertainment", CategoryKind.Expense, "Icon.Film", "#A78BFA"),
        new("Shopping", CategoryKind.Expense, "Icon.ShoppingBag", "#F2B04A"),
        new("BillsUtilities", CategoryKind.Expense, "Icon.Zap", "#2EC4B0"),
        new("Health", CategoryKind.Expense, "Icon.HeartPulse", "#F07272"),
        new("Education", CategoryKind.Expense, "Icon.GraduationCap", "#5B8DEF"),
        new("Housing", CategoryKind.Expense, "Icon.Home", "#C08457"),
        new(FeesKey, CategoryKind.Expense, "Icon.Receipt", "#8A94A6"),
        new("OtherExpense", CategoryKind.Expense, "Icon.MoreHorizontal", "#8A94A6"),
        new("Salary", CategoryKind.Income, "Icon.Briefcase", "#3DBE8B"),
        new("Business", CategoryKind.Income, "Icon.Store", "#2EC4B0"),
        new("Investment", CategoryKind.Income, "Icon.TrendingUp", "#6AA6F2"),
        new("Gift", CategoryKind.Income, "Icon.Gift", "#F4806B"),
        new("OtherIncome", CategoryKind.Income, "Icon.MoreHorizontal", "#8A94A6"),
    ];

    /// <summary>Localized name for built-in categories, stored name for custom ones.</summary>
    public static string DisplayName(Category category, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(localization);
        return category.SystemKey is null ? category.Name : localization.GetString($"Category.{category.SystemKey}");
    }
}
