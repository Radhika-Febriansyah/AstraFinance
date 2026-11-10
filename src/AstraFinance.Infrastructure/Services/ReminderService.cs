using System.Globalization;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Validation;

namespace AstraFinance.Infrastructure.Services;

/// <summary>Notification preferences stored in the settings table.</summary>
public sealed class NotificationSettingsService : INotificationSettingsService
{
    private readonly ISettingsStore _settings;

    public NotificationSettingsService(ISettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var d = NotificationSettings.Default;
        var days = await _settings.GetAsync(SettingKeys.BillReminderDays, cancellationToken).ConfigureAwait(false);
        return new NotificationSettings(
            await GetBoolAsync(SettingKeys.NotificationsEnabled, d.Enabled, cancellationToken).ConfigureAwait(false),
            await GetBoolAsync(SettingKeys.BillRemindersEnabled, d.BillReminders, cancellationToken).ConfigureAwait(false),
            int.TryParse(days, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= NotificationSettings.MaxReminderDays ? n : d.BillReminderDays,
            await GetBoolAsync(SettingKeys.BudgetWarningsEnabled, d.BudgetWarnings, cancellationToken).ConfigureAwait(false),
            await GetBoolAsync(SettingKeys.WindowsNotificationsEnabled, d.WindowsNotifications, cancellationToken).ConfigureAwait(false),
            await GetBoolAsync(SettingKeys.BackupRemindersEnabled, d.BackupReminders, cancellationToken).ConfigureAwait(false),
            int.TryParse(await _settings.GetAsync(SettingKeys.BackupReminderDays, cancellationToken).ConfigureAwait(false), NumberStyles.None, CultureInfo.InvariantCulture, out var b)
                && NotificationSettings.BackupReminderChoices.Contains(b) ? b : d.BackupReminderDays);
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.BillReminderDays is < 0 or > NotificationSettings.MaxReminderDays)
        {
            throw new FinanceValidationException("BillReminderDays", ValidationCodes.ReminderDaysOutOfRange);
        }

        await SetBoolAsync(SettingKeys.NotificationsEnabled, settings.Enabled, cancellationToken).ConfigureAwait(false);
        await SetBoolAsync(SettingKeys.BillRemindersEnabled, settings.BillReminders, cancellationToken).ConfigureAwait(false);
        await _settings.SetAsync(SettingKeys.BillReminderDays, settings.BillReminderDays.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await SetBoolAsync(SettingKeys.BudgetWarningsEnabled, settings.BudgetWarnings, cancellationToken).ConfigureAwait(false);
        await SetBoolAsync(SettingKeys.WindowsNotificationsEnabled, settings.WindowsNotifications, cancellationToken).ConfigureAwait(false);
        if (!NotificationSettings.BackupReminderChoices.Contains(settings.BackupReminderDays))
        {
            throw new FinanceValidationException("BackupReminderDays", ValidationCodes.ReminderDaysOutOfRange);
        }

        await SetBoolAsync(SettingKeys.BackupRemindersEnabled, settings.BackupReminders, cancellationToken).ConfigureAwait(false);
        await _settings.SetAsync(SettingKeys.BackupReminderDays, settings.BackupReminderDays.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct)
    {
        var text = await _settings.GetAsync(key, ct).ConfigureAwait(false);
        return bool.TryParse(text, out var value) ? value : fallback;
    }

    private Task SetBoolAsync(string key, bool value, CancellationToken ct) =>
        _settings.SetAsync(key, value ? "true" : "false", ct);
}

/// <summary>
/// Decides which bill reminders and budget warnings are new. A bill reminds once per occurrence and stage
/// (due soon, due today, overdue); a budget warns once per month and level (warning, then exceeded).
/// What was shown is remembered in the settings table, so restarting the app doesn't repeat reminders.
/// </summary>
public sealed class ReminderService : IReminderService
{
    private readonly INotificationSettingsService _notificationSettings;
    private readonly IBillService _bills;
    private readonly IBudgetService _budgets;
    private readonly ISettingsStore _settings;

    /// <summary>When the app first checked for backup reminders (so a new user isn't reminded on day one).</summary>
    public const string BackupFirstSeenKey = "Reminders.Backup.FirstSeen";

    private readonly IBackupService? _backups;

    public ReminderService(INotificationSettingsService notificationSettings, IBillService bills, IBudgetService budgets, ISettingsStore settings, IBackupService? backups = null)
    {
        _notificationSettings = notificationSettings;
        _bills = bills;
        _budgets = budgets;
        _settings = settings;
        _backups = backups;
    }

    public async Task<IReadOnlyList<Reminder>> CollectDueAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var settings = await _notificationSettings.GetAsync(cancellationToken).ConfigureAwait(false);
        var reminders = new List<Reminder>();
        if (!settings.Enabled)
        {
            return reminders;
        }

        if (settings.BillReminders)
        {
            foreach (var bill in await _bills.ListAsync(today, includePaused: false, cancellationToken).ConfigureAwait(false))
            {
                ReminderKind? kind = bill.Status switch
                {
                    BillStatus.DueSoon => ReminderKind.BillDueSoon,
                    BillStatus.DueToday => ReminderKind.BillDueToday,
                    BillStatus.Overdue => ReminderKind.BillOverdue,
                    _ => null,
                };
                if (kind is null)
                {
                    continue;
                }

                var key = SettingKeys.BillReminderSentPrefix + bill.Bill.Id.ToString("N", CultureInfo.InvariantCulture);
                var marker = $"{bill.NextDue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{kind}";
                if (await _settings.GetAsync(key, cancellationToken).ConfigureAwait(false) == marker)
                {
                    continue;
                }

                reminders.Add(new Reminder(kind.Value, bill.Bill.Id, bill.Bill.Name, bill.Bill.Amount, bill.NextDue, 0));
                await _settings.SetAsync(key, marker, cancellationToken).ConfigureAwait(false);
            }
        }

        if (settings.BudgetWarnings)
        {
            var overview = await _budgets.GetOverviewAsync(today, cancellationToken).ConfigureAwait(false);
            var month = overview.Month.Start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            foreach (var item in overview.Items)
            {
                var status = item.Evaluation.Status;
                if (status == BudgetStatus.Normal)
                {
                    continue;
                }

                var key = SettingKeys.BudgetWarningSentPrefix + item.Budget.Id.ToString("N", CultureInfo.InvariantCulture);
                var previous = await _settings.GetAsync(key, cancellationToken).ConfigureAwait(false);
                if (previous is not null && previous.StartsWith(month + ":", StringComparison.Ordinal)
                    && Enum.TryParse<BudgetStatus>(previous[(month.Length + 1)..], out var shown) && shown >= status)
                {
                    // Already warned at this level (or higher) this month.
                    continue;
                }

                reminders.Add(new Reminder(
                    status == BudgetStatus.Exceeded ? ReminderKind.BudgetExceeded : ReminderKind.BudgetWarning,
                    item.Budget.Id,
                    item.Category.Name,
                    item.Evaluation.Limit,
                    overview.Month.Start,
                    item.Evaluation.UsagePercent,
                    item.Category));
                await _settings.SetAsync(key, $"{month}:{status}", cancellationToken).ConfigureAwait(false);
            }
        }

        if (settings.BackupReminders && _backups is not null && await BackupReminderAsync(today, settings.BackupReminderDays, cancellationToken).ConfigureAwait(false) is { } backup)
        {
            reminders.Add(backup);
        }

        return reminders;
    }

    /// <summary>
    /// A backup reminder when no backup was made for <paramref name="days"/> days (counted from the first check
    /// when there has never been one), repeated at most every <paramref name="days"/> days.
    /// </summary>
    private async Task<Reminder?> BackupReminderAsync(DateOnly today, int days, CancellationToken ct)
    {
        var last = await _backups!.GetLastBackupAsync(ct).ConfigureAwait(false);
        DateOnly since;
        if (last is { } l)
        {
            since = DateOnly.FromDateTime(l.LocalDateTime);
        }
        else
        {
            var firstSeen = await _settings.GetAsync(BackupFirstSeenKey, ct).ConfigureAwait(false);
            if (!DateOnly.TryParseExact(firstSeen, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out since))
            {
                await _settings.SetAsync(BackupFirstSeenKey, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
                return null;
            }
        }

        if (today.DayNumber - since.DayNumber < days)
        {
            return null;
        }

        var lastReminded = await _settings.GetAsync(SettingKeys.BackupReminderSent, ct).ConfigureAwait(false);
        if (DateOnly.TryParseExact(lastReminded, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reminded)
            && reminded >= since && today.DayNumber - reminded.DayNumber < days)
        {
            return null;
        }

        await _settings.SetAsync(SettingKeys.BackupReminderSent, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        return new Reminder(ReminderKind.BackupOverdue, Guid.Empty, string.Empty, 0, since, last is null ? -1 : today.DayNumber - since.DayNumber);
    }
}
