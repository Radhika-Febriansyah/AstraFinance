using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using AstraFinance.App.Localization;
using AstraFinance.App.Services;
using AstraFinance.App.ViewModels;
using AstraFinance.App.Views;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Localization;
using AstraFinance.Infrastructure.Database;
using AstraFinance.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AstraFinance.App;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;
    private readonly RepeatedErrorFilter _uiErrors = new(TimeSpan.FromMinutes(1));
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private string _logsDirectory = string.Empty;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceGuard = SingleInstanceGuard.TryAcquire();
        if (_instanceGuard is null)
        {
            // Another instance is running and has been asked to come to the front.
            Shutdown(0);
            return;
        }

        // Same identity for the process and its Windows notifications (set before any window opens).
        WindowsToastNotifier.SetProcessIdentity();

        try
        {
            var paths = new AppPaths();
            paths.EnsureCreated();
            _logsDirectory = paths.LogsDirectory;

            _services = CompositionRoot.Build(paths);
            _logger = _services.GetRequiredService<ILogger<App>>();
            RegisterGlobalExceptionHandlers();

            var localization = _services.GetRequiredService<ILocalizationService>();
            var preferences = _services.GetRequiredService<IPreferencesService>();
            var loadResult = await preferences.InitializeAsync().ConfigureAwait(true);
            if (loadResult.IsFirstRun)
            {
                // Before onboarding (phase 3) chooses a language, follow the Windows display language.
                var language = LanguageCultures.FromCulture(CultureInfo.CurrentUICulture);
                await preferences.UpdateAsync(p => p with { Language = language }).ConfigureAwait(true);
            }

            // PreferencesService keeps the localization language in sync; mirror it to the thread culture.
            ApplyUiCulture(localization);
            localization.LanguageChanged += (_, _) => ApplyUiCulture(localization);
            LocalizationSource.Instance.Attach(localization);

            _services.GetRequiredService<ThemeService>().Initialize(Resources);

            // Open (create/migrate/verify) the database before any page can read financial data.
            var database = await OpenDatabaseAsync(localization, paths).ConfigureAwait(true);
            if (database is null)
            {
                Shutdown(2);
                return;
            }

            // Charts use the app font (Segoe UI) instead of Skia's default typeface.
            LiveChartsCore.LiveCharts.Configure(config => LiveChartsCore.SkiaSharpView.LiveChartsSkiaSharp.HasTextSettings(config, new LiveChartsCore.SkiaSharpView.TextSettings { DefaultTypeface = SkiaSharp.SKTypeface.FromFamilyName("Segoe UI") }));

            var regional = _services.GetRequiredService<IRegionalFormatService>();
            await regional.LoadAsync().ConfigureAwait(true);
            Converters.DisplayFormat.Service = regional;

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            _instanceGuard.ListenForActivation(() => Dispatcher.BeginInvoke(window.BringToFront));

            await window.ViewModel.InitializeAsync().ConfigureAwait(true);

            if (database.PreMigrationBackupPath is not null)
            {
                _services.GetRequiredService<INotificationService>().Notify(
                    NotificationKind.Info,
                    localization.GetString("Notification.Migrated.Title"),
                    localization.GetString("Notification.Migrated.Body"));
            }

            if (loadResult.WasRecovered)
            {
                _services.GetRequiredService<INotificationService>().Notify(
                    NotificationKind.Warning,
                    localization.GetString("Notification.PreferencesReset.Title"),
                    localization.GetString("Notification.PreferencesReset.Body"));
            }

            LogStarted(_logger, AppInfo.Version);
        }
#pragma warning disable CA1031 // Startup must report any failure instead of terminating silently.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (_logger is not null)
            {
                LogStartupFailed(_logger, ex);
            }
            MessageBox.Show(
                "Astra Finance could not start.\n\n" + ex.Message,
                "Astra Finance",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Initializes the database. On failure the user's data is left untouched, a localized
    /// explanation is shown, and null is returned so the app can exit safely.
    /// </summary>
    private async Task<DatabaseInitResult?> OpenDatabaseAsync(ILocalizationService localization, AppPaths paths)
    {
        try
        {
            return await _services!.GetRequiredService<IDatabaseInitializer>().InitializeAsync().ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Any failure must be reported; the database is never modified after a failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (_logger is not null)
            {
                LogDatabaseFailed(_logger, ex);
            }

            var message = ex switch
            {
                DatabaseNewerThanApplicationException => localization.GetString("Error.Database.Newer"),
                DatabaseIntegrityException => localization.Format("Error.Database.Integrity", paths.DataDirectory),
                _ => localization.Format("Error.Database.Generic", paths.LogsDirectory),
            };
            MessageBox.Show(message, localization.GetString("Error.Database.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private static void ApplyUiCulture(ILocalizationService localization)
    {
        CultureInfo.DefaultThreadCurrentUICulture = localization.Culture;
        CultureInfo.CurrentUICulture = localization.Culture;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                if (_logger is not null)
                {
                    LogUnhandled(_logger, ex, args.IsTerminating);
                }
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            if (_logger is not null)
            {
                LogUnobserved(_logger, args.Exception);
            }

            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        // The same error repeating (e.g. on every layout pass) is logged and shown once per minute.
        if (!_uiErrors.ShouldReport(e.Exception))
        {
            return;
        }

        if (_logger is not null)
        {
            LogUiException(_logger, e.Exception);
        }

        var localization = _services?.GetService<ILocalizationService>();
        var dialogs = _services?.GetService<IDialogService>();
        if (localization is not null && dialogs is not null && MainWindow is { IsVisible: true })
        {
            _ = dialogs.AlertAsync(
                NotificationKind.Error,
                localization.GetString("Error.Unexpected.Title"),
                localization.Format("Error.Unexpected.Body", _logsDirectory));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Astra Finance {Version} started.")]
    private static partial void LogStarted(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The database could not be opened.")]
    private static partial void LogDatabaseFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Startup failed.")]
    private static partial void LogStartupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (terminating: {IsTerminating}).")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved task exception.")]
    private static partial void LogUnobserved(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled UI exception.")]
    private static partial void LogUiException(ILogger logger, Exception exception);
}
