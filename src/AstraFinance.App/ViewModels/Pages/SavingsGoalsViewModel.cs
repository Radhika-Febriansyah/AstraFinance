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
using AstraFinance.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

/// <summary>A savings goal prepared for display.</summary>
public sealed class GoalCardViewModel
{
    public GoalCardViewModel(SavingsGoalSummary summary, IRegionalFormatService format, ILocalizationService localization, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(localization);
        Summary = summary;
        var g = summary.Goal;
        AllocatedText = format.FormatMoney(summary.Allocated);
        TargetText = format.FormatMoney(g.TargetAmount);
        BarPercent = Math.Min(100, summary.ProgressPercent);
        PercentText = string.Create(CultureInfo.InvariantCulture, $"{summary.ProgressPercent}%");
        RemainingText = summary.IsReached
            ? localization.GetString("Goals.Reached")
            : localization.Format("Goals.Remaining", format.FormatMoney(summary.Remaining));
        StatusText = localization.GetString($"GoalStatus.{g.Status}");

        if (g.TargetDate is { } date)
        {
            TargetDateText = localization.Format("Goals.TargetDate", format.FormatDate(date));
            if (!summary.IsReached && g.Status == SavingsGoalStatus.Active)
            {
                var months = ((date.Year - today.Year) * 12) + date.Month - today.Month;
                PaceText = months > 0
                    ? localization.Format("Goals.PerMonth", format.FormatMoney(CeilDiv(summary.Remaining, months)))
                    : localization.GetString(date < today ? "Goals.Overdue" : "Goals.DueThisMonth");
            }
        }
    }

    public SavingsGoalSummary Summary { get; }

    public Guid Id => Summary.Goal.Id;

    public string Name => Summary.Goal.Name;

    public string IconKey => Summary.Goal.IconKey;

    public string ColorHex => Summary.Goal.ColorHex;

    public string AllocatedText { get; }

    public string TargetText { get; }

    public long BarPercent { get; }

    public string PercentText { get; }

    public string RemainingText { get; }

    public string? TargetDateText { get; }

    /// <summary>Monthly amount needed to reach the target on time (active goals with a future date).</summary>
    public string? PaceText { get; }

    public SavingsGoalStatus Status => Summary.Goal.Status;

    public bool IsActive => Status == SavingsGoalStatus.Active;

    public bool IsArchived => Status == SavingsGoalStatus.Archived;

    public bool IsCompleted => Status == SavingsGoalStatus.Completed;

    public string StatusText { get; }

    private static long CeilDiv(long value, long divisor) => (value + divisor - 1) / divisor;
}

public sealed record GoalHistoryRow(string DateText, string AccountName, string AmountText, bool IsRelease, string? Note)
{
    /// <summary>Date, followed by the note only when there is one (no dangling separator).</summary>
    public string Caption => string.IsNullOrWhiteSpace(Note) ? DateText : $"{DateText}  ·  {Note}";
}

/// <summary>
/// Savings goals funded by allocating (earmarking) money already in accounts. Allocations never
/// change balances — the page says so explicitly.
/// </summary>
public sealed partial class SavingsGoalsViewModel : PageViewModel
{
    private readonly ISavingsGoalService _goals;
    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly DataChangeNotifier _changes;
    private readonly Func<GoalFormViewModel> _goalFormFactory;
    private readonly Func<AllocationFormViewModel> _allocationFormFactory;
    private readonly IClock _clock;

    public SavingsGoalsViewModel(
        ILocalizationService localization,
        ISavingsGoalService goals,
        IAccountService accounts,
        IRegionalFormatService format,
        IDialogService dialogs,
        INotificationService notifications,
        DataChangeNotifier changes,
        Func<GoalFormViewModel> goalFormFactory,
        Func<AllocationFormViewModel> allocationFormFactory,
        IClock clock)
        : base(AppPage.SavingsGoals, localization)
    {
        _goals = goals;
        _accounts = accounts;
        _format = format;
        _dialogs = dialogs;
        _notifications = notifications;
        _changes = changes;
        _goalFormFactory = goalFormFactory;
        _allocationFormFactory = allocationFormFactory;
        _clock = clock;
        _changes.Changed += OnDataChanged;
    }

    public ObservableCollection<GoalCardViewModel> Goals { get; } = [];

    [ObservableProperty]
    public partial bool ShowArchived { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    public partial string TotalAllocatedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? OverAllocationWarning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial GoalCardViewModel? Selected { get; private set; }

    public bool HasSelection => Selected is not null;

    public ObservableCollection<GoalHistoryRow> History { get; } = [];

    public ObservableCollection<string> Shares { get; } = [];

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
            var list = await _goals.ListAsync(includeArchived: ShowArchived).ConfigureAwait(true);
            Goals.Clear();
            foreach (var goal in list)
            {
                Goals.Add(new GoalCardViewModel(goal, _format, Localization, today));
            }

            IsEmpty = Goals.Count == 0;
            TotalAllocatedText = _format.FormatMoney(MoneyMath.Sum(list.Where(g => g.Goal.Status != SavingsGoalStatus.Archived).Select(g => g.Allocated)));

            // Spending can push an account below what's earmarked for goals; say so plainly.
            var allocations = await _goals.GetAccountAllocationsAsync().ConfigureAwait(true);
            var over = allocations.Where(a => a.IsOverAllocated).ToList();
            if (over.Count > 0)
            {
                var accounts = (await _accounts.ListAsync(includeInactive: true).ConfigureAwait(true)).ToDictionary(a => a.Account.Id, a => a.Account.Name);
                OverAllocationWarning = Localization.Format("Goals.OverAllocated", string.Join(", ", over.Select(o => accounts.GetValueOrDefault(o.AccountId, "?"))));
            }
            else
            {
                OverAllocationWarning = null;
            }

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
        var card = Goals.FirstOrDefault(g => g.Id == id);
        if (card is null && await _goals.GetAsync(id).ConfigureAwait(true) is { } summary)
        {
            card = new GoalCardViewModel(summary, _format, Localization, _clock.Today);
        }

        Selected = card;
        History.Clear();
        Shares.Clear();
        if (card is null)
        {
            return;
        }

        var accounts = (await _accounts.ListAsync(includeInactive: true).ConfigureAwait(true)).ToDictionary(a => a.Account.Id, a => a.Account.Name);
        foreach (var share in card.Summary.ByAccount)
        {
            Shares.Add($"{accounts.GetValueOrDefault(share.AccountId, "?")}: {_format.FormatMoney(share.Amount)}");
        }

        foreach (var entry in await _goals.GetHistoryAsync(id).ConfigureAwait(true))
        {
            History.Add(new GoalHistoryRow(
                _format.FormatDate(entry.Date),
                accounts.GetValueOrDefault(entry.AccountId, "?"),
                (entry.Amount > 0 ? "+" : string.Empty) + _format.FormatMoney(entry.Amount),
                entry.Amount < 0,
                entry.Note));
        }

        OnPropertyChanged(nameof(HasNoHistory));
    }

    [RelayCommand]
    private void CloseDetails() => Selected = null;

    [RelayCommand]
    private async Task AddAsync()
    {
        var form = _goalFormFactory();
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Goals.Created.Title"), form.Saved!.Name);
            await LoadAsync().ConfigureAwait(true);
            await SelectAsync(form.Saved.Id).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var form = _goalFormFactory();
        form.Load(Selected.Summary.Goal);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            await LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task AllocateAsync() => ShowAllocationAsync(release: false);

    [RelayCommand]
    private Task ReleaseAsync() => ShowAllocationAsync(release: true);

    [RelayCommand]
    private async Task MarkCompletedAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await _goals.SetStatusAsync(Selected.Id, SavingsGoalStatus.Completed).ConfigureAwait(true);
        _notifications.Notify(NotificationKind.Success, Localization.GetString("Goals.Completed.Title"), Selected.Name);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ReopenAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await _goals.SetStatusAsync(Selected.Id, SavingsGoalStatus.Active).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ArchiveAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Localization.GetString("Goals.Archive.Title"),
            Localization.Format("Goals.Archive.Body", Selected.Name, Selected.AllocatedText),
            Localization.GetString("Goals.Archive.Confirm"),
            isDestructive: true).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await _goals.ArchiveAsync(Selected.Id, _clock.Today).ConfigureAwait(true);
        Selected = null;
        await LoadAsync().ConfigureAwait(true);
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

    partial void OnShowArchivedChanged(bool value) => _ = LoadAsync();

    private async Task ShowAllocationAsync(bool release)
    {
        if (Selected is null)
        {
            return;
        }

        var form = _allocationFormFactory();
        if (!await form.InitializeAsync(Selected.Summary.Goal, release).ConfigureAwait(true))
        {
            await _dialogs.AlertAsync(NotificationKind.Info,
                Localization.GetString(release ? "Goals.Release.Title" : "Goals.Allocate.Title"),
                Localization.GetString(release ? "Goals.Release.Nothing" : "Goals.Allocate.NoAccounts")).ConfigureAwait(true);
            return;
        }

        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            await LoadAsync().ConfigureAwait(true);
        }
    }

    private void OnDataChanged(object? sender, DataChangeKind e) => _ = LoadAsync();
}

/// <summary>Create/edit a savings goal.</summary>
public sealed partial class GoalFormViewModel : ValidatableViewModel, IFormDialog
{
    public static readonly IReadOnlyList<string> GoalIcons =
        ["Icon.SavingsGoals", "Icon.PiggyBank", "Icon.Home", "Icon.Car", "Icon.GraduationCap", "Icon.Gift", "Icon.Smartphone", "Icon.Briefcase", "Icon.HeartPulse", "Icon.ShoppingBag"];

    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.Ordinal)
    {
        ["Name"] = nameof(Name),
        ["TargetAmount"] = nameof(TargetText),
    };

    private readonly ISavingsGoalService _goals;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;
    private Guid? _goalId;

    public GoalFormViewModel(ISavingsGoalService goals, IRegionalFormatService format, ILocalizationService localization)
    {
        _goals = goals;
        _format = format;
        _localization = localization;
        SelectedIcon = GoalIcons[0];
        SelectedColor = Core.Services.AccountDefaults.Colors[0];
    }

    public IReadOnlyList<string> IconOptions { get; } = GoalIcons;

    public IReadOnlyList<string> ColorOptions { get; } = Core.Services.AccountDefaults.Colors;

    public string Title => _localization.GetString(_goalId is null ? "Goals.Add.Title" : "Goals.Edit.Title");

    public string PrimaryText => _localization.GetString("Common.Save");

    public bool IsDestructive => false;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TargetText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateOnly? TargetDate { get; set; }

    [ObservableProperty]
    public partial string SelectedIcon { get; set; }

    [ObservableProperty]
    public partial string SelectedColor { get; set; }

    public SavingsGoal? Saved { get; private set; }

    public void Load(SavingsGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        _goalId = goal.Id;
        Name = goal.Name;
        TargetText = MoneyFormatter.Format(goal.TargetAmount, _format.Current, includeSymbol: false);
        TargetDate = goal.TargetDate;
        SelectedIcon = goal.IconKey;
        SelectedColor = goal.ColorHex;
        OnPropertyChanged(nameof(Title));
    }

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        if (string.IsNullOrWhiteSpace(Name))
        {
            SetError(nameof(Name), _localization.GetString("Validation.Required"));
        }

        if (!MoneyParser.TryParse(TargetText, _format.Current, out var target) || target <= 0)
        {
            SetError(nameof(TargetText), _localization.GetString(string.IsNullOrWhiteSpace(TargetText) ? "Validation.Required" : "Validation.AmountMustBePositive"));
        }

        if (HasErrors)
        {
            return false;
        }

        var draft = new SavingsGoalDraft(Name, target, TargetDate, SelectedIcon, SelectedColor);
        try
        {
            Saved = _goalId is { } id
                ? await _goals.UpdateAsync(id, draft).ConfigureAwait(true)
                : await _goals.CreateAsync(draft).ConfigureAwait(true);
            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, FieldMap);
            return false;
        }
    }

    partial void OnNameChanged(string value) => SetError(nameof(Name), null);

    partial void OnTargetTextChanged(string value) => SetError(nameof(TargetText), null);
}

/// <summary>An account choice with how much can be allocated from it (or released back to it).</summary>
public sealed record AllocationAccountOption(Guid Id, string Name, string IconKey, long Limit, string LimitText)
{
    public override string ToString() => Name;
}

/// <summary>Allocate money to a goal from an account, or release it back.</summary>
public sealed partial class AllocationFormViewModel : ValidatableViewModel, IFormDialog
{
    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.Ordinal)
    {
        ["Amount"] = nameof(AmountText),
        ["AccountId"] = nameof(SelectedAccount),
        ["Note"] = nameof(Note),
    };

    private readonly ISavingsGoalService _goals;
    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;
    private SavingsGoal? _goal;

    public AllocationFormViewModel(ISavingsGoalService goals, IAccountService accounts, IRegionalFormatService format, ILocalizationService localization, IClock clock)
    {
        _goals = goals;
        _accounts = accounts;
        _format = format;
        _localization = localization;
        Date = clock.Today;
    }

    public ObservableCollection<AllocationAccountOption> Accounts { get; } = [];

    public bool IsRelease { get; private set; }

    public string Title => _localization.GetString(IsRelease ? "Goals.Release.Title" : "Goals.Allocate.Title");

    public string PrimaryText => _localization.GetString(IsRelease ? "Goals.Release.Primary" : "Goals.Allocate.Primary");

    public bool IsDestructive => false;

    public string Explanation => _localization.GetString(IsRelease ? "Goals.Release.Explanation" : "Goals.Allocate.Explanation");

    public string GoalName => _goal?.Name ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LimitText))]
    public partial AllocationAccountOption? SelectedAccount { get; set; }

    public string LimitText => SelectedAccount is null
        ? string.Empty
        : _localization.Format(IsRelease ? "Goals.Release.Held" : "Goals.Allocate.Available", SelectedAccount.LimitText);

    [ObservableProperty]
    public partial string AmountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateOnly? Date { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>Returns false when there is no account to allocate from (or nothing to release).</summary>
    public async Task<bool> InitializeAsync(SavingsGoal goal, bool release)
    {
        _goal = goal ?? throw new ArgumentNullException(nameof(goal));
        IsRelease = release;
        var accounts = await _accounts.ListAsync(includeInactive: true).ConfigureAwait(true);
        Accounts.Clear();
        if (release)
        {
            var summary = await _goals.GetAsync(goal.Id).ConfigureAwait(true);
            foreach (var share in summary?.ByAccount.Where(s => s.Amount > 0) ?? [])
            {
                var account = accounts.First(a => a.Account.Id == share.AccountId).Account;
                Accounts.Add(new AllocationAccountOption(account.Id, account.Name, account.IconKey, share.Amount, _format.FormatMoney(share.Amount)));
            }
        }
        else
        {
            var info = (await _goals.GetAccountAllocationsAsync().ConfigureAwait(true)).ToDictionary(i => i.AccountId);
            foreach (var a in accounts.Where(a => a.Account.IsActive))
            {
                var available = info.TryGetValue(a.Account.Id, out var i) ? i.Available : Math.Max(0, a.Balance);
                Accounts.Add(new AllocationAccountOption(a.Account.Id, a.Account.Name, a.Account.IconKey, available, _format.FormatMoney(available)));
            }
        }

        SelectedAccount = Accounts.OrderByDescending(a => a.Limit).FirstOrDefault();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(Explanation));
        OnPropertyChanged(nameof(GoalName));
        return Accounts.Count > 0;
    }

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        if (SelectedAccount is null)
        {
            SetError(nameof(SelectedAccount), _localization.GetString("Validation.Required"));
        }

        if (!MoneyParser.TryParse(AmountText, _format.Current, out var amount) || amount <= 0)
        {
            SetError(nameof(AmountText), _localization.GetString(string.IsNullOrWhiteSpace(AmountText) ? "Validation.Required" : "Validation.AmountMustBePositive"));
        }

        if (Date is null)
        {
            SetError(nameof(Date), _localization.GetString("Validation.Required"));
        }

        if (HasErrors)
        {
            return false;
        }

        try
        {
            if (IsRelease)
            {
                await _goals.ReleaseAsync(_goal!.Id, SelectedAccount!.Id, amount, Date!.Value, Note).ConfigureAwait(true);
            }
            else
            {
                await _goals.AllocateAsync(_goal!.Id, SelectedAccount!.Id, amount, Date!.Value, Note).ConfigureAwait(true);
            }

            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, FieldMap);
            return false;
        }
    }

    partial void OnAmountTextChanged(string value) => SetError(nameof(AmountText), null);
}
