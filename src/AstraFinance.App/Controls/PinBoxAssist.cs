using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AstraFinance.App.Controls;

/// <summary>
/// Makes a PasswordBox usable for PIN entry with MVVM: two-way <c>Pin</c> binding and
/// digits-only input (typing and paste). The PIN is never shown or logged.
/// </summary>
public static class PinBoxAssist
{
    public static readonly DependencyProperty PinProperty = DependencyProperty.RegisterAttached(
        "Pin", typeof(string), typeof(PinBoxAssist),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPinChanged));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PinBoxAssist), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PinBoxAssist), new PropertyMetadata(false));

    public static string GetPin(DependencyObject d) => (string)d.GetValue(PinProperty);

    public static void SetPin(DependencyObject d, string value) => d.SetValue(PinProperty, value);

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            box.PasswordChanged += OnPasswordChanged;
            box.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(box, OnPaste);
            box.MaxLength = Core.Security.PinRules.MaxLength;
            InputMethod.SetIsInputMethodEnabled(box, false);
        }
        else
        {
            box.PasswordChanged -= OnPasswordChanged;
            box.PreviewTextInput -= OnPreviewTextInput;
            DataObject.RemovePastingHandler(box, OnPaste);
        }
    }

    private static void OnPinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox box && !(bool)box.GetValue(IsUpdatingProperty))
        {
            var value = e.NewValue as string ?? string.Empty;
            if (box.Password != value)
            {
                box.Password = value;
            }
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetPin(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !text.All(char.IsAsciiDigit))
        {
            e.CancelCommand();
        }
    }
}
