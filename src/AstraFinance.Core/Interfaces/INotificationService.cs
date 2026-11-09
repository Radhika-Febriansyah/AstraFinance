using AstraFinance.Core.Enums;
using AstraFinance.Core.Models;

namespace AstraFinance.Core.Interfaces;

/// <summary>
/// In-app notification center. Notifications are shown as toasts and kept in a session history.
/// Never include PINs or sensitive financial details in notification text.
/// </summary>
public interface INotificationService
{
    IReadOnlyList<AppNotification> History { get; }

    int UnreadCount { get; }

    /// <summary>Raised (on the calling thread) when a notification is added.</summary>
    event EventHandler<AppNotification>? NotificationAdded;

    /// <summary>Raised when the history or unread count changes.</summary>
    event EventHandler? Changed;

    AppNotification Notify(NotificationKind kind, string title, string message);

    void MarkAllRead();

    void Remove(Guid id);

    void Clear();
}
