using System.Runtime.InteropServices;
using System.Windows;
using AstraFinance.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.UI.Notifications;

namespace AstraFinance.App.Services;

/// <summary>
/// Windows notifications for the unpackaged desktop app. On first use it registers the app's notification
/// identity for the current user only (HKCU\Software\Classes\AppUserModelId\Aprotoos.AstraFinance: display
/// name and icon), which is how Windows lets an unpackaged app show notifications. If Windows refuses
/// (policy, notifications turned off), it stops trying for the session; the in-app list still has every reminder.
/// Clicking a Windows notification only dismisses it in version 1.
/// </summary>
public sealed partial class WindowsToastNotifier : IWindowsNotifier
{
    public const string AppUserModelId = "Aprotoos.AstraFinance";

    private readonly IAppPaths _paths;
    private readonly ILogger<WindowsToastNotifier> _logger;
    private ToastNotifier? _notifier;
    private bool _unavailable;

    public WindowsToastNotifier(IAppPaths paths, ILogger<WindowsToastNotifier> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public bool IsAppInForeground => Application.Current?.MainWindow is { IsActive: true, WindowState: not WindowState.Minimized };

    /// <summary>Gives the process the same identity as its notifications (call once at startup, before windows open).</summary>
    public static void SetProcessIdentity()
    {
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
    }

    public void Show(string title, string body)
    {
        if (_unavailable)
        {
            return;
        }

        try
        {
            _notifier ??= CreateNotifier();
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var texts = xml.GetElementsByTagName("text");
            texts[0].AppendChild(xml.CreateTextNode(title));
            texts[1].AppendChild(xml.CreateTextNode(body));
            _notifier.Show(new ToastNotification(xml) { ExpirationTime = DateTimeOffset.Now.AddDays(1) });
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or InvalidOperationException or ArgumentException)
        {
            _unavailable = true;
            LogUnavailable(_logger, ex.GetType().Name);
        }
    }

    private ToastNotifier CreateNotifier()
    {
        string? iconPath = null;
        try
        {
            iconPath = Path.Combine(_paths.DataDirectory, "notification-icon.ico");
            if (!File.Exists(iconPath))
            {
                var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/astra.ico"));
                if (resource is not null)
                {
                    using var source = resource.Stream;
                    using var target = File.Create(iconPath);
                    source.CopyTo(target);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            iconPath = null; // a missing icon is cosmetic
        }

        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}"))
        {
            key.SetValue("DisplayName", "Astra Finance");
            if (iconPath is not null && File.Exists(iconPath))
            {
                key.SetValue("IconUri", iconPath);
            }
        }

        return ToastNotificationManager.CreateToastNotifier(AppUserModelId);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows notifications are unavailable ({ErrorType}); reminders stay in the app.")]
    private static partial void LogUnavailable(ILogger logger, string errorType);
}
