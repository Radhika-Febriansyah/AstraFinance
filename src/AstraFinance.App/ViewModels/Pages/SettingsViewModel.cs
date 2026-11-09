using System.Diagnostics;
using System.Globalization;
using AstraFinance.App.Services;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

public sealed record ThirdPartyComponent(string Name, string License, string Url);

/// <summary>A choice of how many days before a due date bill reminders start.</summary>
public sealed record ReminderDaysOption(int Days, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Settings: General (language), Appearance (theme + accent), Notifications (Phase 7) and About.
/// Remaining sections are added in their roadmap phases.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly IPreferencesService _preferences;
    private readonly ThemeService _theme;
    private readonly IAppPaths _paths;
    private readonly INotificationSettingsService _notificationSettings;
    private readonly IBudgetService _budgets;
    private readonly ReminderScheduler _reminders;
    private bool _loadingNotifications;

    public SettingsViewModel(
        ILocalizationService localization,
        IPreferencesService preferences,
        ThemeService theme,
        IAppPaths paths,
        INotificationSettingsService notificationSettings,
        IBudgetService budgets,
        ReminderScheduler reminders)
        : base(AppPage.Settings, localization)
    {
        _preferences = preferences;
        _theme = theme;
        _paths = paths;
        _notificationSettings = notificationSettings;
        _budgets = budgets;
        _reminders = reminders;
        BuildReminderDayOptions();

        LanguageOptions =
        [
            new(AppLanguage.Indonesian, () => Localization.GetString("Language.Indonesian"), null, SetLanguage),
            new(AppLanguage.English, () => Localization.GetString("Language.English"), null, SetLanguage),
        ];

        ThemeOptions =
        [
            new(AppThemeMode.Light, () => Localization.GetString("Theme.Light"), "Icon.Sun", SetTheme),
            new(AppThemeMode.Dark, () => Localization.GetString("Theme.Dark"), "Icon.Moon", SetTheme),
            new(AppThemeMode.System, () => Localization.GetString("Theme.System"), "Icon.Monitor", SetTheme),
        ];

        AccentOptions = Enum.GetValues<AccentColor>()
            .Select(a => new ChoiceOption<AccentColor>(a, () => Localization.GetString($"Accent.{a}"), null, SetAccent))
            .ToList();

        SyncFromPreferences();
        UpdateSwatches();

        _preferences.Changed += OnPreferencesChanged;
        _theme.ThemeApplied += OnThemeApplied;
    }

    public IReadOnlyList<ChoiceOption<AppLanguage>> LanguageOptions { get; }

    public IReadOnlyList<ChoiceOption<AppThemeMode>> ThemeOptions { get; }

    public IReadOnlyList<ChoiceOption<AccentColor>> AccentOptions { get; }

    // ---------------- Notifications ----------------

    public static IReadOnlyList<int> ReminderDayChoices { get; } = [0, 1, 2, 3, 5, 7, 14];

    [ObservableProperty]
    public partial IReadOnlyList<ReminderDaysOption> ReminderDayOptions { get; private set; } = [];

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; }

    [ObservableProperty]
    public partial bool BillReminders { get; set; }

    [ObservableProperty]
    public partial ReminderDaysOption? SelectedReminderDays { get; set; }

    [ObservableProperty]
    public partial bool BudgetWarnings { get; set; }

    [ObservableProperty]
    public partial bool WindowsNotifications { get; set; }

    /// <summary>Default budget warning threshold (percent); budgets without their own threshold use it.</summary>
    [ObservableProperty]
    public partial string BudgetThresholdText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? BudgetThresholdError { get; private set; }

    [ObservableProperty]
    public partial bool IsNotificationSaveFailed { get; private set; }

    /// <summary>The most recent save of notification preferences (tests await it).</summary>
    public Task LastSave { get; private set; } = Task.CompletedTask;

    public override async Task OnNavigatedToAsync(NavigationTarget target)
    {
        _loadingNotifications = true;
        try
        {
            var s = await _notificationSettings.GetAsync().ConfigureAwait(true);
            NotificationsEnabled = s.Enabled;
            BillReminders = s.BillReminders;
            BudgetWarnings = s.BudgetWarnings;
            WindowsNotifications = s.WindowsNotifications;
            SelectedReminderDays = ReminderDayOptions.FirstOrDefault(o => o.Days == s.BillReminderDays)
                                   ?? ReminderDayOptions.OrderBy(o => Math.Abs(o.Days - s.BillReminderDays)).First();
            BudgetThresholdText = (await _budgets.GetDefaultWarningThresholdAsync().ConfigureAwait(true)).ToString(CultureInfo.InvariantCulture);
            BudgetThresholdError = null;
        }
        finally
        {
            _loadingNotifications = false;
        }
    }

    [ObservableProperty]
    public partial ChoiceOption<AppLanguage>? SelectedLanguage { get; set; }

    public string AppName => Localization.GetString("App.Name");

    public string Version { get; } = AppInfo.Version;

    public string Developer { get; } = AppInfo.Developer;

    public string DataDirectory => _paths.DataDirectory;

    public IReadOnlyList<ThirdPartyComponent> ThirdPartyComponents { get; } =
    [
        new(".NET / WPF", "MIT", "https://github.com/dotnet/wpf"),
        new("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("Microsoft.Extensions.DependencyInjection / Logging", "MIT", "https://github.com/dotnet/runtime"),
        new("Entity Framework Core / Microsoft.Data.Sqlite", "MIT", "https://github.com/dotnet/efcore"),
        new("SQLitePCLRaw", "Apache-2.0", "https://github.com/ericsink/SQLitePCL.raw"),
        new("SQLite", "Public domain", "https://sqlite.org"),
        new("LiveCharts2", "MIT", "https://github.com/beto-rodriguez/LiveCharts2"),
        new("SkiaSharp", "MIT", "https://github.com/mono/SkiaSharp"),
        new("Lucide icon geometry", "ISC", "https://lucide.dev/license"),
    ];

    [RelayCommand]
    public void OpenDataFolder()
    {
        _paths.EnsureCreated();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.DataDirectory}\"") { UseShellExecute = true });
    }

    protected override void OnLanguageChanged()
    {
        foreach (var option in LanguageOptions)
        {
            option.RefreshText();
        }

        foreach (var option in ThemeOptions)
        {
            option.RefreshText();
        }

        foreach (var option in AccentOptions)
        {
            option.RefreshText();
        }

        var days = SelectedReminderDays?.Days;
        BuildReminderDayOptions();
        _loadingNotifications = true;
        SelectedReminderDays = ReminderDayOptions.FirstOrDefault(o => o.Days == days);
        _loadingNotifications = false;
        OnPropertyChanged(nameof(AppName));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preferences.Changed -= OnPreferencesChanged;
            _theme.ThemeApplied -= OnThemeApplied;
        }

        base.Dispose(disposing);
    }

    partial void OnSelectedLanguageChanged(ChoiceOption<AppLanguage>? value)
    {
        if (value is not null && value.Value != _preferences.Current.Language)
        {
            SetLanguage(value.Value);
        }
    }

    partial void OnNotificationsEnabledChanged(bool value) => SaveNotifications();

    partial void OnBillRemindersChanged(bool value) => SaveNotifications();

    partial void OnSelectedReminderDaysChanged(ReminderDaysOption? value) => SaveNotifications();

    partial void OnBudgetWarningsChanged(bool value) => SaveNotifications();

    partial void OnWindowsNotificationsChanged(bool value) => SaveNotifications();

    partial void OnBudgetThresholdTextChanged(string value)
    {
        if (_loadingNotifications)
        {
            return;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var percent) && percent is >= 1 and <= 100)
        {
            BudgetThresholdError = null;
            LastSave = SaveThresholdAsync(percent);
        }
        else
        {
            BudgetThresholdError = Localization.GetString("Validation.ThresholdOutOfRange");
        }
    }

    private void SaveNotifications()
    {
        if (_loadingNotifications || SelectedReminderDays is null)
        {
            return;
        }

        LastSave = SaveNotificationsAsync(new NotificationSettings(
            NotificationsEnabled, BillReminders, SelectedReminderDays.Days, BudgetWarnings, WindowsNotifications));
    }

    private async Task SaveNotificationsAsync(NotificationSettings settings)
    {
        try
        {
            await _notificationSettings.SaveAsync(settings).ConfigureAwait(true);
            IsNotificationSaveFailed = false;
            await _reminders.CheckNowAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            IsNotificationSaveFailed = true;
        }
    }

    private async Task SaveThresholdAsync(int percent)
    {
        try
        {
            await _budgets.SetDefaultWarningThresholdAsync(percent).ConfigureAwait(true);
            IsNotificationSaveFailed = false;
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            IsNotificationSaveFailed = true;
        }
    }

    private void BuildReminderDayOptions() =>
        ReminderDayOptions = ReminderDayChoices
            .Select(d => new ReminderDaysOption(d, d switch
            {
                0 => Localization.GetString("Settings.Reminders.OnDueDate"),
                1 => Localization.GetString("Settings.Reminders.OneDay"),
                _ => Localization.Format("Settings.Reminders.Days", d),
            }))
            .ToList();

    private void SetLanguage(AppLanguage language) =>
        _ = _preferences.UpdateAsync(p => p with { Language = language });

    private void SetTheme(AppThemeMode mode) =>
        _ = _preferences.UpdateAsync(p => p with { ThemeMode = mode });

    private void SetAccent(AccentColor accent) =>
        _ = _preferences.UpdateAsync(p => p with { Accent = accent });

    private void OnPreferencesChanged(object? sender, PreferencesChangedEventArgs e) => SyncFromPreferences();

    private void OnThemeApplied(object? sender, EventArgs e) => UpdateSwatches();

    private void SyncFromPreferences()
    {
        var prefs = _preferences.Current;
        foreach (var option in ThemeOptions)
        {
            option.SetSelectedSilently(option.Value == prefs.ThemeMode);
        }

        foreach (var option in AccentOptions)
        {
            option.SetSelectedSilently(option.Value == prefs.Accent);
        }

        var language = LanguageOptions.First(o => o.Value == prefs.Language);
        if (!ReferenceEquals(SelectedLanguage, language))
        {
            SelectedLanguage = language;
        }
    }

    private void UpdateSwatches()
    {
        foreach (var option in AccentOptions)
        {
            option.SwatchHex = AccentPalette.Get(option.Value, _theme.CurrentTheme).Accent.ToHex();
            option.RefreshText();
        }
    }
}
