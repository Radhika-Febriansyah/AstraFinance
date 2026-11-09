using AstraFinance.Core.Models;

namespace AstraFinance.Core.Interfaces;

public sealed class PreferencesChangedEventArgs(UserPreferences previous, UserPreferences current) : EventArgs
{
    public UserPreferences Previous { get; } = previous;

    public UserPreferences Current { get; } = current;
}

/// <summary>Holds the current <see cref="UserPreferences"/> and persists changes.</summary>
public interface IPreferencesService
{
    UserPreferences Current { get; }

    event EventHandler<PreferencesChangedEventArgs>? Changed;

    /// <summary>Loads persisted preferences. Returns the load result for first-run/recovery handling.</summary>
    Task<PreferencesLoadResult> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an update immediately (raising <see cref="Changed"/>) and then persists it.
    /// Returns false if persisting failed; the change still applies for the current session.
    /// </summary>
    Task<bool> UpdateAsync(Func<UserPreferences, UserPreferences> update, CancellationToken cancellationToken = default);
}
