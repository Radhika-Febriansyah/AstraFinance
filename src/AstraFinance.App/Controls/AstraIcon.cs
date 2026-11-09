using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AstraFinance.App.Controls;

/// <summary>
/// Renders a 24×24 Lucide-style stroke icon. The stroke scales with <see cref="FrameworkElement.Width"/>
/// so an 18px icon keeps the characteristic ~1.5px line weight.
/// Set <see cref="IconKey"/> to a resource key (for data binding) or <see cref="Data"/> directly.
/// </summary>
public sealed class AstraIcon : Control
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(AstraIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(AstraIcon), new PropertyMetadata(null, OnIconKeyChanged));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(AstraIcon), new PropertyMetadata(2d));

    public static readonly DependencyProperty IsFilledProperty = DependencyProperty.Register(
        nameof(IsFilled), typeof(bool), typeof(AstraIcon), new PropertyMetadata(false));

    static AstraIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AstraIcon), new FrameworkPropertyMetadata(typeof(AstraIcon)));
        FocusableProperty.OverrideMetadata(typeof(AstraIcon), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(AstraIcon), new FrameworkPropertyMetadata(false));
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    /// <summary>Stroke thickness in 24×24 icon units.</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    private static void OnIconKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (AstraIcon)d;
        if (e.NewValue is string key && !string.IsNullOrEmpty(key))
        {
            icon.SetResourceReference(DataProperty, key);
        }
        else
        {
            icon.ClearValue(DataProperty);
        }
    }
}
