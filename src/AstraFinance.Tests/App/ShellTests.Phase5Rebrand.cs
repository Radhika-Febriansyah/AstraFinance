using AstraFinance.App.ViewModels;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AstraFinance.Tests.App;

/// <summary>Records which UI sounds were requested (nothing is played).</summary>
internal sealed class RecordingSoundPlayer : IUiSoundPlayer
{
    public List<UiSound> Played { get; } = [];

    public List<UiSound> Previewed { get; } = [];

    public void Play(UiSound sound) => Played.Add(sound);

    public void Preview(UiSound sound) => Previewed.Add(sound);
}

public sealed partial class ShellTests
{
    [Fact]
    public void Notification_cards_put_the_newest_first_keep_errors_and_chime_only_for_important_ones()
    {
        var sounds = new RecordingSoundPlayer();
        var service = new NotificationCenter(new FakeClock());
        using var vm = new NotificationCenterViewModel(service, _services.GetRequiredService<ILocalizationService>(), sounds);

        service.Notify(NotificationKind.Error, "Restore failed", "The backup could not be read.");
        service.Notify(NotificationKind.Success, "Saved", "Lunch");
        service.Notify(NotificationKind.Warning, "Budget warning", "Food & Drinks is at 90 %.");

        Assert.Equal(["Budget warning", "Saved", "Restore failed"], vm.Toasts.Select(t => t.Title));
        Assert.True(vm.Toasts.Single(t => t.Kind == NotificationKind.Error).IsSticky);
        Assert.False(vm.Toasts.Single(t => t.Kind == NotificationKind.Success).IsSticky);
        Assert.Equal([UiSound.ImportantNotice, UiSound.ImportantNotice], sounds.Played); // error + warning, not success

        // A fourth card pushes out the oldest card that would close by itself, never the error.
        service.Notify(NotificationKind.Info, "Backup", "Done");
        Assert.Equal(NotificationCenterViewModel.MaxToasts, vm.Toasts.Count);
        Assert.Contains(vm.Toasts, t => t.Kind == NotificationKind.Error);
        Assert.DoesNotContain(vm.Toasts, t => t.Title == "Saved");
    }

    [Fact]
    public async Task Sound_settings_persist_and_preview_needs_the_master_switch()
    {
        await StartUnlockedAsync();
        var sounds = new RecordingSoundPlayer();
        using var settings = new SoundSettingsViewModel(_services.GetRequiredService<IPreferencesService>(), sounds);

        Assert.False(settings.SoundsEnabled); // off by default
        Assert.False(settings.PreviewCommand.CanExecute(UiSoundCategory.Pin));

        settings.SoundsEnabled = true;
        await settings.LastSave;
        settings.NotificationSounds = false;
        await settings.LastSave;
        settings.Volume = 35;
        await settings.PreviewCommand.ExecuteAsync(UiSoundCategory.Notifications); // flushes the pending volume first

        Assert.True(_store.Saved!.SoundsEnabled);
        Assert.False(_store.Saved.NotificationSounds);
        Assert.Equal(35, _store.Saved.SoundVolume);
        Assert.Equal([UiSound.ImportantNotice], sounds.Previewed); // preview ignores the category switch
    }
}
