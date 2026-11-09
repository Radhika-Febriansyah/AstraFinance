using System.Windows;
using System.Windows.Controls;

namespace AstraFinance.App.Views.Gate;

public partial class OnboardingView : UserControl
{
    /// <summary>Below this width the progress rail is hidden so the form keeps its space.</summary>
    private const double RailBreakpoint = 860;

    public OnboardingView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Rail.Visibility = e.NewSize.Width < RailBreakpoint ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) => NextButton.Focus();
    }
}
