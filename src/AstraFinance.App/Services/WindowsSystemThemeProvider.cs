using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using Microsoft.Win32;

namespace AstraFinance.App.Services;

/// <summary>
/// Reads the Windows "Choose your app mode" setting
/// (HKCU\...\Themes\Personalize\AppsUseLightTheme) and listens for changes.
/// </summary>
public sealed class WindowsSystemThemeProvider : ISystemThemeProvider, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";

    public WindowsSystemThemeProvider()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler? SystemThemeChanged;

    public ResolvedTheme? GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightTheme) switch
            {
                int value => value == 0 ? ResolvedTheme.Dark : ResolvedTheme.Light,
                _ => null,
            };
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            SystemThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
