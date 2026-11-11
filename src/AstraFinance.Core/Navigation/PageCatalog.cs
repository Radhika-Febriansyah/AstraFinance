namespace AstraFinance.Core.Navigation;

/// <summary>Single source of truth for the main navigation structure.</summary>
public static class PageCatalog
{
    public static IReadOnlyList<PageDescriptor> All { get; } =
    [
        new(AppPage.Dashboard, "Nav.Dashboard", "Page.Dashboard.Description", "Icon.Dashboard", 5),
        new(AppPage.Transactions, "Nav.Transactions", "Page.Transactions.Description", "Icon.Transactions", 4),
        new(AppPage.Calendar, "Nav.Calendar", "Page.Calendar.Description", "Icon.Calendar", 4),
        new(AppPage.Accounts, "Nav.Accounts", "Page.Accounts.Description", "Icon.Accounts", 3),
        new(AppPage.Budgets, "Nav.Budgets", "Page.Budgets.Description", "Icon.Budgets", 6),
        new(AppPage.SavingsGoals, "Nav.SavingsGoals", "Page.SavingsGoals.Description", "Icon.SavingsGoals", 6),
        new(AppPage.Bills, "Nav.Bills", "Page.Bills.Description", "Icon.Bills", 7),
        new(AppPage.Analytics, "Nav.Analytics", "Page.Analytics.Description", "Icon.Analytics", 8),
        new(AppPage.Reports, "Nav.Reports", "Page.Reports.Description", "Icon.Reports", 8),
        new(AppPage.Settings, "Nav.Settings", "Page.Settings.Description", "Icon.Settings", 9, IsFooterItem: true),
    ];

    /// <summary>The page shown on every application start.</summary>
    public const AppPage StartupPage = AppPage.Dashboard;

    public static PageDescriptor Get(AppPage page) =>
        All.FirstOrDefault(d => d.Page == page)
        ?? throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown page.");
}
