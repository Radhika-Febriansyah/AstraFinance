using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

/// <summary>
/// Settings → Notifications → Sounds: master switch, overall volume, one switch per sound category and a
/// preview for each. Stored in the local preferences file (no financial data involved).
/// </summary>
public sealed partial class SoundSettingsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan VolumeSaveDelay = TimeSpan.FromMilliseconds(250);

    private readonly IPreferencesService _preferences;
    private readonly IUiSoundPlayer _sounds;
    private readonly bool _loading;
    private CancellationTokenSource? _volumeSave;

    public SoundSettingsViewModel(IPreferencesService preferences, IUiSoundPlayer sounds)
    {
        _preferences = preferences;
        _sounds = sounds;

        _loading = true;
        var p = preferences.Current;
        SoundsEnabled = p.SoundsEnabled;
        Volume = UiSoundRules.Volume(p);
        PinSounds = p.PinSounds;
        NotificationSounds = p.NotificationSounds;
        TransactionSounds = p.TransactionSounds;
        _loading = false;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    public partial bool SoundsEnabled { get; set; }

    [ObservableProperty]
    public partial int Volume { get; set; }

    [ObservableProperty]
    public partial bool PinSounds { get; set; }

    [ObservableProperty]
    public partial bool NotificationSounds { get; set; }

    [ObservableProperty]
    public partial bool TransactionSounds { get; set; }

    /// <summary>The last save (for tests).</summary>
    public Task LastSave { get; private set; } = Task.CompletedTask;

    public void Dispose()
    {
        _volumeSave?.Cancel();
        _volumeSave?.Dispose();
    }

    /// <summary>Plays the category's sound (requires the master switch; ignores the category switch).</summary>
    [RelayCommand(CanExecute = nameof(SoundsEnabled))]
    private async Task PreviewAsync(UiSoundCategory category)
    {
        await FlushVolumeAsync().ConfigureAwait(true);
        _sounds.Preview(category switch
        {
            UiSoundCategory.Pin => UiSound.PinSuccess,
            UiSoundCategory.Notifications => UiSound.ImportantNotice,
            _ => UiSound.TransactionSaved,
        });
    }

    partial void OnSoundsEnabledChanged(bool value) => Save(p => p with { SoundsEnabled = value });

    partial void OnPinSoundsChanged(bool value) => Save(p => p with { PinSounds = value });

    partial void OnNotificationSoundsChanged(bool value) => Save(p => p with { NotificationSounds = value });

    partial void OnTransactionSoundsChanged(bool value) => Save(p => p with { TransactionSounds = value });

    /// <summary>The slider moves in many small steps: save once it settles.</summary>
    partial void OnVolumeChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        _volumeSave?.Cancel();
        _volumeSave?.Dispose();
        _volumeSave = new CancellationTokenSource();
        _ = SaveVolumeLaterAsync(_volumeSave.Token);
    }

    private async Task SaveVolumeLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(VolumeSaveDelay, token).ConfigureAwait(true);
            await FlushVolumeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task FlushVolumeAsync()
    {
        var volume = Math.Clamp(Volume, 0, UiSoundRules.MaxVolume);
        return _preferences.Current.SoundVolume == volume ? Task.CompletedTask : SaveAsync(p => p with { SoundVolume = volume });
    }

    private void Save(Func<UserPreferences, UserPreferences> update)
    {
        if (!_loading)
        {
            _ = SaveAsync(update);
        }
    }

    private Task SaveAsync(Func<UserPreferences, UserPreferences> update)
    {
        LastSave = _preferences.UpdateAsync(update);
        return LastSave;
    }
}
