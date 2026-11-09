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
        services.AddSingleton<IPinService, PinService>();
        services.AddSingleton<IOnboardingService, OnboardingService>();
        services.AddSingleton<IRegionalFormatService, RegionalFormatService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IBudgetService, BudgetService>();
        services.AddSingleton<ISavingsGoalService, SavingsGoalService>();

        // Global search: each module adds its own ISearchProvider as it is implemented.
        services.AddSingleton<ISearchProvider, PageSearchProvider>();
        services.AddSingleton<ISearchProvider, AccountSearchProvider>();
        services.AddSingleton<ISearchProvider, TransactionSearchProvider>();
        services.AddSingleton<ISearchProvider, CategorySearchProvider>();
        services.AddSingleton<ISearchProvider, BudgetSearchProvider>();
        services.AddSingleton<ISearchProvider, SavingsGoalSearchProvider>();
        services.AddSingleton<IGlobalSearchService, GlobalSearchService>();

        // Shell services
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton(CreatePageRegistry());
        services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
        services.AddSingleton<IFileSavePicker, WindowsFileSavePicker>();
        services.AddSingleton<DataChangeNotifier>();

        // Forms and gates are created per use.
        AddFactory<AccountEditorViewModel>(services);
        AddFactory<BalanceAdjustmentViewModel>(services);
        AddFactory<TransactionFormViewModel>(services);
        AddFactory<BudgetFormViewModel>(services);
        AddFactory<GoalFormViewModel>(services);
        AddFactory<AllocationFormViewModel>(services);
        AddFactory<OnboardingViewModel>(services);
        AddFactory<LockViewModel>(services);

        // View models & views
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<NotificationCenterViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
    }

    /// <summary>
    /// Page registrations. Modules not yet implemented use <see cref="UpcomingModuleViewModel"/>;
    /// each roadmap phase replaces its entry with the real page view model.
    /// </summary>
    public static PageRegistry CreatePageRegistry()
    {
        var registry = new PageRegistry()
            .Register(AppPage.Dashboard, sp => ActivatorUtilities.CreateInstance<DashboardViewModel>(sp))
            .Register(AppPage.Settings, sp => ActivatorUtilities.CreateInstance<SettingsViewModel>(sp))
            .Register(AppPage.Accounts, sp => ActivatorUtilities.CreateInstance<AccountsViewModel>(sp))
            .Register(AppPage.Transactions, sp => ActivatorUtilities.CreateInstance<TransactionsViewModel>(sp))
            .Register(AppPage.Budgets, sp => ActivatorUtilities.CreateInstance<BudgetsViewModel>(sp))
            .Register(AppPage.SavingsGoals, sp => ActivatorUtilities.CreateInstance<SavingsGoalsViewModel>(sp));

        foreach (var descriptor in PageCatalog.All)
        {
            if (!registry.IsRegistered(descriptor.Page))
            {
                var page = descriptor.Page;
                registry.Register(page, sp => new UpcomingModuleViewModel(page, sp.GetRequiredService<ILocalizationService>()));
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
