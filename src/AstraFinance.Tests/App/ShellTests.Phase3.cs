using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Dialogs;
using AstraFinance.App.ViewModels.Gate;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Navigation;

namespace AstraFinance.Tests.App;

/// <summary>Phase 3: onboarding wizard, lock screen and Accounts page.</summary>
public sealed partial class ShellTests
{
    [Fact]
    public async Task Onboarding_wizard_completes_all_steps_and_opens_the_dashboard()
    {
        await Shell.InitializeAsync();
        var wizard = Assert.IsType<OnboardingViewModel>(Shell.Gate);
        Assert.Equal(OnboardingStep.Welcome, wizard.CurrentStep);

        // 1. Welcome → language switches live
        wizard.LanguageOptions.Single(o => o.Value == AppLanguage.Indonesian).IsSelected = true;
        await Task.Yield();
        Assert.Equal("Lanjut", wizard.NextText);
        await wizard.NextCommand.ExecuteAsync(null);

        // 2. Regional
        Assert.Equal(OnboardingStep.Regional, wizard.CurrentStep);
        Assert.Contains("Rp1.234.567", wizard.RegionalPreview, StringComparison.Ordinal);
        wizard.SelectedDateFormat = wizard.DateFormatOptions.Single(o => o.Value == DateFormatStyle.YearMonthDay);
        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Equal(DateFormatStyle.YearMonthDay, Get<IRegionalFormatService>().Current.DateStyle);

        // 3. First account (opening balance may be zero)
        Assert.Equal(OnboardingStep.FirstAccount, wizard.CurrentStep);
        var editor = wizard.AccountEditor!;
        Assert.Equal("Tunai", editor.Name); // default name in the chosen language
        editor.Name = "Dompet";
        editor.OpeningBalanceText = "0";
        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Equal(OnboardingStep.Pin, wizard.CurrentStep);
        Assert.False(wizard.CanChangeCurrency);

        // 4. PIN: mismatch is rejected, then accepted
        wizard.Pin = "1357";
        wizard.ConfirmPin = "1358";
        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Equal(OnboardingStep.Pin, wizard.CurrentStep);
        Assert.NotNull(wizard.GetError(nameof(OnboardingViewModel.ConfirmPin)));
        wizard.ConfirmPin = "1357";
        await wizard.NextCommand.ExecuteAsync(null);
        Assert.True(await Get<IPinService>().HasPinAsync());

        // 5. Backup
        Assert.Equal(OnboardingStep.Backup, wizard.CurrentStep);
        wizard.ScheduleOptions.Single(o => o.Value == BackupSchedule.Daily).IsSelected = true;
        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Equal(BackupSchedule.Daily, (await Get<IBackupService>().GetSettingsAsync()).Schedule);

        // 6. Completion → Dashboard
        Assert.Equal(OnboardingStep.Completion, wizard.CurrentStep);
        Assert.Equal("Dompet · Rp0", wizard.SummaryAccount);
        await wizard.NextCommand.ExecuteAsync(null);

        Assert.Null(Shell.Gate);
        Assert.IsType<DashboardViewModel>(Shell.CurrentPage);
        Assert.True((await Get<IOnboardingService>().GetStateAsync()).IsCompleted);
        Assert.Single(await Get<IAccountService>().ListAsync());
    }

    [Fact]
    public async Task Interrupted_onboarding_resumes_without_duplicating_the_account()
    {
        await Shell.InitializeAsync();
        var wizard = Assert.IsType<OnboardingViewModel>(Shell.Gate);
        await wizard.NextCommand.ExecuteAsync(null);                  // welcome
        await wizard.NextCommand.ExecuteAsync(null);                  // regional
        wizard.AccountEditor!.OpeningBalanceText = "150.000";
        await wizard.NextCommand.ExecuteAsync(null);                  // account created
        Assert.Equal(OnboardingStep.Pin, wizard.CurrentStep);

        // "Restart": a new wizard from persisted state.
        var state = await Get<IOnboardingService>().GetStateAsync();
        var resumed = Get<Func<OnboardingViewModel>>()();
        await resumed.InitializeAsync(state, () => Task.CompletedTask);
        Assert.Equal(OnboardingStep.Pin, resumed.CurrentStep);

        // Going back to the account step edits the same account instead of creating another.
        await resumed.BackCommand.ExecuteAsync(null);
        Assert.True(resumed.AccountEditor!.IsEditMode);
        resumed.AccountEditor.Name = "Cash renamed";
        await resumed.NextCommand.ExecuteAsync(null);

        var accounts = await Get<IAccountService>().ListAsync();
        var only = Assert.Single(accounts);
        Assert.Equal("Cash renamed", only.Account.Name);
        Assert.Equal(150_000, only.Balance);
    }

    [Fact]
    public async Task Account_editor_validates_name_and_amount_and_allows_negative_opening()
    {
        var editor = Get<Func<AccountEditorViewModel>>()();
        editor.Name = " ";
        editor.OpeningBalanceText = "12,5"; // IDR has no decimals
        Assert.False(await editor.SubmitAsync());
        Assert.NotNull(editor.GetError(nameof(AccountEditorViewModel.Name)));
        Assert.NotNull(editor.GetError(nameof(AccountEditorViewModel.OpeningBalanceText)));

        editor.Name = "Kartu Kredit";
        editor.OpeningBalanceText = "-1.250.000";
        Assert.True(await editor.SubmitAsync());
        Assert.Equal(-1_250_000, editor.SavedAccount!.OpeningBalance);
        Assert.True(editor.IsEditMode); // a second submit edits rather than duplicates
    }

    [Fact]
    public async Task Account_type_choice_suggests_an_icon_until_the_user_picks_one()
    {
        var editor = Get<Func<AccountEditorViewModel>>()();
        editor.SelectedType = editor.TypeOptions.Single(o => o.Value == AccountType.BankAccount);
        Assert.Equal("Icon.Landmark", editor.SelectedIcon);

        editor.SelectedIcon = "Icon.Store";
        editor.SelectedType = editor.TypeOptions.Single(o => o.Value == AccountType.EWallet);
        Assert.Equal("Icon.Store", editor.SelectedIcon);
    }

    [Fact]
    public async Task Accounts_page_lists_accounts_with_total_and_opens_details()
    {
        await StartUnlockedAsync();
        var accounts = Get<IAccountService>();
        var bank = await accounts.CreateAsync(new AccountDraft("BCA", AccountType.BankAccount, 2_000_000, "Icon.Landmark", "#6AA6F2"));

        await Shell.NavigateAsync(AppPage.Accounts);
        var page = Assert.IsType<AccountsViewModel>(Shell.CurrentPage);

        Assert.Equal(2, page.Accounts.Count);
        Assert.Equal("Rp2.100.000", page.TotalBalanceText);
        Assert.Equal("Bank account", page.Accounts.Single(a => a.Name == "BCA").TypeText);

        await page.OpenCommand.ExecuteAsync(page.Accounts.Single(a => a.Name == "BCA"));
        Assert.True(page.IsDetailsMode);
        Assert.Equal("Rp2.000.000", page.Breakdown[0].Value);
        Assert.Equal("Rp2.000.000", page.Breakdown[^1].Value);
        Assert.True(page.HasNoHistory);

        // Search result targets open the same details view.
        await Get<INavigationService>().NavigateAsync(new NavigationTarget(AppPage.Accounts, bank.Id));
        var viaSearch = Assert.IsType<AccountsViewModel>(Shell.CurrentPage);
        Assert.Equal(bank.Id, viaSearch.Selected!.Id);
    }

    [Fact]
    public async Task Balance_adjustment_dialog_records_a_documented_correction()
    {
        await StartUnlockedAsync();
        await Shell.NavigateAsync(AppPage.Accounts);
        var page = Assert.IsType<AccountsViewModel>(Shell.CurrentPage);
        await page.OpenCommand.ExecuteAsync(page.Accounts.Single());

        var dialogs = Get<DialogService>();
        var pending = page.AdjustBalanceCommand.ExecuteAsync(null);
        var form = Assert.IsType<BalanceAdjustmentViewModel>(dialogs.ActiveDialog!.Form);
        Assert.Equal("Rp100.000", form.CurrentBalanceText);

        form.NewBalanceText = "92.500";
        Assert.Equal("Adjustment to record: -Rp7.500", form.DifferenceText);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null); // no reason yet
        Assert.NotNull(dialogs.ActiveDialog);
        Assert.NotNull(form.GetError(nameof(BalanceAdjustmentViewModel.Reason)));

        form.Reason = "Cash count";
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await pending;

        Assert.Null(dialogs.ActiveDialog);
        Assert.Equal("Rp92.500", page.Breakdown[^1].Value);
        var row = Assert.Single(page.Adjustments);
        Assert.Equal("Cash count", row.Title);
        Assert.Equal("-Rp7.500", row.AmountText);
    }

    [Fact]
    public async Task Deactivating_requires_confirmation_and_keeps_the_account_in_the_total()
    {
        await StartUnlockedAsync();
        await Shell.NavigateAsync(AppPage.Accounts);
        var page = Assert.IsType<AccountsViewModel>(Shell.CurrentPage);
        await page.OpenCommand.ExecuteAsync(page.Accounts.Single());
        var dialogs = Get<DialogService>();

        // Cancel: nothing changes.
        var cancelled = page.ToggleActiveCommand.ExecuteAsync(null);
        Assert.True(dialogs.ActiveDialog!.IsDestructive);
        dialogs.ActiveDialog.CancelCommand.Execute(null);
        await cancelled;
        Assert.True(page.Selected!.IsActive);

        // Confirm.
        var confirmed = page.ToggleActiveCommand.ExecuteAsync(null);
        await dialogs.ActiveDialog!.AcceptCommand.ExecuteAsync(null);
        await confirmed;
        Assert.False(page.Selected!.IsActive);

        await page.BackToListCommand.ExecuteAsync(null);
        Assert.Empty(page.Accounts);
        Assert.True(page.IsEmpty);
        Assert.Equal("Rp100.000", page.TotalBalanceText);
        page.ShowInactive = true;
        await Task.Delay(50);
        Assert.Single(page.Accounts);
    }

    [Fact]
    public async Task Account_card_deactivation_uses_the_same_confirmation()
    {
        await StartUnlockedAsync();
        await Shell.NavigateAsync(AppPage.Accounts);
        var page = Assert.IsType<AccountsViewModel>(Shell.CurrentPage);
        var dialogs = Get<DialogService>();
        var card = page.Accounts.Single();

        // Cancel from the card: still active, still on the list.
        var cancelled = page.ToggleActiveItemCommand.ExecuteAsync(card);
        Assert.True(dialogs.ActiveDialog!.IsDestructive);
        dialogs.ActiveDialog.CancelCommand.Execute(null);
        await cancelled;
        Assert.True(page.Accounts.Single().IsActive);

        // Confirm from the card: deactivated, history kept (it reappears with "show inactive").
        var confirmed = page.ToggleActiveItemCommand.ExecuteAsync(page.Accounts.Single());
        await dialogs.ActiveDialog!.AcceptCommand.ExecuteAsync(null);
        await confirmed;
        Assert.False(page.IsDetailsMode);
        Assert.Empty(page.Accounts);
        page.ShowInactive = true;
        await Task.Delay(50);
        Assert.False(page.Accounts.Single().IsActive);
    }

    [Fact]
    public async Task Back_up_now_creates_a_backup_and_notifies()
    {
        await StartUnlockedAsync();
        var notifications = Get<INotificationService>();
        await Shell.BackUpNowCommand.ExecuteAsync(null);

        var latest = notifications.History[0];
        Assert.Equal(NotificationKind.Success, latest.Kind);
        Assert.True(File.Exists(latest.Message));
    }

    [Fact]
    public void Money_parse_uses_regional_format_from_the_service()
    {
        var format = Get<IRegionalFormatService>().Current;
        Assert.Equal(CurrencyInfo.Idr, format.Currency);
        Assert.True(MoneyParser.TryParse("1.000", format, out var v));
        Assert.Equal(1_000, v);
    }
}
