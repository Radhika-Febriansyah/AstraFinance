using System.ComponentModel;
using System.Windows.Controls;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Models;

namespace AstraFinance.App.Views.Pages;

public partial class TransactionsPage : UserControl
{
    public TransactionsPage()
    {
        InitializeComponent();
    }

    /// <summary>Below this width the details panel overlays the table instead of narrowing it.</summary>
    private const double OverlayBreakpoint = 1000;

    private void Root_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        // Keep Date, Description and Amount visible on small windows; the details panel still shows every field.
        var width = e.NewSize.Width;

        // Summary cards stay one compact row (icon tiles hidden when narrow) so the table keeps its room;
        // on very short windows they give way to the table entirely.
        SummaryGrid.Columns = width < 560 ? 2 : 4;
        SummaryGrid.Visibility = e.NewSize.Height < 520 || (width < 560 && e.NewSize.Height < 700) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        var tileVisibility = width < 900 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        foreach (var card in SummaryGrid.Children.OfType<System.Windows.Controls.Border>())
        {
            if (card.Child is System.Windows.Controls.DockPanel { Children.Count: > 0 } panel && panel.Children[0] is System.Windows.Controls.Border tile)
            {
                tile.Visibility = tileVisibility;
                ((System.Windows.FrameworkElement)panel.Children[1]).Margin = width < 900 ? new System.Windows.Thickness(0) : new System.Windows.Thickness(12, 0, 0, 0);
            }
        }
        TypeColumn.Visibility = width < 820 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        AccountColumn.Visibility = width < 820 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        CategoryColumn.Visibility = width < 640 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        var overlay = e.NewSize.Width < OverlayBreakpoint;
        System.Windows.Controls.Grid.SetColumn(DetailsPanel, overlay ? 0 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(DetailsPanel, overlay ? 2 : 1);
        DetailsPanel.HorizontalAlignment = overlay ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Stretch;
        DetailsPanel.Margin = overlay ? new System.Windows.Thickness(0) : new System.Windows.Thickness(16, 0, 0, 0);
        DetailsPanel.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, overlay ? "Brush.Popup" : "Brush.Surface");
        if (overlay)
        {
            DetailsPanel.SetResourceReference(EffectProperty, "Effect.Shadow.Popup");
        }
        else
        {
            DetailsPanel.ClearValue(EffectProperty);
        }
    }

    /// <summary>Sorting is done by the database (all pages), not by the grid on the visible page.</summary>
    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not TransactionsViewModel vm || string.IsNullOrEmpty(e.Column.SortMemberPath))
        {
            return;
        }

        vm.SortBy(e.Column.SortMemberPath);
        foreach (var column in Grid.Columns)
        {
            column.SortDirection = null;
        }

        e.Column.SortDirection = vm.Sort is TransactionSort.DateAscending or TransactionSort.AmountAscending
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;
    }
}
