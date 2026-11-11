using System.Buffers.Binary;
using AstraFinance.App.Services;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Models;
using AstraFinance.Core.Services;
using AstraFinance.Infrastructure.Preferences;
using AstraFinance.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstraFinance.Tests.App;

/// <summary>Notification timing, optional UI sounds and their preferences (rebranding phase 5).</summary>
public class UiFeedbackTests
{
    [Fact]
    public void Errors_stay_until_dismissed() =>
        Assert.Null(ToastTiming.DurationFor(NotificationKind.Error, "Restore failed", "Details"));

    [Fact]
    public void Card_duration_follows_length_and_type_within_bounds()
    {
        Assert.Equal(ToastTiming.Minimum, ToastTiming.DurationFor(NotificationKind.Success, "Saved", "ok"));

        var longText = new string('x', 500);
        Assert.Equal(ToastTiming.Maximum, ToastTiming.DurationFor(NotificationKind.Info, "Title", longText));

        const string message = "Your data was upgraded for this version. A safety copy was saved first.";
        var info = ToastTiming.DurationFor(NotificationKind.Info, "Data updated", message)!.Value;
        var warning = ToastTiming.DurationFor(NotificationKind.Warning, "Data updated", message)!.Value;
        Assert.True(info > ToastTiming.Minimum);
        Assert.True(warning > info);
    }

    [Fact]
    public void Master_switch_category_and_volume_decide_whether_a_sound_plays()
    {
        var on = UserPreferences.Default with { SoundsEnabled = true };

        Assert.False(UiSoundRules.IsAudible(UserPreferences.Default, UiSound.PinSuccess)); // off by default
        Assert.True(UiSoundRules.IsAudible(on, UiSound.PinSuccess));
        Assert.False(UiSoundRules.IsAudible(on with { PinSounds = false }, UiSound.PinError));
        Assert.True(UiSoundRules.IsAudible(on with { PinSounds = false }, UiSound.TransactionSaved));
        Assert.False(UiSoundRules.IsAudible(on with { NotificationSounds = false }, UiSound.ImportantNotice));
        Assert.False(UiSoundRules.IsAudible(on with { SoundVolume = 0 }, UiSound.ImportantNotice));
        Assert.Equal(100, UiSoundRules.Volume(on with { SoundVolume = 250 }));
        Assert.Equal(0, UiSoundRules.Volume(on with { SoundVolume = -5 }));
    }

    [Theory]
    [InlineData(UiSound.PinSuccess)]
    [InlineData(UiSound.PinError)]
    [InlineData(UiSound.ImportantNotice)]
    [InlineData(UiSound.TransactionSaved)]
    public void Tones_are_short_valid_wav_files_scaled_by_volume(UiSound sound)
    {
        var wav = ToneSynthesizer.Render(sound, 100);
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal("WAVE"u8.ToArray(), wav[8..12]);
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)));   // PCM
        Assert.Equal(ToneSynthesizer.SampleRate, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        var dataBytes = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40));
        Assert.Equal(wav.Length - 44, dataBytes);

        var seconds = dataBytes / 2.0 / ToneSynthesizer.SampleRate;
        Assert.InRange(seconds, 0.1, 0.5); // a short cue, never continuous audio

        var peak = Samples(wav).Max(s => Math.Abs((int)s));
        Assert.InRange(peak, 1, (int)(short.MaxValue * 0.36));
        Assert.All(Samples(ToneSynthesizer.Render(sound, 0)), s => Assert.Equal(0, s));
        Assert.True(Samples(ToneSynthesizer.Render(sound, 30)).Max(s => Math.Abs((int)s)) < peak);
    }

    [Fact]
    public async Task Preferences_from_version_1_0_load_with_sounds_off()
    {
        using var dir = new TempDirectory();
        var paths = new AppPaths(dir.Path);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.PreferencesFile,
            """{ "SchemaVersion": 1, "Language": "English", "ThemeMode": "System", "Accent": "Blue", "IsSidebarExpanded": true }""");
        using var store = new JsonPreferencesStore(paths, NullLogger<JsonPreferencesStore>.Instance);

        var result = await store.LoadAsync();

        Assert.False(result.WasRecovered);
        Assert.False(result.Preferences.SoundsEnabled);
        Assert.Equal(60, result.Preferences.SoundVolume);
        Assert.True(result.Preferences.PinSounds && result.Preferences.NotificationSounds && result.Preferences.TransactionSounds);
    }

    private static short[] Samples(byte[] wav)
    {
        var samples = new short[(wav.Length - 44) / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + i * 2));
        }

        return samples;
    }
}
