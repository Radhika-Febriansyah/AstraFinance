using AstraFinance.Core.Models;

namespace AstraFinance.Core.Interfaces;

/// <summary>Outcome of loading preferences.</summary>
/// <param name="Preferences">The loaded (or default) preferences.</param>
/// <param name="WasRecovered">True when an unreadable file was set aside and defaults were used.</param>
/// <param name="IsFirstRun">True when no preferences file existed yet.</param>
public sealed record PreferencesLoadResult(UserPreferences Preferences, bool WasRecovered, bool IsFirstRun = false);

/// <summary>Loads and persists <see cref="UserPreferences"/>.</summary>
public interface IPreferencesStore
{
    /// <summary>Loads preferences. Never throws for missing or corrupt files; returns defaults instead.</summary>
    Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Atomically persists preferences.</summary>
    Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
}
