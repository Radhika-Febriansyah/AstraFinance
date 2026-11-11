using AstraFinance.App.Services;
using AstraFinance.App.ViewModels;
using AstraFinance.App.ViewModels.Dialogs;
using AstraFinance.App.ViewModels.Gate;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.App.Views;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Localization;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Security;
using AstraFinance.Core.Services;
using AstraFinance.Infrastructure.Backup;
using AstraFinance.Infrastructure.Database;
using AstraFinance.Infrastructure.Logging;
using AstraFinance.Infrastructure.Preferences;
using AstraFinance.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AstraFinance.App;

/// <summary>Dependency-injection configuration for the whole application.</summary>
public static class CompositionRoot
{
    public static ServiceProvider Build(IAppPaths paths)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, paths);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public static void ConfigureServices(IServiceCollection services, IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // Logging: local file only. Never log PINs or financial content.
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);

            // EF Core command logging stays off: diagnostics must never carry query details.
            builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            builder.AddProvider(new FileLoggerProvider(paths.LogsDirectory));
        });

        // Core / infrastructure
        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ILocalizationService>(_ => new LocalizationService());
        services.AddSingleton<IPreferencesStore, JsonPreferencesStore>();
        services.AddSingleton<INotificationService, NotificationCenter>();
        services.AddSingleton<IPreferencesService, PreferencesService>();
        services.AddSingleton<ISystemThemeProvider, WindowsSystemThemeProvider>();
        services.AddSingleton<ThemeService>();

        // Database & financial services (SQLite via EF Core; short-lived contexts per operation).
        services.AddAstraDatabase(paths);
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<ICategoryService, CategoryService>();
        services.AddSingleton<IAccountService, AccountService>();
        services.AddSingleton<ITransactionService, TransactionService>();

        // Onboarding, security, regional format and backups.
        services.AddSingleton(new PinHasher());
        services.AddSingleton(PinAttemptPolicy.Default);
        services.AddSingleton<PinService>();
        services.AddSingleton<IPinService>(sp => sp.GetRequiredService<PinService>());
        services.AddSingleton<IPinSecuritySettings>(sp => sp.GetRequiredService<PinService>());
        services.AddSingleton<IOnboardingService, OnboardingService>();
        services.AddSingleton<IRegionalFormatService, RegionalFormatService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IBudgetService, BudgetService>();
        services.AddSingleton<ISavingsGoalService, SavingsGoalService>();
        services.AddSingleton<INotificationSettingsService, NotificationSettingsService>();
        services.AddSingleton<IBillService, BillService>();
        services.AddSingleton<IReminderService, ReminderService>();
        services.AddSingleton<IAnalyticsService, AnalyticsService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IReportExporter, AstraFinance.Infrastructure.Export.ReportExporter>();
        services.AddSingleton<IRestoreService, AstraFinance.Infrastructure.Backup.RestoreService>();
        services.AddSingleton<IDataExportService, AstraFinance.Infrastructure.Export.DataExportService>();
        services.AddSingleton<ITransactionImportService, AstraFinance.Infrastructure.Export.TransactionImportService>();
        services.AddSingleton<IWindowsNotifier, WindowsToastNotifier>();
        services.AddSingleton<ReminderScheduler>();

        // Global search: each module adds its own ISearchProvider as it is implemented.
        services.AddSingleton<ISearchProvider, PageSearchProvider>();
        services.AddSingleton<ISearchProvider, AccountSearchProvider>();
        services.AddSingleton<ISearchProvider, TransactionSearchProvider>();
        services.AddSingleton<ISearchProvider, CategorySearchProvider>();
        services.AddSingleton<ISearchProvider, BudgetSearchProvider>();
        services.AddSingleton<ISearchProvider, BillSearchProvider>();
        services.AddSingleton<ISearchProvider, SavingsGoalSearchProvider>();
        services.AddSingleton<IGlobalSearchService, GlobalSearchService>();

        // Shell services
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton(CreatePageRegistry());
        services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
        services.AddSingleton<IFileSavePicker, WindowsFileSavePicker>();
        services.AddSingleton<IFileOpenPicker, WindowsFileOpenPicker>();
        services.AddSingleton<AppSession>();
        services.AddSingleton<DataChangeNotifier>();

        // Forms and gates are created per use.
        AddFactory<AccountEditorViewModel>(services);
        AddFactory<BalanceAdjustmentViewModel>(services);
        AddFactory<TransactionFormViewModel>(services);
        AddFactory<BudgetFormViewModel>(services);
        AddFactory<GoalFormViewModel>(services);
        AddFactory<AllocationFormViewModel>(services);
        AddFactory<BillFormViewModel>(services);
        AddFactory<PayBillFormViewModel>(services);
        AddFactory<CategoryFormViewModel>(services);
        AddFactory<ChangePinFormViewModel>(services);
        AddFactory<ImportFormViewModel>(services);
        services.AddTransient<CategoriesSettingsViewModel>();
        services.AddTransient<SecuritySettingsViewModel>();
        services.AddTransient<DataSettingsViewModel>();
        AddFactory<OnboardingViewModel>(services);
        AddFactory<LockViewModel>(services);

        // View models & views
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<NotificationCenterViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
    }

    /// <summary>Page registrations: every page in <see cref="PageCatalog"/> has its real view model.</summary>
    public static PageRegistry CreatePageRegistry()
    {
        var registry = new PageRegistry()
            .Register(AppPage.Dashboard, sp => ActivatorUtilities.CreateInstance<DashboardViewModel>(sp))
            .Register(AppPage.Settings, sp => ActivatorUtilities.CreateInstance<SettingsViewModel>(sp))
            .Register(AppPage.Accounts, sp => ActivatorUtilities.CreateInstance<AccountsViewModel>(sp))
            .Register(AppPage.Transactions, sp => ActivatorUtilities.CreateInstance<TransactionsViewModel>(sp))
            .Register(AppPage.Calendar, sp => ActivatorUtilities.CreateInstance<CalendarViewModel>(sp))
            .Register(AppPage.Budgets, sp => ActivatorUtilities.CreateInstance<BudgetsViewModel>(sp))
            .Register(AppPage.SavingsGoals, sp => ActivatorUtilities.CreateInstance<SavingsGoalsViewModel>(sp))
            .Register(AppPage.Bills, sp => ActivatorUtilities.CreateInstance<BillsViewModel>(sp))
            .Register(AppPage.Analytics, sp => ActivatorUtilities.CreateInstance<AnalyticsViewModel>(sp))
            .Register(AppPage.Reports, sp => ActivatorUtilities.CreateInstance<ReportsViewModel>(sp));

        foreach (var descriptor in PageCatalog.All)
        {
            if (!registry.IsRegistered(descriptor.Page))
            {
                throw new InvalidOperationException($"No view model is registered for page {descriptor.Page}.");
            }
        }

        return registry;
    }

    private static void AddFactory<T>(IServiceCollection services)
        where T : class
    {
        services.AddTransient<T>();
        services.AddSingleton<Func<T>>(sp => () => sp.GetRequiredService<T>());
    }
}
