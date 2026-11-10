using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AstraFinance.App.ViewModels.Pages;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;

namespace AstraFinance.App.Views.Pages;

/// <summary>
/// Analytics view. The view model exposes plain data; this code-behind builds the LiveCharts2 series in the
/// current theme colours and switches between two-column and one-column layouts.
/// </summary>
public partial class AnalyticsPage : UserControl
{
    private AnalyticsViewModel? _vm;

    public AnalyticsPage()
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

    private static SKColor Hex(string hex) => SKColor.TryParse(hex, out var c) ? c : SKColors.Gray;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = e.NewValue as AnalyticsViewModel;
        if (_vm is not null)
        {
            _vm.ChartsChanged += OnChartsChanged;
            Rebuild();
        }
    }

    private void Detach()
    {
        if (_vm is not null)
        {
            _vm.ChartsChanged -= OnChartsChanged;
            _vm = null;
        }
    }

    private void OnChartsChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (_vm is null)
        {
            return;
        }

        var vm = _vm;
        var income = Color("Brush.Success");
        var expense = Color("Brush.Danger");
        var accent = Color("Brush.Accent");

        // Income vs expenses per bucket.
        IncomeExpenseChart.Series =
        [
            Columns(vm.Text("TransactionType.Income"), vm.Series.Select(p => p.Income), income, vm),
            Columns(vm.Text("Dashboard.Expenses"), vm.Series.Select(p => p.Expense), expense, vm),
        ];
        StyleAxes(IncomeExpenseChart, vm, vm.Series.Select(p => p.Label), minZero: true);

        // Cash flow: running net over the period (can go below zero).
        CashFlowChart.Series =
        [
            new LineSeries<long>
            {
                Name = vm.Text("Analytics.CashFlow"),
                Values = vm.Series.Select(p => p.CumulativeNet).ToArray(),
                Stroke = new SolidColorPaint(accent) { StrokeThickness = 2.5f },
                Fill = new SolidColorPaint(accent.WithAlpha(40)),
                GeometrySize = vm.Series.Count > 40 ? 0 : 6,
                GeometryStroke = new SolidColorPaint(accent) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(Color("Brush.Surface")),
                LineSmoothness = 0.3,
                YToolTipLabelFormatter = p => vm.FormatMoney((long)p.Coordinate.PrimaryValue),
            },
        ];
        StyleAxes(CashFlowChart, vm, vm.Series.Select(p => p.Label), minZero: false);

        // Expense breakdown (donut); the legend list beside it is clickable.
        ExpenseChart.Series = vm.ExpenseBreakdown.Select(s => (ISeries)new PieSeries<long>
        {
            Name = s.Name,
            Values = [s.Amount],
            Fill = new SolidColorPaint(Hex(s.ColorHex)),
            Stroke = new SolidColorPaint(Color("Brush.Surface")) { StrokeThickness = 2 },
            InnerRadius = 55,
            HoverPushout = 6,
            ToolTipLabelFormatter = _ => $"{s.AmountText} ({s.PercentText})",
        }).ToArray();
        ExpenseChart.LegendPosition = LegendPosition.Hidden;
        ExpenseChart.TooltipBackgroundPaint = new SolidColorPaint(Color("Brush.Tooltip.Background"));
        ExpenseChart.TooltipTextPaint = new SolidColorPaint(Color("Brush.Tooltip.Text"));

        // Monthly trends (12 months).
        TrendChart.Series =
        [
            Line(vm.Text("TransactionType.Income"), vm.Trend.Select(p => p.Income), income, vm),
            Line(vm.Text("Dashboard.Expenses"), vm.Trend.Select(p => p.Expense), expense, vm),
        ];
        StyleAxes(TrendChart, vm, vm.Trend.Select(p => p.Label), minZero: true);

        // Spending by weekday.
        WeekdayChart.Series = [Columns(vm.Text("Dashboard.Expenses"), vm.Weekdays.Select(w => w.Amount), expense, vm)];
        StyleAxes(WeekdayChart, vm, vm.Weekdays.Select(w => w.Label), minZero: true);
    }

    private static ColumnSeries<long> Columns(string name, IEnumerable<long> values, SKColor color, AnalyticsViewModel vm) => new()
    {
        Name = name,
        Values = values.ToArray(),
        Fill = new SolidColorPaint(color),
        MaxBarWidth = 18,
        Padding = 2,
        Rx = 3,
        Ry = 3,
        YToolTipLabelFormatter = p => vm.FormatMoney((long)p.Coordinate.PrimaryValue),
    };

    private static LineSeries<long> Line(string name, IEnumerable<long> values, SKColor color, AnalyticsViewModel vm) => new()
    {
        Name = name,
        Values = values.ToArray(),
        Stroke = new SolidColorPaint(color) { StrokeThickness = 2.5f },
        Fill = null,
        GeometrySize = 6,
        GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 2 },
        GeometryFill = new SolidColorPaint(Color("Brush.Surface")),
        LineSmoothness = 0.3,
        YToolTipLabelFormatter = p => vm.FormatMoney((long)p.Coordinate.PrimaryValue),
    };

    private static void StyleAxes(CartesianChart chart, AnalyticsViewModel vm, IEnumerable<string> labels, bool minZero)
    {
        var text = Color("Brush.Text.Secondary");
        chart.XAxes =
        [
            new Axis
            {
                Labels = labels.ToArray(),
                LabelsPaint = new SolidColorPaint(text),
                TextSize = 11,
                SeparatorsPaint = null,
                TicksPaint = null,
            },
        ];
        chart.YAxes =
        [
            new Axis
            {
                MinLimit = minZero ? 0 : null,
                Labeler = v => vm.FormatCompact(v),
                LabelsPaint = new SolidColorPaint(text),
                TextSize = 11,
                SeparatorsPaint = new SolidColorPaint(Color("Brush.Divider")) { StrokeThickness = 1 },
            },
        ];
        chart.LegendPosition = LegendPosition.Hidden;
        chart.TooltipPosition = TooltipPosition.Top;
        chart.TooltipBackgroundPaint = new SolidColorPaint(Color("Brush.Tooltip.Background"));
        chart.TooltipTextPaint = new SolidColorPaint(Color("Brush.Tooltip.Text"));
        chart.AnimationsSpeed = TimeSpan.FromMilliseconds(250);
    }

    /// <summary>Two chart columns on wide windows, one on narrow ones; KPI cards 4 → 2 → 1.</summary>
    private void ApplyLayout(double width)
    {
        ChartGrid.Columns = width < 1000 ? 1 : 2;
        KpiGrid.Columns = width < 620 ? 1 : width < 1000 ? 2 : 4;
    }
}
