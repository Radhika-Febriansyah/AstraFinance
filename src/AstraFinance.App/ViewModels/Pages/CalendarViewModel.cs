using System.Collections.ObjectModel;
using System.Globalization;
using AstraFinance.App.Services;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

/// <summary>One cell of the month grid.</summary>
public sealed partial class CalendarDayViewModel : ObservableObject
{
    public CalendarDayViewModel(DateOnly date, DayTotals? totals, bool isInMonth, bool isToday, string automationName)
    {
        Date = date;
        Totals = totals;
        IsInMonth = isInMonth;
        IsToday = isToday;
        AutomationName = automationName;
    }

    public DateOnly Date { get; }

    public DayTotals? Totals { get; }

    public string DayNumber => Date.Day.ToString(CultureInfo.InvariantCulture);

    public bool IsInMonth { get; }

    public bool IsToday { get; }

    public bool HasIncome => Totals?.Income > 0;

    public bool HasExpense => Totals?.Expense > 0;

    /// <summary>Transfers only (no income or expense): shown as a neutral marker.</summary>
    public bool HasOnlyTransfers => Totals is { Income: 0, Expense: 0, Count: > 0 };

    public string AutomationName { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Read-only financial calendar: income and expense per day for a month, today highlighted, and the
/// selected day's transactions. Uses the existing transaction query; no data is changed here.
/// </summary>
public sealed partial class CalendarViewModel : PageViewModel
{
    private const int QueryPageSize = 500;

    private readonly IClock _clock;
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;
    private readonly INavigationService _navigation;
    private readonly DataChangeNotifier _changes;

    private IReadOnlyList<Transaction> _loaded = [];
    private FinanceLookups? _lookups;
    private DateOnly _month;

    public CalendarViewModel(
        ILocalizationService localization,
        IClock clock,
        ITransactionService transactions,
        ICategoryService categories,
        IAccountService accounts,
        IRegionalFormatService format,
        INavigationService navigation,
        DataChangeNotifier changes)
        : base(AppPage.Calendar, localization)
    {
        _clock = clock;
        _transactions = transactions;
        _categories = categories;
        _accounts = accounts;
        _format = format;
        _navigation = navigation;
        _changes = changes;
        _changes.Changed += OnDataChanged;
        _month = new DateOnly(clock.Today.Year, clock.Today.Month, 1);
        SelectedDate = clock.Today;
    }

    public ObservableCollection<string> WeekdayHeaders { get; } = [];

    public ObservableCollection<CalendarDayViewModel> Days { get; } = [];

    public ObservableCollection<TransactionRowViewModel> SelectedRows { get; } = [];

    [ObservableProperty]
    public partial string MonthText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCurrentMonth { get; private set; } = true;

    [ObservableProperty]
    public partial string MonthIncomeText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MonthExpenseText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MonthNetText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMonthNetNegative { get; private set; }

    [ObservableProperty]
    public partial DateOnly SelectedDate { get; private set; }

    [ObservableProperty]
    public partial string SelectedDayTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedIncomeText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedExpenseText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedNetText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelectedNetNegative { get; private set; }

    [ObservableProperty]
    public partial bool HasNoSelectedRows { get; private set; } = true;

    public override Task OnNavigatedToAsync(NavigationTarget target) => LoadAsync();

    [RelayCommand]
    private Task PreviousMonthAsync() => ShowMonthAsync(_month.AddMonths(-1), select: null);

    [RelayCommand]
    private Task NextMonthAsync() => ShowMonthAsync(_month.AddMonths(1), select: null);

    [RelayCommand]
    private Task TodayAsync() => ShowMonthAsync(new DateOnly(_clock.Today.Year, _clock.Today.Month, 1), _clock.Today);

    /// <summary>Selects a day; a day from the neighbouring month switches to that month.</summary>
    [RelayCommand]
    private async Task SelectDayAsync(CalendarDayViewModel? day)
    {
        if (day is null)
        {
            return;
        }

        if (!day.IsInMonth)
        {
            await ShowMonthAsync(new DateOnly(day.Date.Year, day.Date.Month, 1), day.Date).ConfigureAwait(true);
            return;
        }

        Select(day.Date);
    }

    [RelayCommand]
    private Task OpenTransactionAsync(TransactionRowViewModel? row) =>
        row is null ? Task.CompletedTask : _navigation.NavigateAsync(new NavigationTarget(AppPage.Transactions, row.Id));

    /// <summary>Reloads the visible month from the database.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            _lookups = await FinanceLookups.LoadAsync(_categories, _accounts, Localization).ConfigureAwait(true);

            var grid = CalendarSummary.GridDates(_month.Year, _month.Month, Localization.Culture.DateTimeFormat.FirstDayOfWeek);
            _loaded = await LoadRangeAsync(grid[0], grid[^1]).ConfigureAwait(true);
            var totals = CalendarSummary.ByDay(_loaded);

            BuildHeaders();
            Days.Clear();
            foreach (var date in grid)
            {
                totals.TryGetValue(date, out var t);
                Days.Add(new CalendarDayViewModel(date, t, date.Month == _month.Month, date == _clock.Today, DayAutomationName(date, t)));
            }

            var inMonth = totals.Values.Where(t => t.Date.Year == _month.Year && t.Date.Month == _month.Month).ToList();
            var income = inMonth.Sum(t => t.Income);
            var expense = inMonth.Sum(t => t.Expense);
            MonthIncomeText = _format.FormatMoney(income);
            MonthExpenseText = _format.FormatMoney(expense);
            MonthNetText = Signed(income - expense);
            IsMonthNetNegative = income - expense < 0;
            MonthText = _month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Localization.Culture);
            IsCurrentMonth = _month.Year == _clock.Today.Year && _month.Month == _clock.Today.Month;

            var selection = SelectedDate.Year == _month.Year && SelectedDate.Month == _month.Month
                ? SelectedDate
                : IsCurrentMonth ? _clock.Today : _month;
            Select(selection);
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected override void OnLanguageChanged() => _ = LoadAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _changes.Changed -= OnDataChanged;
        }

        base.Dispose(disposing);
    }

    private async Task ShowMonthAsync(DateOnly month, DateOnly? select)
    {
        _month = month;
        SelectedDate = select ?? month;
        await LoadAsync().ConfigureAwait(true);
    }

    private void Select(DateOnly date)
    {
        SelectedDate = date;
        foreach (var d in Days)
        {
            d.IsSelected = d.Date == date;
        }

        var dayTransactions = _loaded.Where(t => t.Date == date)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToList();
        var totals = CalendarSummary.ByDay(dayTransactions).GetValueOrDefault(date) ?? new DayTotals(date, 0, 0, 0);

        SelectedDayTitle = date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM yyyy", Localization.Culture);
        SelectedIncomeText = _format.FormatMoney(totals.Income);
        SelectedExpenseText = _format.FormatMoney(totals.Expense);
        SelectedNetText = Signed(totals.Net);
        IsSelectedNetNegative = totals.Net < 0;

        SelectedRows.Clear();
        if (_lookups is not null)
        {
            foreach (var t in dayTransactions)
            {
                SelectedRows.Add(new TransactionRowViewModel(t, _lookups, _format));
            }
        }

        HasNoSelectedRows = SelectedRows.Count == 0;
    }

    /// <summary>Every (non-deleted) transaction in the range, read page by page through the shared query.</summary>
    private async Task<IReadOnlyList<Transaction>> LoadRangeAsync(DateOnly from, DateOnly to)
    {
        var all = new List<Transaction>();
        for (var page = 1; ; page++)
        {
            var result = await _transactions.QueryAsync(new TransactionQuery
            {
                From = from,
                To = to,
                Sort = TransactionSort.DateAscending,
                Page = page,
                PageSize = QueryPageSize,
            }).ConfigureAwait(true);
            all.AddRange(result.Items);
            if (page >= result.PageCount)
            {
                return all;
            }
        }
    }

    private void BuildHeaders()
    {
        var format = Localization.Culture.DateTimeFormat;
        WeekdayHeaders.Clear();
        for (var i = 0; i < 7; i++)
        {
            WeekdayHeaders.Add(format.AbbreviatedDayNames[((int)format.FirstDayOfWeek + i) % 7]);
        }
    }

    private string DayAutomationName(DateOnly date, DayTotals? totals) =>
        Localization.Format(
            "Calendar.Day.Automation",
            date.ToDateTime(TimeOnly.MinValue).ToString("D", Localization.Culture),
            _format.FormatMoney(totals?.Income ?? 0),
            _format.FormatMoney(totals?.Expense ?? 0));

    private string Signed(long value) => (value > 0 ? "+" : string.Empty) + _format.FormatMoney(value);

    private void OnDataChanged(object? sender, DataChangeKind e) => _ = LoadAsync();
}
