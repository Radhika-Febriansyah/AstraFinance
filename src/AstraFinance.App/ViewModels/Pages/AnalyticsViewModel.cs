using System.Globalization;
using AstraFinance.App.Services;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Rules;
using AstraFinance.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

/// <summary>One point of a time series (label + values) for the charts.</summary>
public sealed record ChartPoint(string Label, long Income, long Expense, long CumulativeNet);

/// <summary>A category share (expense breakdown slices, income sources).</summary>
public sealed record CategoryShare(Guid? CategoryId, string Name, string IconKey, string ColorHex, long Amount, string AmountText, long Percent, string PercentText);

/// <summary>Spending per weekday.</summary>
public sealed record WeekdayPoint(string Label, long Amount);

/// <summary>
/// Interactive analytics for a week, month, year or custom range. All figures come from the database through
/// <see cref="IAnalyticsService"/>; charts are drawn by the view from the plain data exposed here.
/// </summary>
public sealed partial class AnalyticsViewModel : PageViewModel
{
    public const int TrendMonths = 12;

    private readonly IAnalyticsService _analytics;
    private readonly ITransactionService _transactions;
    private readonly IRegionalFormatService _format;
    private readonly INavigationService _navigation;
    private readonly DataChangeNotifier _changes;
    private readonly ThemeService _theme;
    private readonly IClock _clock;
    private bool _suppressReload;
    private int _loadVersion;

    public AnalyticsViewModel(
        ILocalizationService localization,
        IAnalyticsService analytics,
        ITransactionService transactions,
        IRegionalFormatService format,
        INavigationService navigation,
        DataChangeNotifier changes,
        ThemeService theme,
        IClock clock)
        : base(AppPage.Analytics, localization)
    {
        _analytics = analytics;
        _transactions = transactions;
        _format = format;
        _navigation = navigation;
        _changes = changes;
        _theme = theme;
        _clock = clock;
        Anchor = clock.Today;
        PeriodOptions =
        [
            new(AnalyticsPeriod.Week, () => Localization.GetString("Analytics.Period.Week"), null, SetPeriod),
            new(AnalyticsPeriod.Month, () => Localization.GetString("Analytics.Period.Month"), null, SetPeriod),
            new(AnalyticsPeriod.Year, () => Localization.GetString("Analytics.Period.Year"), null, SetPeriod),
            new(AnalyticsPeriod.Custom, () => Localization.GetString("Analytics.Period.Custom"), null, SetPeriod),
        ];
        PeriodOptions[1].SetSelectedSilently(true);
        _changes.Changed += OnDataChanged;
        _theme.ThemeApplied += OnThemeApplied;
    }

    /// <summary>Raised when the chart data or theme changes (the view rebuilds its series).</summary>
    public event EventHandler? ChartsChanged;

    public IReadOnlyList<ChoiceOption<AnalyticsPeriod>> PeriodOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom), nameof(CanNavigate))]
    public partial AnalyticsPeriod Period { get; private set; } = AnalyticsPeriod.Month;

    [ObservableProperty]
    public partial DateOnly Anchor { get; private set; }

    [ObservableProperty]
    public partial DateOnly? CustomFrom { get; set; }

    [ObservableProperty]
    public partial DateOnly? CustomTo { get; set; }

    [ObservableProperty]
    public partial string? CustomRangeError { get; private set; }

    public bool IsCustom => Period == AnalyticsPeriod.Custom;

    public bool CanNavigate => !IsCustom;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial DateRange Range { get; private set; }

    [ObservableProperty]
    public partial string PeriodText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasData { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    public partial string IncomeText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ExpenseText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string NetText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNetNegative { get; private set; }

    [ObservableProperty]
    public partial string CountsText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string AverageDailyText { get; private set; } = string.Empty;

    /// <summary>Share of income kept (net ÷ income), or a note when there was no income.</summary>
    [ObservableProperty]
    public partial string SavingsRateText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ChartPoint> Series { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ChartPoint> Trend { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasTrend { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<CategoryShare> ExpenseBreakdown { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasExpenses { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<CategoryShare> IncomeSources { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasIncome { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<WeekdayPoint> Weekdays { get; private set; } = [];

    [ObservableProperty]
    public partial string TopWeekdayText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? LargestExpenseText { get; private set; }

    /// <summary>Daily buckets for short ranges, monthly otherwise (used for the series chart caption).</summary>
    [ObservableProperty]
    public partial string SeriesCaption { get; private set; } = string.Empty;

    public string Text(string key) => Localization.GetString(key);

    public string FormatMoney(long value) => _format.FormatMoney(value);

    public string FormatCompact(double value) =>
        MoneyFormatter.FormatCompact((long)Math.Round(value), _format.Current,
            [Localization.GetString("Compact.Thousand"), Localization.GetString("Compact.Million"), Localization.GetString("Compact.Billion"), Localization.GetString("Compact.Trillion")]);

    public override Task OnNavigatedToAsync(NavigationTarget target) => LoadAsync();

    public async Task LoadAsync()
    {
        var range = ResolveRange();
        if (range is null)
        {
            return;
        }

        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var snapshot = await _analytics.GetSnapshotAsync(range.Value).ConfigureAwait(true);
            var trend = await _transactions.GetMonthlyTotalsAsync(range.Value.End, TrendMonths).ConfigureAwait(true);
            if (version != _loadVersion)
            {
                return; // a newer request superseded this one
            }

            Apply(snapshot, trend);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private Task PreviousAsync() => MoveAsync(-1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextAsync() => MoveAsync(1);

    /// <summary>Periods after the current one would only show empty future data.</summary>
    private bool CanGoNext() => !IsCustom && Range.End < _clock.Today;

    [RelayCommand]
    private Task ThisPeriodAsync()
    {
        Anchor = _clock.Today;
        return LoadAsync();
    }

    [RelayCommand]
    private Task OpenCategoryAsync(CategoryShare? share) =>
        share?.CategoryId is { } id ? _navigation.NavigateAsync(new NavigationTarget(AppPage.Transactions, id)) : Task.CompletedTask;

    protected override void OnLanguageChanged() => _ = LoadAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _changes.Changed -= OnDataChanged;
            _theme.ThemeApplied -= OnThemeApplied;
        }

        base.Dispose(disposing);
    }

    partial void OnCustomFromChanged(DateOnly? value) => ReloadForCustom();

    partial void OnCustomToChanged(DateOnly? value) => ReloadForCustom();

    private void ReloadForCustom()
    {
        if (IsCustom && !_suppressReload)
        {
            _ = LoadAsync();
        }
    }

    private Task MoveAsync(int steps)
    {
        if (IsCustom)
        {
            return Task.CompletedTask;
        }

        Anchor = AnalyticsPeriods.Shift(Period, Anchor, steps);
        return LoadAsync();
    }

    private void SetPeriod(AnalyticsPeriod period)
    {
        if (period == AnalyticsPeriod.Custom && CustomFrom is null && CustomTo is null)
        {
            // Start the custom range from what is currently shown.
            _suppressReload = true;
            CustomFrom = Range.Start == default ? DateRange.MonthOf(_clock.Today).Start : Range.Start;
            CustomTo = Range.End == default ? DateRange.MonthOf(_clock.Today).End : Range.End;
            _suppressReload = false;
        }

        Period = period;
        NextCommand.NotifyCanExecuteChanged();
        _ = LoadAsync();
    }

    /// <summary>The range to show, or null (with an error message) for an invalid custom range.</summary>
    private DateRange? ResolveRange()
    {
        CustomRangeError = null;
        if (!IsCustom)
        {
            return AnalyticsPeriods.Resolve(Period, Anchor);
        }

        if (CustomFrom is null || CustomTo is null)
        {
            CustomRangeError = Localization.GetString("Analytics.Custom.Required");
            return null;
        }

        if (CustomTo < CustomFrom)
        {
            CustomRangeError = Localization.GetString("Analytics.Custom.Order");
            return null;
        }

        return new DateRange(CustomFrom.Value, CustomTo.Value);
    }

    private void Apply(AnalyticsSnapshot s, IReadOnlyList<MonthlyTotals> trend)
    {
        var culture = Localization.Culture;
        Range = s.Range;
        PeriodText = Period switch
        {
            AnalyticsPeriod.Month => s.Range.Start.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", culture),
            AnalyticsPeriod.Year => s.Range.Start.Year.ToString(CultureInfo.InvariantCulture),
            _ => Localization.Format("Report.Period", _format.FormatDate(s.Range.Start), _format.FormatDate(s.Range.End)),
        };

        HasData = s.HasData;
        IsEmpty = !s.HasData;
        IncomeText = _format.FormatMoney(s.Income);
        ExpenseText = _format.FormatMoney(s.Expense);
        NetText = (s.Net > 0 ? "+" : string.Empty) + _format.FormatMoney(s.Net);
        IsNetNegative = s.Net < 0;
        CountsText = Localization.Format("Analytics.Counts", s.IncomeCount, s.ExpenseCount);
        AverageDailyText = _format.FormatMoney(s.AverageDailyExpense);
        SavingsRateText = s.Income > 0
            ? Localization.Format("Analytics.SavingsRate", s.Net >= 0 ? MoneyMath.PercentOf(s.Net, s.Income) : -MoneyMath.PercentOf(-s.Net, s.Income))
            : Localization.GetString("Analytics.SavingsRate.NoIncome");

        long running = 0;
        Series = s.Series.Select(b =>
        {
            running = checked(running + b.Net);
            var label = s.Bucket == AnalyticsBucket.Day
                ? b.Start.Day.ToString(CultureInfo.InvariantCulture)
                : b.Start.ToDateTime(TimeOnly.MinValue).ToString(s.Series.Count > 12 ? "MMM yy" : "MMM", culture);
            return new ChartPoint(label, b.Income, b.Expense, running);
        }).ToList();
        SeriesCaption = Localization.GetString(s.Bucket == AnalyticsBucket.Day ? "Analytics.PerDay" : "Analytics.PerMonth");

        Trend = trend.Select(m => new ChartPoint(
            new DateOnly(m.Year, m.Month, 1).ToDateTime(TimeOnly.MinValue).ToString("MMM yy", culture), m.Income, m.Expense, m.Net)).ToList();
        HasTrend = trend.Any(m => m.Income != 0 || m.Expense != 0);

        ExpenseBreakdown = Shares(s.ExpenseByCategory, s.Expense);
        HasExpenses = s.ExpenseCount > 0;
        IncomeSources = Shares(s.IncomeByCategory, s.Income);
        HasIncome = s.IncomeCount > 0;

        var names = culture.DateTimeFormat.AbbreviatedDayNames; // Sunday first
        Weekdays = Enumerable.Range(0, 7).Select(i => new WeekdayPoint(names[(i + 1) % 7], s.ExpenseByWeekday[i])).ToList();
        if (s.ExpenseCount > 0)
        {
            var top = Enumerable.Range(0, 7).MaxBy(i => s.ExpenseByWeekday[i]);
            TopWeekdayText = Localization.Format("Analytics.TopWeekday", culture.DateTimeFormat.DayNames[(top + 1) % 7], _format.FormatMoney(s.ExpenseByWeekday[top]));
        }
        else
        {
            TopWeekdayText = Localization.GetString("Analytics.NoExpenses");
        }

        LargestExpenseText = s.LargestExpense is { } largest
            ? Localization.Format("Analytics.Largest", _format.FormatMoney(largest.Amount), Describe(largest), _format.FormatDate(largest.Date))
            : null;

        ChartsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string Describe(Transaction t) =>
        string.IsNullOrWhiteSpace(t.Description) ? Localization.GetString("Analytics.NoDescription") : t.Description;

    private List<CategoryShare> Shares(IReadOnlyList<CategoryAmount> items, long total) =>
        items.Select(c => new CategoryShare(
            c.Category?.Id,
            c.Category is null ? Localization.GetString("Report.Uncategorized") : DefaultCategories.DisplayName(c.Category, Localization),
            c.Category?.IconKey ?? "Icon.MoreHorizontal",
            c.Category?.ColorHex ?? "#8A94A6",
            c.Amount,
            _format.FormatMoney(c.Amount),
            MoneyMath.PercentOf(c.Amount, total),
            MoneyMath.PercentOf(c.Amount, total).ToString(CultureInfo.InvariantCulture) + "%"))
        .ToList();

    private void OnDataChanged(object? sender, DataChangeKind e) => _ = LoadAsync();

    private void OnThemeApplied(object? sender, EventArgs e) => ChartsChanged?.Invoke(this, EventArgs.Empty);
}
