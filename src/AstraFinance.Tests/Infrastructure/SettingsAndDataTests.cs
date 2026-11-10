using System.Text.Json;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Validation;
using AstraFinance.Infrastructure.Backup;
using AstraFinance.Infrastructure.Export;
using Microsoft.Data.Sqlite;

namespace AstraFinance.Tests.Infrastructure;

/// <summary>Phase 9: security and category rules, backup history and restore, JSON export, CSV import, backup reminders.</summary>
public class SettingsAndDataTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    // ======================= Security =======================

    [Fact]
    public async Task Failed_pin_cooldown_follows_the_saved_settings()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.Pin.SetPinAsync("2468");
        Assert.Equal(5, (await ctx.Pin.GetAttemptSettingsAsync()).FreeAttempts);

        await ctx.Pin.SaveAttemptSettingsAsync(new PinAttemptSettings(3, TimeSpan.FromMinutes(1)));
        PinCheckResult result = null!;
        for (var i = 0; i < 3; i++)
        {
            result = await ctx.Pin.VerifyAsync("0000");
        }

        Assert.Equal(PinCheckOutcome.Incorrect, result.Outcome);
        Assert.Equal(ctx.Clock.UtcNow + TimeSpan.FromMinutes(1), result.RetryAt);
        Assert.Equal(PinCheckOutcome.CoolingDown, (await ctx.Pin.VerifyAsync("2468")).Outcome);

        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Pin.SaveAttemptSettingsAsync(new PinAttemptSettings(2, TimeSpan.FromMinutes(1))));
        Assert.Equal(ValidationCodes.AttemptSettingsOutOfRange, ex.Errors[0].Code);
        Assert.Equal(TimeSpan.FromMinutes(15), new PinAttemptSettings(5, TimeSpan.FromSeconds(30)).MaxCooldown);
        Assert.Equal(TimeSpan.FromMinutes(40), new PinAttemptSettings(5, TimeSpan.FromMinutes(5)).MaxCooldown);
    }

    [Fact]
    public async Task Transfer_fee_category_cannot_be_deactivated_but_others_can()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var fees = await ctx.CategoryAsync("Fees");
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Categories.SetActiveAsync(fees.Id, false));
        Assert.Equal(ValidationCodes.FeeCategoryRequired, ex.Errors[0].Code);

        var gift = await ctx.CategoryAsync("Gift");
        await ctx.Categories.SetActiveAsync(gift.Id, false);
        Assert.False((await ctx.Categories.GetAsync(gift.Id))!.IsActive);

        var custom = await ctx.Categories.CreateAsync(new CategoryDraft("Pets", CategoryKind.Expense, "Icon.HeartPulse", "#F07272"));
        await ctx.Categories.UpdateAsync(custom.Id, "Pet care", "Icon.HeartPulse", "#3DBE8B");
        Assert.Equal("Pet care", (await ctx.Categories.GetAsync(custom.Id))!.Name);
        await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Categories.CreateAsync(new CategoryDraft("pet care", CategoryKind.Expense, "Icon.HeartPulse", "#F07272")));
    }

    // ======================= Backup history & restore =======================

    [Fact]
    public async Task Restore_replaces_the_data_after_taking_a_safety_copy()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var cash = await ctx.NewAccountAsync("Cash", 100_000);
        await ctx.ExpenseAsync(cash, 10_000, description: "Before backup");
        var backup = await ctx.Backup.CreateBackupAsync(isAutomatic: false);

        await ctx.ExpenseAsync(cash, 25_000, description: "After backup");
        await ctx.NewAccountAsync("Wallet", 50_000);
        Assert.Equal(65_000, await ctx.BalanceAsync(cash));

        var inspection = await ctx.Restore.InspectAsync(backup.Path);
        Assert.True(inspection.IsValid);
        Assert.Equal((1, 1, false), (inspection.AccountCount, inspection.TransactionCount, inspection.NeedsUpgrade));

        ctx.Clock.UtcNow = ctx.Clock.UtcNow.AddMinutes(5);
        var result = await ctx.Restore.RestoreAsync(backup.Path);

        Assert.Equal(90_000, await ctx.BalanceAsync(cash));
        Assert.Single(await ctx.Accounts.ListAsync(includeInactive: true));
        Assert.Equal("Before backup", Assert.Single((await ctx.Transactions.QueryAsync(new TransactionQuery())).Items).Description);

        // The data from before the restore is kept in the safety copy.
        var safety = await ctx.Restore.InspectAsync(result.SafetyBackupPath);
        Assert.Equal((2, 2), (safety.AccountCount, safety.TransactionCount));
        var history = await ctx.Restore.ListBackupsAsync();
        Assert.Contains(history, b => b.Kind == BackupKind.Manual && b.Path == backup.Path);
        Assert.Contains(history, b => b.Kind == BackupKind.PreRestore);

        // And the restored database keeps working normally.
        await ctx.ExpenseAsync(cash, 1_000, description: "After restore");
        Assert.Equal(89_000, await ctx.BalanceAsync(cash));
    }

    [Fact]
    public async Task Invalid_backups_are_refused_and_the_current_data_stays()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var cash = await ctx.NewAccountAsync("Cash", 100_000);
        var good = await ctx.Backup.CreateBackupAsync(isAutomatic: false);

        var text = Path.Combine(ctx.Paths.DataDirectory, "notes.db");
        await File.WriteAllTextAsync(text, "not a database");
        Assert.Equal(RestoreProblems.NotADatabase, (await ctx.Restore.InspectAsync(text)).ProblemCode);

        var empty = Path.Combine(ctx.Paths.DataDirectory, "empty.db");
        using (var c = new SqliteConnection($"Data Source={empty};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE Something (Id INTEGER);";
            cmd.ExecuteNonQuery();
        }

        Assert.Equal(RestoreProblems.NotAstra, (await ctx.Restore.InspectAsync(empty)).ProblemCode);

        var future = Path.Combine(ctx.Paths.DataDirectory, "future.db");
        File.Copy(good.Path, future);
        using (var c = new SqliteConnection($"Data Source={future};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29990101000000_FromTheFuture', '99.0');";
            cmd.ExecuteNonQuery();
        }

        Assert.Equal(RestoreProblems.NewerVersion, (await ctx.Restore.InspectAsync(future)).ProblemCode);
        Assert.Equal(RestoreProblems.SameFile, (await ctx.Restore.InspectAsync(ctx.Paths.DatabaseFile)).ProblemCode);
        Assert.Equal(RestoreProblems.NotFound, (await ctx.Restore.InspectAsync(Path.Combine(ctx.Paths.DataDirectory, "missing.db"))).ProblemCode);

        await ctx.ExpenseAsync(cash, 5_000);
        var ex = await Assert.ThrowsAsync<InvalidBackupException>(() => ctx.Restore.RestoreAsync(future));
        Assert.Equal(RestoreProblems.NewerVersion, ex.Code);
        Assert.Equal(95_000, await ctx.BalanceAsync(cash)); // untouched
    }

    // ======================= JSON export =======================

    [Fact]
    public async Task Json_export_contains_all_records_including_deleted_but_never_security_data()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.Pin.SetPinAsync("2468");
        var cash = await ctx.NewAccountAsync("Cash", 100_000);
        var bank = await ctx.NewAccountAsync("Bank", 1_000_000);
        await ctx.ExpenseAsync(cash, 45_000, description: "Lunch");
        await ctx.TransferAsync(bank, cash, 200_000, fee: 2_500);
        var deleted = await ctx.ExpenseAsync(cash, 9_000, description: "Oops");
        await ctx.Transactions.DeleteAsync(deleted.Id);
        var path = Path.Combine(ctx.Paths.DataDirectory, "export.json");

        var count = await ctx.DataExport.ExportJsonAsync(path);

        var text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("PinHash", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pbkdf2", text, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.Equal("astra-finance-export", root.GetProperty("format").GetString());
        Assert.Equal("IDR", root.GetProperty("currency").GetProperty("code").GetString());
        Assert.Equal(2, root.GetProperty("accounts").GetArrayLength());
        var transactions = root.GetProperty("transactions").EnumerateArray().ToList();
        Assert.Equal(4, transactions.Count); // lunch, transfer, fee, deleted
        Assert.Contains(transactions, t => t.GetProperty("description").GetString() == "Oops" && t.GetProperty("isDeleted").GetBoolean());
        Assert.Contains(transactions, t => t.GetProperty("transferId").ValueKind == JsonValueKind.String);
        Assert.Equal(45_000, transactions.Single(t => t.GetProperty("description").GetString() == "Lunch").GetProperty("amount").GetInt64());
        Assert.True(count >= 2 + 4 + 15); // accounts + transactions + built-in categories
    }

    // ======================= CSV import =======================

    [Fact]
    public void Csv_parser_handles_quotes_semicolons_and_multiline_fields()
    {
        var table = TransactionImportService.Parse("﻿Tanggal;Keterangan;Jumlah\r\n01/11/2026;\"Kopi; \"\"enak\"\"\";-25.000\r\n\r\n02/11/2026;\"Gaji\nNovember\";8.000.000\r\n");
        Assert.Equal(["Tanggal", "Keterangan", "Jumlah"], table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Kopi; \"enak\"", table.Rows[0][1]);
        Assert.Equal("Gaji\nNovember", table.Rows[1][1]);
    }

    [Fact]
    public async Task Astra_export_csv_imports_income_and_expenses_and_flags_duplicates()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var cash = await ctx.NewAccountAsync("Cash", 100_000);
        var csv = TransactionImportService.Parse(
            "Date,Type,Description,Category,Account,To account,Amount,Currency,Notes,Note\r\n" +
            "02/11/2026,Income,November salary,Salary,Cash,,8000000,IDR,,\r\n" +
            "03/11/2026,Expense,Lunch,Food & Drinks,Cash,,-45000,IDR,with team,\r\n" +
            "04/11/2026,Transfer,Top up,,Bank,Cash,500000,IDR,,\r\n" +
            "05/11/2026,Expense,Gym,Olahraga,Cash,,-150000,IDR,,\r\n" +
            "06/11/2026,Expense,Groceries,Food & Drinks,Bank,,-99000,IDR,,\r\n" +
            "07/11/2026,Expense,'=Formula,Shopping,Cash,,-1000,IDR,,\r\n" +
            "31/02/2026,Expense,Bad date,Shopping,Cash,,-1000,IDR,,\r\n" +
            "08/11/2026,Expense,Bad amount,Shopping,Cash,,abc,IDR,,\r\n");

        var mapping = ctx.Import.GuessMapping(csv.Headers);
        Assert.Equal(new ImportMapping(0, 6, 2, 3, 1, 8, 4), mapping);
        var options = new ImportOptions(cash.Id, mapping, DateFormatStyle.DayMonthYear, ImportNumberStyle.Plain);
        var preview = await ctx.Import.PreviewAsync(csv, options);

        Assert.Equal((4, 0, 2, 2), (preview.ReadyCount, preview.DuplicateCount, preview.SkippedCount, preview.ErrorCount));
        var rows = preview.Rows;
        Assert.Equal((TransactionType.Income, 8_000_000L, "Salary"), (rows[0].Type, rows[0].Amount, rows[0].CategoryName));
        Assert.Equal((TransactionType.Expense, 45_000L, "with team"), (rows[1].Type, rows[1].Amount, rows[1].Notes));
        Assert.Equal(ImportMessages.Transfer, rows[2].MessageCode);
        Assert.Equal(ImportMessages.CategoryFallback, rows[3].MessageCode);   // unknown category → Other expenses
        Assert.Equal(ImportMessages.OtherAccount, rows[4].MessageCode);
        Assert.Equal("=Formula", rows[5].Description);                          // export's formula protection removed
        Assert.Equal(ImportMessages.BadDate, rows[6].MessageCode);
        Assert.Equal(ImportMessages.BadAmount, rows[7].MessageCode);

        Assert.Equal(4, await ctx.Import.ImportAsync(cash.Id, preview.Rows.Where(r => r.Status == ImportRowStatus.Ready).ToList()));
        Assert.Equal(100_000 + 8_000_000 - 45_000 - 150_000 - 1_000, await ctx.BalanceAsync(cash));

        // Importing the same file again: everything already exists.
        var again = await ctx.Import.PreviewAsync(csv, options);
        Assert.Equal((0, 4), (again.ReadyCount, again.DuplicateCount));
        Assert.All(again.Rows.Where(r => r.Status == ImportRowStatus.Duplicate), r => Assert.Equal(ImportMessages.ExistsInAccount, r.MessageCode));
    }

    [Fact]
    public async Task Bank_style_csv_uses_signs_regional_numbers_and_rolls_back_on_failure()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var bank = await ctx.NewAccountAsync("BCA", 1_000_000);
        var csv = TransactionImportService.Parse(
            "Tanggal;Keterangan;Jumlah\n" +
            "1/11/2026;Gaji;8.000.000\n" +
            "2-11-2026;Kopi;-25.000\n" +
            "2026-11-03;Kopi;-25.000\n" +
            "03/11/2026;Kopi;-25.000\n");
        var mapping = ctx.Import.GuessMapping(csv.Headers);
        Assert.Equal(new ImportMapping(0, 2, 1, null, null, null, null), mapping);

        var preview = await ctx.Import.PreviewAsync(csv, new ImportOptions(bank.Id, mapping, DateFormatStyle.DayMonthYear, ImportNumberStyle.Regional));
        Assert.Equal([TransactionType.Income, TransactionType.Expense, TransactionType.Expense, TransactionType.Expense], preview.Rows.Select(r => r.Type));
        Assert.Equal([D(11, 1), D(11, 2), D(11, 3), D(11, 3)], preview.Rows.Select(r => r.Date));
        Assert.Equal(ImportMessages.RepeatedInFile, preview.Rows[3].MessageCode); // same day, amount and description

        // Something changes between preview and import: nothing is saved at all.
        await ctx.Accounts.SetActiveAsync(bank.Id, false);
        var ex = await Assert.ThrowsAsync<ImportFailedException>(() => ctx.Import.ImportAsync(bank.Id, preview.Rows));
        Assert.Equal(2, ex.LineNumber);
        Assert.Equal(0, (await ctx.Transactions.QueryAsync(new TransactionQuery())).TotalCount);

        await ctx.Accounts.SetActiveAsync(bank.Id, true);
        Assert.Equal(4, await ctx.Import.ImportAsync(bank.Id, preview.Rows)); // duplicates included on purpose
        Assert.Equal(1_000_000 + 8_000_000 - 75_000, await ctx.BalanceAsync(bank));
    }

    // ======================= Backup reminders =======================

    [Fact]
    public async Task Backup_reminder_after_the_configured_days_without_a_backup()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.NotificationSettings.SaveAsync(NotificationSettings.Default with { BackupReminderDays = 7 });

        Assert.Empty(await ctx.Reminders.CollectDueAsync(D(11, 9)));      // first check: starts counting
        Assert.Empty(await ctx.Reminders.CollectDueAsync(D(11, 15)));
        var never = Assert.Single(await ctx.Reminders.CollectDueAsync(D(11, 16)));
        Assert.Equal((ReminderKind.BackupOverdue, -1L), (never.Kind, never.Percent));
        Assert.Empty(await ctx.Reminders.CollectDueAsync(D(11, 17)));      // not every day

        ctx.Clock.UtcNow = new DateTimeOffset(2026, 11, 17, 2, 0, 0, TimeSpan.Zero);
        await ctx.Backup.CreateBackupAsync(isAutomatic: false);
        Assert.Empty(await ctx.Reminders.CollectDueAsync(D(11, 20)));
        var overdue = Assert.Single(await ctx.Reminders.CollectDueAsync(D(11, 24)));
        Assert.Equal(7, overdue.Percent);

        await ctx.NotificationSettings.SaveAsync(NotificationSettings.Default with { BackupReminders = false });
        Assert.Empty(await ctx.Reminders.CollectDueAsync(D(12, 31)));
        await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.NotificationSettings.SaveAsync(NotificationSettings.Default with { BackupReminderDays = 10 }));
    }
}
