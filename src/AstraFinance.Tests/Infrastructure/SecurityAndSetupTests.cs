using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Money;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Security;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;
using AstraFinance.Infrastructure.Backup;
using AstraFinance.Infrastructure.Services;

namespace AstraFinance.Tests.Infrastructure;

public class SecurityAndSetupTests
{
    // ---------------- PIN hashing ----------------

    [Fact]
    public void Pin_hash_is_salted_pbkdf2_and_verifies_only_the_right_pin()
    {
        var hasher = new PinHasher(1_000);
        var a = hasher.Hash("2468");
        var b = hasher.Hash("2468");

        Assert.StartsWith("pbkdf2-sha256$1000$", a, StringComparison.Ordinal);
        Assert.NotEqual(a, b); // unique salt
        Assert.DoesNotContain("2468", a, StringComparison.Ordinal);
        Assert.True(PinHasher.Verify("2468", a));
        Assert.False(PinHasher.Verify("2469", a));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("pbkdf2-sha256$1000$notbase64$x")]
    [InlineData("pbkdf2-sha256$10$AAAA$AAAA")]
    [InlineData("md5$1$a$b")]
    public void Malformed_hashes_never_verify(string stored) => Assert.False(PinHasher.Verify("1234", stored));

    [Fact]
    public void Default_work_factor_meets_current_guidance() =>
        Assert.True(new PinHasher().Iterations >= 600_000);

    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("١٢٣٤", false)] // non-ASCII digits
    [InlineData(null, false)]
    public void Pin_format_is_4_to_8_ascii_digits(string? pin, bool valid) => Assert.Equal(valid, PinRules.IsValidFormat(pin));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 30)]
    [InlineData(6, 60)]
    [InlineData(7, 120)]
    [InlineData(10, 900)]
    [InlineData(40, 900)]
    public void Cooldown_doubles_after_free_attempts_and_is_capped(int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), PinAttemptPolicy.Default.CooldownAfter(failures));

    // ---------------- PIN service ----------------

    [Fact]
    public async Task Pin_is_stored_only_as_a_hash()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        Assert.False(await ctx.Pin.HasPinAsync());

        await ctx.Pin.SetPinAsync("97531");

        Assert.True(await ctx.Pin.HasPinAsync());
        var stored = await ctx.Settings.GetAsync(Phase3SettingKeys.PinHash);
        Assert.DoesNotContain("97531", stored, StringComparison.Ordinal);
        Assert.Equal(PinCheckOutcome.Success, (await ctx.Pin.VerifyAsync("97531")).Outcome);
    }

    [Fact]
    public async Task Invalid_pin_format_is_rejected()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() => ctx.Pin.SetPinAsync("12"));
        Assert.True(ex.Has(ValidationCodes.PinFormat));
    }

    [Fact]
    public async Task Failed_attempts_trigger_a_persisted_cooldown_that_blocks_even_the_correct_pin()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.Pin.SetPinAsync("2468");

        for (var i = 1; i <= 4; i++)
        {
            var r = await ctx.Pin.VerifyAsync("0000");
            Assert.Equal(PinCheckOutcome.Incorrect, r.Outcome);
            Assert.Equal(5 - i, r.RemainingFreeAttempts);
            Assert.Null(r.RetryAt);
        }

        var fifth = await ctx.Pin.VerifyAsync("0000");
        Assert.Equal(ctx.Clock.UtcNow.AddSeconds(30), fifth.RetryAt);

        // Correct PIN during the cooldown is still refused — also by a new service instance (restart).
        var restarted = new PinService(ctx.Settings, ctx.Clock, new PinHasher(1_000), PinAttemptPolicy.Default);
        Assert.Equal(PinCheckOutcome.CoolingDown, (await restarted.VerifyAsync("2468")).Outcome);

        ctx.Clock.UtcNow = ctx.Clock.UtcNow.AddSeconds(31);
        Assert.Equal(PinCheckOutcome.Success, (await restarted.VerifyAsync("2468")).Outcome);

        // Success resets the counter.
        Assert.Equal(4, (await restarted.VerifyAsync("0000")).RemainingFreeAttempts);
        restarted.Dispose();
    }

    [Fact]
    public async Task Changing_the_pin_requires_the_current_pin()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.Pin.SetPinAsync("1111");

        Assert.Equal(PinCheckOutcome.Incorrect, (await ctx.Pin.ChangePinAsync("9999", "2222")).Outcome);
        Assert.Equal(PinCheckOutcome.Success, (await ctx.Pin.ChangePinAsync("1111", "2222")).Outcome);
        Assert.Equal(PinCheckOutcome.Success, (await ctx.Pin.VerifyAsync("2222")).Outcome);
        Assert.Equal(PinCheckOutcome.Incorrect, (await ctx.Pin.VerifyAsync("1111")).Outcome);
    }

    // ---------------- Onboarding ----------------

    [Fact]
    public async Task Onboarding_progress_is_persisted_and_resumed_safely()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var initial = await ctx.Onboarding.GetStateAsync();
        Assert.Equal(new OnboardingState(false, OnboardingStep.Welcome, null), initial);

        // Saved past the account step without an account (interrupted write) → resume at the account step.
        await ctx.Onboarding.SaveStepAsync(OnboardingStep.Backup);
        Assert.Equal(OnboardingStep.FirstAccount, (await ctx.Onboarding.GetStateAsync()).CurrentStep);

        var account = await ctx.NewAccountAsync();
        await ctx.Onboarding.SetFirstAccountAsync(account.Id);
        var resumed = await ctx.Onboarding.GetStateAsync();
        Assert.Equal(OnboardingStep.Backup, resumed.CurrentStep);
        Assert.Equal(account.Id, resumed.FirstAccountId);
    }

    [Fact]
    public async Task Onboarding_cannot_complete_without_an_account_and_a_pin()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await Assert.ThrowsAsync<OnboardingIncompleteException>(() => ctx.Onboarding.CompleteAsync());

        await ctx.NewAccountAsync();
        await Assert.ThrowsAsync<OnboardingIncompleteException>(() => ctx.Onboarding.CompleteAsync());

        await ctx.Pin.SetPinAsync("1234");
        await ctx.Onboarding.CompleteAsync();
        Assert.True((await ctx.Onboarding.GetStateAsync()).IsCompleted);
    }

    // ---------------- Regional format ----------------

    [Fact]
    public async Task Currency_is_locked_once_accounts_exist_but_formats_can_change()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.Regional.LoadAsync();
        Assert.True(await ctx.Regional.CanChangeCurrencyAsync());
        await ctx.Regional.UpdateAsync(ctx.Regional.Current with { Currency = CurrencyInfo.Usd });
        Assert.Equal("$12,34", ctx.Regional.FormatMoney(1_234)); // number style is still dot-group/comma-decimal

        await ctx.NewAccountAsync(opening: 1_000);
        Assert.False(await ctx.Regional.CanChangeCurrencyAsync());
        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() =>
            ctx.Regional.UpdateAsync(ctx.Regional.Current with { Currency = CurrencyInfo.Idr }));
        Assert.True(ex.Has(ValidationCodes.CurrencyLocked));

        await ctx.Regional.UpdateAsync(ctx.Regional.Current with { DateStyle = DateFormatStyle.YearMonthDay });
        Assert.Equal("2026-11-09", ctx.Regional.FormatDate(FinanceTestContext.Today));
        Assert.Equal(DateFormatStyle.YearMonthDay, (await ctx.Settings.GetRegionalFormatAsync()).DateStyle);
    }

    [Theory]
    [InlineData("-50.000", -50_000)]
    [InlineData("−1.500", -1_500)]
    [InlineData("75.000", 75_000)]
    public void Signed_amounts_parse_for_overdrawn_balances(string text, long expected)
    {
        Assert.True(MoneyParser.TryParseSigned(text, RegionalFormat.Default, out var value));
        Assert.Equal(expected, value);
    }

    // ---------------- Backups ----------------

    [Fact]
    public async Task Backup_settings_default_to_weekly_in_the_app_folder()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var settings = await ctx.Backup.GetSettingsAsync();
        Assert.Equal(ctx.Paths.BackupsDirectory, settings.Directory);
        Assert.Equal(BackupSchedule.Weekly, settings.Schedule);
        Assert.Equal(BackupSettings.DefaultRetention, settings.RetentionCount);
    }

    [Fact]
    public async Task Unwritable_backup_folder_is_rejected()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        var file = Path.Combine(ctx.Paths.DataDirectory, "not-a-folder.txt");
        await File.WriteAllTextAsync(file, "x");

        var ex = await Assert.ThrowsAsync<FinanceValidationException>(() =>
            ctx.Backup.SaveSettingsAsync(new BackupSettings(file, BackupSchedule.Daily, 5)));
        Assert.True(ex.Has(ValidationCodes.DirectoryNotWritable));
    }

    [Fact]
    public async Task Manual_backup_is_a_verified_copy_and_updates_last_backup()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.NewAccountAsync(opening: 5_000);

        var backup = await ctx.Backup.CreateBackupAsync(isAutomatic: false);

        Assert.True(File.Exists(backup.Path));
        Assert.False(backup.IsAutomatic);
        Assert.True(SqliteBackup.IntegrityCheck(backup.Path));
        Assert.Equal(ctx.Clock.UtcNow, await ctx.Backup.GetLastBackupAsync());
    }

    [Theory]
    [InlineData(BackupSchedule.Off, null, 0, false)]
    [InlineData(BackupSchedule.Daily, null, 0, true)]
    [InlineData(BackupSchedule.Daily, 0, 23, false)]
    [InlineData(BackupSchedule.Daily, 0, 24, true)]
    [InlineData(BackupSchedule.Weekly, 0, 24 * 6, false)]
    [InlineData(BackupSchedule.Weekly, 0, 24 * 7, true)]
    [InlineData(BackupSchedule.Monthly, 0, 24 * 29, false)]
    [InlineData(BackupSchedule.Monthly, 0, 24 * 30, true)]
    public void Scheduled_backup_due_rules(BackupSchedule schedule, int? lastOffsetHours, int nowOffsetHours, bool due)
    {
        var start = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset? last = lastOffsetHours is null ? null : start.AddHours(lastOffsetHours.Value);
        Assert.Equal(due, BackupService.IsDue(schedule, last, start.AddHours(nowOffsetHours)));
    }

    [Fact]
    public async Task Retention_removes_only_old_automatic_backups()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.Backup.SaveSettingsAsync(new BackupSettings(ctx.Paths.BackupsDirectory, BackupSchedule.Daily, 2));

        var manual = await ctx.Backup.CreateBackupAsync(isAutomatic: false);
        for (var i = 0; i < 4; i++)
        {
            ctx.Clock.UtcNow = ctx.Clock.UtcNow.AddDays(1);
            Assert.NotNull(await ctx.Backup.RunScheduledBackupIfDueAsync());
        }

        Assert.Null(await ctx.Backup.RunScheduledBackupIfDueAsync()); // not due again the same day

        var automatic = Directory.GetFiles(ctx.Paths.BackupsDirectory, "*-auto.db");
        Assert.Equal(2, automatic.Length);
        Assert.True(File.Exists(manual.Path));
    }

    // ---------------- Search ----------------

    [Fact]
    public async Task Account_search_finds_accounts_with_balance_and_navigation_target()
    {
        await using var ctx = await FinanceTestContext.CreateAsync();
        await ctx.Regional.LoadAsync();
        var bca = await ctx.NewAccountAsync("BCA Tabungan", 2_500_000, AccountType.BankAccount);
        await ctx.NewAccountAsync("GoPay", 0, AccountType.EWallet);

        var provider = new AccountSearchProvider(ctx.Accounts, ctx.Regional);
        var results = await provider.SearchAsync("tabung", 8, CancellationToken.None);

        var hit = Assert.Single(results);
        Assert.Equal("BCA Tabungan", hit.Title);
        Assert.Equal("Rp2.500.000", hit.Subtitle);
        Assert.Equal(new NavigationTarget(AppPage.Accounts, bca.Id), hit.Target);
    }
}
