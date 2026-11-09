using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Services;

public static class ThemeResolver
{
    /// <summary>
    /// Resolves the theme to apply. When following the system and the system preference
    /// is unavailable, Light is used because it matches the Windows default.
    /// </summary>
    public static ResolvedTheme Resolve(AppThemeMode mode, ResolvedTheme? systemTheme) => mode switch
    {
        AppThemeMode.Light => ResolvedTheme.Light,
        AppThemeMode.Dark => ResolvedTheme.Dark,
        AppThemeMode.System => systemTheme ?? ResolvedTheme.Light,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
