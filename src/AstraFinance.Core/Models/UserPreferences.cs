using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Models;

/// <summary>
/// Non-financial UI preferences that must be available before the database is opened
/// (for example on the PIN screen). Financial and regional settings live in the database.
/// </summary>
public sealed record UserPreferences
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public AppLanguage Language { get; init; } = AppLanguage.Indonesian;

    public AppThemeMode ThemeMode { get; init; } = AppThemeMode.System;

    public AccentColor Accent { get; init; } = AccentColor.Blue;

    public bool IsSidebarExpanded { get; init; } = true;

    // Optional UI sounds (1.1). Off by default; older preference files load with these defaults.

    /// <summary>Master switch: when false, no UI sound plays.</summary>
    public bool SoundsEnabled { get; init; }

    /// <summary>Overall UI sound volume, 0–100.</summary>
    public int SoundVolume { get; init; } = 60;

    public bool PinSounds { get; init; } = true;

    public bool NotificationSounds { get; init; } = true;

    public bool TransactionSounds { get; init; } = true;

    public static UserPreferences Default { get; } = new();
}
