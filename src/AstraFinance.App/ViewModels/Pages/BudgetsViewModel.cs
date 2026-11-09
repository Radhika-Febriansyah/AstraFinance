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
using AstraFinance.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Pages;

/// <summary>A budget prepared for display (also used by the dashboard summary).</summary>
public sealed class BudgetRowViewModel
{
    public BudgetRowViewModel(BudgetStatusItem item, IRegionalFormatService format, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(localization);
        Item = item;
        var e = item.Evaluation;
        Name = DefaultCategories.DisplayName(item.Category, localization);
        LimitText = format.FormatMoney(e.Limit);
        SpentText = format.FormatMoney(e.Spent);
        RemainingText = e.Remaining >= 0
            ? localization.Format("Budgets.Left", format.FormatMoney(e.Remaining))
            : localization.Format("Budgets.Over", format.FormatMoney(-e.Remaining));
        BarPercent = Math.Min(100, e.UsagePercent);
        PercentText = string.Create(CultureInfo.InvariantCulture, $"{e.UsagePercent}%");
        StatusText = localization.GetString($"BudgetStatus.{e.Status}");
    }

    public BudgetStatusItem Item { get; }

    public Guid Id => Item.Budget.Id;

    public Guid CategoryId => Item.Category.Id;

    public string Name { get; }

    public string IconKey => Item.Category.IconKey;

    public string ColorHex => Item.Category.ColorHex;

    public string LimitText { get; }

    public string SpentText { get; }

    public string RemainingText { get; }

    /// <summary>Progress bar value, capped at 100.</summary>
    public long BarPercent { get; }

    public string PercentText { get; }

    public BudgetStatus Status => Item.Evaluation.Status;

    public string StatusText { get; }

    public string? Notes => Item.Budget.Notes;
}

/// <summary>Monthly budgets per expense category with usage and status.</summary>
public sealed partial class BudgetsViewModel : PageViewModel
{
    private readonly IBudgetService _budgets;
    private readonly IRegionalFormatService _format;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly INavigationService _navigation;
    private readonly DataChangeNotifier _changes;
    private readonly Func<BudgetFormViewModel> _formFactory;
    private readonly IClock _clock;

    public BudgetsViewModel(
        ILocalizationService localization,
        IBudgetService budgets,
        IRegionalFormatService format,
        IDialogService dialogs,
        INotificationService notifications,
        INavigationService navigation,
        DataChangeNotifier changes,
        Func<BudgetFormViewModel> formFactory,
        IClock clock)
        : base(AppPage.Budgets, localization)
    {
        _budgets = budgets;
        _format = format;
        _dialogs = dialogs;
        _notifications = notifications;
        _navigation = navigation;
        _changes = changes;
        _formFactory = formFactory;
        _clock = clock;
        Month = DateRange.MonthOf(clock.Today).Start;
        _changes.Changed += OnDataChanged;
    }

    public ObservableCollection<BudgetRowViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthText), nameof(IsCurrentMonth))]
    public partial DateOnly Month { get; private set; }

    public string MonthText => Month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Localization.Culture);

    public bool IsCurrentMonth => Month == DateRange.MonthOf(_clock.Today).Start;

    [ObservableProperty]
    public partial string TotalLimitText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TotalSpentText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TotalRemainingText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsTotalOver { get; private set; }

    [ObservableProperty]
    public partial long TotalBarPercent { get; private set; }

    [ObservableProperty]
    public partial string DefaultThresholdText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    public override Task OnNavigatedToAsync(NavigationTarget target) => LoadAsync();

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var overview = await _budgets.GetOverviewAsync(Month).ConfigureAwait(true);
            Items.Clear();
            foreach (var item in overview.Items)
            {
                Items.Add(new BudgetRowViewModel(item, _format, Localization));
            }

            TotalLimitText = _format.FormatMoney(overview.TotalLimit);
            TotalSpentText = _format.FormatMoney(overview.TotalSpent);
            IsTotalOver = overview.TotalRemaining < 0;
            TotalRemainingText = overview.TotalRemaining >= 0
                ? Localization.Format("Budgets.Left", _format.FormatMoney(overview.TotalRemaining))
                : Localization.Format("Budgets.Over", _format.FormatMoney(-overview.TotalRemaining));
            TotalBarPercent = Math.Min(100, MoneyMath.PercentOf(overview.TotalSpent, overview.TotalLimit));
            DefaultThresholdText = Localization.Format("Budgets.DefaultThreshold", await _budgets.GetDefaultWarningThresholdAsync().ConfigureAwait(true));
            IsEmpty = Items.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task PreviousMonthAsync()
    {
        Month = Month.AddMonths(-1);
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextMonthAsync()
    {
        Month = Month.AddMonths(1);
        return LoadAsync();
    }

    [RelayCommand]
    private Task ThisMonthAsync()
    {
        Month = DateRange.MonthOf(_clock.Today).Start;
        return LoadAsync();
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var form = _formFactory();
        await form.InitializeAsync(null, Month).ConfigureAwait(true);
        if (form.HasNoAvailableCategories)
        {
            await _dialogs.AlertAsync(NotificationKind.Info, Localization.GetString("Budgets.Add.Title"), Localization.GetString("Budgets.AllCategoriesBudgeted")).ConfigureAwait(true);
            return;
        }

        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _notifications.Notify(NotificationKind.Success, Localization.GetString("Budgets.Saved.Title"), form.SelectedCategory?.Name ?? string.Empty);
            _changes.Notify(DataChangeKind.Transactions);
        }
    }

    [RelayCommand]
    private async Task EditAsync(BudgetRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var form = _formFactory();
        await form.InitializeAsync(row.Item.Budget, Month).ConfigureAwait(true);
        if (await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            _changes.Notify(DataChangeKind.Transactions);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(BudgetRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Localization.GetString("Budgets.Delete.Title"),
            Localization.Format("Budgets.Delete.Body", row.Name),
            Localization.GetString("Transactions.Delete.Confirm"),
            isDestructive: true).ConfigureAwait(true);
        if (confirmed)
        {
            await _budgets.DeleteAsync(row.Id).ConfigureAwait(true);
            _changes.Notify(DataChangeKind.Transactions);
        }
    }

    /// <summary>Shows the expenses behind a budget.</summary>
    [RelayCommand]
    private Task OpenTransactionsAsync(BudgetRowViewModel? row) =>
        row is null ? Task.CompletedTask : _navigation.NavigateAsync(new NavigationTarget(AppPage.Transactions, row.CategoryId));

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(MonthText));
        _ = LoadAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _changes.Changed -= OnDataChanged;
        }

        base.Dispose(disposing);
    }

    private void OnDataChanged(object? sender, DataChangeKind e) => _ = LoadAsync();
}

/// <summary>Create/edit a monthly budget.</summary>
public sealed partial class BudgetFormViewModel : ValidatableViewModel, IFormDialog
{
    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.Ordinal)
    {
        ["CategoryId"] = nameof(SelectedCategory),
        ["Amount"] = nameof(AmountText),
        ["WarningThresholdPercent"] = nameof(ThresholdText),
        ["StartMonth"] = nameof(StartMonth),
        ["Notes"] = nameof(Notes),
    };

    private readonly IBudgetService _budgets;
    private readonly ICategoryService _categories;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;
    private Guid? _budgetId;

    public BudgetFormViewModel(IBudgetService budgets, ICategoryService categories, IRegionalFormatService format, ILocalizationService localization)
    {
        _budgets = budgets;
        _categories = categories;
        _format = format;
        _localization = localization;
    }

    public ObservableCollection<CategoryOption> Categories { get; } = [];

    public bool HasNoAvailableCategories => Categories.Count == 0;

    public bool IsEdit => _budgetId is not null;

    public string Title => _localization.GetString(IsEdit ? "Budgets.Edit.Title" : "Budgets.Add.Title");

    public string PrimaryText => _localization.GetString("Common.Save");

    public bool IsDestructive => false;

    [ObservableProperty]
    public partial CategoryOption? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string AmountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateOnly? StartMonth { get; set; }

    [ObservableProperty]
    public partial bool UseCustomThreshold { get; set; }

    [ObservableProperty]
    public partial string ThresholdText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DefaultThresholdHint { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    public Budget? Saved { get; private set; }

    public async Task InitializeAsync(Budget? existing, DateOnly month)
    {
        _budgetId = existing?.Id;
        var defaultThreshold = await _budgets.GetDefaultWarningThresholdAsync().ConfigureAwait(true);
        DefaultThresholdHint = _localization.Format("Budgets.Field.Threshold.Default", defaultThreshold);

        // Expense categories that don't have a budget yet (plus the one being edited).
        var budgeted = (await _budgets.ListAsync().ConfigureAwait(true)).Select(b => b.CategoryId).ToHashSet();
        Categories.Clear();
        foreach (var c in await _categories.ListAsync(CategoryKind.Expense, includeInactive: true).ConfigureAwait(true))
        {
            if ((c.IsActive && !budgeted.Contains(c.Id)) || c.Id == existing?.CategoryId)
            {
                Categories.Add(new CategoryOption(c.Id, DefaultCategories.DisplayName(c, _localization), c.IconKey, c.Kind));
            }
        }

        if (existing is null)
        {
            StartMonth = DateRange.MonthOf(month).Start;
            ThresholdText = defaultThreshold.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            SelectedCategory = Categories.First(c => c.Id == existing.CategoryId);
            AmountText = MoneyFormatter.Format(existing.Amount, _format.Current, includeSymbol: false);
            StartMonth = existing.StartDate;
            UseCustomThreshold = existing.WarningThresholdPercent is not null;
            ThresholdText = (existing.WarningThresholdPercent ?? defaultThreshold).ToString(CultureInfo.InvariantCulture);
            Notes = existing.Notes ?? string.Empty;
        }

        OnPropertyChanged(nameof(IsEdit));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasNoAvailableCategories));
    }

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        if (SelectedCategory is null)
        {
            SetError(nameof(SelectedCategory), _localization.GetString("Validation.Required"));
        }

        if (!MoneyParser.TryParse(AmountText, _format.Current, out var amount) || amount <= 0)
        {
            SetError(nameof(AmountText), _localization.GetString(string.IsNullOrWhiteSpace(AmountText) ? "Validation.Required" : "Validation.AmountMustBePositive"));
        }

        int? threshold = null;
        if (UseCustomThreshold)
        {
            if (int.TryParse(ThresholdText, NumberStyles.None, CultureInfo.InvariantCulture, out var t) && t is >= 1 and <= 100)
            {
                threshold = t;
            }
            else
            {
                SetError(nameof(ThresholdText), _localization.GetString("Validation.ThresholdOutOfRange"));
            }
        }

        if (StartMonth is null)
        {
            SetError(nameof(StartMonth), _localization.GetString("Validation.Required"));
        }

        if (HasErrors)
        {
            return false;
        }

        var draft = new BudgetDraft(SelectedCategory!.Id, amount, StartMonth!.Value, threshold, Notes);
        try
        {
            Saved = _budgetId is { } id
                ? await _budgets.UpdateAsync(id, draft).ConfigureAwait(true)
                : await _budgets.CreateAsync(draft).ConfigureAwait(true);
            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, FieldMap);
            return false;
        }
    }

    partial void OnAmountTextChanged(string value) => SetError(nameof(AmountText), null);

    partial void OnSelectedCategoryChanged(CategoryOption? value) => SetError(nameof(SelectedCategory), null);

    partial void OnThresholdTextChanged(string value) => SetError(nameof(ThresholdText), null);
}
