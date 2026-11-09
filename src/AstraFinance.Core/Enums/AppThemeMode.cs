namespace AstraFinance.Core.Enums;

/// <summary>Appearance mode selected by the user.</summary>
public enum AppThemeMode
{
    /// <summary>Follow the Windows "app mode" (light/dark) preference.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>The concrete theme actually applied to the UI.</summary>
public enum ResolvedTheme
{
    Light = 0,
    Dark = 1,
}
