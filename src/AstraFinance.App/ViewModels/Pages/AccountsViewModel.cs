using System.Collections.ObjectModel;
using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Dialogs;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

public sealed class AccountItemViewModel
{
    public AccountItemViewModel(AccountSummary summary, IRegionalFormatService format, ILocalizationService localization)
    {
        Summary = summary;
        BalanceText = format.FormatMoney(summary.Balance);
        TypeText = localization.GetString($"AccountType.{summary.Account.Type}");
    }

    public AccountSummary Summary { get; }

    public Guid Id => Summary.Account.Id;

    public string Name => Summary.Account.Name;

    public string IconKey => Summary.Account.IconKey;

    public string ColorHex => Summary.Account.ColorHex;

    public bool IsActive => Summary.Account.IsActive;

    public bool IsNegative => Summary.Balance < 0;

    public string TypeText { get; }

    public string BalanceText { get; }
}

/// <summary>One labelled figure in the balance breakdown.</summary>
public sealed record BreakdownRow(string Label, string Value, bool IsEmphasized = false);

/// <summary>An account-history row, signed from the perspective of the account being viewed.</summary>
public sealed record AccountHistoryRow(
    Guid Id, string DateText, string Title, string Subtitle, string AmountText, bool IsIncoming, string IconKey, bool IsAdjustment);

/// <summary>
/// Accounts: list with balances and total, account details (balance breakdown, transaction and
/// adjustment history), create/edit, documented balance adjustment and deactivation.
/// </summary>
public sealed partial class AccountsViewModel : PageViewModel
{
    public const int HistoryPageSize = 50;

    private readonly IAccountService _accounts;
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly IRegionalFormatService _format;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly Func<AccountEditorViewModel> _editorFactory;
    private readonly Func<BalanceAdjustmentViewModel> _adjustmentFactory;
    private readonly Func<TransactionFormViewModel> _transactionFormFactory;
    private readonly INavigationService _navigation;
    private readonly DataChangeNotifier _changes;
    private int _historyPage;

    public AccountsViewModel(
        ILocalizationService localization,
        IAccountService accounts,
        ITransactionService transactions,
        ICategoryService categories,
        IRegionalFormatService format,
        IDialogService dialogs,
        INotificationService notifications,
        Func<AccountEditorViewModel> editorFactory,
        Func<BalanceAdjustmentViewModel> adjustmentFactory,
        Func<TransactionFormViewModel> transactionFormFactory,
        INavigationService navigation,
        DataChangeNotifier changes)
        : base(AppPage.Accounts, localization)
    {
        _transactionFormFactory = transactionFormFactory;
        _navigation = navigation;
        _changes = changes;
        _changes.Changed += OnDataChanged;
        _accounts = accounts;
        _transactions = transactions;
        _categories = categories;
        _format = format;
        _dialogs = dialogs;
        _notifications = notifications;
        _editorFactory = editorFactory;
        _adjustmentFactory = adjustmentFactory;
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];

    [ObservableProperty]
    public partial string TotalBalanceText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowInactive { get; set; }

    public bool IsEmpty => !IsLoading && Accounts.Count == 0;

    // ---- Details
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsMode))]
    public partial AccountItemViewModel? Selected { get; private set; }

    public bool IsDetailsMode => Selected is not null;

    public ObservableCollection<BreakdownRow> Breakdown { get; } = [];

    public ObservableCollection<AccountHistoryRow> History { get; } = [];

    public ObservableCollection<AccountHistoryRow> Adjustments { get; } = [];

    [ObservableProperty]
    public partial bool HasMoreHistory { get; private set; }

    public bool HasNoHistory => History.Count == 0;

    public bool HasAdjustments => Adjustments.Count > 0;

    public override async Task OnNavigatedToAsync(NavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        await LoadListAsync().ConfigureAwait(true);
        if (target.EntityId is { } id)
        {
            await OpenDetailsAsync(id).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        var editor = _editorFactory();
        if (await _dialogs.ShowFormAsync(editor).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Accounts.Created.Title"), editor.SavedAccount!.Name);
            await LoadListAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task OpenAsync(AccountItemViewModel? item) =>
        item is null ? Task.CompletedTask : OpenDetailsAsync(item.Id);

    [RelayCommand]
    private async Task BackToListAsync()
    {
        Selected = null;
        await LoadListAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var editor = _editorFactory();
        editor.LoadForEdit(Selected.Summary.Account);
        if (await _dialogs.ShowFormAsync(editor).ConfigureAwait(true))
        {
            await OpenDetailsAsync(Selected.Id).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task AdjustBalanceAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var form = _adjustmentFactory();
        form.Load(Selected.Summary);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Accounts.Adjusted.Title"), Selected.Name);
            await OpenDetailsAsync(Selected.Id).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var deactivate = Selected.IsActive;
        if (deactivate)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                Localization.GetString("Accounts.Deactivate.Title"),
                Localization.Format("Accounts.Deactivate.Body", Selected.Name),
                Localization.GetString("Accounts.Deactivate.Confirm"),
                isDestructive: true).ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }
        }

        await _accounts.SetActiveAsync(Selected.Id, !deactivate).ConfigureAwait(true);
        await OpenDetailsAsync(Selected.Id).ConfigureAwait(true);
    }

    /// <summary>Transfer money from this account to another (same form and service as the Transactions page).</summary>
    [RelayCommand]
    private async Task TransferAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var form = _transactionFormFactory();
        await form.InitializeForCreateAsync(quick: false, TransactionType.Transfer, Selected.Id).ConfigureAwait(true);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Transactions.Saved.Title"), Localization.GetString("TransactionType.Transfer"));
        }
    }

    /// <summary>Opens a history row's transaction on the Transactions page.</summary>
    [RelayCommand]
    private Task OpenHistoryItemAsync(AccountHistoryRow? row) =>
        row is null || row.IsAdjustment
            ? Task.CompletedTask
            : _navigation.NavigateAsync(new NavigationTarget(AppPage.Transactions, row.Id));

    [RelayCommand]
    private async Task LoadMoreHistoryAsync()
    {
        if (Selected is not null)
        {
            await LoadHistoryPageAsync(Selected.Id, _historyPage + 1).ConfigureAwait(true);
        }
    }

    protected override void OnLanguageChanged()
    {
        _ = Selected is null ? LoadListAsync() : OpenDetailsAsync(Selected.Id);
    }

    partial void OnShowInactiveChanged(bool value) => _ = LoadListAsync();

    private void OnDataChanged(object? sender, DataChangeKind e) =>
        _ = Selected is null ? LoadListAsync() : OpenDetailsAsync(Selected.Id);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _changes.Changed -= OnDataChanged;
        }

        base.Dispose(disposing);
    }

    private async Task LoadListAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _accounts.ListAsync(includeInactive: ShowInactive).ConfigureAwait(true);
            Accounts.Clear();
            foreach (var summary in list)
            {
                Accounts.Add(new AccountItemViewModel(summary, _format, Localization));
            }

            TotalBalanceText = _format.FormatMoney(await _accounts.GetTotalBalanceAsync().ConfigureAwait(true));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task OpenDetailsAsync(Guid id)
    {
        var summary = await _accounts.GetAsync(id).ConfigureAwait(true);
        if (summary is null)
        {
            Selected = null;
            return;
        }

        Selected = new AccountItemViewModel(summary, _format, Localization);
        var c = summary.Components;
        Breakdown.Clear();
        Breakdown.Add(new(Localization.GetString("Accounts.Breakdown.Opening"), _format.FormatMoney(c.OpeningBalance)));
        Breakdown.Add(new(Localization.GetString("Accounts.Breakdown.Income"), Signed(c.Income)));
        Breakdown.Add(new(Localization.GetString("Accounts.Breakdown.Expense"), Signed(-c.Expense)));
        Breakdown.Add(new(Localization.GetString("Accounts.Breakdown.TransfersIn"), Signed(c.TransfersIn)));
        Breakdown.Add(new(Localization.GetString("Accounts.Breakdown.TransfersOut"), Signed(-c.TransfersOut)));
        Breakdown.Add(new(Localization.GetString("Accounts.Breakdown.Adjustments"), Signed(c.Adjustments)));
        Breakdown.Add(new(Localization.GetString("Accounts.Breakdown.Balance"), _format.FormatMoney(summary.Balance), IsEmphasized: true));

        Adjustments.Clear();
        foreach (var a in await _accounts.ListAdjustmentsAsync(id).ConfigureAwait(true))
        {
            Adjustments.Add(new AccountHistoryRow(
                a.Id,
                _format.FormatDate(a.Date),
                a.Reason,
                Localization.Format("Accounts.Adjustment.From", _format.FormatMoney(a.BalanceBefore), _format.FormatMoney(a.BalanceBefore + a.Amount)),
                Signed(a.Amount),
                a.Amount > 0,
                "Icon.Scale",
                IsAdjustment: true));
        }

        OnPropertyChanged(nameof(HasAdjustments));
        History.Clear();
        await LoadHistoryPageAsync(id, 1).ConfigureAwait(true);
    }

    private async Task LoadHistoryPageAsync(Guid accountId, int page)
    {
        var result = await _transactions.QueryAsync(new TransactionQuery
        {
            AccountIds = [accountId],
            Page = page,
            PageSize = HistoryPageSize,
        }).ConfigureAwait(true);

        var categories = (await _categories.ListAsync(includeInactive: true).ConfigureAwait(true)).ToDictionary(c => c.Id);
        var accounts = (await _accounts.ListAsync(includeInactive: true).ConfigureAwait(true)).ToDictionary(a => a.Account.Id, a => a.Account.Name);

        foreach (var t in result.Items)
        {
            History.Add(ToRow(t, accountId, categories, accounts));
        }

        _historyPage = page;
        HasMoreHistory = History.Count < result.TotalCount;
        OnPropertyChanged(nameof(HasNoHistory));
    }

    /// <summary>Formats a change with an explicit sign ("+Rp5.000", "-Rp5.000", "Rp0").</summary>
    private string Signed(long amount) => (amount > 0 ? "+" : string.Empty) + _format.FormatMoney(amount);

    private AccountHistoryRow ToRow(Transaction t, Guid accountId, Dictionary<Guid, Category> categories, Dictionary<Guid, string> accounts)
    {
        var category = t.CategoryId is { } cid && categories.TryGetValue(cid, out var cat) ? cat : null;
        var categoryName = category is null ? string.Empty : DefaultCategories.DisplayName(category, Localization);

        bool incoming;
        string title;
        string subtitle;
        string icon;
        if (t.Type == TransactionType.Transfer)
        {
            incoming = t.DestinationAccountId == accountId;
            var other = incoming ? t.AccountId : t.DestinationAccountId!.Value;
            var otherName = accounts.GetValueOrDefault(other, "?");
            title = string.IsNullOrWhiteSpace(t.Description)
                ? Localization.Format(incoming ? "Accounts.History.TransferFrom" : "Accounts.History.TransferTo", otherName)
                : t.Description;
            subtitle = Localization.GetString("TransactionType.Transfer");
            icon = "Icon.Transactions";
        }
        else
        {
            incoming = t.Type == TransactionType.Income;
            title = string.IsNullOrWhiteSpace(t.Description) ? categoryName : t.Description;
            subtitle = categoryName;
            icon = category?.IconKey ?? "Icon.Layers";
        }

        var amount = Signed(incoming ? t.Amount : -t.Amount);
        return new AccountHistoryRow(t.Id, _format.FormatDate(t.Date), title, subtitle, amount, incoming, icon, IsAdjustment: false);
    }
}
