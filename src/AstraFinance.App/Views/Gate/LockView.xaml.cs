using System.ComponentModel;
using System.Windows.Controls;
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
}
