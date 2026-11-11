using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Interfaces;

/// <summary>Plays short optional UI tones, following the user's sound preferences.</summary>
public interface IUiSoundPlayer
{
    /// <summary>Plays the sound if the master switch, its category and the volume allow it.</summary>
    void Play(UiSound sound);

    /// <summary>Plays the sound for the Settings preview when the master switch is on, even if its category is off.</summary>
    void Preview(UiSound sound);
}
