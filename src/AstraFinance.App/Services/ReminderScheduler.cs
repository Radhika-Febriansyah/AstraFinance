using System.Windows.Threading;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Services;
using Microsoft.Extensions.Logging;

namespace AstraFinance.App.Services;

/// <summary>Shows a notification outside the app (Windows notification centre).</summary>
public interface IWindowsNotifier
{
    /// <summary>True when the Astra window is in the foreground (then the in-app toast is enough).</summary>
    bool IsAppInForeground { get; }

    void Show(string title, string body);
}

/// <summary>
/// Checks for bill reminders and budget warnings while the app is unlocked: right after unlocking, every
/// 30 minutes, and after transactions change. Reminders always go to the in-app notification list; when enabled,
/// they also appear as Windows notifications — without amounts, because those can be seen on the lock screen
/// and in the Windows notification centre.
/// </summary>
public sealed partial class ReminderScheduler : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly IReminderService _reminders;
    private readonly INotificationSettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IWindowsNotifier _windows;
    private readonly ILocalizationService _localization;
    private readonly IRegionalFormatService _format;
    private readonly IClock _clock;
    private readonly DataChangeNotifier _changes;
    private readonly ILogger<ReminderScheduler> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DispatcherTimer? _timer;
    private bool _running;

    public ReminderScheduler(
        IReminderService reminders,
        INotificationSettingsService settings,
        INotificationService notifications,
        IWindowsNotifier windows,
        ILocalizationService localization,
        IRegionalFormatService format,
        IClock clock,
        DataChangeNotifier changes,
        ILogger<ReminderScheduler> logger)
    {
        _reminders = reminders;
        _settings = settings;
        _notifications = notifications;
        _windows = windows;
        _localization = localization;
        _format = format;
        _clock = clock;
        _changes = changes;
        _logger = logger;
    }

    public bool IsRunning => _running;

    /// <summary>Starts checking (after unlock). Returns the first check so callers and tests can await it.</summary>
    public Task StartAsync()
    {
        if (!_running)
        {
            _running = true;
            _changes.Changed += OnDataChanged;
            _timer = new DispatcherTimer { Interval = Interval };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        return CheckNowAsync();
    }

    /// <summary>Stops checking (when the app locks), so nothing is shown over the lock screen.</summary>
    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _changes.Changed -= OnDataChanged;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    public async Task CheckNowAsync()
    {
        if (!_running || !await _gate.WaitAsync(0).ConfigureAwait(true))
        {
            return; // a check is already in progress; it will see the same data
        }

        try
        {
            var reminders = await _reminders.CollectDueAsync(_clock.Today).ConfigureAwait(true);
            if (reminders.Count == 0)
            {
                return;
            }

            var settings = await _settings.GetAsync().ConfigureAwait(true);
            foreach (var reminder in reminders)
            {
                var (kind, title, body, privateBody) = Describe(reminder);
                _notifications.Notify(kind, title, body);
                if (settings.WindowsNotifications && !_windows.IsAppInForeground)
                {
                    _windows.Show(title, privateBody);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Reminders are a convenience; never let a failed check disturb the app.
            LogCheckFailed(_logger, ex.GetType().Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        Stop();
        _gate.Dispose();
    }

    /// <summary>Title, in-app body (with amount) and Windows body (without amount).</summary>
    internal (NotificationKind Kind, string Title, string Body, string PrivateBody) Describe(Reminder r)
    {
        var date = _format.FormatDate(r.Date);
        var amount = _format.FormatMoney(r.Amount);
        switch (r.Kind)
        {
            case ReminderKind.BillDueSoon:
                var days = r.Date.DayNumber - _clock.Today.DayNumber;
                var when = _localization.Format(days == 1 ? "Reminder.Bill.DueTomorrow" : "Reminder.Bill.DueInDays", days, date);
                return (NotificationKind.Info, _localization.Format("Reminder.Bill.DueSoon.Title", r.Name),
                    $"{amount} · {when}", when);
            case ReminderKind.BillDueToday:
                var today = _localization.Format("Reminder.Bill.DueToday.Body", date);
                return (NotificationKind.Warning, _localization.Format("Reminder.Bill.DueToday.Title", r.Name), $"{amount} · {today}", today);
            case ReminderKind.BillOverdue:
                var overdue = _localization.Format("Reminder.Bill.Overdue.Body", date);
                return (NotificationKind.Warning, _localization.Format("Reminder.Bill.Overdue.Title", r.Name), $"{amount} · {overdue}", overdue);
            case ReminderKind.BudgetWarning:
            case ReminderKind.BudgetExceeded:
                var category = r.Category is null ? r.Name : DefaultCategories.DisplayName(r.Category, _localization);
                var exceeded = r.Kind == ReminderKind.BudgetExceeded;
                var title = _localization.Format(exceeded ? "Reminder.Budget.Exceeded.Title" : "Reminder.Budget.Warning.Title", category);
                var used = _localization.Format("Reminder.Budget.Body", r.Percent, amount);
                var usedPrivate = _localization.Format("Reminder.Budget.BodyPrivate", r.Percent);
                return (exceeded ? NotificationKind.Warning : NotificationKind.Info, title, used, usedPrivate);
            default:
                return (NotificationKind.Info, r.Name, date, date);
        }
    }

    private void OnTick(object? sender, EventArgs e) => _ = CheckNowAsync();

    private void OnDataChanged(object? sender, DataChangeKind e) => _ = CheckNowAsync();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reminder check failed ({ErrorType}).")]
    private static partial void LogCheckFailed(ILogger logger, string errorType);
}
