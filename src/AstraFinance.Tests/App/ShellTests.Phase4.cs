using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Dialogs;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Entities;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;

namespace AstraFinance.Tests.App;

/// <summary>Phase 4: Quick Add, full form, Transactions page, export and search.</summary>
public sealed partial class ShellTests
{
    private async Task<(Guid Food, Guid Salary, Guid Cash, Guid Bank)> SeedAsync()
    {
        var categories = await Get<ICategoryService>().ListAsync(includeInactive: true);
        var bank = await Get<IAccountService>().CreateAsync(new AccountDraft("BCA", AccountType.BankAccount, 5_000_000, "Icon.Landmark", "#6AA6F2"));
        var cash = (await Get<IAccountService>().ListAsync()).Single(a => a.Account.Name == "Cash").Account.Id;
        return (categories.Single(c => c.SystemKey == "Food").Id, categories.Single(c => c.SystemKey == "Salary").Id, cash, bank.Id);
    }

    private Task<TransactionSaveResult> AddAsync(TransactionType type, long amount, Guid account, Guid? category, string description = "", DateOnly? date = null, Guid? destination = null, long fee = 0) =>
        Get<ITransactionService>().CreateAsync(new TransactionDraft
        {
            Type = type, Amount = amount, AccountId = account, CategoryId = category, DestinationAccountId = destination,
            Description = description, Date = date ?? new DateOnly(2026, 11, 9), TransferFee = fee,
        });

    [Fact]
    public async Task Quick_add_saves_through_the_shared_service_and_is_unavailable_while_locked()
    {
        await Shell.InitializeAsync();
        Assert.False(Shell.QuickAddCommand.CanExecute(null)); // onboarding gate

        await StartUnlockedAsync();
        var (food, _, cash, _) = await SeedAsync();
        Assert.True(Shell.QuickAddCommand.CanExecute(null));

        var dialogs = Get<DialogService>();
        var pending = Shell.QuickAddCommand.ExecuteAsync(null);
        var form = Assert.IsType<TransactionFormViewModel>(dialogs.ActiveDialog!.Form);
        Assert.True(form.IsQuick);
        Assert.Equal("Quick Add", form.Title);
        Assert.Equal(TransactionType.Expense, form.SelectedType);
        Assert.Equal(cash, form.SelectedAccount!.Id);

        // Missing amount and category → errors, dialog stays open.
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        Assert.NotNull(dialogs.ActiveDialog);
        Assert.NotNull(form.GetError(nameof(TransactionFormViewModel.AmountText)));
        Assert.NotNull(form.GetError(nameof(TransactionFormViewModel.SelectedCategory)));

        form.AmountText = "35.000";
        form.SelectedCategory = form.Categories.Single(c => c.Id == food);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await pending;

        var saved = Assert.Single((await Get<ITransactionService>().QueryAsync(new TransactionQuery())).Items);
        Assert.Equal(35_000, saved.Amount);
        Assert.Equal(food, saved.CategoryId);
        Assert.Equal("Transaction saved", Get<INotificationService>().History[0].Title);

        // A second submit of the same form (e.g. double click) saves nothing new.
        Assert.True(await form.SubmitAsync());
        Assert.Equal(1, (await Get<ITransactionService>().QueryAsync(new TransactionQuery())).TotalCount);
    }

    [Fact]
    public async Task Full_form_records_a_transfer_with_fee_and_edit_can_remove_the_fee()
    {
        await StartUnlockedAsync();
        var (_, _, cash, bank) = await SeedAsync();

        var form = Get<Func<TransactionFormViewModel>>()();
        await form.InitializeForCreateAsync(quick: false, TransactionType.Transfer, bank);
        Assert.True(form.IsTransfer);
        Assert.Equal(bank, form.SelectedAccount!.Id);
        Assert.Equal(cash, form.SelectedDestination!.Id);
        Assert.Empty(form.Categories); // switching type to transfer hides categories

        form.AmountText = "1.000.000";
        form.FeeText = "6.500";
        form.Description = "Top up cash";
        Assert.True(await form.SubmitAsync());

        var accounts = Get<IAccountService>();
        Assert.Equal(3_993_500, await accounts.GetBalanceAsync(bank));
        Assert.Equal(1_100_000, await accounts.GetBalanceAsync(cash));

        // Editing a fee row opens its transfer.
        var edit = Get<Func<TransactionFormViewModel>>()();
        Assert.True(await edit.InitializeForEditAsync(form.Saved!.Fee!.Id));
        Assert.True(edit.IsEdit);
        Assert.Equal("6.500", edit.FeeText);
        Assert.Equal("Top up cash", edit.Description);

        edit.FeeText = string.Empty;
        Assert.True(await edit.SubmitAsync());
        Assert.Equal(4_000_000, await accounts.GetBalanceAsync(bank));
    }

    [Fact]
    public async Task Form_maps_service_validation_to_fields()
    {
        await StartUnlockedAsync();
        await SeedAsync();
        var form = Get<Func<TransactionFormViewModel>>()();
        await form.InitializeForCreateAsync(quick: false, TransactionType.Transfer);
        form.AmountText = "10";
        form.SelectedDestination = form.SelectedAccount; // same account

        Assert.False(await form.SubmitAsync());
        Assert.Equal("Choose two different accounts for a transfer.", form.GetError(nameof(TransactionFormViewModel.SelectedDestination)));
    }

    [Fact]
    public async Task Transactions_page_filters_searches_sorts_and_summarizes()
    {
        await StartUnlockedAsync();
        var (food, salary, cash, bank) = await SeedAsync();
        await AddAsync(TransactionType.Income, 8_000_000, bank, salary, "November salary");
        await AddAsync(TransactionType.Expense, 45_000, cash, food, "Lunch");
        await AddAsync(TransactionType.Expense, 120_000, bank, food, "Groceries", new DateOnly(2026, 10, 20));
        await AddAsync(TransactionType.Transfer, 500_000, bank, null, destination: cash, fee: 2_500);

        await Shell.NavigateAsync(AppPage.Transactions);
        var page = Assert.IsType<TransactionsViewModel>(Shell.CurrentPage);
        Assert.Equal(5, page.Rows.Count);
        Assert.Equal("5 transactions · Income Rp8.000.000 · Expenses Rp167.500", page.SummaryText);
        Assert.Equal("Rp500.000", page.Rows.Single(r => r.Transaction.Type == TransactionType.Transfer).AmountText); // neutral
        Assert.Equal("-Rp45.000", page.Rows.Single(r => r.Title == "Lunch").AmountText);

        // Period: this month excludes October.
        page.SelectedPeriod = page.PeriodOptions.Single(o => o.Value == PeriodPreset.ThisMonth);
        await Settle();
        Assert.DoesNotContain(page.Rows, r => r.Title == "Groceries");

        // Type filter.
        page.TypeOptions.Single(o => o.Value == TransactionType.Income).IsSelected = true;
        await Settle();
        Assert.Equal("November salary", Assert.Single(page.Rows).Title);

        page.ClearFiltersCommand.Execute(null);
        await Settle();
        Assert.Equal(5, page.Rows.Count);

        // Search by localized category name and by amount.
        page.SearchText = "food";
        await Settle(TransactionsViewModel.SearchDebounce);
        Assert.Equal(2, page.Rows.Count);
        page.SearchText = "120.000";
        await Settle(TransactionsViewModel.SearchDebounce);
        Assert.Equal("Groceries", Assert.Single(page.Rows).Title);
        page.SearchText = string.Empty;
        await Settle(TransactionsViewModel.SearchDebounce);

        // Sort by amount (database-side).
        page.SortBy("Amount");
        await Settle();
        Assert.Equal(8_000_000, page.Rows[0].Transaction.Amount);
        page.SortBy("Amount");
        await Settle();
        Assert.Equal(2_500, page.Rows[0].Transaction.Amount);
    }

    [Fact]
    public async Task Transactions_page_pages_large_lists()
    {
        await StartUnlockedAsync();
        var (food, _, cash, _) = await SeedAsync();
        for (var i = 0; i < 105; i++)
        {
            await AddAsync(TransactionType.Expense, 1_000 + i, cash, food, $"Item {i}");
        }

        await Shell.NavigateAsync(AppPage.Transactions);
        var page = Assert.IsType<TransactionsViewModel>(Shell.CurrentPage);
        Assert.Equal(TransactionsViewModel.PageSize, page.Rows.Count);
        Assert.Equal("Page 1 of 2", page.PageText);
        Assert.False(page.PreviousPageCommand.CanExecute(null));

        await page.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(5, page.Rows.Count);
        Assert.False(page.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task Delete_requires_confirmation_and_restore_brings_it_back()
    {
        await StartUnlockedAsync();
        var (food, _, cash, _) = await SeedAsync();
        await AddAsync(TransactionType.Expense, 45_000, cash, food, "Lunch");
        await Shell.NavigateAsync(AppPage.Transactions);
        var page = Assert.IsType<TransactionsViewModel>(Shell.CurrentPage);
        var dialogs = Get<DialogService>();

        page.Selected = page.Rows.Single();
        var deleting = page.DeleteCommand.ExecuteAsync(null);
        Assert.True(dialogs.ActiveDialog!.IsDestructive);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await deleting;
        await Settle();

        Assert.Empty(page.Rows);
        Assert.Equal(100_000, await Get<IAccountService>().GetBalanceAsync(cash));

        page.ShowDeleted = true;
        await Settle();
        page.Selected = Assert.Single(page.Rows);
        Assert.True(page.Selected.IsDeleted);
        await page.RestoreCommand.ExecuteAsync(null);
        await Settle();

        Assert.Equal(55_000, await Get<IAccountService>().GetBalanceAsync(cash));
        page.ShowDeleted = false;
        await Settle();
        Assert.Single(page.Rows);
    }

    [Fact]
    public async Task Export_writes_every_filtered_transaction_across_pages()
    {
        await StartUnlockedAsync();
        var (food, salary, cash, bank) = await SeedAsync();
        for (var i = 0; i < 520; i++)
        {
            await AddAsync(TransactionType.Expense, 1_000, cash, food, $"Snack {i}");
        }

        await AddAsync(TransactionType.Income, 8_000_000, bank, salary, "Salary, November");
        await AddAsync(TransactionType.Transfer, 50_000, bank, null, "=cmd", destination: cash, fee: 2_500);

        await Shell.NavigateAsync(AppPage.Transactions);
        var page = Assert.IsType<TransactionsViewModel>(Shell.CurrentPage);

        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "all.csv");
        Assert.Equal(523, await page.ExportToFileAsync(path));
        var lines = (await File.ReadAllTextAsync(path)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(524, lines.Length); // header + all rows (more than one 500-row batch)
        Assert.Equal("Date,Type,Description,Category,Account,To account,Amount,Currency,Notes,Note", lines[0]);
        Assert.Contains("09/11/2026,Income,\"Salary, November\",Salary,BCA,,8000000,IDR,,", lines);
        Assert.Contains(lines, l => l.StartsWith("09/11/2026,Transfer,'=cmd,,BCA,Cash,50000,IDR", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(",-2500,IDR,,Transfer fee", StringComparison.Ordinal));

        // Filters are respected.
        page.TypeOptions.Single(o => o.Value == TransactionType.Income).IsSelected = true;
        await Settle();
        Assert.Equal(1, await page.ExportToFileAsync(path));

        // Export command uses the save picker and notifies.
        SavePicker.NextResult = Path.Combine(dir.Path, "picked.csv");
        await page.ExportCommand.ExecuteAsync(null);
        Assert.True(File.Exists(SavePicker.NextResult));
        Assert.Equal("1 transactions exported", Get<INotificationService>().History[0].Title);
    }

    [Fact]
    public async Task Search_results_open_transaction_details_or_category_filter()
    {
        await StartUnlockedAsync();
        var (food, _, cash, _) = await SeedAsync();
        var lunch = await AddAsync(TransactionType.Expense, 45_000, cash, food, "Team lunch");

        var groups = await Get<IGlobalSearchService>().SearchAsync("lunch", CancellationToken.None);
        var hit = Assert.Single(groups.Single(g => g.Category == SearchCategory.Transactions).Results);
        Assert.Equal("Team lunch", hit.Title);
        Assert.Contains("Rp45.000", hit.Subtitle, StringComparison.Ordinal);

        var byAmount = await Get<IGlobalSearchService>().SearchAsync("45.000", CancellationToken.None);
        Assert.Contains(byAmount, g => g.Category == SearchCategory.Transactions);

        await Get<INavigationService>().NavigateAsync(hit.Target);
        var page = Assert.IsType<TransactionsViewModel>(Shell.CurrentPage);
        Assert.Equal(lunch.Transaction.Id, page.Selected!.Id);

        var categoryHit = (await Get<IGlobalSearchService>().SearchAsync("food", CancellationToken.None))
            .Single(g => g.Category == SearchCategory.Categories).Results[0];
        Assert.Equal("Food & Drinks", categoryHit.Title);
        await Get<INavigationService>().NavigateAsync(categoryHit.Target);
        var filtered = Assert.IsType<TransactionsViewModel>(Shell.CurrentPage);
        Assert.Equal(food, filtered.SelectedCategoryFilter!.Value);
        Assert.Single(filtered.Rows);
    }

    [Fact]
    public async Task Account_transfer_action_prefills_the_transfer_form()
    {
        await StartUnlockedAsync();
        var (_, _, _, bank) = await SeedAsync();
        await Get<INavigationService>().NavigateAsync(new NavigationTarget(AppPage.Accounts, bank));
        var accounts = Assert.IsType<AccountsViewModel>(Shell.CurrentPage);

        var dialogs = Get<DialogService>();
        var pending = accounts.TransferCommand.ExecuteAsync(null);
        var form = Assert.IsType<TransactionFormViewModel>(dialogs.ActiveDialog!.Form);
        Assert.Equal(TransactionType.Transfer, form.SelectedType);
        Assert.Equal(bank, form.SelectedAccount!.Id);

        form.AmountText = "250.000";
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await pending;
        await Settle();

        Assert.Equal("Rp4.750.000", accounts.Breakdown[^1].Value); // page refreshed via change notification
        Assert.Single(accounts.History);
    }

    /// <summary>Lets fire-and-forget reloads (filters, change notifications) finish.</summary>
    private static async Task Settle(TimeSpan? extra = null)
    {
        await Task.Delay((extra ?? TimeSpan.Zero) + TimeSpan.FromMilliseconds(150));
    }
}
