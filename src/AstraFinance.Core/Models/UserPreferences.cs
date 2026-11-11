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

    public static UserPreferences Default { get; } = new();
}
