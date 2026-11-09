using System.Collections.ObjectModel;
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
using AstraFinance.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

/// <summary>A bill prepared for display.</summary>
public sealed class BillRowViewModel
{
    public BillRowViewModel(BillSummary summary, IRegionalFormatService format, ILocalizationService localization, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(localization);
        Summary = summary;
        var bill = summary.Bill;
        CategoryName = summary.Category is null ? string.Empty : DefaultCategories.DisplayName(summary.Category, localization);
        AmountText = format.FormatMoney(bill.Amount);
        RecurrenceText = localization.GetString($"Recurrence.{bill.Recurrence}");
        NextDueText = format.FormatDate(summary.NextDue);
        StatusText = localization.GetString($"BillStatus.{summary.Status}");
        ScheduleText = $"{RecurrenceText}  ·  {summary.AccountName}";
        PayAutomationName = localization.Format("Bills.PayNamed", bill.Name);

        var days = summary.NextDue.DayNumber - today.DayNumber;
        DueText = summary.Status switch
        {
            BillStatus.Paused => localization.GetString("Bills.PausedHint"),
            BillStatus.Overdue when summary.MissedCount > 1 => localization.Format("Bills.Missed", summary.MissedCount, NextDueText),
            BillStatus.Overdue => localization.Format(-days == 1 ? "Bills.OverdueOne" : "Bills.OverdueDays", -days, NextDueText),
            BillStatus.DueToday => localization.Format("Bills.DueTodayOn", NextDueText),
            _ => localization.Format(days == 1 ? "Bills.DueInOne" : "Bills.DueInDays", days, NextDueText),
        };

        if (summary.LastHandled is { } last)
        {
            LastText = last.IsSkipped || last.Transaction is null
                ? localization.Format("Bills.LastSkipped", format.FormatDate(last.DueDate))
                : localization.Format("Bills.LastPaid", format.FormatMoney(last.Transaction.Amount), format.FormatDate(last.Transaction.Date));
        }
    }

    public BillSummary Summary { get; }

    public Guid Id => Summary.Bill.Id;

    public string Name => Summary.Bill.Name;

    public string IconKey => Summary.Category?.IconKey ?? "Icon.Bills";

    public string ColorHex => Summary.Category?.ColorHex ?? "#8A94A6";

    public string CategoryName { get; }

    public string AmountText { get; }

    public string RecurrenceText { get; }

    /// <summary>Recurrence and payment account.</summary>
    public string ScheduleText { get; }

    public string NextDueText { get; }

    /// <summary>"Due in 3 days (12/11/2026)", "Overdue by 2 days", "3 payments missed since ...".</summary>
    public string DueText { get; }

    public string? LastText { get; }

    public BillStatus Status => Summary.Status;

    public string StatusText { get; }

    public bool IsPaused => Summary.Status == BillStatus.Paused;

    public bool IsActive => !IsPaused;

    public bool CanSkipMissed => Summary.MissedCount > 1;

    /// <summary>Due soon, today or overdue: the row's Pay button is the primary action.</summary>
    public bool IsPayProminent => Summary.Status is BillStatus.DueSoon or BillStatus.DueToday or BillStatus.Overdue;

    public bool IsPayQuiet => Summary.Status == BillStatus.Upcoming;

    public string PayAutomationName { get; }

    public bool CanDelete => !Summary.HasHistory;

    public string? Notes => Summary.Bill.Notes;
}

/// <summary>One recorded occurrence in a bill's history.</summary>
public sealed record BillHistoryRow(Guid BillId, DateOnly DueDate, string DueText, string Caption, string AmountText, bool IsSkipped, bool IsVoided, bool CanUndo);

/// <summary>
/// Recurring bills and subscriptions. A due date never changes a balance: only "Pay" (a confirmed payment)
/// records an Expense; "Skip" marks an occurrence handled without one.
/// </summary>
public sealed partial class BillsViewModel : PageViewModel
{
    /// <summary>Look-ahead window for the "expected soon" total.</summary>
    public const int LookAheadDays = 30;

    private readonly IBillService _bills;
    private readonly IRegionalFormatService _format;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly DataChangeNotifier _changes;
    private readonly Func<BillFormViewModel> _formFactory;
    private readonly Func<PayBillFormViewModel> _payFactory;
    private readonly ReminderScheduler _reminders;
    private readonly IClock _clock;

    public BillsViewModel(
        ILocalizationService localization,
        IBillService bills,
        IRegionalFormatService format,
        IDialogService dialogs,
        INotificationService notifications,
        DataChangeNotifier changes,
        Func<BillFormViewModel> formFactory,
        Func<PayBillFormViewModel> payFactory,
        ReminderScheduler reminders,
        IClock clock)
        : base(AppPage.Bills, localization)
    {
        _bills = bills;
        _format = format;
        _dialogs = dialogs;
        _notifications = notifications;
        _changes = changes;
        _formFactory = formFactory;
        _payFactory = payFactory;
        _reminders = reminders;
        _clock = clock;
        _changes.Changed += OnDataChanged;
    }

    public ObservableCollection<BillRowViewModel> Bills { get; } = [];

    public ObservableCollection<BillHistoryRow> History { get; } = [];

    [ObservableProperty]
    public partial bool ShowPaused { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    /// <summary>Expected payments in the next 30 days at each bill's usual amount.</summary>
    [ObservableProperty]
    public partial string UpcomingTotalText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string UpcomingCountText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OverdueText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasOverdue { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial BillRowViewModel? Selected { get; private set; }

    public bool HasSelection => Selected is not null;

    public bool HasNoHistory => History.Count == 0;

    public override async Task OnNavigatedToAsync(NavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        await LoadAsync().ConfigureAwait(true);
        if (target.EntityId is { } id)
        {
            await SelectAsync(id).ConfigureAwait(true);
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var selectedId = Selected?.Id;
            var today = _clock.Today;
            var list = await _bills.ListAsync(today, includePaused: ShowPaused).ConfigureAwait(true);
            Bills.Clear();
            foreach (var bill in list)
            {
                Bills.Add(new BillRowViewModel(bill, _format, Localization, today));
            }

            IsEmpty = Bills.Count == 0;

            var active = list.Where(b => b.Status != BillStatus.Paused).ToList();
            var horizon = today.AddDays(LookAheadDays); // today and the 29 days after it
            long upcoming = 0;
            var count = 0;
            foreach (var b in active)
            {
                // Every open occurrence before the horizon (overdue ones included), at the usual amount.
                var n = BillSchedule.CountBetween(b.Bill.FirstDueDate, b.Bill.Recurrence, b.NextDue, horizon);
                upcoming = checked(upcoming + (b.Bill.Amount * n));
                count += n;
            }

            UpcomingTotalText = _format.FormatMoney(upcoming);
            UpcomingCountText = Localization.Format(count == 1 ? "Bills.UpcomingCountOne" : "Bills.UpcomingCount", count, LookAheadDays);
            var overdue = active.Where(b => b.Status == BillStatus.Overdue).ToList();
            HasOverdue = overdue.Count > 0;
            OverdueText = HasOverdue
                ? Localization.Format(overdue.Count == 1 ? "Bills.OverdueSummaryOne" : "Bills.OverdueSummary", overdue.Count)
                : Localization.GetString("Bills.NothingOverdue");

            if (selectedId is { } id)
            {
                await SelectAsync(id).ConfigureAwait(true);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectAsync(Guid id)
    {
        var row = Bills.FirstOrDefault(b => b.Id == id);
        if (row is null && await _bills.GetAsync(id, _clock.Today).ConfigureAwait(true) is { } summary)
        {
            row = new BillRowViewModel(summary, _format, Localization, _clock.Today);
        }

        Selected = row;
        History.Clear();
        if (row is not null)
        {
            var entries = await _bills.GetHistoryAsync(id).ConfigureAwait(true);
            var latestHandled = entries.FirstOrDefault(e => !e.IsVoided);
            foreach (var e in entries)
            {
                string caption;
                string amount;
                if (e.IsVoided)
                {
                    caption = Localization.GetString("Bills.History.Voided");
                    amount = string.Empty;
                }
                else if (e.IsSkipped || e.Transaction is null)
                {
                    caption = Localization.GetString("Bills.History.Skipped");
                    amount = string.Empty;
                }
                else
                {
                    caption = Localization.Format("Bills.History.Paid", _format.FormatDate(e.Transaction.Date), e.AccountName ?? string.Empty);
                    amount = "-" + _format.FormatMoney(e.Transaction.Amount);
                }

                History.Add(new BillHistoryRow(
                    id,
                    e.DueDate,
                    Localization.Format("Bills.History.Due", _format.FormatDate(e.DueDate)),
                    caption,
                    amount,
                    e.IsSkipped,
                    e.IsVoided,
                    CanUndo: e.IsSkipped && ReferenceEquals(e, latestHandled) && row.IsActive));
            }
        }

        OnPropertyChanged(nameof(HasNoHistory));
    }

    [RelayCommand]
    private void CloseDetails() => Selected = null;

    [RelayCommand]
    private async Task AddAsync()
    {
        var form = _formFactory();
        if (!await form.InitializeAsync(null).ConfigureAwait(true))
        {
            await _dialogs.AlertAsync(NotificationKind.Info, Localization.GetString("Bills.Add.Title"), Localization.GetString("Bills.NoAccounts")).ConfigureAwait(true);
            return;
        }

        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Bills.Saved.Title"), form.Saved!.Name);
            await LoadAsync().ConfigureAwait(true);
            await SelectAsync(form.Saved.Id).ConfigureAwait(true);
            _ = _reminders.CheckNowAsync();
        }
    }

    [RelayCommand]
    private async Task EditAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        var form = _formFactory();
        await form.InitializeAsync(row.Summary.Bill).ConfigureAwait(true);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Bills.Saved.Title"), form.Saved!.Name);
            await LoadAsync().ConfigureAwait(true);
            _ = _reminders.CheckNowAsync();
        }
    }

    [RelayCommand]
    private async Task PayAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null || row.IsPaused)
        {
            return;
        }

        var form = _payFactory();
        await form.InitializeAsync(row.Summary).ConfigureAwait(true);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Bills.Paid.Title"),
                Localization.Format("Bills.Paid.Body", row.Name, _format.FormatMoney(form.Saved!.Amount)));
            _changes.Notify(DataChangeKind.Transactions);
            await SelectAsync(row.Id).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task SkipAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null || row.IsPaused)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Localization.GetString("Bills.Skip.Title"),
            Localization.Format("Bills.Skip.Body", row.Name, row.NextDueText),
            Localization.GetString("Bills.Skip.Confirm")).ConfigureAwait(true);
        if (confirmed && await RunAsync(() => _bills.SkipAsync(row.Id, row.Summary.NextDue)).ConfigureAwait(true))
        {
            await ReloadAndSelectAsync(row.Id).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task SkipMissedAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null || !row.CanSkipMissed)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Localization.GetString("Bills.SkipMissed.Title"),
            Localization.Format("Bills.SkipMissed.Body", row.Name, row.Summary.MissedCount),
            Localization.GetString("Bills.Skip.Confirm")).ConfigureAwait(true);
        if (confirmed && await RunAsync(() => _bills.SkipMissedAsync(row.Id, _clock.Today)).ConfigureAwait(true))
        {
            await ReloadAndSelectAsync(row.Id).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task UndoSkipAsync(BillHistoryRow? entry)
    {
        if (entry is null || !entry.CanUndo)
        {
            return;
        }

        if (await RunAsync(() => _bills.UndoSkipAsync(entry.BillId, entry.DueDate)).ConfigureAwait(true))
        {
            await ReloadAndSelectAsync(entry.BillId).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task TogglePausedAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null)
        {
            return;
        }

        if (row.IsActive)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                Localization.GetString("Bills.Pause.Title"),
                Localization.Format("Bills.Pause.Body", row.Name),
                Localization.GetString("Bills.Pause")).ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }
        }

        if (await RunAsync(() => _bills.SetActiveAsync(row.Id, row.IsPaused)).ConfigureAwait(true))
        {
            if (row.IsActive && !ShowPaused)
            {
                Selected = null;
            }

            await ReloadAndSelectAsync(row.IsActive && !ShowPaused ? null : row.Id).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(BillRowViewModel? row)
    {
        row ??= Selected;
        if (row is null || !row.CanDelete)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Localization.GetString("Bills.Delete.Title"),
            Localization.Format("Bills.Delete.Body", row.Name),
            Localization.GetString("Bills.Delete.Confirm"),
            isDestructive: true).ConfigureAwait(true);
        if (confirmed && await RunAsync(() => _bills.DeleteAsync(row.Id)).ConfigureAwait(true))
        {
            Selected = null;
            await LoadAsync().ConfigureAwait(true);
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

    partial void OnShowPausedChanged(bool value) => _ = LoadAsync();

    private async Task ReloadAndSelectAsync(Guid? id)
    {
        _ = _reminders.CheckNowAsync();
        await LoadAsync().ConfigureAwait(true);
        if (id is { } value)
        {
            await SelectAsync(value).ConfigureAwait(true);
        }
    }

    /// <summary>Runs a bill operation; a validation failure (e.g. a stale page) is explained instead of thrown.</summary>
    private async Task<bool> RunAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
            return true;
        }
        catch (FinanceValidationException ex)
        {
            await _dialogs.AlertAsync(NotificationKind.Warning, Localization.GetString("Nav.Bills"),
                Localization.GetString($"Validation.{ex.Errors[0].Code}")).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            return false;
        }
    }

    private void OnDataChanged(object? sender, DataChangeKind e) => _ = LoadAsync();
}

/// <summary>Create or edit a recurring bill.</summary>
public sealed partial class BillFormViewModel : ValidatableViewModel, IFormDialog
{
    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.Ordinal)
    {
        ["Name"] = nameof(Name),
        ["Amount"] = nameof(AmountText),
        ["CategoryId"] = nameof(SelectedCategory),
        ["PaymentAccountId"] = nameof(SelectedAccount),
        ["FirstDueDate"] = nameof(FirstDueDate),
        ["Notes"] = nameof(Notes),
    };

    private readonly IBillService _bills;
    private readonly ICategoryService _categories;
    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;
    private readonly IClock _clock;
    private Guid? _billId;

    public BillFormViewModel(IBillService bills, ICategoryService categories, IAccountService accounts, IRegionalFormatService format, ILocalizationService localization, IClock clock)
    {
        _bills = bills;
        _categories = categories;
        _accounts = accounts;
        _format = format;
        _localization = localization;
        _clock = clock;
        RecurrenceOptions =
        [
            new(RecurrenceFrequency.Daily, () => localization.GetString("Recurrence.Daily"), null, r => Recurrence = r),
            new(RecurrenceFrequency.Weekly, () => localization.GetString("Recurrence.Weekly"), null, r => Recurrence = r),
            new(RecurrenceFrequency.Monthly, () => localization.GetString("Recurrence.Monthly"), null, r => Recurrence = r),
            new(RecurrenceFrequency.Yearly, () => localization.GetString("Recurrence.Yearly"), null, r => Recurrence = r),
        ];
        SelectRecurrence(RecurrenceFrequency.Monthly);
    }

    public ObservableCollection<CategoryOption> Categories { get; } = [];

    public ObservableCollection<AccountOption> Accounts { get; } = [];

    public IReadOnlyList<ChoiceOption<RecurrenceFrequency>> RecurrenceOptions { get; }

    public bool IsEdit => _billId is not null;

    public string Title => _localization.GetString(IsEdit ? "Bills.Edit.Title" : "Bills.Add.Title");

    public string PrimaryText => _localization.GetString("Common.Save");

    public bool IsDestructive => false;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AmountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CategoryOption? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial AccountOption? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial DateOnly? FirstDueDate { get; set; }

    [ObservableProperty]
    public partial RecurrenceFrequency Recurrence { get; private set; }

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    public Bill? Saved { get; private set; }

    /// <summary>Loads choices (and the bill being edited). Returns false when there is no active account to pay from.</summary>
    public async Task<bool> InitializeAsync(Bill? existing)
    {
        _billId = existing?.Id;
        Categories.Clear();
        foreach (var c in await _categories.ListAsync(CategoryKind.Expense, includeInactive: true).ConfigureAwait(true))
        {
            if (c.IsActive || c.Id == existing?.CategoryId)
            {
                Categories.Add(new CategoryOption(c.Id, DefaultCategories.DisplayName(c, _localization), c.IconKey, c.Kind));
            }
        }

        Accounts.Clear();
        foreach (var a in await _accounts.ListAsync(includeInactive: true).ConfigureAwait(true))
        {
            if (a.Account.IsActive || a.Account.Id == existing?.PaymentAccountId)
            {
                Accounts.Add(new AccountOption(a.Account.Id, a.Account.Name, a.Account.IconKey));
            }
        }

        if (existing is null)
        {
            var defaultCategory = await _categories.ListAsync(CategoryKind.Expense).ConfigureAwait(true);
            var utilities = defaultCategory.FirstOrDefault(c => c.SystemKey == "BillsUtilities");
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == utilities?.Id) ?? Categories.FirstOrDefault();
            SelectedAccount = Accounts.FirstOrDefault();
            FirstDueDate = _clock.Today;
        }
        else
        {
            Name = existing.Name;
            AmountText = MoneyFormatter.Format(existing.Amount, _format.Current, includeSymbol: false);
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == existing.CategoryId);
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == existing.PaymentAccountId);
            FirstDueDate = existing.FirstDueDate;
            SelectRecurrence(existing.Recurrence);
            Notes = existing.Notes ?? string.Empty;
        }

        ClearAllErrors();
        OnPropertyChanged(nameof(IsEdit));
        OnPropertyChanged(nameof(Title));
        return Accounts.Count > 0;
    }

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        if (string.IsNullOrWhiteSpace(Name))
        {
            SetError(nameof(Name), _localization.GetString("Validation.Required"));
        }

        if (!MoneyParser.TryParse(AmountText, _format.Current, out var amount) || amount <= 0)
        {
            SetError(nameof(AmountText), _localization.GetString(string.IsNullOrWhiteSpace(AmountText) ? "Validation.Required" : "Validation.AmountMustBePositive"));
        }

        if (SelectedCategory is null)
        {
            SetError(nameof(SelectedCategory), _localization.GetString("Validation.Required"));
        }

        if (SelectedAccount is null)
        {
            SetError(nameof(SelectedAccount), _localization.GetString("Validation.Required"));
        }

        if (FirstDueDate is null)
        {
            SetError(nameof(FirstDueDate), _localization.GetString("Validation.Required"));
        }

        if (HasErrors)
        {
            return false;
        }

        var draft = new BillDraft(Name, amount, SelectedCategory!.Id, SelectedAccount!.Id, FirstDueDate!.Value, Recurrence, Notes);
        try
        {
            Saved = _billId is { } id
                ? await _bills.UpdateAsync(id, draft).ConfigureAwait(true)
                : await _bills.CreateAsync(draft).ConfigureAwait(true);
            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, FieldMap);
            return false;
        }
    }

    partial void OnNameChanged(string value) => SetError(nameof(Name), null);

    partial void OnAmountTextChanged(string value) => SetError(nameof(AmountText), null);

    partial void OnSelectedCategoryChanged(CategoryOption? value) => SetError(nameof(SelectedCategory), null);

    partial void OnSelectedAccountChanged(AccountOption? value) => SetError(nameof(SelectedAccount), null);

    partial void OnFirstDueDateChanged(DateOnly? value) => SetError(nameof(FirstDueDate), null);

    private void SelectRecurrence(RecurrenceFrequency value)
    {
        foreach (var option in RecurrenceOptions)
        {
            option.SetSelectedSilently(option.Value == value);
        }

        Recurrence = value;
    }
}

/// <summary>Confirms the payment of a bill's next open occurrence, creating one Expense.</summary>
public sealed partial class PayBillFormViewModel : ValidatableViewModel, IFormDialog
{
    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.Ordinal)
    {
        ["Amount"] = nameof(AmountText),
        ["AccountId"] = nameof(SelectedAccount),
        ["Date"] = nameof(PaidDate),
        ["Notes"] = nameof(Note),
    };

    private readonly IBillService _bills;
    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;
    private BillSummary? _bill;

    /// <summary>Generated once per dialog, so a double-submitted confirmation records one payment.</summary>
    private readonly Guid _requestId = Guid.NewGuid();

    public PayBillFormViewModel(IBillService bills, IAccountService accounts, IRegionalFormatService format, ILocalizationService localization, IClock clock)
    {
        _bills = bills;
        _accounts = accounts;
        _format = format;
        _localization = localization;
        PaidDate = clock.Today;
    }

    public ObservableCollection<AccountOption> Accounts { get; } = [];

    public string Title => _localization.GetString("Bills.Pay.Title");

    public string PrimaryText => _localization.GetString("Bills.Pay.Confirm");

    public bool IsDestructive => false;

    public string BillName => _bill?.Bill.Name ?? string.Empty;

    public string DueText => _bill is null ? string.Empty : _localization.Format("Bills.Pay.Due", _format.FormatDate(_bill.NextDue));

    public string Explanation => _localization.GetString("Bills.Pay.Explanation");

    [ObservableProperty]
    public partial string AmountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AccountOption? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial DateOnly? PaidDate { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    public Transaction? Saved { get; private set; }

    public async Task InitializeAsync(BillSummary bill)
    {
        _bill = bill ?? throw new ArgumentNullException(nameof(bill));
        Accounts.Clear();
        foreach (var a in await _accounts.ListAsync().ConfigureAwait(true))
        {
            Accounts.Add(new AccountOption(a.Account.Id, a.Account.Name, a.Account.IconKey));
        }

        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == bill.Bill.PaymentAccountId) ?? Accounts.FirstOrDefault();
        AmountText = MoneyFormatter.Format(bill.Bill.Amount, _format.Current, includeSymbol: false);
        ClearAllErrors();
        OnPropertyChanged(nameof(BillName));
        OnPropertyChanged(nameof(DueText));
    }

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        if (!MoneyParser.TryParse(AmountText, _format.Current, out var amount) || amount <= 0)
        {
            SetError(nameof(AmountText), _localization.GetString(string.IsNullOrWhiteSpace(AmountText) ? "Validation.Required" : "Validation.AmountMustBePositive"));
        }

        if (SelectedAccount is null)
        {
            SetError(nameof(SelectedAccount), _localization.GetString("Validation.Required"));
        }

        if (PaidDate is null)
        {
            SetError(nameof(PaidDate), _localization.GetString("Validation.Required"));
        }

        if (HasErrors || _bill is null)
        {
            return false;
        }

        try
        {
            Saved = await _bills.PayAsync(new BillPaymentDraft(_requestId, _bill.Bill.Id, _bill.NextDue, amount, SelectedAccount!.Id, PaidDate!.Value, Note)).ConfigureAwait(true);
            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, FieldMap);
            return false;
        }
    }

    partial void OnAmountTextChanged(string value) => SetError(nameof(AmountText), null);

    partial void OnSelectedAccountChanged(AccountOption? value) => SetError(nameof(SelectedAccount), null);
}
