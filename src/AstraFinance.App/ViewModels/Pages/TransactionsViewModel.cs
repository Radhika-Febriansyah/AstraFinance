using System.Collections.ObjectModel;
using System.Globalization;
using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Dialogs;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Rules;
using AstraFinance.Core.Services;
using AstraFinance.Infrastructure.Export;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

public enum PeriodPreset
{
    AllTime = 0,
    ThisMonth = 1,
    LastMonth = 2,
    Last3Months = 3,
    ThisYear = 4,
    Custom = 5,
}

/// <summary>
/// Transactions: searchable, filterable, sortable and paged list; details; add/edit (full form);
/// delete and restore (deleted view); CSV export of everything that matches the current filters.
/// </summary>
public sealed partial class TransactionsViewModel : PageViewModel
{
    public const int PageSize = 100;
    public const int ExportBatchSize = 500;

    private static readonly string[] ExportColumns = ["Date", "Type", "Description", "Category", "Account", "ToAccount", "Amount", "Currency", "Notes", "Fee"];
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IFileSavePicker _savePicker;
    private readonly IClock _clock;
    private readonly DataChangeNotifier _changes;
    private readonly Func<TransactionFormViewModel> _formFactory;
    private FinanceLookups? _lookups;
    private CancellationTokenSource? _searchCts;
    private bool _suppressReload;

    public TransactionsViewModel(
        ILocalizationService localization,
        ITransactionService transactions,
        ICategoryService categories,
        IAccountService accounts,
        IRegionalFormatService format,
        IDialogService dialogs,
        INotificationService notifications,
        IFileSavePicker savePicker,
        IClock clock,
        DataChangeNotifier changes,
        Func<TransactionFormViewModel> formFactory)
        : base(AppPage.Transactions, localization)
    {
        _transactions = transactions;
        _categories = categories;
        _accounts = accounts;
        _format = format;
        _dialogs = dialogs;
        _notifications = notifications;
        _savePicker = savePicker;
        _clock = clock;
        _changes = changes;
        _formFactory = formFactory;

        PeriodOptions = Enum.GetValues<PeriodPreset>()
            .Select(p => new SelectOption<PeriodPreset>(p, localization.GetString($"Period.{p}")))
            .ToList();
        _suppressReload = true;
        SelectedPeriod = PeriodOptions[0];
        _suppressReload = false;

        TypeOptions =
        [
            new(null, () => Localization.GetString("Transactions.Filter.AllTypes"), null, SetTypeFilter),
            new(TransactionType.Income, () => Localization.GetString("TransactionType.Income"), null, SetTypeFilter),
            new(TransactionType.Expense, () => Localization.GetString("TransactionType.Expense"), null, SetTypeFilter),
            new(TransactionType.Transfer, () => Localization.GetString("TransactionType.Transfer"), null, SetTypeFilter),
        ];
        TypeOptions[0].SetSelectedSilently(true);

        _changes.Changed += OnDataChanged;
    }

    public ObservableCollection<TransactionRowViewModel> Rows { get; } = [];

    // ---------------- Filters ----------------
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public IReadOnlyList<SelectOption<PeriodPreset>> PeriodOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomPeriod))]
    public partial SelectOption<PeriodPreset> SelectedPeriod { get; set; }

    public bool IsCustomPeriod => SelectedPeriod?.Value == PeriodPreset.Custom;

    [ObservableProperty]
    public partial DateOnly? CustomFrom { get; set; }

    [ObservableProperty]
    public partial DateOnly? CustomTo { get; set; }

    public IReadOnlyList<ChoiceOption<TransactionType?>> TypeOptions { get; }

    [ObservableProperty]
    public partial TransactionType? TypeFilter { get; private set; }

    public ObservableCollection<SelectOption<Guid?>> CategoryOptions { get; } = [];

    [ObservableProperty]
    public partial SelectOption<Guid?>? SelectedCategoryFilter { get; set; }

    public ObservableCollection<SelectOption<Guid?>> AccountOptions { get; } = [];

    [ObservableProperty]
    public partial SelectOption<Guid?>? SelectedAccountFilter { get; set; }

    [ObservableProperty]
    public partial bool ShowDeleted { get; set; }

    [ObservableProperty]
    public partial TransactionSort Sort { get; private set; } = TransactionSort.DateDescending;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText) || SelectedPeriod?.Value != PeriodPreset.AllTime || TypeFilter is not null
        || SelectedCategoryFilter?.Value is not null || SelectedAccountFilter?.Value is not null;

    // ---------------- Results ----------------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    public partial int CurrentPage { get; private set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    public partial int PageCount { get; private set; }

    public string PageText => Localization.Format("Transactions.Page", CurrentPage, Math.Max(1, PageCount));

    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    public partial bool HasNoRecordsAtAll { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsExporting { get; private set; }

    // ---------------- Details ----------------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial TransactionRowViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    public partial string? SelectedFeeText { get; private set; }

    [ObservableProperty]
    public partial string? SelectedCreatedText { get; private set; }

    public override async Task OnNavigatedToAsync(NavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        await LoadLookupsAsync().ConfigureAwait(true);

        if (target.EntityId is { } id && _lookups!.Categories.ContainsKey(id))
        {
            // Category search result → transactions in that category.
            _suppressReload = true;
            SelectedCategoryFilter = CategoryOptions.FirstOrDefault(o => o.Value == id);
            _suppressReload = false;
            await ReloadAsync().ConfigureAwait(true);
            return;
        }

        await ReloadAsync().ConfigureAwait(true);
        if (target.EntityId is { } transactionId)
        {
            await ShowDetailsAsync(transactionId).ConfigureAwait(true);
        }
    }

    /// <summary>Builds the service query for the current filters.</summary>
    public TransactionQuery BuildQuery(int page, int pageSize)
    {
        var (from, to) = ResolvePeriod();
        var search = SearchText.Trim();
        IReadOnlyCollection<Guid>? searchCategories = null;
        IReadOnlyCollection<Guid>? searchAccounts = null;
        long? searchAmount = null;
        if (search.Length > 0 && _lookups is not null)
        {
            var normalized = SearchMatcher.Normalize(search);
            searchCategories = _lookups.Categories.Values
                .Where(c => SearchMatcher.Score(normalized, DefaultCategories.DisplayName(c, Localization)) > 0)
                .Select(c => c.Id).ToList();
            searchAccounts = _lookups.Accounts.Values
                .Where(a => SearchMatcher.Score(normalized, a.Name) > 0)
                .Select(a => a.Id).ToList();
            if (MoneyParser.TryParse(search, _format.Current, out var amount) && amount > 0)
            {
                searchAmount = amount;
            }
        }

        return new TransactionQuery
        {
            Search = search.Length > 0 ? search : null,
            SearchCategoryIds = searchCategories,
            SearchAccountIds = searchAccounts,
            SearchAmount = searchAmount,
            From = from,
            To = to,
            Types = TypeFilter is { } type ? [type] : null,
            CategoryIds = SelectedCategoryFilter?.Value is { } c ? [c] : null,
            AccountIds = SelectedAccountFilter?.Value is { } a ? [a] : null,
            DeletedOnly = ShowDeleted,
            Sort = Sort,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>Clicking a sortable column header: toggles direction, or switches column.</summary>
    public void SortBy(string column)
    {
        Sort = (column, Sort) switch
        {
            ("Date", TransactionSort.DateDescending) => TransactionSort.DateAscending,
            ("Date", _) => TransactionSort.DateDescending,
            ("Amount", TransactionSort.AmountDescending) => TransactionSort.AmountAscending,
            ("Amount", _) => TransactionSort.AmountDescending,
            _ => Sort,
        };
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var form = _formFactory();
        await form.InitializeForCreateAsync(quick: false).ConfigureAwait(true);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Transactions.Saved.Title"), _format.FormatMoney(form.Saved!.Transaction.Amount));
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var form = _formFactory();
        if (await form.InitializeForEditAsync(Selected.Id).ConfigureAwait(true)
            && await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            await ShowDetailsAsync(form.Saved!.Transaction.Id).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var id = Selected.Transaction.ParentTransactionId ?? Selected.Id;
        var isTransfer = Selected.Transaction.Type == TransactionType.Transfer || Selected.IsFee;
        var confirmed = await _dialogs.ConfirmAsync(
            Localization.GetString("Transactions.Delete.Title"),
            Localization.GetString(isTransfer ? "Transactions.Delete.BodyTransfer" : "Transactions.Delete.Body"),
            Localization.GetString("Transactions.Delete.Confirm"),
            isDestructive: true).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await _transactions.DeleteAsync(id).ConfigureAwait(true);
        Selected = null;
        _notifications.Notify(NotificationKind.Success, Localization.GetString("Transactions.Deleted.Title"), Localization.GetString("Transactions.Deleted.Body"));
        _changes.Notify(DataChangeKind.Transactions);
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await _transactions.RestoreAsync(Selected.Transaction.ParentTransactionId ?? Selected.Id).ConfigureAwait(true);
        Selected = null;
        _notifications.Notify(NotificationKind.Success, Localization.GetString("Transactions.Restored.Title"), string.Empty);
        _changes.Notify(DataChangeKind.Transactions);
    }

    [RelayCommand]
    private void CloseDetails() => Selected = null;

    [RelayCommand]
    private async Task OpenParentTransferAsync()
    {
        if (Selected?.Transaction.ParentTransactionId is { } parent)
        {
            await ShowDetailsAsync(parent).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressReload = true;
        SearchText = string.Empty;
        SelectedPeriod = PeriodOptions[0];
        CustomFrom = null;
        CustomTo = null;
        foreach (var o in TypeOptions)
        {
            o.SetSelectedSilently(o.Value is null);
        }

        TypeFilter = null;
        SelectedCategoryFilter = CategoryOptions.FirstOrDefault();
        SelectedAccountFilter = AccountOptions.FirstOrDefault();
        _suppressReload = false;
        _ = ReloadAsync();
    }

    private bool CanGoPrevious() => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousPageAsync() => LoadPageAsync(CurrentPage - 1);

    private bool CanGoNext() => CurrentPage < PageCount;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextPageAsync() => LoadPageAsync(CurrentPage + 1);

    private bool CanExport() => !IsExporting;

    /// <summary>Exports every transaction matching the filters (all pages, never just the visible one).</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var fileName = string.Create(CultureInfo.InvariantCulture, $"astra-transactions-{_clock.Today:yyyy-MM-dd}.csv");
        var path = _savePicker.PickSaveFile(Localization.GetString("Transactions.Export.Title"), fileName, Localization.GetString("Transactions.Export.Filter"));
        if (path is null)
        {
            return;
        }

        IsExporting = true;
        try
        {
            var count = await ExportToFileAsync(path).ConfigureAwait(true);
            _notifications.Notify(NotificationKind.Success, Localization.Format("Transactions.Export.Done", count), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Notify(NotificationKind.Error, Localization.GetString("Transactions.Export.Failed"), Localization.GetString("Transactions.Export.FailedBody"));
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>Writes the CSV and returns the number of exported transactions.</summary>
    public async Task<int> ExportToFileAsync(string path)
    {
        await LoadLookupsAsync().ConfigureAwait(true);
        var lookups = _lookups!;
        var currency = _format.Current.Currency;
        var rows = new List<IReadOnlyList<CsvCell>>();
        var page = 1;
        int total;
        do
        {
            var result = await _transactions.QueryAsync(BuildQuery(page, ExportBatchSize)).ConfigureAwait(true);
            total = result.TotalCount;
            foreach (var t in result.Items)
            {
                var signed = t.Type == TransactionType.Expense ? -t.Amount : t.Amount;
                rows.Add(
                [
                    _format.FormatDate(t.Date),
                    lookups.TypeName(t.Type),
                    t.Description,
                    t.Type == TransactionType.Transfer ? string.Empty : lookups.CategoryName(t.CategoryId),
                    lookups.AccountName(t.AccountId),
                    t.DestinationAccountId is null ? string.Empty : lookups.AccountName(t.DestinationAccountId),
                    new CsvCell(MoneyFormatter.FormatPlain(signed, currency), IsNumeric: true),
                    currency.Code,
                    t.Notes ?? string.Empty,
                    t.ParentTransactionId is null ? string.Empty : Localization.GetString("Transactions.Export.FeeMarker"),
                ]);
            }

            page++;
        }
        while (rows.Count < total);

        var headers = ExportColumns
            .Select(h => Localization.GetString($"Transactions.Export.Column.{h}"))
            .ToList();
        await CsvWriter.WriteAsync(path, headers, rows).ConfigureAwait(true);
        return rows.Count;
    }

    protected override void OnLanguageChanged() => _ = ReloadOptionsAndDataAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _changes.Changed -= OnDataChanged;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
        }

        base.Dispose(disposing);
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        _ = DebouncedReloadAsync(_searchCts.Token);
    }

    partial void OnSelectedPeriodChanged(SelectOption<PeriodPreset> value)
    {
        if (value?.Value == PeriodPreset.Custom && CustomFrom is null && CustomTo is null)
        {
            var month = DateRange.MonthOf(_clock.Today);
            _suppressReload = true;
            CustomFrom = month.Start;
            CustomTo = month.End;
            _suppressReload = false;
        }

        TriggerReload();
    }

    partial void OnCustomFromChanged(DateOnly? value) => TriggerReload();

    partial void OnCustomToChanged(DateOnly? value) => TriggerReload();

    partial void OnSelectedCategoryFilterChanged(SelectOption<Guid?>? value) => TriggerReload();

    partial void OnSelectedAccountFilterChanged(SelectOption<Guid?>? value) => TriggerReload();

    partial void OnShowDeletedChanged(bool value)
    {
        Selected = null;
        TriggerReload();
    }

    partial void OnSelectedChanged(TransactionRowViewModel? value) => _ = LoadSelectionExtrasAsync(value);

    private void SetTypeFilter(TransactionType? type)
    {
        TypeFilter = type;
        TriggerReload();
    }

    private void TriggerReload()
    {
        if (!_suppressReload)
        {
            _ = ReloadAsync();
        }
    }

    private async Task DebouncedReloadAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounce, token).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnDataChanged(object? sender, DataChangeKind e) => _ = ReloadOptionsAndDataAsync();

    private async Task ReloadOptionsAndDataAsync()
    {
        var selectedId = Selected?.Id;
        await LoadLookupsAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
        if (selectedId is { } id)
        {
            await ShowDetailsAsync(id).ConfigureAwait(true);
        }
    }

    private async Task LoadLookupsAsync()
    {
        _lookups = await FinanceLookups.LoadAsync(_categories, _accounts, Localization).ConfigureAwait(true);

        var categoryId = SelectedCategoryFilter?.Value;
        var accountId = SelectedAccountFilter?.Value;
        _suppressReload = true;
        CategoryOptions.Clear();
        CategoryOptions.Add(new SelectOption<Guid?>(null, Localization.GetString("Transactions.Filter.AllCategories")));
        foreach (var c in _lookups.Categories.Values.OrderBy(c => c.Kind).ThenBy(c => c.SortOrder))
        {
            CategoryOptions.Add(new SelectOption<Guid?>(c.Id, DefaultCategories.DisplayName(c, Localization)));
        }

        AccountOptions.Clear();
        AccountOptions.Add(new SelectOption<Guid?>(null, Localization.GetString("Transactions.Filter.AllAccounts")));
        foreach (var a in _lookups.Accounts.Values.OrderBy(a => a.SortOrder))
        {
            AccountOptions.Add(new SelectOption<Guid?>(a.Id, a.Name));
        }

        SelectedCategoryFilter = CategoryOptions.FirstOrDefault(o => o.Value == categoryId) ?? CategoryOptions[0];
        SelectedAccountFilter = AccountOptions.FirstOrDefault(o => o.Value == accountId) ?? AccountOptions[0];
        _suppressReload = false;
    }

    private Task ReloadAsync() => LoadPageAsync(1);

    private async Task LoadPageAsync(int page)
    {
        if (_lookups is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var query = BuildQuery(page, PageSize);
            var result = await _transactions.QueryAsync(query).ConfigureAwait(true);
            var summary = await _transactions.SummarizeAsync(query).ConfigureAwait(true);

            Rows.Clear();
            foreach (var t in result.Items)
            {
                Rows.Add(new TransactionRowViewModel(t, _lookups, _format));
            }

            CurrentPage = page;
            PageCount = result.PageCount;
            SummaryText = Localization.Format(
                "Transactions.Summary", summary.Count, _format.FormatMoney(summary.Income), _format.FormatMoney(summary.Expense));
            IsEmpty = result.TotalCount == 0;
            HasNoRecordsAtAll = IsEmpty && !HasActiveFilters && !ShowDeleted;
            OnPropertyChanged(nameof(HasActiveFilters));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ShowDetailsAsync(Guid id)
    {
        var row = Rows.FirstOrDefault(r => r.Id == id);
        if (row is null && await _transactions.GetAsync(id).ConfigureAwait(true) is { } t && _lookups is not null)
        {
            row = new TransactionRowViewModel(t, _lookups, _format);
        }

        Selected = row;
    }

    private async Task LoadSelectionExtrasAsync(TransactionRowViewModel? row)
    {
        SelectedFeeText = null;
        SelectedCreatedText = null;
        if (row is null)
        {
            return;
        }

        var local = row.Transaction.CreatedAtUtc.ToLocalTime();
        SelectedCreatedText = $"{_format.FormatDate(DateOnly.FromDateTime(local))} {local.ToString("t", Localization.Culture)}";
        if (row.Transaction.Type == TransactionType.Transfer && !row.IsDeleted
            && await _transactions.GetFeeAsync(row.Id).ConfigureAwait(true) is { } fee)
        {
            SelectedFeeText = _format.FormatMoney(fee.Amount);
        }
    }

    private (DateOnly? From, DateOnly? To) ResolvePeriod()
    {
        var today = _clock.Today;
        return SelectedPeriod?.Value switch
        {
            PeriodPreset.ThisMonth => (DateRange.MonthOf(today).Start, DateRange.MonthOf(today).End),
            PeriodPreset.LastMonth => (DateRange.MonthOf(today.AddMonths(-1)).Start, DateRange.MonthOf(today.AddMonths(-1)).End),
            PeriodPreset.Last3Months => (DateRange.MonthOf(today.AddMonths(-2)).Start, DateRange.MonthOf(today).End),
            PeriodPreset.ThisYear => (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
            PeriodPreset.Custom => (CustomFrom, CustomTo),
            _ => (null, null),
        };
    }
}
