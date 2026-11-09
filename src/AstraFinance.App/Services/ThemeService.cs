using System.Windows;
using System.Windows.Media;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Services;

namespace AstraFinance.App.Services;

/// <summary>
/// Applies Light/Dark/Follow System appearance and the accent preset by swapping the color
/// dictionaries merged into Application.Resources. All UI colors are DynamicResource, so the
/// switch is instant and needs no restart.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private static readonly Uri LightUri = new("/AstraFinance;component/Themes/Colors.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("/AstraFinance;component/Themes/Colors.Dark.xaml", UriKind.Relative);

    private readonly ISystemThemeProvider _systemTheme;
    private readonly IPreferencesService _preferences;
    private ResourceDictionary? _host;
    private ResourceDictionary? _colors;
    private ResourceDictionary? _accent;

    public ThemeService(ISystemThemeProvider systemTheme, IPreferencesService preferences)
    {
        _systemTheme = systemTheme;
        _preferences = preferences;
        _systemTheme.SystemThemeChanged += OnSystemThemeChanged;
        _preferences.Changed += OnPreferencesChanged;
    }

    public ResolvedTheme CurrentTheme { get; private set; } = ResolvedTheme.Light;

    public AccentColor CurrentAccent { get; private set; } = AccentColor.Coral;

    /// <summary>Raised on the UI thread after a theme or accent change has been applied.</summary>
    public event EventHandler? ThemeApplied;

    /// <summary>Attaches to the application resources and applies the current preferences.</summary>
    public void Initialize(ResourceDictionary applicationResources)
    {
        _host = applicationResources;
        Apply(force: true);
    }

    public void Apply(bool force = false)
    {
        if (_host is null)
        {
            return;
        }

        var prefs = _preferences.Current;
        var theme = ThemeResolver.Resolve(prefs.ThemeMode, _systemTheme.GetSystemTheme());
        if (!force && theme == CurrentTheme && prefs.Accent == CurrentAccent)
        {
            return;
        }

        var colors = new ResourceDictionary { Source = theme == ResolvedTheme.Dark ? DarkUri : LightUri };
        var accent = BuildAccentDictionary(AccentPalette.Get(prefs.Accent, theme));

        // Color dictionaries go first; control styles reference them via DynamicResource.
        Replace(ref _colors, colors, initialIndex: 0);
        Replace(ref _accent, accent, initialIndex: 1);

        CurrentTheme = theme;
        CurrentAccent = prefs.Accent;
        ThemeApplied?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _systemTheme.SystemThemeChanged -= OnSystemThemeChanged;
        _preferences.Changed -= OnPreferencesChanged;
    }

    public static Color ToColor(RgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static ResourceDictionary BuildAccentDictionary(AccentSwatch swatch)
    {
        static SolidColorBrush Brush(RgbColor c)
        {
            var brush = new SolidColorBrush(ToColor(c));
            brush.Freeze();
            return brush;
        }

        return new ResourceDictionary
        {
            ["Color.Accent"] = ToColor(swatch.Accent),
            ["Brush.Accent"] = Brush(swatch.Accent),
            ["Brush.Accent.Hover"] = Brush(swatch.Hover),
            ["Brush.Accent.Pressed"] = Brush(swatch.Pressed),
            ["Brush.Accent.Subtle"] = Brush(swatch.Subtle),
            ["Brush.Accent.Text"] = Brush(swatch.Text),
            ["Brush.Accent.Foreground"] = Brush(swatch.Foreground),
            ["Brush.Focus"] = Brush(swatch.Text),
        };
    }

    /// <summary>Swaps a theme dictionary in place, or inserts it at <paramref name="initialIndex"/> the first time.</summary>
    private void Replace(ref ResourceDictionary? current, ResourceDictionary next, int initialIndex)
    {
        var merged = _host!.MergedDictionaries;
        var index = current is null ? -1 : merged.IndexOf(current);
        if (index >= 0)
        {
            merged[index] = next;
        }
        else
        {
            merged.Insert(Math.Min(initialIndex, merged.Count), next);
        }

        current = next;
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        if (_preferences.Current.ThemeMode != AppThemeMode.System)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() => Apply());
    }

    private void OnPreferencesChanged(object? sender, PreferencesChangedEventArgs e)
    {
        if (e.Previous.ThemeMode != e.Current.ThemeMode || e.Previous.Accent != e.Current.Accent)
        {
            Apply();
        }
    }
}
