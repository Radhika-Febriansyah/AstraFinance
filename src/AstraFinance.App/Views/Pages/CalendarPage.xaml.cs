using System.Windows;
using System.Windows.Controls;

namespace AstraFinance.App.Views.Pages;

public partial class CalendarPage : UserControl
{
    private const double StackedBreakpoint = 900;

    public CalendarPage()
    {
        InitializeComponent();
    }

    /// <summary>Narrow windows: the selected-day panel moves below the month grid.</summary>
    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < StackedBreakpoint;
        DetailsColumn.Width = stacked ? new GridLength(0) : new GridLength(340);
        Grid.SetColumn(DetailsCard, stacked ? 0 : 1);
        Grid.SetRow(DetailsCard, stacked ? 3 : 2);
        DetailsCard.Margin = stacked ? new Thickness(0, 16, 0, 0) : new Thickness(16, 0, 0, 0);
        Grid.SetColumnSpan(CalendarCard, stacked ? 2 : 1);
    }
}
