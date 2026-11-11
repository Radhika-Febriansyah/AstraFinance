using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AstraFinance.App.Interop;
using AstraFinance.App.Services;
using AstraFinance.App.ViewModels;
using AstraFinance.Core.Enums;

namespace AstraFinance.App.Views;

/// <summary>
/// Application shell window. Code-behind is limited to view concerns: animation,
/// responsive width reporting, keyboard routing and native title-bar tinting.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly IEasingFunction Ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

    private readonly ThemeService _theme;
    private UIElement? _focusBeforeDialog;

    public MainWindow(ShellViewModel viewModel, ThemeService theme)
    {
        ViewModel = viewModel;
        _theme = theme;
        DataContext = viewModel;
        InitializeComponent();

        Sidebar.Width = viewModel.SidebarVisualWidth;
        ContentHost.Margin = new Thickness(viewModel.SidebarLayoutWidth, 0, 0, 0);

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        theme.ThemeApplied += OnThemeApplied;
        SourceInitialized += (_, _) => ApplyTitleBar();
        SizeChanged += (_, e) => ViewModel.UpdateLayoutWidth(e.NewSize.Width);
        PreviewKeyDown += OnWindowPreviewKeyDown;
        Closed += (_, _) =>
        {
            theme.ThemeApplied -= OnThemeApplied;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    public ShellViewModel ViewModel { get; }

    /// <summary>Restores and focuses the window (used when a second instance is launched).</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.SidebarVisualWidth):
                Sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(ViewModel.SidebarVisualWidth, MotionPreferences.For(AnimationDuration)) { EasingFunction = Ease });
                Sidebar.Effect = ViewModel.IsSidebarOverlay ? (System.Windows.Media.Effects.Effect)FindResource("Effect.Shadow.Popup") : null;
                break;
            case nameof(ShellViewModel.SidebarLayoutWidth):
                ContentHost.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(ViewModel.SidebarLayoutWidth, 0, 0, 0), MotionPreferences.For(AnimationDuration)) { EasingFunction = Ease });
                break;
            case nameof(ShellViewModel.IsCompact):
                ApplyCompactLayout(ViewModel.IsCompact);
                break;
            case nameof(ShellViewModel.Gate) when ViewModel.Gate is null:
                // The gate's focused control is gone; keep shortcuts (Ctrl+N, Ctrl+K…) working.
                Dispatcher.BeginInvoke(() => FocusShell(), System.Windows.Threading.DispatcherPriority.Input);
                break;
        }
    }

    private void ApplyCompactLayout(bool compact)
    {
        SearchHost.Margin = compact ? new Thickness(8, 0, 8, 0) : new Thickness(16, 0, 16, 0);
        Topbar.Margin = compact ? new Thickness(16, 0, 8, 0) : new Thickness(24, 0, 16, 0);
        if (compact)
        {
            // Icon-only; the accessible name and tooltip still describe the action.
            BindingOperations.ClearBinding(QuickAddButton, ContentProperty);
            QuickAddButton.Content = null;
        }
        else
        {
            QuickAddButton.SetBinding(ContentProperty, new Binding("[Shell.QuickAdd]") { Source = Localization.LocalizationSource.Instance });
        }

        QuickAddButton.Padding = compact ? new Thickness(10, 0, 10, 0) : new Thickness(14, 0, 14, 0);
        Application.Current.Resources["Padding.Page.Current"] = FindResource(compact ? "Padding.Page.Compact" : "Padding.Page");
    }

    private void OnThemeApplied(object? sender, EventArgs e) => ApplyTitleBar();

    /// <summary>
    /// Moves keyboard focus into the shell (the selected navigation item, else the window), so window
    /// shortcuts always have a focused element to route through.
    /// </summary>
    private void FocusShell() => FocusShell(force: false);

    private void FocusShell(bool force)
    {
        if (ViewModel.IsGated || DialogLayer.Visibility == Visibility.Visible)
        {
            return;
        }

        if (!force && Keyboard.FocusedElement is UIElement { IsVisible: true } current && IsAncestorOf(current))
        {
            return;
        }

        Focus();
    }

    private void ApplyTitleBar()
    {
        // The title bar continues the Deep Navy brand sidebar in both themes (light caption buttons).
        var caption = (Color)FindResource("Color.Sidebar");
        var text = ((SolidColorBrush)FindResource("Brush.Sidebar.Text")).Color;
        DwmTitleBar.Apply(this, isDark: true, caption, text, border: caption);
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && (e.Key == Key.K || e.Key == Key.F) && DialogLayer.Visibility != Visibility.Visible && !ViewModel.IsGated)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && ViewModel.IsSidebarOverlay)
        {
            ViewModel.CloseOverlayCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ViewModel.Search.MoveHighlight(1);
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.Search.MoveHighlight(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _ = ViewModel.Search.OpenHighlightedAsync();
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel.Search.Clear();
                FocusShell(force: true);
                e.Handled = true;
                break;
        }
    }

    private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.Search.Query))
        {
            ViewModel.Search.IsOpen = true;
        }
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        AppMenu.PlacementTarget = MenuButton;
        AppMenu.HorizontalOffset = -(220 - MenuButton.ActualWidth);
        AppMenu.IsOpen = true;
    }

    private void PageHost_TargetUpdated(object? sender, DataTransferEventArgs e)
    {
        // Subtle page entrance: fade + 8px rise (instant when Windows animations are off).
        var duration = MotionPreferences.For(AnimationDuration);
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = Ease });
        if (PageHost.RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = Ease });
        }
    }

    /// <summary>
    /// Enter submits and Esc cancels the dialog — unless the key belongs to an open dropdown or
    /// calendar inside it, which must handle the key itself (e.g. Esc closes only the calendar).
    /// </summary>
    private void DialogLayer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Escape or Key.Enter) || ViewModel.Dialogs.ActiveDialog is not { } dialog)
        {
            return;
        }

        if (IsInsidePopup(e.OriginalSource as DependencyObject)
            || (e.Key == Key.Enter && e.OriginalSource is TextBox { AcceptsReturn: true }))
        {
            return;
        }

        var openDropDown = FindOpenDropDown(DialogLayer);
        if (openDropDown is not null)
        {
            if (e.Key == Key.Escape)
            {
                CloseDropDown(openDropDown);
                e.Handled = true;
            }

            return;
        }

        var command = e.Key == Key.Escape ? (System.Windows.Input.ICommand)dialog.CancelCommand : dialog.AcceptCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        e.Handled = true;
    }

    private bool IsInsidePopup(DependencyObject? source) =>
        source is Visual visual && PresentationSource.FromVisual(visual)?.RootVisual is { } root && !ReferenceEquals(root, this);

    private static Control? FindOpenDropDown(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ComboBox { IsDropDownOpen: true } combo)
            {
                return combo;
            }

            if (child is DatePicker { IsDropDownOpen: true } picker)
            {
                return picker;
            }

            if (FindOpenDropDown(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void CloseDropDown(Control control)
    {
        switch (control)
        {
            case ComboBox combo:
                combo.IsDropDownOpen = false;
                break;
            case DatePicker picker:
                picker.IsDropDownOpen = false;
                break;
        }
    }

    /// <summary>Dialog entrance: the scrim fades in while the panel fades and scales up from 96 %.</summary>
    private void AnimateDialogIn()
    {
        var duration = MotionPreferences.For(AnimationDuration);
        DialogScrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        DialogPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = Ease });
        if (DialogPanel.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, duration) { EasingFunction = Ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, duration) { EasingFunction = Ease });
        }
    }

    private void DialogLayer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var open = (bool)e.NewValue;

        // Modal behaviour: everything behind the dialog becomes non-interactive.
        ContentHost.IsEnabled = !open;
        Sidebar.IsEnabled = !open;

        GateHost.IsEnabled = !open;
        if (!open)
        {
            // Return focus to where the user was (or the shell) so keyboard shortcuts keep working.
            var previous = _focusBeforeDialog;
            _focusBeforeDialog = null;
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (previous is { IsVisible: true, IsEnabled: true } && previous.Focus())
                    {
                        return;
                    }

                    FocusShell();
                },
                System.Windows.Threading.DispatcherPriority.Input);
        }

        if (open)
        {
            AnimateDialogIn();
            _focusBeforeDialog = Keyboard.FocusedElement as UIElement;
            ViewModel.Search.Close();
            ViewModel.Notifications.IsOpen = false;

            // Forms: focus the first field; messages: focus the primary button.
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (ViewModel.Dialogs.ActiveDialog?.IsForm == true)
                    {
                        DialogForm.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                    }
                    else
                    {
                        DialogPrimaryButton.Focus();
                    }
                },
                System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
