using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Services;
using Microsoft.Win32;

namespace AstraFinance.App.Services;

/// <summary>What kind of data changed (pages reload when data they show changes).</summary>
public enum DataChangeKind
{
    Transactions = 0,
    Accounts = 1,
}

/// <summary>App-wide notification that financial data changed, e.g. after Quick Add from any page.</summary>
public sealed class DataChangeNotifier
{
    public event EventHandler<DataChangeKind>? Changed;

    public void Notify(DataChangeKind kind) => Changed?.Invoke(this, kind);
}

/// <summary>Native "Save as" dialog (native file dialogs are allowed by the design guidelines).</summary>
public interface IFileSavePicker
{
    string? PickSaveFile(string title, string defaultFileName, string filter);
}

public sealed class WindowsFileSavePicker : IFileSavePicker
{
    public string? PickSaveFile(string title, string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }
}

/// <summary>Snapshot of category and account names/icons used to present transactions.</summary>
public sealed class FinanceLookups
{
    private FinanceLookups(IReadOnlyDictionary<Guid, Category> categories, IReadOnlyDictionary<Guid, Account> accounts, ILocalizationService localization)
    {
        Categories = categories;
        Accounts = accounts;
        Localization = localization;
    }

    public IReadOnlyDictionary<Guid, Category> Categories { get; }

    public IReadOnlyDictionary<Guid, Account> Accounts { get; }

    public ILocalizationService Localization { get; }

    public static async Task<FinanceLookups> LoadAsync(ICategoryService categories, IAccountService accounts, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(accounts);
        var cats = (await categories.ListAsync(includeInactive: true).ConfigureAwait(true)).ToDictionary(c => c.Id);
        var accs = (await accounts.ListAsync(includeInactive: true).ConfigureAwait(true)).ToDictionary(a => a.Account.Id, a => a.Account);
        return new FinanceLookups(cats, accs, localization);
    }

    public string CategoryName(Guid? id) =>
        id is { } key && Categories.TryGetValue(key, out var c) ? DefaultCategories.DisplayName(c, Localization) : string.Empty;

    public string CategoryIcon(Guid? id) =>
        id is { } key && Categories.TryGetValue(key, out var c) ? c.IconKey : "Icon.Layers";

    public string AccountName(Guid? id) =>
        id is { } key && Accounts.TryGetValue(key, out var a) ? a.Name : "?";

    public string TypeName(TransactionType type) => Localization.GetString($"TransactionType.{type}");

    /// <summary>Description, or a sensible fallback (category name / "Transfer to X").</summary>
    public string Title(Transaction t, Guid? perspectiveAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(t);
        if (!string.IsNullOrWhiteSpace(t.Description))
        {
            return t.Description;
        }

        if (t.Type != TransactionType.Transfer)
        {
            return CategoryName(t.CategoryId);
        }

        var incoming = perspectiveAccountId is not null && t.DestinationAccountId == perspectiveAccountId;
        return incoming
            ? Localization.Format("Accounts.History.TransferFrom", AccountName(t.AccountId))
            : Localization.Format("Accounts.History.TransferTo", AccountName(t.DestinationAccountId));
    }

    /// <summary>Account column text: "Wallet" or "BCA → GoPay" for transfers.</summary>
    public string AccountText(Transaction t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return t.Type == TransactionType.Transfer
            ? $"{AccountName(t.AccountId)} → {AccountName(t.DestinationAccountId)}"
            : AccountName(t.AccountId);
    }
}

/// <summary>A transaction prepared for display in tables, lists and details.</summary>
public sealed class TransactionRowViewModel
{
    public TransactionRowViewModel(Transaction transaction, FinanceLookups lookups, IRegionalFormatService format, Guid? perspectiveAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(format);
        Transaction = transaction;
        DateText = format.FormatDate(transaction.Date);
        Title = lookups.Title(transaction, perspectiveAccountId);
        TypeText = lookups.TypeName(transaction.Type);
        AccountText = lookups.AccountText(transaction);
        IsFee = transaction.ParentTransactionId is not null;
        CategoryText = transaction.Type == TransactionType.Transfer
            ? lookups.TypeName(TransactionType.Transfer)
            : lookups.CategoryName(transaction.CategoryId);
        IconKey = transaction.Type == TransactionType.Transfer ? "Icon.Transactions" : lookups.CategoryIcon(transaction.CategoryId);

        // Direction: income +, expense −; transfers are neutral unless viewed from one account.
        Direction = transaction.Type switch
        {
            TransactionType.Income => 1,
            TransactionType.Expense => -1,
            _ when perspectiveAccountId is null => 0,
            _ => transaction.DestinationAccountId == perspectiveAccountId ? 1 : -1,
        };
        var money = format.FormatMoney(transaction.Amount);
        AmountText = Direction switch
        {
            1 => "+" + money,
            -1 => "-" + money,
            _ => money,
        };
        Notes = transaction.Notes;
    }

    public Transaction Transaction { get; }

    public Guid Id => Transaction.Id;

    public bool IsDeleted => Transaction.IsDeleted;

    public bool IsFee { get; }

    public string DateText { get; }

    public string Title { get; }

    public string CategoryText { get; }

    public string AccountText { get; }

    public string TypeText { get; }

    public string AmountText { get; }

    public string IconKey { get; }

    public string? Notes { get; }

    /// <summary>1 = money in, -1 = money out, 0 = neutral (transfer between own accounts).</summary>
    public int Direction { get; }

    public bool IsIncoming => Direction > 0;

    public bool IsNeutral => Direction == 0;
}
