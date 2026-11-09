using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Interfaces;

/// <summary>Reads the operating system's light/dark application theme preference.</summary>
public interface ISystemThemeProvider
{
    /// <summary>The current system theme, or null when it cannot be determined.</summary>
    ResolvedTheme? GetSystemTheme();

    /// <summary>Raised when the system preference may have changed.</summary>
    event EventHandler? SystemThemeChanged;
}
