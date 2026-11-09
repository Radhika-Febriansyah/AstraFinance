using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AstraFinance.App.ViewModels.Pages;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace AstraFinance.App.Views.Pages;

/// <summary>
/// Dashboard view. The view model exposes plain cash-flow data; this code-behind turns it into
/// LiveCharts2 series using the current theme colours and adapts the layout to the width.
/// </summary>
public partial class DashboardPage : UserControl
{
    private const double StackedBreakpoint = 900;
    private DashboardViewModel? _vm;

    public DashboardPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        Unloaded += (_, _) => Detach();
    }

    private static SKColor Color(string brushKey) =>
        Application.Current?.TryFindResource(brushKey) is SolidColorBrush brush
            ? new SKColor(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A)
            : SKColors.Gray;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = e.NewValue as DashboardViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.ChartStyleChanged += OnChartStyleChanged;
            RebuildChart();
        }
    }

    private void Detach()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.ChartStyleChanged -= OnChartStyleChanged;
            _vm = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.CashFlow))
        {
            RebuildChart();
        }
    }

    private void OnChartStyleChanged(object? sender, EventArgs e) => RebuildChart();

    private void RebuildChart()
    {
        if (_vm is null)
        {
            return;
        }

        var vm = _vm;
        var points = vm.CashFlow;
        var text = Color("Brush.Text.Secondary");
        var grid = Color("Brush.Divider");

        CashFlowChart.Series =
        [
            new ColumnSeries<long>
            {
                Name = vm.Text("TransactionType.Income"),
                Values = points.Select(p => p.Income).ToArray(),
                Fill = new SolidColorPaint(Color("Brush.Success")),
                MaxBarWidth = 22,
                Padding = 4,
                Rx = 4,
                Ry = 4,
                YToolTipLabelFormatter = p => vm.FormatMoney((long)p.Coordinate.PrimaryValue),
            },
            new ColumnSeries<long>
            {
                Name = vm.Text("Dashboard.Expenses"),
                Values = points.Select(p => p.Expense).ToArray(),
                Fill = new SolidColorPaint(Color("Brush.Danger")),
                MaxBarWidth = 22,
                Padding = 4,
                Rx = 4,
                Ry = 4,
                YToolTipLabelFormatter = p => vm.FormatMoney((long)p.Coordinate.PrimaryValue),
            },
        ];

        CashFlowChart.XAxes =
        [
            new Axis
            {
                Labels = points.Select(p => p.Label).ToArray(),
                LabelsPaint = new SolidColorPaint(text),
                TextSize = 12,
                SeparatorsPaint = null,
                TicksPaint = null,
            },
        ];

        CashFlowChart.YAxes =
        [
            new Axis
            {
                MinLimit = 0,
                Labeler = v => vm.FormatCompact(v),
                LabelsPaint = new SolidColorPaint(text),
                TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(grid) { StrokeThickness = 1 },
            },
        ];

        CashFlowChart.LegendPosition = LegendPosition.Hidden;
        CashFlowChart.TooltipPosition = TooltipPosition.Top;
        CashFlowChart.TooltipBackgroundPaint = new SolidColorPaint(Color("Brush.Tooltip.Background"));
        CashFlowChart.TooltipTextPaint = new SolidColorPaint(Color("Brush.Tooltip.Text"));
        CashFlowChart.AnimationsSpeed = TimeSpan.FromMilliseconds(250);
    }

    /// <summary>Three summary cards side by side on wide windows; stacked layout on narrow ones.</summary>
    private void ApplyLayout(double width)
    {
        var stacked = width < StackedBreakpoint;
        SummaryGrid.Columns = width < 640 ? 1 : 3;
        Grid.SetColumn(RecentCard, stacked ? 0 : 1);
        Grid.SetRow(RecentCard, stacked ? 1 : 0);
        Grid.SetColumnSpan(CashFlowCard, stacked ? 2 : 1);
        Grid.SetColumnSpan(RecentCard, stacked ? 2 : 1);
        Grid.SetRow(BudgetCard, stacked ? 2 : 1);
        CashFlowCard.Margin = stacked ? new Thickness(0, 0, 0, 16) : new Thickness(0, 0, 16, 16);
    }
}
