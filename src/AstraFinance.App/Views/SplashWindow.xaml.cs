using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using AstraFinance.App.Interop;
using AstraFinance.App.Localization;
using AstraFinance.App.Services;

namespace AstraFinance.App.Views;

/// <summary>
/// Startup splash. Pure presentation: App shows it while the database opens and the shell initializes,
/// then closes it. Animations follow the Windows "Animation effects" setting.
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>Shortest time the splash stays up, so a fast start doesn't flash it.</summary>
    private static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(500);

    private readonly Stopwatch _shown = new();

    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"{LocalizationSource.Instance["About.Version"]} {AppInfo.Version}";
        SourceInitialized += (_, _) => DwmTitleBar.RoundCorners(this);
        Loaded += (_, _) => OnLoaded();
    }

    private void OnLoaded()
    {
        _shown.Start();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionPreferences.For(new Duration(TimeSpan.FromMilliseconds(260)))));

        if (MotionPreferences.AnimationsEnabled)
        {
            var sweep = new DoubleAnimation(-40, 120, new Duration(TimeSpan.FromMilliseconds(1100)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            RunnerOffset.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, sweep);
        }
        else
        {
            // Reduced motion: a static centered segment instead of the sweep.
            RunnerOffset.X = 40;
        }
    }

    /// <summary>Fades the splash out (after the short minimum display time) and closes it.</summary>
    public async Task CloseGracefullyAsync()
    {
        var remaining = MinimumVisible - _shown.Elapsed;
        if (_shown.IsRunning && remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining).ConfigureAwait(true);
        }

        var duration = MotionPreferences.For(new Duration(TimeSpan.FromMilliseconds(180)));
        if (duration.TimeSpan > TimeSpan.Zero)
        {
            var fade = new DoubleAnimation(0, duration);
            var done = new TaskCompletionSource();
            fade.Completed += (_, _) => done.TrySetResult();
            BeginAnimation(OpacityProperty, fade);
            await done.Task.ConfigureAwait(true);
        }

        Close();
    }
}
