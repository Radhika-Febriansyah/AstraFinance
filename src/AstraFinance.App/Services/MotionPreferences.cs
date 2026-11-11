using System.Windows;

namespace AstraFinance.App.Services;

/// <summary>
/// Follows the Windows "Animation effects" setting (Settings → Accessibility → Visual effects).
/// When the user has turned animations off, decorative motion completes instantly.
/// </summary>
public static class MotionPreferences
{
    /// <summary>True when Windows allows client-area animations.</summary>
    public static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    /// <summary>Returns <paramref name="duration"/>, or zero when the user prefers reduced motion.</summary>
    public static Duration For(Duration duration) => AnimationsEnabled ? duration : new Duration(TimeSpan.Zero);
}
