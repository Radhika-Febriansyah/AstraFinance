using System.Windows;

namespace AstraFinance.App.Controls;

/// <summary>Attached properties used by Astra control templates.</summary>
public static class Assist
{
    /// <summary>Resource key of an icon geometry shown before the content.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Assist), new PropertyMetadata(null));

    /// <summary>Shows a loading spinner and blocks interaction while true.</summary>
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.RegisterAttached(
        "IsBusy", typeof(bool), typeof(Assist), new PropertyMetadata(false));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Assist), new PropertyMetadata(new CornerRadius(8)));

    /// <summary>Placeholder text shown by text inputs when empty.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Assist), new PropertyMetadata(null));

    /// <summary>Badge text displayed on an icon button (e.g. unread count). Hidden when null or empty.</summary>
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.RegisterAttached(
        "Badge", typeof(string), typeof(Assist), new PropertyMetadata(null));

    /// <summary>
    /// True for controls drawn on a strong fill or gradient (primary, danger): hover lightens and
    /// pressed darkens the fill instead of using the neutral surface overlays.
    /// </summary>
    public static readonly DependencyProperty IsOnFillProperty = DependencyProperty.RegisterAttached(
        "IsOnFill", typeof(bool), typeof(Assist), new PropertyMetadata(false));

    public static string? GetIcon(DependencyObject element) => (string?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string? value) => element.SetValue(IconProperty, value);

    public static bool GetIsBusy(DependencyObject element) => (bool)element.GetValue(IsBusyProperty);

    public static void SetIsBusy(DependencyObject element, bool value) => element.SetValue(IsBusyProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static string? GetPlaceholder(DependencyObject element) => (string?)element.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject element, string? value) => element.SetValue(PlaceholderProperty, value);

    public static string? GetBadge(DependencyObject element) => (string?)element.GetValue(BadgeProperty);

    public static void SetBadge(DependencyObject element, string? value) => element.SetValue(BadgeProperty, value);

    public static bool GetIsOnFill(DependencyObject element) => (bool)element.GetValue(IsOnFillProperty);

    public static void SetIsOnFill(DependencyObject element, bool value) => element.SetValue(IsOnFillProperty, value);
}
