using AstraFinance.App.Services;
using AstraFinance.App.ViewModels.Gate;
using AstraFinance.App.ViewModels.Pages;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;

namespace AstraFinance.Tests.App;

/// <summary>Phase 9: settings sections, categories, regional formats, security, backups/restore, import/export.</summary>
public sealed partial class ShellTests
{
    private async Task<SettingsViewModel> OpenSettingsAsync(SettingsSection section)
    {
        await Shell.NavigateAsync(AppPage.Settings);
        var page = Assert.IsType<SettingsViewModel>(Shell.CurrentPage);
        page.SectionOptions.Single(o => o.Value == section).IsSelected = true;
        Assert.Equal(section, page.Section);
        return page;
    }

    [Fact]
    public async Task Settings_categories_add_edit_and_deactivate()
    {
        await StartUnlockedAsync();
        var page = await OpenSettingsAsync(SettingsSection.Categories);
        Assert.Equal(7, page.SectionOptions.Count);
        var categories = page.Categories;
        Assert.False(categories.Expense.Single(c => c.Category.SystemKey == "Fees").CanDeactivate);
        var dialogs = Get<DialogService>();

        var adding = categories.AddExpenseCommand.ExecuteAsync(null);
        var form = Assert.IsType<CategoryFormViewModel>(dialogs.ActiveDialog!.Form);
        Assert.Equal("Add expense category", form.Title);
        form.Name = "Pets";
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await adding;
        Assert.Contains(categories.Expense, c => c.Name == "Pets" && !c.IsBuiltIn);

        // Renaming a built-in category; keeping its name keeps it translated.
        var editing = categories.EditCommand.ExecuteAsync(categories.Expense.Single(c => c.Category.SystemKey == "Food"));
        var edit = Assert.IsType<CategoryFormViewModel>(dialogs.ActiveDialog!.Form);
        Assert.True(edit.IsBuiltIn);
        edit.SelectedColor = edit.ColorOptions[2];
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await editing;
        var food = categories.Expense.Single(c => c.Category.SystemKey == "Food");
        Assert.Equal(("Food & Drinks", edit.ColorOptions[2]), (food.Name, food.ColorHex));

        // Deactivate (confirmed): hidden unless deactivated categories are shown.
        var gift = categories.Income.Single(c => c.Category.SystemKey == "Gift");
        var deactivating = categories.ToggleActiveCommand.ExecuteAsync(gift);
        await dialogs.ActiveDialog!.AcceptCommand.ExecuteAsync(null);
        await deactivating;
        Assert.DoesNotContain(categories.Income, c => c.Category.SystemKey == "Gift");
        categories.ShowInactive = true;
        await categories.LoadAsync();
        Assert.False(categories.Income.Single(c => c.Category.SystemKey == "Gift").IsActive);
        Assert.DoesNotContain(await Get<ICategoryService>().ListAsync(CategoryKind.Income), c => c.SystemKey == "Gift");
    }

    [Fact]
    public async Task Settings_regional_formats_apply_everywhere()
    {
        await StartUnlockedAsync();
        var page = await OpenSettingsAsync(SettingsSection.General);
        Assert.Equal("IDR (Rp)", page.CurrencyText);

        page.SelectedDateFormat = page.DateFormatOptions.Single(o => o.Value == DateFormatStyle.YearMonthDay);
        await page.LastSave;
        page.SelectedNumberFormat = page.NumberFormatOptions.Single(o => o.Value == NumberFormatStyle.CommaGroupDotDecimal);
        await page.LastSave;

        var format = Get<IRegionalFormatService>();
        Assert.Equal((DateFormatStyle.YearMonthDay, NumberFormatStyle.CommaGroupDotDecimal), (format.Current.DateStyle, format.Current.NumberStyle));
        Assert.Equal("Rp1,500,000", format.FormatMoney(1_500_000));
        Assert.Equal("2026-12-31", format.FormatDate(new DateOnly(2026, 12, 31)));
        Assert.Equal("IDR", format.Current.Currency.Code); // the currency never changes here
    }

    [Fact]
    public async Task Settings_security_changes_pin_saves_cooldown_and_locks()
    {
        await StartUnlockedAsync();
        var page = await OpenSettingsAsync(SettingsSection.Security);
        var security = page.Security;
        var dialogs = Get<DialogService>();
        Assert.Equal(5, security.SelectedAttempts!.Value);

        var changing = security.ChangePinCommand.ExecuteAsync(null);
        var form = Assert.IsType<ChangePinFormViewModel>(dialogs.ActiveDialog!.Form);
        form.CurrentPin = "1111";
        form.NewPin = "135790";
        form.ConfirmPin = "135790";
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        Assert.Equal("Incorrect PIN. 4 attempt(s) left before a cooldown.", form.GetError(nameof(ChangePinFormViewModel.CurrentPin)));
        form.CurrentPin = TestPin;
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await changing;
        Assert.Equal(PinCheckOutcome.Success, (await Get<IPinService>().VerifyAsync("135790")).Outcome);

        security.SelectedAttempts = security.AttemptOptions.Single(o => o.Value == 3);
        await security.LastSave;
        security.SelectedCooldown = security.CooldownOptions.Single(o => o.Value == 300);
        await security.LastSave;
        Assert.Equal(new PinAttemptSettings(3, TimeSpan.FromMinutes(5)), await Get<IPinSecuritySettings>().GetAttemptSettingsAsync());
        Assert.Equal("After 3 wrong PINs, entry pauses for 5 min; each further wrong PIN doubles the pause, up to 40 min.", security.CooldownSummary);

        await security.LockNowCommand.ExecuteAsync(null);
        Assert.IsType<LockViewModel>(Shell.Gate);
    }

    [Fact]
    public async Task Settings_backup_now_and_restore_through_the_shell()
    {
        await StartUnlockedAsync();
        var (food, _, cash, _) = await SeedAsync();
        await AddAsync(TransactionType.Expense, 10_000, cash, food, "Before backup");
        var page = await OpenSettingsAsync(SettingsSection.Data);
        var data = page.Data;
        await data.BackUpNowCommand.ExecuteAsync(null);
        var backup = data.History.Single(b => b.KindText == "Manual backup"); // the scheduled automatic backup also ran at unlock

        await AddAsync(TransactionType.Expense, 25_000, cash, food, "After backup");
        Assert.Equal(65_000, await Get<IAccountService>().GetBalanceAsync(cash));

        var dialogs = Get<DialogService>();
        var restoring = data.RestoreCommand.ExecuteAsync(backup);
        await Settle();
        Assert.True(dialogs.ActiveDialog!.IsDestructive);
        Assert.Contains("1 transactions", dialogs.ActiveDialog.Message, StringComparison.Ordinal);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await restoring;

        // Restored, then locked: the backup's PIN applies.
        var gate = Assert.IsType<LockViewModel>(Shell.Gate);
        Assert.Equal(90_000, await Get<IAccountService>().GetBalanceAsync(cash));
        Assert.Equal("Backup restored", Get<INotificationService>().History[0].Title);
        gate.Pin = TestPin;
        await gate.UnlockCommand.ExecuteAsync(null);
        Assert.Null(Shell.Gate);
        Assert.IsType<DashboardViewModel>(Shell.CurrentPage);

        // An invalid file is refused before anything happens.
        var bogus = Path.Combine(_dir.Path, "bogus.db");
        await File.WriteAllTextAsync(bogus, "nope");
        OpenPicker.NextResult = bogus;
        var settings = await OpenSettingsAsync(SettingsSection.Data);
        var refused = settings.Data.RestoreFromFileCommand.ExecuteAsync(null);
        await Settle();
        Assert.Equal("This file is not an Astra Finance backup.", dialogs.ActiveDialog!.Message);
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await refused;
        Assert.Equal(90_000, await Get<IAccountService>().GetBalanceAsync(cash));
    }

    [Fact]
    public async Task Settings_export_json_and_import_csv()
    {
        await StartUnlockedAsync();
        var (_, _, cash, _) = await SeedAsync();
        var page = await OpenSettingsAsync(SettingsSection.Data);

        var json = Path.Combine(_dir.Path, "all.json");
        SavePicker.NextResult = json;
        await page.Data.ExportJsonCommand.ExecuteAsync(null);
        Assert.True(File.Exists(json));
        Assert.EndsWith("records exported", Get<INotificationService>().History[0].Title, StringComparison.Ordinal);

        var csv = Path.Combine(_dir.Path, "bank.csv");
        await File.WriteAllTextAsync(csv, "Tanggal;Keterangan;Jumlah\n01/11/2026;Gaji;8.000.000\n02/11/2026;Kopi;-25.000\n03/11/2026;Transfer?;abc\n");
        OpenPicker.NextResult = csv;
        var dialogs = Get<DialogService>();
        var importing = page.Data.ImportCsvCommand.ExecuteAsync(null);
        await Settle();
        var form = Assert.IsType<ImportFormViewModel>(dialogs.ActiveDialog!.Form);
        form.SelectedAccount = form.Accounts.Single(a => a.Id == cash);
        await form.RefreshPreviewAsync();
        Assert.Equal(ImportNumberStyle.Regional, form.NumberStyle!.Value);
        Assert.Equal("2 ready · 0 possible duplicates · 0 skipped · 1 with errors", form.SummaryText);
        Assert.Equal("Import 2", form.PrimaryText);
        Assert.Equal(["Ready", "Ready", "Error"], form.PreviewRows.Select(r => r.Status));
        Assert.Equal(["Other Income", "Other Expenses"], form.PreviewRows.Take(2).Select(r => r.CategoryText)); // translated, not internal keys
        await dialogs.ActiveDialog.AcceptCommand.ExecuteAsync(null);
        await importing;

        Assert.Equal("2 transactions imported", Get<INotificationService>().History[0].Title);
        Assert.Equal(100_000 + 8_000_000 - 25_000, await Get<IAccountService>().GetBalanceAsync(cash));
    }

    [Fact]
    public void Backup_reminder_text_never_contains_amounts()
    {
        var scheduler = Get<ReminderScheduler>();
        var (_, title, body, privateBody) = scheduler.Describe(new Reminder(ReminderKind.BackupOverdue, Guid.Empty, string.Empty, 0, new DateOnly(2026, 11, 1), 8));
        Assert.Equal("Time for a backup", title);
        Assert.Equal("Your last backup was 8 days ago. Go to Settings → Data management.", body);
        Assert.Equal(body, privateBody);
    }
}
