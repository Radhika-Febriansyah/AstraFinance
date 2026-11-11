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
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }

            if (e.NewValue is INotifyPropertyChanged newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
            }

            UpdateSummaryVisibility();
        };
    }

    /// <summary>False when the window is too short for the summary cards.</summary>
    private bool _summaryFits = true;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransactionsViewModel.ShowDeleted))
        {
            UpdateSummaryVisibility();
        }
    }

    /// <summary>
    /// The summary cards describe real income and expenses, so they are hidden while the list shows deleted
    /// transactions (which never count toward balances) as well as on very short windows.
    /// </summary>
    private void UpdateSummaryVisibility() =>
        SummaryGrid.Visibility = _summaryFits && DataContext is not TransactionsViewModel { ShowDeleted: true }
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    /// <summary>Below this width the details panel overlays the table instead of narrowing it.</summary>
    private const double OverlayBreakpoint = 1000;

    /// <summary>
    /// Smallest content height (without page padding) that still shows the toolbar and several table rows.
    /// Shorter windows scroll the page instead of squeezing the table to its header.
    /// </summary>
    private const double MinimumContentHeight = 560;

    private void Root_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        // Measurements use the visible area (the scroll viewer) minus the page padding, as before the page could scroll.
        var padding = RootGrid.Margin;
        var size = new System.Windows.Size(
            Math.Max(0, e.NewSize.Width - padding.Left - padding.Right),
            Math.Max(0, e.NewSize.Height - padding.Top - padding.Bottom));
        RootGrid.Height = Math.Max(size.Height, MinimumContentHeight);

        var width = size.Width;

        // Summary cards stay one compact row (icon tiles hidden when narrow) so the table keeps its room;
        // on very short windows they give way to the table entirely.
        SummaryGrid.Columns = width < 560 ? 2 : 4;
        _summaryFits = !(size.Height < 520 || (width < 560 && size.Height < 700));
        UpdateSummaryVisibility();
        var tileVisibility = width < 900 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        foreach (var card in SummaryGrid.Children.OfType<System.Windows.Controls.Border>())
        {
            if (card.Child is System.Windows.Controls.DockPanel { Children.Count: > 0 } panel && panel.Children[0] is System.Windows.Controls.Border tile)
            {
                tile.Visibility = tileVisibility;
                ((System.Windows.FrameworkElement)panel.Children[1]).Margin = width < 900 ? new System.Windows.Thickness(0) : new System.Windows.Thickness(12, 0, 0, 0);
            }
        }

        var overlay = width < OverlayBreakpoint;
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

    /// <summary>
    /// Columns follow the table's own width, so they also give way when the docked details panel narrows the table.
    /// Date, Description and Amount always stay; the details panel still shows every field.
    /// </summary>
    private void TableCard_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        TypeColumn.Visibility = width < 820 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        AccountColumn.Visibility = width < 820 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        CategoryColumn.Visibility = width < 640 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
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
