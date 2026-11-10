using System.Windows;
using System.Windows.Controls;

namespace AstraFinance.App.Views.Pages;

public partial class ReportsPage : UserControl
{
    private const double StackedBreakpoint = 820;

    public ReportsPage()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    /// <summary>Report list beside the options on wide windows; stacked above them on narrow ones.</summary>
    private void ApplyLayout(double width)
    {
        var stacked = width < StackedBreakpoint;
        LeftColumn.Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(300);
        RightColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(OptionsPanel, stacked ? 0 : 1);
        Grid.SetRow(OptionsPanel, stacked ? 1 : 0);
        ReportCard.Margin = stacked ? new Thickness(0, 0, 0, 16) : new Thickness(0, 0, 16, 16);
    }
}
