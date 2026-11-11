using System.Collections.ObjectModel;
using AstraFinance.App.Services;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels.Dialogs;

public sealed record CategoryOption(Guid Id, string Name, string IconKey, CategoryKind Kind)
{
    public override string ToString() => Name;
}

public sealed record AccountOption(Guid Id, string Name, string IconKey)
{
    public override string ToString() => Name;
}

/// <summary>
/// Quick Add (type, amount, category, account, date) and the full form (plus description, notes and
/// transfer fee) share this view model, its validation and <see cref="ITransactionService"/>.
/// The draft id is fixed when the form opens, so a double submit saves once.
/// </summary>
public sealed partial class TransactionFormViewModel : ValidatableViewModel, IFormDialog
{
    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.Ordinal)
    {
        ["Amount"] = nameof(AmountText),
        ["TransferFee"] = nameof(FeeText),
        ["FeeCategoryId"] = nameof(FeeText),
        ["CategoryId"] = nameof(SelectedCategory),
        ["AccountId"] = nameof(SelectedAccount),
        ["DestinationAccountId"] = nameof(SelectedDestination),
        ["Date"] = nameof(Date),
        ["Description"] = nameof(Description),
        ["Notes"] = nameof(Notes),
    };

    private readonly ITransactionService _transactions;
    private readonly IUiSoundPlayer _sounds;
    private readonly ICategoryService _categoryService;
    private readonly IAccountService _accountService;
    private readonly IRegionalFormatService _format;
    private readonly ILocalizationService _localization;
    private readonly DataChangeNotifier _changes;
    private readonly List<CategoryOption> _allCategories = [];
    private Guid _draftId = Guid.NewGuid();
    private bool _isEdit;

    public TransactionFormViewModel(
        ITransactionService transactions,
        ICategoryService categories,
        IAccountService accounts,
        IRegionalFormatService format,
        ILocalizationService localization,
        IClock clock,
        DataChangeNotifier changes,
        IUiSoundPlayer sounds)
    {
        _sounds = sounds;
        _transactions = transactions;
        _categoryService = categories;
        _accountService = accounts;
        _format = format;
        _localization = localization;
        _changes = changes;
        Date = clock.Today;
        TypeOptions =
        [
            new(TransactionType.Expense, () => localization.GetString("TransactionType.Expense"), "Icon.ArrowUpRight", t => SelectedType = t),
            new(TransactionType.Income, () => localization.GetString("TransactionType.Income"), "Icon.ArrowDownLeft", t => SelectedType = t),
            new(TransactionType.Transfer, () => localization.GetString("TransactionType.Transfer"), "Icon.Transactions", t => SelectedType = t),
        ];
        TypeOptions[0].SetSelectedSilently(true);
    }

    public IReadOnlyList<ChoiceOption<TransactionType>> TypeOptions { get; }

    public ObservableCollection<CategoryOption> Categories { get; } = [];

    public ObservableCollection<AccountOption> Accounts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransfer))]
    public partial TransactionType SelectedType { get; set; } = TransactionType.Expense;

    public bool IsTransfer => SelectedType == TransactionType.Transfer;

    /// <summary>Quick Add hides description, notes and the transfer fee.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(IsFull))]
    public partial bool IsQuick { get; private set; }

    public bool IsFull => !IsQuick;

    public bool IsEdit => _isEdit;

    public string Title => _localization.GetString(_isEdit ? "Transactions.Edit.Title" : IsQuick ? "Shell.QuickAdd" : "Transactions.Add.Title");

    public string PrimaryText => _localization.GetString("Common.Save");

    public bool IsDestructive => false;

    public string CurrencySymbol => _format.Current.Currency.Symbol;

    [ObservableProperty]
    public partial string AmountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FeeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CategoryOption? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial AccountOption? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial AccountOption? SelectedDestination { get; set; }

    [ObservableProperty]
    public partial DateOnly? Date { get; set; }

    public TransactionSaveResult? Saved { get; private set; }

    public async Task InitializeForCreateAsync(bool quick, TransactionType type = TransactionType.Expense, Guid? accountId = null)
    {
        IsQuick = quick;
        await LoadOptionsAsync(null).ConfigureAwait(true);
        SelectType(type);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == accountId) ?? Accounts.FirstOrDefault();
        if (IsTransfer)
        {
            SelectedDestination = Accounts.FirstOrDefault(a => a.Id != SelectedAccount?.Id);
        }
    }

    /// <summary>Loads a transaction for editing. A transfer fee opens its transfer (fees are edited with it).</summary>
    public async Task<bool> InitializeForEditAsync(Guid id)
    {
        var t = await _transactions.GetAsync(id).ConfigureAwait(true);
        if (t is { ParentTransactionId: { } parentId })
        {
            t = await _transactions.GetAsync(parentId).ConfigureAwait(true);
        }

        if (t is null || t.IsDeleted)
        {
            return false;
        }

        _isEdit = true;
        _draftId = t.Id;
        IsQuick = false;
        await LoadOptionsAsync(t).ConfigureAwait(true);
        SelectType(t.Type);
        AmountText = MoneyFormatter.Format(t.Amount, _format.Current, includeSymbol: false);
        Description = t.Description;
        Notes = t.Notes ?? string.Empty;
        Date = t.Date;
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == t.CategoryId);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == t.AccountId);
        SelectedDestination = Accounts.FirstOrDefault(a => a.Id == t.DestinationAccountId);

        if (t.Type == TransactionType.Transfer)
        {
            var fee = await _transactions.GetFeeAsync(t.Id).ConfigureAwait(true);
            FeeText = fee is null ? string.Empty : MoneyFormatter.Format(fee.Amount, _format.Current, includeSymbol: false);
        }

        OnPropertyChanged(nameof(IsEdit));
        OnPropertyChanged(nameof(Title));
        return true;
    }

    [RelayCommand]
    private void ShowMore() => IsQuick = false;

    public async Task<bool> SubmitAsync()
    {
        ClearAllErrors();
        var required = _localization.GetString("Validation.Required");

        if (!MoneyParser.TryParse(AmountText, _format.Current, out var amount) || amount <= 0)
        {
            SetError(nameof(AmountText), _localization.GetString(string.IsNullOrWhiteSpace(AmountText) ? "Validation.Required" : "Validation.AmountMustBePositive"));
        }

        long fee = 0;
        if (IsTransfer && !string.IsNullOrWhiteSpace(FeeText) && !MoneyParser.TryParse(FeeText, _format.Current, out fee))
        {
            SetError(nameof(FeeText), _localization.GetString("Validation.InvalidAmount"));
        }

        if (SelectedAccount is null)
        {
            SetError(nameof(SelectedAccount), required);
        }

        if (IsTransfer && SelectedDestination is null)
        {
            SetError(nameof(SelectedDestination), _localization.GetString("Validation.TransferDestinationRequired"));
        }

        if (!IsTransfer && SelectedCategory is null)
        {
            SetError(nameof(SelectedCategory), required);
        }

        if (Date is null)
        {
            SetError(nameof(Date), required);
        }

        if (HasErrors)
        {
            return false;
        }

        var draft = new TransactionDraft
        {
            Id = _draftId,
            Type = SelectedType,
            Amount = amount,
            Description = Description,
            Notes = Notes,
            CategoryId = IsTransfer ? null : SelectedCategory!.Id,
            AccountId = SelectedAccount!.Id,
            DestinationAccountId = IsTransfer ? SelectedDestination!.Id : null,
            Date = Date!.Value,
            TransferFee = IsTransfer ? fee : 0,
        };

        try
        {
            Saved = _isEdit
                ? await _transactions.UpdateAsync(draft).ConfigureAwait(true)
                : await _transactions.CreateAsync(draft).ConfigureAwait(true);
            _changes.Notify(DataChangeKind.Transactions);
            _sounds.Play(UiSound.TransactionSaved); // optional; silent unless enabled in Settings
            return true;
        }
        catch (FinanceValidationException ex)
        {
            ApplyValidation(ex, _localization, FieldMap);
            return false;
        }
    }

    partial void OnSelectedTypeChanged(TransactionType value)
    {
        var kind = value == TransactionType.Income ? CategoryKind.Income : CategoryKind.Expense;
        var keep = SelectedCategory;
        Categories.Clear();
        foreach (var c in _allCategories.Where(c => value != TransactionType.Transfer && c.Kind == kind))
        {
            Categories.Add(c);
        }

        SelectedCategory = keep is not null && Categories.Contains(keep) ? keep : null;
        if (value == TransactionType.Transfer && SelectedDestination is null)
        {
            SelectedDestination = Accounts.FirstOrDefault(a => a.Id != SelectedAccount?.Id);
        }

        SetError(nameof(SelectedCategory), null);
        SetError(nameof(SelectedDestination), null);
        GeneralError = null;
    }

    partial void OnAmountTextChanged(string value) => SetError(nameof(AmountText), null);

    partial void OnFeeTextChanged(string value) => SetError(nameof(FeeText), null);

    partial void OnSelectedCategoryChanged(CategoryOption? value) => SetError(nameof(SelectedCategory), null);

    partial void OnSelectedAccountChanged(AccountOption? value) => SetError(nameof(SelectedAccount), null);

    partial void OnSelectedDestinationChanged(AccountOption? value) => SetError(nameof(SelectedDestination), null);

    private void SelectType(TransactionType type)
    {
        foreach (var option in TypeOptions)
        {
            option.SetSelectedSilently(option.Value == type);
        }

        SelectedType = type;
        OnSelectedTypeChanged(type); // ensure categories load even when the type is unchanged
    }

    /// <summary>Active accounts/categories, plus any inactive ones the edited record already uses.</summary>
    private async Task LoadOptionsAsync(Transaction? editing)
    {
        var keepIds = new HashSet<Guid?> { editing?.AccountId, editing?.DestinationAccountId, editing?.CategoryId };

        _allCategories.Clear();
        foreach (var c in await _categoryService.ListAsync(includeInactive: true).ConfigureAwait(true))
        {
            if (c.IsActive || keepIds.Contains(c.Id))
            {
                _allCategories.Add(new CategoryOption(c.Id, DefaultCategories.DisplayName(c, _localization), c.IconKey, c.Kind));
            }
        }

        Accounts.Clear();
        foreach (var a in await _accountService.ListAsync(includeInactive: true).ConfigureAwait(true))
        {
            if (a.Account.IsActive || keepIds.Contains(a.Account.Id))
            {
                Accounts.Add(new AccountOption(a.Account.Id, a.Account.Name, a.Account.IconKey));
            }
        }
    }
}
