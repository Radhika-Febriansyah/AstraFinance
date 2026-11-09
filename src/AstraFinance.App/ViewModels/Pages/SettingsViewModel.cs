using System.Diagnostics;
using AstraFinance.App.Services;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

public sealed record ThirdPartyComponent(string Name, string License, string Url);

/// <summary>
/// Settings. Phase 1 implements General → language, Appearance (theme + accent) and About.
/// Remaining sections are added in their roadmap phases.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly IPreferencesService _preferences;
    private readonly ThemeService _theme;
    private readonly IAppPaths _paths;

    public SettingsViewModel(ILocalizationService localization, IPreferencesService preferences, ThemeService theme, IAppPaths paths)
        : base(AppPage.Settings, localization)
    {
        _preferences = preferences;
        _theme = theme;
        _paths = paths;

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
