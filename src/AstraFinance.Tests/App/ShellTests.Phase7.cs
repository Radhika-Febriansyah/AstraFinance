using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;

namespace AstraFinance.Tests.App;

/// <summary>Phase 7: Bills page, payment confirmation, reminders and notification settings.</summary>
public sealed partial class ShellTests
{
    private async Task<Guid> UtilitiesAsync() =>
        (await Get<ICategoryService>().ListAsync(CategoryKind.Expense)).Single(c => c.SystemKey == "BillsUtilities").Id;

    private async Task<Guid> NewBillAsync(string name, DateOnly firstDue, Guid account, long amount = 350_000) =>
        (await Get<IBillService>().CreateAsync(new BillDraft(name, amount, await UtilitiesAsync(), account, firstDue, RecurrenceFrequency.Monthly, null))).Id;

    [Fact]
    public async Task Bills_page_adds_pays_skips_and_undoes_a_skip()
    {
        await StartUnlockedAsync();
        var (_, _, _, bank) = await SeedAsync();
        await Shell.NavigateAsync(AppPage.Bills);
        var page = Assert.IsType<BillsViewModel>(Shell.CurrentPage);
        Assert.True(page.IsEmpty);
        var dialogs = Get<DialogService>();

        // Add through the form: defaults are Bills & Utilities, monthly, due today.
        var adding = page.AddCommand.ExecuteAsync(null);
        var form = Assert.IsType<BillFormViewModel>(dialogs.ActiveDialog!.Form);
        Assert.Equal(await UtilitiesAsync(), form.SelectedCategory!.Id);
        Assert.Equal(RecurrenceFrequency.Monthly, form.Recurrence);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        Assert.Equal("This field is required.", form.GetError(nameof(BillFormViewModel.Name)));
        form.Name = "Electricity";
        form.AmountText = "350.000";
        form.FirstDueDate = new DateOnly(2026, 11, 11);
        form.SelectedAccount = form.Accounts.Single(a => a.Id == bank);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await adding;

        var row = Assert.Single(page.Bills);
        Assert.Equal(BillStatus.DueSoon, row.Status);
        Assert.StartsWith("Due in 2 days", row.DueText, StringComparison.Ordinal);
        Assert.True(row.IsPayProminent);
        Assert.Equal("Rp350.000", page.UpcomingTotalText);
        Assert.Equal(5_000_000, await Get<IAccountService>().GetBalanceAsync(bank)); // a bill alone changes nothing

        // Pay a different actual amount.
        var paying = page.PayCommand.ExecuteAsync(row);
        var pay = Assert.IsType<PayBillFormViewModel>(dialogs.ActiveDialog!.Form);
        Assert.Equal("350.000", pay.AmountText);
        Assert.Equal(bank, pay.SelectedAccount!.Id);
        pay.AmountText = "362.500";
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await paying;
        await Settle();

        Assert.Equal(5_000_000 - 362_500, await Get<IAccountService>().GetBalanceAsync(bank));
        Assert.Equal("Payment recorded", Get<INotificationService>().History[0].Title);
        row = Assert.Single(page.Bills);
        Assert.Equal(new DateOnly(2026, 12, 11), row.Summary.NextDue);
        Assert.Equal(BillStatus.Upcoming, row.Status);
        Assert.StartsWith("Last paid Rp362.500", row.LastText, StringComparison.Ordinal);
        Assert.Equal("-Rp362.500", Assert.Single(page.History).AmountText);

        // Skip the next one, then undo the skip.
        var skipping = page.SkipCommand.ExecuteAsync(row);
        await dialogs.ActiveDialog!.AcceptCommand.ExecuteAsync(null);
        await skipping;
        Assert.Equal(new DateOnly(2027, 1, 11), page.Selected!.Summary.NextDue);
        var skipped = page.History[0];
        Assert.True(skipped.CanUndo);
        Assert.Equal("Skipped (no expense recorded)", skipped.Caption);
        await page.UndoSkipCommand.ExecuteAsync(skipped);
        Assert.Equal(new DateOnly(2026, 12, 11), page.Selected!.Summary.NextDue);
        Assert.Equal(1, (await Get<ITransactionService>().QueryAsync(new TransactionQuery())).TotalCount);
    }

    [Fact]
    public async Task Bills_page_overdue_summary_skip_missed_pause_and_delete()
    {
        await StartUnlockedAsync();
        var (_, _, cash, _) = await SeedAsync();
        var internet = await NewBillAsync("Internet", new DateOnly(2026, 10, 1), cash);
        var streaming = await NewBillAsync("Streaming", new DateOnly(2026, 11, 25), cash, 65_000);

        await Shell.NavigateAsync(AppPage.Bills);
        var page = Assert.IsType<BillsViewModel>(Shell.CurrentPage);
        var dialogs = Get<DialogService>();
        Assert.True(page.HasOverdue);
        Assert.Equal("1 bill is overdue", page.OverdueText);
        var overdue = page.Bills[0];
        Assert.Equal(internet, overdue.Id);
        Assert.StartsWith("2 payments missed", overdue.DueText, StringComparison.Ordinal);
        Assert.True(overdue.CanSkipMissed);

        var skipping = page.SkipMissedCommand.ExecuteAsync(overdue);
        await dialogs.ActiveDialog!.AcceptCommand.ExecuteAsync(null);
        await skipping;
        Assert.False(page.HasOverdue);
        Assert.Equal(new DateOnly(2026, 12, 1), page.Bills.Single(b => b.Id == internet).Summary.NextDue);
        Assert.False(page.Bills.Single(b => b.Id == internet).CanDelete); // has history now

        // Pause (confirmed) hides the bill; it comes back with "Show paused".
        var pausing = page.TogglePausedCommand.ExecuteAsync(page.Bills.Single(b => b.Id == streaming));
        await dialogs.ActiveDialog!.AcceptCommand.ExecuteAsync(null);
        await pausing;
        Assert.DoesNotContain(page.Bills, b => b.Id == streaming);
        page.ShowPaused = true;
        await Settle();
        var paused = page.Bills.Single(b => b.Id == streaming);
        Assert.True(paused.IsPaused);
        Assert.False(paused.IsPayProminent || paused.IsPayQuiet);

        // No history: can be deleted (confirmed, destructive).
        var deleting = page.DeleteCommand.ExecuteAsync(paused);
        Assert.True(dialogs.ActiveDialog!.IsDestructive);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await deleting;
        Assert.DoesNotContain(page.Bills, b => b.Id == streaming);
    }

    [Fact]
    public async Task Reminders_reach_the_notification_list_and_windows_without_amounts_and_stop_while_locked()
    {
        await StartUnlockedAsync();
        var (_, _, cash, _) = await SeedAsync();
        var scheduler = Get<ReminderScheduler>();
        var notifications = Get<INotificationService>();
        Assert.True(scheduler.IsRunning);

        await NewBillAsync("Electricity", new DateOnly(2026, 11, 10), cash);
        await scheduler.CheckNowAsync();
        var reminder = notifications.History[0];
        Assert.Equal("Electricity is due soon", reminder.Title);
        Assert.Contains("Rp350.000", reminder.Message, StringComparison.Ordinal);
        var toast = Assert.Single(WindowsNotifier.Shown);
        Assert.Equal("Electricity is due soon", toast.Title);
        Assert.DoesNotContain("Rp", toast.Body, StringComparison.Ordinal); // no amounts outside the app

        await scheduler.CheckNowAsync();
        Assert.Single(WindowsNotifier.Shown); // shown once

        // Locked: nothing is checked or shown.
        await Shell.LockCommand.ExecuteAsync(null);
        Assert.False(scheduler.IsRunning);
        await NewBillAsync("Water", new DateOnly(2026, 11, 9), cash, 90_000);
        await scheduler.CheckNowAsync();
        Assert.Single(WindowsNotifier.Shown);

        // Unlock: the new reminder appears; with the app in the foreground the in-app list is enough.
        WindowsNotifier.IsAppInForeground = true;
        var gate = Assert.IsType<AstraFinance.App.ViewModels.Gate.LockViewModel>(Shell.Gate);
        gate.Pin = TestPin;
        await gate.UnlockCommand.ExecuteAsync(null);
        await Settle();
        Assert.True(scheduler.IsRunning);
        Assert.Equal("Water is due today", notifications.History[0].Title);
        Assert.Single(WindowsNotifier.Shown);
    }

    [Fact]
    public async Task Budget_warning_is_announced_after_spending_and_settings_control_reminders()
    {
        await StartUnlockedAsync();
        var (food, _, cash, _) = await SeedAsync();
        await Get<IBudgetService>().CreateAsync(new BudgetDraft(food, 100_000, new DateOnly(2026, 11, 1), null, null));

        await AddAsync(TransactionType.Expense, 90_000, cash, food);
        Get<DataChangeNotifier>().Notify(DataChangeKind.Transactions);
        await Settle();
        var warning = Get<INotificationService>().History[0];
        Assert.Equal("Food & Drinks budget is nearly used", warning.Title);
        Assert.Equal("90% of Rp100.000 used this month", warning.Message);
        Assert.Equal("90% used this month", Assert.Single(WindowsNotifier.Shown).Body);

        // Settings: preferences load, save and take effect.
        await Shell.NavigateAsync(AppPage.Settings);
        var settings = Assert.IsType<SettingsViewModel>(Shell.CurrentPage);
        Assert.True(settings.NotificationsEnabled);
        Assert.Equal(3, settings.SelectedReminderDays!.Days);
        Assert.Equal("80", settings.BudgetThresholdText);

        settings.SelectedReminderDays = settings.ReminderDayOptions.Single(o => o.Days == 7);
        await settings.LastSave;
        settings.WindowsNotifications = false;
        await settings.LastSave;
        var saved = await Get<INotificationSettingsService>().GetAsync();
        Assert.Equal((7, false), (saved.BillReminderDays, saved.WindowsNotifications));

        settings.BudgetThresholdText = "150";
        Assert.Equal("Enter a percentage from 1 to 100.", settings.BudgetThresholdError);
        settings.BudgetThresholdText = "95";
        await settings.LastSave;
        Assert.Null(settings.BudgetThresholdError);
        Assert.Equal(95, await Get<IBudgetService>().GetDefaultWarningThresholdAsync());

        // A bill 6 days away is "due soon" with a 7-day window; Windows notifications are now off.
        await NewBillAsync("Internet", new DateOnly(2026, 11, 15), cash);
        await Get<ReminderScheduler>().CheckNowAsync();
        Assert.Equal("Internet is due soon", Get<INotificationService>().History[0].Title);
        Assert.Single(WindowsNotifier.Shown);
    }

    [Fact]
    public async Task Bills_are_searchable_and_open_their_details()
    {
        await StartUnlockedAsync();
        var (_, _, cash, _) = await SeedAsync();
        var id = await NewBillAsync("Netflix", new DateOnly(2026, 11, 20), cash, 186_000);

        var hit = (await Get<IGlobalSearchService>().SearchAsync("netf", CancellationToken.None))
            .Single(g => g.Category == SearchCategory.Bills).Results[0];
        Assert.Equal("Netflix", hit.Title);
        Assert.StartsWith("Rp186.000, Monthly", hit.Subtitle, StringComparison.Ordinal);
        await Get<INavigationService>().NavigateAsync(hit.Target);
        Assert.Equal(id, Assert.IsType<BillsViewModel>(Shell.CurrentPage).Selected!.Id);
    }
}
