using System.Collections.ObjectModel;
using System.Globalization;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstraFinance.App.ViewModels;

public sealed class NotificationItemViewModel
{
    public NotificationItemViewModel(AppNotification notification, CultureInfo culture)
    {
        Notification = notification;
        TimeText = notification.CreatedAt.ToLocalTime().ToString("t", culture);
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
}

/// <summary>Bell flyout (history + unread badge) and transient toasts.</summary>
public sealed partial class NotificationCenterViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(9);
    public const int MaxToasts = 3;

    private readonly INotificationService _service;
    private readonly ILocalizationService _localization;
    private readonly SynchronizationContext? _ui;

    public NotificationCenterViewModel(INotificationService service, ILocalizationService localization)
    {
        _service = service;
        _localization = localization;
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
    public void DismissToast(NotificationItemViewModel? item)
    {
        if (item is not null)
        {
            Toasts.Remove(item);
        }
    }

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
        Toasts.Add(toast);
        while (Toasts.Count > MaxToasts)
        {
            Toasts.RemoveAt(0);
        }

        await Task.Delay(notification.Kind == NotificationKind.Error ? ErrorToastDuration : ToastDuration).ConfigureAwait(true);
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
