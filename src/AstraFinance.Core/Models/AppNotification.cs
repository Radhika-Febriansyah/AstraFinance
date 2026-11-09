using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Models;

/// <summary>An in-app notification. Messages must never contain PINs or sensitive data.</summary>
public sealed record AppNotification(
    Guid Id,
    NotificationKind Kind,
    string Title,
    string Message,
    DateTimeOffset CreatedAt);
