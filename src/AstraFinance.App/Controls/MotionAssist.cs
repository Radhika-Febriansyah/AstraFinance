using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AstraFinance.App.Services;

namespace AstraFinance.App.Controls;

/// <summary>
/// Small, reusable entrance/exit motion for floating elements (notification cards). Durations go through
/// <see cref="MotionPreferences"/>, so everything is instant when Windows animations are turned off.
/// </summary>
public static class MotionAssist
{
    private static readonly Duration EnterDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly Duration LeaveDuration = new(TimeSpan.FromMilliseconds(180));
    private const double SlideOffset = 24;

    /// <summary>Fade in and slide in from the right when the element is loaded.</summary>
    public static readonly DependencyProperty AnimateEntranceProperty = DependencyProperty.RegisterAttached(
        "AnimateEntrance", typeof(bool), typeof(MotionAssist), new PropertyMetadata(false, OnAnimateEntranceChanged));

    /// <summary>When set to true, fade out and slide to the right (the owner removes the element afterwards).</summary>
    public static readonly DependencyProperty IsLeavingProperty = DependencyProperty.RegisterAttached(
        "IsLeaving", typeof(bool), typeof(MotionAssist), new PropertyMetadata(false, OnIsLeavingChanged));

    public static bool GetAnimateEntrance(DependencyObject element) => (bool)element.GetValue(AnimateEntranceProperty);

    public static void SetAnimateEntrance(DependencyObject element, bool value) => element.SetValue(AnimateEntranceProperty, value);

    public static bool GetIsLeaving(DependencyObject element) => (bool)element.GetValue(IsLeavingProperty);

    public static void SetIsLeaving(DependencyObject element, bool value) => element.SetValue(IsLeavingProperty, value);

    private static void OnAnimateEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is true)
        {
            element.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnLoaded;
        var duration = MotionPreferences.For(EnterDuration);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        Translate(element).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(SlideOffset, 0, duration) { EasingFunction = ease });
    }

    private static void OnIsLeavingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is true)
        {
            var duration = MotionPreferences.For(LeaveDuration);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
            Translate(element).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(SlideOffset, duration) { EasingFunction = ease });
        }
    }

    private static TranslateTransform Translate(UIElement element)
    {
        if (element.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }

        return translate;
    }
}
