using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;

namespace AstraFinance.Core.Services;

/// <summary>Thread-safe in-memory notification history (newest first, bounded).</summary>
public sealed class NotificationCenter : INotificationService
{
    public const int MaxHistory = 50;

    private readonly IClock _clock;
    private readonly Lock _lock = new();
    private readonly List<AppNotification> _items = [];
    private int _unread;

    public NotificationCenter(IClock clock)
    {
        _clock = clock;
    }

    public IReadOnlyList<AppNotification> History
    {
        get
        {
            lock (_lock)
            {
                return _items.ToList();
            }
        }
    }

    public int UnreadCount
    {
        get
        {
            lock (_lock)
            {
                return _unread;
            }
        }
    }

    public event EventHandler<AppNotification>? NotificationAdded;

    public event EventHandler? Changed;

    public AppNotification Notify(NotificationKind kind, string title, string message)
    {
        var notification = new AppNotification(Guid.NewGuid(), kind, title ?? string.Empty, message ?? string.Empty, _clock.UtcNow);
        lock (_lock)
        {
            _items.Insert(0, notification);
            if (_items.Count > MaxHistory)
            {
                _items.RemoveRange(MaxHistory, _items.Count - MaxHistory);
            }

            _unread = Math.Min(_unread + 1, _items.Count);
        }

        NotificationAdded?.Invoke(this, notification);
        Changed?.Invoke(this, EventArgs.Empty);
        return notification;
    }

    public void MarkAllRead()
    {
        lock (_lock)
        {
            if (_unread == 0)
            {
                return;
            }

            _unread = 0;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid id)
    {
        lock (_lock)
        {
            if (_items.RemoveAll(n => n.Id == id) == 0)
            {
                return;
            }

            _unread = Math.Min(_unread, _items.Count);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            _unread = 0;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
