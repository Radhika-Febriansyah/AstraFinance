using System.Collections.ObjectModel;
using System.Globalization;
using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Dialogs;
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

/// <summary>One month of the cash-flow chart.</summary>
public sealed record CashFlowPoint(string Label, long Income, long Expense);

/// <summary>
/// Dashboard: greeting and date, total balance, this month's income and expenses, a six-month
/// cash-flow chart, recent transactions and the budget summary. Every figure comes from the
/// database; nothing is simulated. Detailed views live on their own pages.
/// </summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    public const int CashFlowMonths = 6;
    public const int RecentCount = 5;

    private readonly IClock _clock;
    private readonly IAccountService _accounts;
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly IRegionalFormatService _format;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly DataChangeNotifier _changes;
    private readonly Func<TransactionFormViewModel> _formFactory;
    private readonly CancellationTokenSource _cts = new();
    private readonly ThemeService _theme;

    /// <summary>Raised when theme colours change so the view can repaint the chart.</summary>
    public event EventHandler? ChartStyleChanged;

    public DashboardViewModel(
        ILocalizationService localization,
        IClock clock,
        IAccountService accounts,
        ITransactionService transactions,
        ICategoryService categories,
        IRegionalFormatService format,
        INavigationService navigation,
        IDialogService dialogs,
        INotificationService notifications,
        DataChangeNotifier changes,
        Func<TransactionFormViewModel> formFactory,
        ThemeService theme)
        : base(AppPage.Dashboard, localization)
    {
        _theme = theme;
        _theme.ThemeApplied += OnThemeApplied;
        _clock = clock;
        _accounts = accounts;
        _transactions = transactions;
        _categories = categories;
        _format = format;
        _navigation = navigation;
        _dialogs = dialogs;
        _notifications = notifications;
        _changes = changes;
        _formFactory = formFactory;
        _changes.Changed += OnDataChanged;
    }

    public string Greeting => Localization.GetString(GreetingProvider.GetGreetingKey(TimeOnly.FromDateTime(_clock.LocalNow)));

    public string DateText => _clock.LocalNow.ToString("D", Localization.Culture);

    public string MonthName => _clock.LocalNow.ToString("MMMM yyyy", Localization.Culture);

    // ---------------- Summary cards ----------------
    [ObservableProperty]
    public partial string TotalBalanceText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsTotalNegative { get; private set; }

    [ObservableProperty]
    public partial string AccountsText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MonthIncomeText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MonthExpenseText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MonthNetText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMonthNetNegative { get; private set; }

    // ---------------- Cash flow ----------------
    /// <summary>Six months, oldest first. The view turns this into a chart.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CashFlowPoint> CashFlow { get; private set; } = [];

    /// <summary>True when the period has no income or expenses: show an empty state, not an empty chart.</summary>
    [ObservableProperty]
    public partial bool HasNoCashFlow { get; private set; } = true;

    [ObservableProperty]
    public partial string CashFlowPeriodText { get; private set; } = string.Empty;

    // ---------------- Recent transactions ----------------
    public ObservableCollection<TransactionRowViewModel> Recent { get; } = [];

    [ObservableProperty]
    public partial bool HasNoTransactions { get; private set; }

    public bool IsLoaded { get; private set; }

    /// <summary>Localized text for chart series names (the chart is built in code).</summary>
    public string Text(string key) => Localization.GetString(key);

    /// <summary>Formats chart values (tooltips) in the user's currency.</summary>
    public string FormatMoney(long value) => _format.FormatMoney(value);

    /// <summary>Compact axis labels ("Rp8jt", "$2.3M").</summary>
    public string FormatCompact(double value) =>
        MoneyFormatter.FormatCompact((long)Math.Round(value), _format.Current, CompactSuffixes());

    public override async Task OnNavigatedToAsync(NavigationTarget target)
    {
        _ = RunClockAsync(_cts.Token);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task OpenAccountsAsync() => _navigation.NavigateAsync(AppPage.Accounts);

    [RelayCommand]
    private Task OpenAnalyticsAsync() => _navigation.NavigateAsync(AppPage.Analytics);

    [RelayCommand]
    private Task OpenTransactionsAsync() => _navigation.NavigateAsync(AppPage.Transactions);

    [RelayCommand]
    private Task OpenTransactionAsync(TransactionRowViewModel? row) =>
        row is null ? Task.CompletedTask : _navigation.NavigateAsync(new NavigationTarget(AppPage.Transactions, row.Id));

    /// <summary>Quick Add (same simplified form as Ctrl+N).</summary>
    [RelayCommand]
    private async Task QuickAddAsync()
    {
        var form = _formFactory();
        await form.InitializeForCreateAsync(quick: true).ConfigureAwait(true);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Transactions.Saved.Title"),
                Localization.GetString($"TransactionType.{form.Saved!.Transaction.Type}"));
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var today = _clock.Today;
            var accounts = await _accounts.ListAsync(includeInactive: true).ConfigureAwait(true);
            var total = await _accounts.GetTotalBalanceAsync().ConfigureAwait(true);
            TotalBalanceText = _format.FormatMoney(total);
            IsTotalNegative = total < 0;
            var active = accounts.Count(a => a.Account.IsActive);
            AccountsText = Localization.Format("Dashboard.Accounts", active);

            var month = await _transactions.GetIncomeExpenseTotalsAsync(DateRange.MonthOf(today)).ConfigureAwait(true);
            MonthIncomeText = _format.FormatMoney(month.Income);
            MonthExpenseText = _format.FormatMoney(month.Expense);
            MonthNetText = Localization.Format("Dashboard.Net", (month.Net > 0 ? "+" : string.Empty) + _format.FormatMoney(month.Net));
            IsMonthNetNegative = month.Net < 0;

            var monthly = await _transactions.GetMonthlyTotalsAsync(today, CashFlowMonths).ConfigureAwait(true);
            CashFlow = monthly
                .Select(m => new CashFlowPoint(
                    new DateTime(m.Year, m.Month, 1).ToString("MMM", Localization.Culture),
                    m.Income,
                    m.Expense))
                .ToList();
            HasNoCashFlow = monthly.All(m => m.Income == 0 && m.Expense == 0);
            var first = monthly[0];
            CashFlowPeriodText = $"{new DateTime(first.Year, first.Month, 1).ToString("MMM yyyy", Localization.Culture)} – {today.ToDateTime(TimeOnly.MinValue).ToString("MMM yyyy", Localization.Culture)}";

            var recent = await _transactions.QueryAsync(new TransactionQuery { PageSize = RecentCount }).ConfigureAwait(true);
            var lookups = await FinanceLookups.LoadAsync(_categories, _accounts, Localization).ConfigureAwait(true);
            Recent.Clear();
            foreach (var t in recent.Items)
            {
                Recent.Add(new TransactionRowViewModel(t, lookups, _format));
            }

            HasNoTransactions = recent.TotalCount == 0;
            IsLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(MonthName));
        _ = LoadAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _changes.Changed -= OnDataChanged;
            _theme.ThemeApplied -= OnThemeApplied;
            _cts.Cancel();
            _cts.Dispose();
        }

        base.Dispose(disposing);
    }

    private string[] CompactSuffixes() =>
    [
        Localization.GetString("Compact.Thousand"),
        Localization.GetString("Compact.Million"),
        Localization.GetString("Compact.Billion"),
        Localization.GetString("Compact.Trillion"),
    ];

    private void OnDataChanged(object? sender, DataChangeKind e) => _ = LoadAsync();

    private void OnThemeApplied(object? sender, EventArgs e) => ChartStyleChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Keeps the greeting and date current while the dashboard stays open (e.g. past midnight).</summary>
    private async Task RunClockAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        var lastDay = _clock.Today;
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(true))
            {
                OnPropertyChanged(nameof(Greeting));
                OnPropertyChanged(nameof(DateText));
                if (_clock.Today != lastDay)
                {
                    // A new day (or month) changes "this month" and the chart window.
                    lastDay = _clock.Today;
                    OnPropertyChanged(nameof(MonthName));
                    await LoadAsync().ConfigureAwait(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
