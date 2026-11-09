using AstraFinance.App.Services;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstraFinance.App.ViewModels.Dialogs;

/// <summary>Simple labelled option for combo boxes (ToString feeds accessibility and text search).</summary>
public sealed record SelectOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Create/edit account form. Used as a dialog and embedded in onboarding.</summary>
public sealed partial class AccountEditorViewModel : ValidatableViewModel, IFormDialog
{
    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.Ordinal)
    {
        ["Name"] = nameof(Name),
        ["OpeningBalance"] = nameof(OpeningBalanceText),
    };

    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;
    private Guid? _accountId;
    private bool _iconChosenByUser;

    public AccountEditorViewModel(IAccountService accounts, IRegionalFormatService format, ILocalizationService localization)
    {
        _accounts = accounts;
        _format = format;
        _localization = localization;
        TypeOptions = Enum.GetValues<AccountType>()
            .Select(t => new SelectOption<AccountType>(t, localization.GetString($"AccountType.{t}")))
            .ToList();
        SelectedType = TypeOptions[0];
        SelectedIcon = AccountDefaults.IconFor(SelectedType.Value);
        SelectedColor = AccountDefaults.Colors[0];
    }

    public IReadOnlyList<SelectOption<AccountType>> TypeOptions { get; }

    public IReadOnlyList<string> IconOptions { get; } = AccountDefaults.Icons;

    public IReadOnlyList<string> ColorOptions { get; } = AccountDefaults.Colors;

    public bool IsEditMode => _accountId is not null;

    public string Title => _localization.GetString(IsEditMode ? "Accounts.Edit.Title" : "Accounts.Add.Title");

    public string PrimaryText => _localization.GetString(IsEditMode ? "Common.Save" : "Accounts.Add.Primary");

    public bool IsDestructive => false;

    public string CurrencySymbol => _format.Current.Currency.Symbol;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SelectOption<AccountType> SelectedType { get; set; }

    [ObservableProperty]
    public partial string OpeningBalanceText { get; set; } = string.Empty;

    /// <summary>Formatted opening balance shown read-only when editing.</summary>
    [ObservableProperty]
    public partial string OpeningBalanceDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedIcon { get; set; }

    [ObservableProperty]
    public partial string SelectedColor { get; set; }

    public Account? SavedAccount { get; private set; }

    public void LoadForEdit(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _accountId = account.Id;
        Name = account.Name;
        SelectedType = TypeOptions.First(o => o.Value == account.Type);
        SelectedIcon = account.IconKey;
        _iconChosenByUser = true;
        SelectedColor = account.ColorHex;
        OpeningBalanceDisplay = _format.FormatMoney(account.OpeningBalance);
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PrimaryText));
    }

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        var name = Name.Trim();
        if (name.Length == 0)
        {
            SetError(nameof(Name), _localization.GetString("Validation.Required"));
        }

        long opening = 0;
        if (!IsEditMode && !string.IsNullOrWhiteSpace(OpeningBalanceText)
            && !MoneyParser.TryParseSigned(OpeningBalanceText, _format.Current, out opening))
        {
            SetError(nameof(OpeningBalanceText), _localization.GetString("Validation.InvalidAmount"));
        }

        if (HasErrors)
        {
            return false;
        }

        try
        {
            SavedAccount = IsEditMode
                ? await _accounts.UpdateAsync(_accountId!.Value, new AccountUpdate(name, SelectedType.Value, SelectedIcon, SelectedColor)).ConfigureAwait(true)
                : await _accounts.CreateAsync(new AccountDraft(name, SelectedType.Value, opening, SelectedIcon, SelectedColor)).ConfigureAwait(true);
            if (!IsEditMode)
            {
                LoadForEdit(SavedAccount);
            }

            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, FieldMap);
            return false;
        }
    }

    partial void OnSelectedTypeChanged(SelectOption<AccountType> value)
    {
        if (!_iconChosenByUser && value is not null)
        {
            SelectedIcon = AccountDefaults.IconFor(value.Value);
            _iconChosenByUser = false;
        }
    }

    partial void OnSelectedIconChanged(string value) => _iconChosenByUser = value != AccountDefaults.IconFor(SelectedType?.Value ?? AccountType.Cash);

    partial void OnNameChanged(string value) => SetError(nameof(Name), null);

    partial void OnOpeningBalanceTextChanged(string value) => SetError(nameof(OpeningBalanceText), null);
}

/// <summary>Documented balance correction: the user enters the actual balance and a reason.</summary>
public sealed partial class BalanceAdjustmentViewModel : ValidatableViewModel, IFormDialog
{
    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.Ordinal)
    {
        ["Reason"] = nameof(Reason),
        ["TargetBalance"] = nameof(NewBalanceText),
    };

    private readonly IAccountService _accounts;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;
    private Guid _accountId;
    private long _currentBalance;

    public BalanceAdjustmentViewModel(IAccountService accounts, IRegionalFormatService format, ILocalizationService localization, IClock clock)
    {
        _accounts = accounts;
        _format = format;
        _localization = localization;
        Date = clock.Today;
    }

    public string Title => _localization.GetString("Accounts.Adjust.Title");

    public string PrimaryText => _localization.GetString("Accounts.Adjust.Primary");

    public bool IsDestructive => false;

    public string AccountName { get; private set; } = string.Empty;

    public string CurrentBalanceText => _format.FormatMoney(_currentBalance);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DifferenceText))]
    public partial string NewBalanceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateOnly? Date { get; set; }

    /// <summary>Live preview of the recorded correction, e.g. "+Rp5.000".</summary>
    public string DifferenceText
    {
        get
        {
            if (!MoneyParser.TryParseSigned(NewBalanceText, _format.Current, out var target))
            {
                return string.Empty;
            }

            var delta = target - _currentBalance;
            return _localization.Format("Accounts.Adjust.Difference", (delta > 0 ? "+" : string.Empty) + _format.FormatMoney(delta));
        }
    }

    public BalanceAdjustment? SavedAdjustment { get; private set; }

    public void Load(AccountSummary account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _accountId = account.Account.Id;
        _currentBalance = account.Balance;
        AccountName = account.Account.Name;
        NewBalanceText = MoneyFormatter.Format(account.Balance, _format.Current, includeSymbol: false);
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(CurrentBalanceText));
    }

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        if (!MoneyParser.TryParseSigned(NewBalanceText, _format.Current, out var target))
        {
            SetError(nameof(NewBalanceText), _localization.GetString("Validation.InvalidAmount"));
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            SetError(nameof(Reason), _localization.GetString("Validation.Required"));
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
            SavedAdjustment = await _accounts.AdjustBalanceAsync(_accountId, target, Reason, Date!.Value).ConfigureAwait(true);
            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, FieldMap);
            return false;
        }
    }

    partial void OnNewBalanceTextChanged(string value) => SetError(nameof(NewBalanceText), null);

    partial void OnReasonChanged(string value) => SetError(nameof(Reason), null);
}
