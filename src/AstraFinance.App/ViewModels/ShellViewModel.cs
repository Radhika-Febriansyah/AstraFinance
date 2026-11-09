using System.Collections.ObjectModel;
using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Gate;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels;

/// <summary>
/// Main window shell: sidebar navigation, top bar (breadcrumb, search, notifications,
/// application menu, Quick Add) and responsive layout state.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    /// <summary>Below this window width the sidebar no longer takes layout space when expanded (it overlays).</summary>
    public const double NarrowBreakpoint = 1000;

    /// <summary>Below this window width the breadcrumb drops its "Astra Finance ›" prefix.</summary>
    public const double MediumBreakpoint = 1200;

    /// <summary>Below this window width the top bar switches to its compact form.</summary>
    public const double CompactBreakpoint = 760;

    public const double SidebarExpandedWidth = 240;
    public const double SidebarCollapsedWidth = 68;

    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly IPreferencesService _preferences;
    private readonly IOnboardingService _onboarding;
    private readonly IPinService _pin;
    private readonly IBackupService _backup;
    private readonly INotificationService _notificationService;
    private readonly ReminderScheduler _reminders;
    private readonly Func<OnboardingViewModel> _onboardingFactory;
    private readonly Func<LockViewModel> _lockFactory;
    private readonly Func<AstraFinance.App.ViewModels.Dialogs.TransactionFormViewModel> _transactionFormFactory;
    private bool _syncingSelection;

    public ShellViewModel(
        INavigationService navigation,
        ILocalizationService localization,
        IPreferencesService preferences,
        SearchViewModel search,
        NotificationCenterViewModel notifications,
        DialogService dialogs,
        IOnboardingService onboarding,
        IPinService pin,
        IBackupService backup,
        INotificationService notificationService,
        ReminderScheduler reminders,
        Func<OnboardingViewModel> onboardingFactory,
        Func<LockViewModel> lockFactory,
        Func<AstraFinance.App.ViewModels.Dialogs.TransactionFormViewModel> transactionFormFactory)
    {
        _transactionFormFactory = transactionFormFactory;
        _onboarding = onboarding;
        _pin = pin;
        _backup = backup;
        _notificationService = notificationService;
        _reminders = reminders;
        _onboardingFactory = onboardingFactory;
        _lockFactory = lockFactory;
        _navigation = navigation;
        _localization = localization;
        _preferences = preferences;
        Search = search;
        Notifications = notifications;
        Dialogs = dialogs;

        foreach (var descriptor in PageCatalog.All)
        {
            var item = new NavItemViewModel(descriptor, localization);
            (descriptor.IsFooterItem ? FooterItems : PrimaryItems).Add(item);
        }

        IsSidebarExpanded = preferences.Current.IsSidebarExpanded;

        _navigation.Navigated += OnNavigated;
        _localization.LanguageChanged += OnLanguageChanged;
        _preferences.Changed += OnPreferencesChanged;
    }

    public ObservableCollection<NavItemViewModel> PrimaryItems { get; } = [];

    public ObservableCollection<NavItemViewModel> FooterItems { get; } = [];

    public SearchViewModel Search { get; }

    public NotificationCenterViewModel Notifications { get; }

    public DialogService Dialogs { get; }

    public PageViewModel? CurrentPage => _navigation.CurrentPage;

    public string PageTitle => CurrentPage?.Title ?? string.Empty;

    [ObservableProperty]
    public partial NavItemViewModel? SelectedPrimaryItem { get; set; }

    [ObservableProperty]
    public partial NavItemViewModel? SelectedFooterItem { get; set; }

    /// <summary>The user's persisted sidebar preference.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarOpen), nameof(SidebarVisualWidth), nameof(SidebarLayoutWidth), nameof(IsSidebarOverlay), nameof(SidebarToggleTooltip))]
    public partial bool IsSidebarExpanded { get; private set; }

    /// <summary>Temporary expansion while the window is narrow (overlay mode).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarOpen), nameof(SidebarVisualWidth), nameof(SidebarLayoutWidth), nameof(IsSidebarOverlay), nameof(SidebarToggleTooltip))]
    public partial bool IsOverlayOpen { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarOpen), nameof(SidebarVisualWidth), nameof(SidebarLayoutWidth), nameof(IsSidebarOverlay), nameof(SidebarToggleTooltip))]
    public partial bool IsNarrow { get; private set; }

    [ObservableProperty]
    public partial bool IsCompact { get; private set; }

    [ObservableProperty]
    public partial bool IsMedium { get; private set; }

    /// <summary>True when labels are visible in the sidebar.</summary>
    public bool IsSidebarOpen => IsNarrow ? IsOverlayOpen : IsSidebarExpanded;

    public bool IsSidebarOverlay => IsNarrow && IsOverlayOpen;

    public double SidebarVisualWidth => IsSidebarOpen ? SidebarExpandedWidth : SidebarCollapsedWidth;

    public double SidebarLayoutWidth => IsNarrow ? SidebarCollapsedWidth : SidebarVisualWidth;

    public string SidebarToggleTooltip => _localization.GetString(IsSidebarOpen ? "Shell.Sidebar.Collapse" : "Shell.Sidebar.Expand");

    public AppThemeMode ThemeMode => _preferences.Current.ThemeMode;

    public AppLanguage Language => _preferences.Current.Language;

    public bool IsThemeLight => ThemeMode == AppThemeMode.Light;

    public bool IsThemeDark => ThemeMode == AppThemeMode.Dark;

    public bool IsThemeSystem => ThemeMode == AppThemeMode.System;

    public bool IsLanguageEnglish => Language == AppLanguage.English;

    public bool IsLanguageIndonesian => Language == AppLanguage.Indonesian;

    /// <summary>
    /// Full-window gate (onboarding wizard or PIN lock). While set, the shell is hidden and
    /// navigation/shortcuts are disabled, so the Dashboard can't be reached before setup or unlock.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGated))]
    [NotifyCanExecuteChangedFor(nameof(ToggleSidebarCommand), nameof(NavigateCommand), nameof(LockCommand), nameof(BackUpNowCommand), nameof(QuickAddCommand))]
    public partial object? Gate { get; private set; }

    public bool IsGated => Gate is not null;

    /// <summary>Startup: PIN lock (if a PIN exists), then onboarding (if unfinished), then the Dashboard.</summary>
    public async Task InitializeAsync()
    {
        if (await _pin.HasPinAsync().ConfigureAwait(true))
        {
            await ShowLockAsync(ProceedAfterUnlockAsync).ConfigureAwait(true);
        }
        else
        {
            await ProceedAfterUnlockAsync().ConfigureAwait(true);
        }
    }

    private bool CanUseShell() => !IsGated;

    [RelayCommand(CanExecute = nameof(CanUseShell))]
    public async Task LockAsync()
    {
        if (!await _pin.HasPinAsync().ConfigureAwait(true))
        {
            return;
        }

        Search.Clear();
        Notifications.IsOpen = false;
        IsOverlayOpen = false;
        _reminders.Stop(); // nothing is announced while the app is locked
        await ShowLockAsync(() =>
        {
            SetGate(null);
            return _reminders.StartAsync();
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanUseShell))]
    public async Task BackUpNowAsync()
    {
        try
        {
            var file = await _backup.CreateBackupAsync(isAutomatic: false).ConfigureAwait(true);
            _notificationService.Notify(NotificationKind.Success, _localization.GetString("Backup.Created.Title"), file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            _notificationService.Notify(NotificationKind.Error, _localization.GetString("Backup.Failed.Title"), _localization.GetString("Backup.Failed.Body"));
        }
    }

    private async Task ShowLockAsync(Func<Task> onUnlocked)
    {
        var vm = _lockFactory();
        await vm.InitializeAsync(onUnlocked).ConfigureAwait(true);
        SetGate(vm);
    }

    private async Task ProceedAfterUnlockAsync()
    {
        var state = await _onboarding.GetStateAsync().ConfigureAwait(true);
        if (!state.IsCompleted)
        {
            var wizard = _onboardingFactory();
            await wizard.InitializeAsync(state, ProceedAfterUnlockAsync).ConfigureAwait(true);
            SetGate(wizard);
            return;
        }

        SetGate(null);
        await _navigation.NavigateAsync(PageCatalog.StartupPage).ConfigureAwait(true);
        _ = RunScheduledBackupAsync();
        _ = _reminders.StartAsync();
    }

    private void SetGate(object? gate)
    {
        if (Gate is IDisposable old && !ReferenceEquals(old, gate))
        {
            old.Dispose();
        }

        Gate = gate;
    }

    private async Task RunScheduledBackupAsync()
    {
        try
        {
            await _backup.RunScheduledBackupIfDueAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            _notificationService.Notify(NotificationKind.Warning, _localization.GetString("Backup.Failed.Title"), _localization.GetString("Backup.Failed.Body"));
        }
    }

    /// <summary>Called by the view whenever the window is resized.</summary>
    public void UpdateLayoutWidth(double width)
    {
        IsNarrow = width < NarrowBreakpoint;
        IsCompact = width < CompactBreakpoint;
        IsMedium = width < MediumBreakpoint;
        if (!IsNarrow)
        {
            IsOverlayOpen = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseShell))]
    public async Task ToggleSidebarAsync()
    {
        if (IsNarrow)
        {
            IsOverlayOpen = !IsOverlayOpen;
            return;
        }

        IsSidebarExpanded = !IsSidebarExpanded;
        var expanded = IsSidebarExpanded;
        await _preferences.UpdateAsync(p => p with { IsSidebarExpanded = expanded }).ConfigureAwait(true);
    }

    [RelayCommand]
    public void CloseOverlay() => IsOverlayOpen = false;

    [RelayCommand(CanExecute = nameof(CanUseShell))]
    public Task NavigateAsync(AppPage page)
    {
        IsOverlayOpen = false;
        return _navigation.NavigateAsync(page);
    }

    /// <summary>Quick Add Transaction (Ctrl+N): the simplified form, sharing validation and saving with the full form.</summary>
    [RelayCommand(CanExecute = nameof(CanUseShell))]
    public async Task QuickAddAsync()
    {
        if (Dialogs.ActiveDialog is not null)
        {
            return;
        }

        var form = _transactionFormFactory();
        await form.InitializeForCreateAsync(quick: true).ConfigureAwait(true);
        if (await Dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notificationService.Notify(
                NotificationKind.Success,
                _localization.GetString("Transactions.Saved.Title"),
                _localization.GetString($"TransactionType.{form.Saved!.Transaction.Type}"));
        }
    }

    public string QuickAddTooltip => _localization.GetString("Shell.QuickAdd.Tooltip");

    [RelayCommand]
    public Task ShowAboutAsync() => Dialogs.AlertAsync(
        NotificationKind.Info,
        _localization.GetString("About.Title"),
        _localization.Format("About.Summary", AppInfo.Version, AppInfo.Developer) + Environment.NewLine + Environment.NewLine
            + _localization.GetString("About.Privacy"));

    [RelayCommand]
    public Task SetThemeAsync(AppThemeMode mode) => _preferences.UpdateAsync(p => p with { ThemeMode = mode });

    [RelayCommand]
    public Task SetLanguageAsync(AppLanguage language) => _preferences.UpdateAsync(p => p with { Language = language });

    public void Dispose()
    {
        _navigation.Navigated -= OnNavigated;
        _localization.LanguageChanged -= OnLanguageChanged;
        _preferences.Changed -= OnPreferencesChanged;

        // Search and Notifications are DI-owned singletons; the container disposes them.
    }

    partial void OnSelectedPrimaryItemChanged(NavItemViewModel? value)
    {
        if (value is not null && !_syncingSelection)
        {
            _ = NavigateAsync(value.Page);
        }
    }

    partial void OnSelectedFooterItemChanged(NavItemViewModel? value)
    {
        if (value is not null && !_syncingSelection)
        {
            _ = NavigateAsync(value.Page);
        }
    }

    private void OnNavigated(object? sender, EventArgs e)
    {
        var page = CurrentPage?.Page;
        _syncingSelection = true;
        try
        {
            SelectedPrimaryItem = PrimaryItems.FirstOrDefault(i => i.Page == page);
            SelectedFooterItem = FooterItems.FirstOrDefault(i => i.Page == page);
        }
        finally
        {
            _syncingSelection = false;
        }

        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(PageTitle));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in PrimaryItems.Concat(FooterItems))
        {
            item.RefreshText();
        }

        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(SidebarToggleTooltip));
        OnPropertyChanged(nameof(QuickAddTooltip));
    }

    private void OnPreferencesChanged(object? sender, PreferencesChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsLanguageEnglish));
        OnPropertyChanged(nameof(IsLanguageIndonesian));
        if (e.Current.IsSidebarExpanded != IsSidebarExpanded)
        {
            IsSidebarExpanded = e.Current.IsSidebarExpanded;
        }
    }
}
