using System.Collections.ObjectModel;
using System.Globalization;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using AstraFinance.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels;

public sealed partial class NotificationItemViewModel : ObservableObject
{
    public NotificationItemViewModel(AppNotification notification, CultureInfo culture)
    {
        Notification = notification;
        TimeText = notification.CreatedAt.ToLocalTime().ToString("t", culture);
        Duration = ToastTiming.DurationFor(notification.Kind, notification.Title, notification.Message);
    }

    public AppNotification Notification { get; }

    public Guid Id => Notification.Id;

    public NotificationKind Kind => Notification.Kind;

    public string Title => Notification.Title;

    public string Message => Notification.Message;

    public string TimeText { get; }

    public string IconKey => Kind switch
    {
        NotificationKind.Success => "Icon.CircleCheck",
        NotificationKind.Warning => "Icon.AlertTriangle",
        NotificationKind.Error => "Icon.AlertCircle",
        _ => "Icon.Info",
    };

    /// <summary>How long the card stays on screen; null = until the user dismisses it.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>True for critical notices that wait for the user.</summary>
    public bool IsSticky => Duration is null;

    /// <summary>Set by the view while the pointer or keyboard focus is on the card: the countdown pauses.</summary>
    [ObservableProperty]
    public partial bool IsHovered { get; set; }

    /// <summary>Time left as a fraction (1 → 0), for the card's countdown bar.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; } = 1;

    /// <summary>True while the card fades out before it is removed.</summary>
    [ObservableProperty]
    public partial bool IsLeaving { get; set; }
}

/// <summary>
/// Bell flyout (history + unread badge) and the floating notification cards: newest on top, a duration based
/// on type and length (<see cref="ToastTiming"/>), paused while hovered; critical notices stay until dismissed.
/// </summary>
public sealed partial class NotificationCenterViewModel : ObservableObject, IDisposable
{
    public const int MaxToasts = 3;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(180);

    private readonly INotificationService _service;
    private readonly ILocalizationService _localization;
    private readonly IUiSoundPlayer _sounds;
    private readonly SynchronizationContext? _ui;

    public NotificationCenterViewModel(INotificationService service, ILocalizationService localization, IUiSoundPlayer sounds)
    {
        _service = service;
        _localization = localization;
        _sounds = sounds;
        _ui = SynchronizationContext.Current;
        _service.NotificationAdded += OnNotificationAdded;
        _service.Changed += OnChanged;
        Reload();
    }

    public ObservableCollection<NotificationItemViewModel> Items { get; } = [];

    public ObservableCollection<NotificationItemViewModel> Toasts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadBadge))]
    public partial int UnreadCount { get; private set; }

    public string? UnreadBadge => UnreadCount switch
    {
        0 => null,
        > 9 => "9+",
        _ => UnreadCount.ToString(CultureInfo.InvariantCulture),
    };

    public bool IsEmpty => Items.Count == 0;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [RelayCommand]
    public void ClearAll() => _service.Clear();

    [RelayCommand]
    public void Remove(NotificationItemViewModel? item)
    {
        if (item is not null)
        {
            _service.Remove(item.Id);
        }
    }

    [RelayCommand]
    public Task DismissToastAsync(NotificationItemViewModel? item) =>
        item is null ? Task.CompletedTask : LeaveAsync(item);

    public void Dispose()
    {
        _service.NotificationAdded -= OnNotificationAdded;
        _service.Changed -= OnChanged;
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            _service.MarkAllRead();
        }
    }

    private void OnNotificationAdded(object? sender, AppNotification e) => Post(() => _ = ShowToastAsync(e));

    private void OnChanged(object? sender, EventArgs e) => Post(Reload);

    private async Task ShowToastAsync(AppNotification notification)
    {
        var toast = new NotificationItemViewModel(notification, _localization.Culture);
        Toasts.Insert(0, toast); // newest on top
        while (Toasts.Count > MaxToasts)
        {
            // Make room by dropping the oldest card that would close by itself; critical ones go last.
            var oldest = Toasts.LastOrDefault(t => !t.IsSticky) ?? Toasts[^1];
            Toasts.Remove(oldest);
        }

        if (notification.Kind is NotificationKind.Warning or NotificationKind.Error)
        {
            _sounds.Play(UiSound.ImportantNotice);
        }

        if (toast.Duration is not { } duration)
        {
            return; // stays until dismissed
        }

        var remaining = duration;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (remaining > TimeSpan.Zero && Toasts.Contains(toast))
        {
            await Task.Delay(Tick).ConfigureAwait(true);
            var elapsed = clock.Elapsed;
            clock.Restart();
            if (!toast.IsHovered)
            {
                remaining -= elapsed;
                toast.Progress = Math.Max(0, remaining / duration);
            }
        }

        if (Toasts.Contains(toast))
        {
            await LeaveAsync(toast).ConfigureAwait(true);
        }
    }

    private async Task LeaveAsync(NotificationItemViewModel toast)
    {
        if (toast.IsLeaving)
        {
            return;
        }

        toast.IsLeaving = true;
        if (Services.MotionPreferences.AnimationsEnabled)
        {
            await Task.Delay(LeaveDuration).ConfigureAwait(true);
        }

        Toasts.Remove(toast);
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var n in _service.History)
        {
            Items.Add(new NotificationItemViewModel(n, _localization.Culture));
        }

        UnreadCount = IsOpen ? 0 : _service.UnreadCount;
        if (IsOpen && _service.UnreadCount > 0)
        {
            _service.MarkAllRead();
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void Post(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }
}
