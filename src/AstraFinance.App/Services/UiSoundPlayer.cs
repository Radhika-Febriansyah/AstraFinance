using System.IO;
using System.Media;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Services;

namespace AstraFinance.App.Services;

/// <summary>
/// Plays the generated UI tones through the Windows sound player (no audio dependency). Every call checks
/// the user's preferences; the master switch disables all sounds. Sound is cosmetic: failures are ignored.
/// </summary>
public sealed class UiSoundPlayer : IUiSoundPlayer, IDisposable
{
    private readonly IPreferencesService _preferences;
    private readonly Dictionary<(UiSound Sound, int Volume), byte[]> _cache = [];
    private SoundPlayer? _player;
    private MemoryStream? _stream;

    public UiSoundPlayer(IPreferencesService preferences)
    {
        _preferences = preferences;
    }

    public void Play(UiSound sound)
    {
        var prefs = _preferences.Current;
        if (UiSoundRules.IsAudible(prefs, sound))
        {
            PlayNow(sound, UiSoundRules.Volume(prefs));
        }
    }

    public void Preview(UiSound sound)
    {
        var prefs = _preferences.Current;
        var volume = UiSoundRules.Volume(prefs);
        if (prefs.SoundsEnabled && volume > 0)
        {
            PlayNow(sound, volume);
        }
    }

    public void Dispose() => Release();

    private void PlayNow(UiSound sound, int volume)
    {
        try
        {
            if (!_cache.TryGetValue((sound, volume), out var wav))
            {
                wav = ToneSynthesizer.Render(sound, volume);
                _cache[(sound, volume)] = wav;
            }

            Release();
            _stream = new MemoryStream(wav, writable: false);
            _player = new SoundPlayer(_stream);
            _player.Play(); // asynchronous; a new tone replaces one still playing
        }
#pragma warning disable CA1031 // A missing audio device or driver must never affect the app.
        catch (Exception)
#pragma warning restore CA1031
        {
            Release();
        }
    }

    private void Release()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        _stream?.Dispose();
        _stream = null;
    }
}
