using AstraFinance.App;
using AstraFinance.App.Services;
using AstraFinance.App.ViewModels;
using AstraFinance.App.ViewModels.Gate;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Localization;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Security;
using AstraFinance.Core.Services;
using AstraFinance.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AstraFinance.Tests.App;

/// <summary>Shell and page view models wired through the real composition root and a real database.</summary>
public sealed partial class ShellTests : IAsyncLifetime, IDisposable
{
    internal const string TestPin = "2468";

    private readonly TempDirectory _dir = new();
    private readonly ServiceProvider _services;
    private readonly InMemoryPreferencesStore _store = new();

    internal FakeFileSavePicker SavePicker { get; } = new();

    internal FakeWindowsNotifier WindowsNotifier { get; } = new();

    internal FakeFileOpenPicker OpenPicker { get; } = new();

    public ShellTests()
    {
        var collection = new ServiceCollection();
        CompositionRoot.ConfigureServices(collection, new AppPaths(_dir.Path));

        // Replace OS/disk dependencies with test doubles.
        collection.AddSingleton<IPreferencesStore>(_store);
        collection.AddSingleton<ISystemThemeProvider>(new FakeSystemTheme());
        collection.AddSingleton<IClock>(new FakeClock());
        collection.AddSingleton<ILocalizationService>(new LocalizationService(AppLanguage.English));
        collection.AddSingleton(new PinHasher(1_000)); // fast hashing for tests only
        collection.AddSingleton<IFolderPicker>(new FakeFolderPicker());
        collection.AddSingleton<IFileSavePicker>(SavePicker);
        collection.AddSingleton<IFileOpenPicker>(OpenPicker);
        collection.AddSingleton<IWindowsNotifier>(WindowsNotifier); // never show real Windows notifications from tests
        _services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public async Task InitializeAsync()
    {
        // Mirror app startup: preferences are loaded first (English for readable assertions).
        _store.LoadResult = new PreferencesLoadResult(UserPreferences.Default with { Language = AppLanguage.English }, false);
        await _services.GetRequiredService<IPreferencesService>().InitializeAsync();
        await _services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        await _services.GetRequiredService<IRegionalFormatService>().LoadAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    internal T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    /// <summary>Completes setup directly through the services (account + PIN + completed flag).</summary>
    private async Task CompleteSetupAsync()
    {
        var account = await Get<IAccountService>().CreateAsync(new AccountDraft("Cash", AccountType.Cash, 100_000, "Icon.Banknote", "#F4806B"));
        await Get<IOnboardingService>().SetFirstAccountAsync(account.Id);
        await Get<IPinService>().SetPinAsync(TestPin);
        await Get<IOnboardingService>().CompleteAsync();
    }

    /// <summary>Starts the shell and unlocks it with the test PIN.</summary>
    private async Task StartUnlockedAsync()
    {
        await CompleteSetupAsync();
        await Shell.InitializeAsync();
        var gate = Assert.IsType<LockViewModel>(Shell.Gate);
        gate.Pin = TestPin;
        await gate.UnlockCommand.ExecuteAsync(null);
        Assert.Null(Shell.Gate);
    }

    private ShellViewModel Shell => _services.GetRequiredService<ShellViewModel>();

    private INavigationService Navigation => _services.GetRequiredService<INavigationService>();

    [Fact]
    public void Every_page_resolves_to_a_view_model()
    {
        var registry = _services.GetRequiredService<PageRegistry>();
        foreach (var page in PageCatalog.All)
        {
            using var vm = registry.Create(page.Page, _services);
            Assert.Equal(page.Page, vm.Page);
            Assert.False(string.IsNullOrWhiteSpace(vm.Title));
        }
    }

    [Fact]
    public async Task First_start_shows_onboarding_and_not_the_dashboard()
    {
        await Shell.InitializeAsync();

        Assert.IsType<OnboardingViewModel>(Shell.Gate);
        Assert.True(Shell.IsGated);
        Assert.Null(Shell.CurrentPage);
        Assert.False(Shell.NavigateCommand.CanExecute(AppPage.Dashboard));
        Assert.False(Shell.ToggleSidebarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Startup_after_setup_requires_pin_then_shows_dashboard()
    {
        await CompleteSetupAsync();
        await Shell.InitializeAsync();

        var gate = Assert.IsType<LockViewModel>(Shell.Gate);
        Assert.Null(Shell.CurrentPage);

        gate.Pin = "1111";
        await gate.UnlockCommand.ExecuteAsync(null);
        Assert.Same(gate, Shell.Gate);
        Assert.Contains("4 attempt", gate.ErrorMessage, StringComparison.Ordinal);

        gate.Pin = TestPin;
        await gate.UnlockCommand.ExecuteAsync(null);

        Assert.IsType<DashboardViewModel>(Shell.CurrentPage);
        Assert.Equal(AppPage.Dashboard, Shell.SelectedPrimaryItem?.Page);
        Assert.Null(Shell.SelectedFooterItem);
        Assert.Equal("Dashboard", Shell.PageTitle);
    }

    [Fact]
    public async Task Manual_lock_hides_the_shell_until_the_pin_is_entered()
    {
        await StartUnlockedAsync();
        var page = Shell.CurrentPage;

        await Shell.LockCommand.ExecuteAsync(null);
        var gate = Assert.IsType<LockViewModel>(Shell.Gate);

        gate.Pin = TestPin;
        await gate.UnlockCommand.ExecuteAsync(null);
        Assert.Null(Shell.Gate);
        Assert.Same(page, Shell.CurrentPage);
    }

    [Fact]
    public async Task Selecting_a_nav_item_navigates_and_disposes_previous_page()
    {
        await StartUnlockedAsync();
        var dashboard = Shell.CurrentPage!;
        var disposed = false;
        dashboard.PropertyChanged += (_, _) => { };

        Shell.SelectedFooterItem = Shell.FooterItems.Single();
        await Task.Yield();

        Assert.IsType<SettingsViewModel>(Shell.CurrentPage);
        Assert.Null(Shell.SelectedPrimaryItem);

        // A disposed page no longer reacts to language changes.
        dashboard.PropertyChanged += (_, _) => disposed = true;
        _services.GetRequiredService<ILocalizationService>().SetLanguage(AppLanguage.Indonesian);
        Assert.False(disposed);
        Assert.Equal("Pengaturan", Shell.PageTitle);
    }

    [Fact]
    public async Task Every_page_opens_its_real_view_model()
    {
        foreach (var descriptor in PageCatalog.All)
        {
            await Navigation.NavigateAsync(descriptor.Page);
            var page = Navigation.CurrentPage!;
            Assert.Equal(descriptor.Page, page.Page);
            Assert.DoesNotContain("Upcoming", page.GetType().Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Navigating_to_the_current_page_is_a_no_op()
    {
        await Navigation.NavigateAsync(AppPage.Reports);
        var first = Navigation.CurrentPage;
        await Navigation.NavigateAsync(AppPage.Reports);
        Assert.Same(first, Navigation.CurrentPage);
        Assert.IsType<ReportsViewModel>(first);
    }

    [Fact]
    public async Task Toggling_sidebar_on_wide_window_persists_preference()
    {
        Shell.UpdateLayoutWidth(1400);
        Assert.True(Shell.IsSidebarOpen);
        Assert.Equal(ShellViewModel.SidebarExpandedWidth, Shell.SidebarLayoutWidth);

        await Shell.ToggleSidebarAsync();

        Assert.False(Shell.IsSidebarOpen);
        Assert.Equal(ShellViewModel.SidebarCollapsedWidth, Shell.SidebarVisualWidth);
        Assert.False(_store.Saved!.IsSidebarExpanded);
    }

    [Fact]
    public async Task Narrow_window_collapses_sidebar_and_toggle_opens_overlay_without_persisting()
    {
        Shell.UpdateLayoutWidth(900);
        Assert.True(Shell.IsNarrow);
        Assert.False(Shell.IsCompact);
        Assert.False(Shell.IsSidebarOpen);

        await Shell.ToggleSidebarAsync();

        Assert.True(Shell.IsSidebarOverlay);
        Assert.Equal(ShellViewModel.SidebarExpandedWidth, Shell.SidebarVisualWidth);
        Assert.Equal(ShellViewModel.SidebarCollapsedWidth, Shell.SidebarLayoutWidth);
        Assert.Equal(0, _store.SaveCount);

        await Shell.NavigateAsync(AppPage.Reports);
        Assert.False(Shell.IsSidebarOverlay);

        Shell.UpdateLayoutWidth(700);
        Assert.True(Shell.IsCompact);
    }

    [Fact]
    public async Task Theme_and_language_commands_update_preferences()
    {
        await Shell.SetThemeCommand.ExecuteAsync(AppThemeMode.Dark);
        await Shell.SetLanguageCommand.ExecuteAsync(AppLanguage.Indonesian);

        Assert.True(Shell.IsThemeDark);
        Assert.True(Shell.IsLanguageIndonesian);
        Assert.Equal(AppThemeMode.Dark, _store.Saved!.ThemeMode);
        Assert.Equal(AppLanguage.Indonesian, _store.Saved.Language);
    }

    [Fact]
    public async Task Search_shows_grouped_results_and_opens_highlighted_result()
    {
        var search = Shell.Search;
        search.Query = "rep";
        await search.PendingSearch;

        Assert.True(search.IsOpen);
        var group = Assert.Single(search.Groups);
        Assert.Equal("Pages", group.Header);
        Assert.Equal("Reports", search.Highlighted?.Title);

        await search.OpenHighlightedAsync();

        Assert.False(search.IsOpen);
        Assert.Equal(string.Empty, search.Query);
        Assert.Equal(AppPage.Reports, Navigation.CurrentPage?.Page);
    }

    [Fact]
    public async Task Search_highlight_wraps_and_reports_no_results()
    {
        var search = Shell.Search;
        search.Query = "a";
        await search.PendingSearch;
        var count = search.Groups.Sum(g => g.Items.Count);
        Assert.True(count > 1);

        search.MoveHighlight(-1);
        Assert.Same(search.Groups[^1].Items[^1], search.Highlighted);
        search.MoveHighlight(1);
        Assert.Same(search.Groups[0].Items[0], search.Highlighted);

        search.Query = "zzzz";
        await search.PendingSearch;
        Assert.True(search.HasNoResults);
        Assert.Contains("zzzz", search.NoResultsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dialog_service_shows_one_dialog_and_returns_result()
    {
        var dialogs = _services.GetRequiredService<DialogService>();
        var pending = dialogs.ConfirmAsync("Delete?", "Really?", "Delete", isDestructive: true);

        Assert.NotNull(dialogs.ActiveDialog);
        Assert.True(dialogs.ActiveDialog!.IsDestructive);
        Assert.Equal("Cancel", dialogs.ActiveDialog.SecondaryText);

        dialogs.ActiveDialog.AcceptCommand.Execute(null);
        Assert.True(await pending);
        Assert.Null(dialogs.ActiveDialog);
    }

    [Fact]
    public void Notification_center_view_model_tracks_badge()
    {
        var vm = _services.GetRequiredService<NotificationCenterViewModel>();
        var service = _services.GetRequiredService<INotificationService>();
        for (var i = 0; i < 12; i++)
        {
            service.Notify(NotificationKind.Success, "Saved", "ok");
        }

        Assert.Equal("9+", vm.UnreadBadge);
        Assert.Equal(NotificationCenterViewModel.MaxToasts, vm.Toasts.Count);

        vm.IsOpen = true;
        Assert.Null(vm.UnreadBadge);

        vm.ClearAll();
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void Dashboard_greeting_and_date_come_from_clock_and_language()
    {
        var registry = _services.GetRequiredService<PageRegistry>();
        using var dashboard = (DashboardViewModel)registry.Create(AppPage.Dashboard, _services);

        Assert.Equal("Good morning", dashboard.Greeting);
        Assert.Equal("Monday, November 9, 2026", dashboard.DateText);

        _services.GetRequiredService<ILocalizationService>().SetLanguage(AppLanguage.Indonesian);
        Assert.Equal("Selamat pagi", dashboard.Greeting);
        Assert.Equal("Senin, 09 November 2026", dashboard.DateText);
    }

    [Fact]
    public async Task Settings_choices_reflect_and_update_preferences()
    {
        await Navigation.NavigateAsync(AppPage.Settings);
        var settings = Assert.IsType<SettingsViewModel>(Navigation.CurrentPage);

        Assert.True(settings.ThemeOptions.Single(o => o.Value == AppThemeMode.System).IsSelected);
        settings.AccentOptions.Single(o => o.Value == AccentColor.Teal).IsSelected = true;
        settings.SelectedLanguage = settings.LanguageOptions.Single(o => o.Value == AppLanguage.Indonesian);
        await Task.Yield();

        Assert.Equal(AccentColor.Teal, _store.Saved!.Accent);
        Assert.Equal(AppLanguage.Indonesian, _store.Saved.Language);
        Assert.False(settings.AccentOptions.Single(o => o.Value == AccentColor.Coral).IsSelected);
        Assert.Equal("Bahasa Indonesia", settings.SelectedLanguage!.ToString());
        Assert.All(settings.AccentOptions, o => Assert.StartsWith("#", o.SwatchHex, StringComparison.Ordinal));
    }
}
