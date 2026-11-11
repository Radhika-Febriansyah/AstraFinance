using AstraFinance.Core.Enums;
using AstraFinance.Core.Models;

namespace AstraFinance.Core.Services;

/// <summary>When an in-app notification card closes by itself, and when it waits for the user.</summary>
public static class ToastTiming
{
    /// <summary>Shortest time any self-closing card stays visible.</summary>
    public static readonly TimeSpan Minimum = TimeSpan.FromSeconds(4);

    /// <summary>Longest time a self-closing card stays visible.</summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromSeconds(12);

    /// <summary>Reading time per character (about 15 characters per second).</summary>
    public static readonly TimeSpan PerCharacter = TimeSpan.FromMilliseconds(65);

    /// <summary>
    /// How long a card stays before closing by itself, or null when it must stay until the user dismisses it.
    /// Errors are critical and stay; warnings get extra time; everything scales with the text length.
    /// </summary>
    public static TimeSpan? DurationFor(NotificationKind kind, string? title, string? message)
    {
        if (kind == NotificationKind.Error)
        {
            return null;
        }

        var characters = (title?.Length ?? 0) + (message?.Length ?? 0);
        var reading = TimeSpan.FromTicks(PerCharacter.Ticks * characters);
        var extra = kind == NotificationKind.Warning ? TimeSpan.FromSeconds(3) : TimeSpan.Zero;
        var total = TimeSpan.FromSeconds(2) + reading + extra;
        return total < Minimum ? Minimum : total > Maximum ? Maximum : total;
    }
}

/// <summary>Whether an optional UI sound may play, and how loud.</summary>
public static class UiSoundRules
{
    public const int MaxVolume = 100;

    public static UiSoundCategory CategoryOf(UiSound sound) => sound switch
    {
        UiSound.PinSuccess or UiSound.PinError => UiSoundCategory.Pin,
        UiSound.ImportantNotice => UiSoundCategory.Notifications,
        _ => UiSoundCategory.Transactions,
    };

    public static bool IsCategoryEnabled(UserPreferences preferences, UiSoundCategory category)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return category switch
        {
            UiSoundCategory.Pin => preferences.PinSounds,
            UiSoundCategory.Notifications => preferences.NotificationSounds,
            _ => preferences.TransactionSounds,
        };
    }

    /// <summary>The master switch disables every sound; otherwise the sound's category must be on and the volume above zero.</summary>
    public static bool IsAudible(UserPreferences preferences, UiSound sound)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences.SoundsEnabled
            && Volume(preferences) > 0
            && IsCategoryEnabled(preferences, CategoryOf(sound));
    }

    /// <summary>The stored volume clamped to 0–100 (a hand-edited preferences file can't make sounds louder).</summary>
    public static int Volume(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return Math.Clamp(preferences.SoundVolume, 0, MaxVolume);
    }
}
