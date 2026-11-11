using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using AstraFinance.App.ViewModels.Gate;

namespace AstraFinance.App.Views.Gate;

public partial class LockView : UserControl
{
    public LockView()
    {
        InitializeComponent();
        Loaded += (_, _) => FocusPin();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LockViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is LockViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LockViewModel.FocusRequest))
        {
            FocusPin();
        }
    }

    private void FocusPin() =>
        Dispatcher.BeginInvoke(() => PinInput.Focus(), DispatcherPriority.Input);

    // The on-screen keypad types into the PIN box exactly like the keyboard, so the PIN box stays the single
    // input path (length limit, caret position and the view-model binding behave the same either way).
    private void Digit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string digit })
        {
            PinInput.Focus();
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, PinInput, digit));
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        PinInput.Focus();
        EditingCommands.Backspace.Execute(null, PinInput);
    }
}
