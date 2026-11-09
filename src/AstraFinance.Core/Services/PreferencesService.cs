using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;

namespace AstraFinance.Core.Services;

public sealed class PreferencesService : IPreferencesService
{
    private readonly IPreferencesStore _store;
    private readonly INotificationService _notifications;
    private readonly ILocalizationService _localization;

    public PreferencesService(IPreferencesStore store, INotificationService notifications, ILocalizationService localization)
    {
        _store = store;
        _notifications = notifications;
        _localization = localization;
    }

    public UserPreferences Current { get; private set; } = UserPreferences.Default;

    public event EventHandler<PreferencesChangedEventArgs>? Changed;

    public async Task<PreferencesLoadResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _store.LoadAsync(cancellationToken).ConfigureAwait(true);
        Apply(result.Preferences);
        return result;
    }

    public async Task<bool> UpdateAsync(Func<UserPreferences, UserPreferences> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var next = update(Current);
        if (next == Current)
        {
            return true;
        }

        Apply(next);

        try
        {
            await _store.SaveAsync(next, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Notify(
                NotificationKind.Error,
                _localization.GetString("Error.PreferencesSave.Title"),
                _localization.GetString("Error.PreferencesSave.Body"));
            return false;
        }
    }

    private void Apply(UserPreferences next)
    {
        var previous = Current;
        Current = next;

        // The UI language always follows the preference (single source of truth).
        _localization.SetLanguage(next.Language);
        if (previous != next)
        {
            Changed?.Invoke(this, new PreferencesChangedEventArgs(previous, next));
        }
    }
}
