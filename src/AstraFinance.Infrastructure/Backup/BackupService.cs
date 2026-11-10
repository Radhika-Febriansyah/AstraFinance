using System.Globalization;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Services;
using AstraFinance.Core.Validation;
using Microsoft.Extensions.Logging;

namespace AstraFinance.Infrastructure.Backup;

/// <summary>
/// Manual and scheduled backups using SQLite's online backup API (safe while the app is running,
/// includes WAL contents, verified with an integrity check). Retention only ever deletes older
/// <em>automatic</em> backups; manual backups are never removed.
/// </summary>
public sealed partial class BackupService : IBackupService, IDisposable
{
    public const string FilePrefix = "astra-backup-";
    public const string AutomaticSuffix = "-auto";

    private readonly ISettingsStore _settings;
    private readonly IAppPaths _paths;
    private readonly IClock _clock;
    private readonly ILogger<BackupService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupService(ISettingsStore settings, IAppPaths paths, IClock clock, ILogger<BackupService> logger)
    {
        _settings = settings;
        _paths = paths;
        _clock = clock;
        _logger = logger;
    }

    public void Dispose() => _gate.Dispose();

    public async Task<BackupSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var dir = await _settings.GetAsync(Phase3SettingKeys.BackupDirectory, cancellationToken).ConfigureAwait(false);
        var schedule = await _settings.GetAsync(Phase3SettingKeys.BackupSchedule, cancellationToken).ConfigureAwait(false);
        var retention = await _settings.GetAsync(Phase3SettingKeys.BackupRetention, cancellationToken).ConfigureAwait(false);
        return new BackupSettings(
            string.IsNullOrWhiteSpace(dir) ? _paths.BackupsDirectory : dir,
            Enum.TryParse<BackupSchedule>(schedule, out var s) && Enum.IsDefined(s) ? s : BackupSchedule.Weekly,
            int.TryParse(retention, NumberStyles.None, CultureInfo.InvariantCulture, out var r) && r > 0 ? r : BackupSettings.DefaultRetention);
    }

    public async Task SaveSettingsAsync(BackupSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetFullPath(settings.Directory);
        if (!IsWritableDirectory(directory))
        {
            throw new FinanceValidationException("Directory", ValidationCodes.DirectoryNotWritable);
        }

        await _settings.SetAsync(Phase3SettingKeys.BackupDirectory, directory, cancellationToken).ConfigureAwait(false);
        await _settings.SetAsync(Phase3SettingKeys.BackupSchedule, settings.Schedule.ToString(), cancellationToken).ConfigureAwait(false);
        await _settings.SetAsync(Phase3SettingKeys.BackupRetention, Math.Max(1, settings.RetentionCount).ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
    }

    public bool IsWritableDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".astra-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public async Task<BackupFileInfo> CreateBackupAsync(bool isAutomatic, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(settings.Directory);

            var now = _clock.UtcNow;
            var stamp = now.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var suffix = isAutomatic ? AutomaticSuffix : string.Empty;
            var path = Path.Combine(settings.Directory, $"{FilePrefix}{stamp}{suffix}.db");
            for (var i = 2; File.Exists(path); i++)
            {
                path = Path.Combine(settings.Directory, $"{FilePrefix}{stamp}-{i}{suffix}.db");
            }

            await SqliteBackup.BackupAsync(_paths.DatabaseFile, path, cancellationToken).ConfigureAwait(false);
            await _settings.SetAsync(Phase3SettingKeys.BackupLastUtc, now.ToString("O", CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);

            if (isAutomatic)
            {
                ApplyRetention(settings);
            }

            LogBackupCreated(_logger, isAutomatic);
            return new BackupFileInfo(path, now, new FileInfo(path).Length, isAutomatic);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackupFileInfo?> RunScheduledBackupIfDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var last = await GetLastBackupAsync(cancellationToken).ConfigureAwait(false);
        return IsDue(settings.Schedule, last, _clock.UtcNow)
            ? await CreateBackupAsync(isAutomatic: true, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<DateTimeOffset?> GetLastBackupAsync(CancellationToken cancellationToken = default) =>
        DateTimeOffset.TryParse(
            await _settings.GetAsync(Phase3SettingKeys.BackupLastUtc, cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var last)
            ? last
            : null;

    /// <summary>Whether a scheduled backup is due given the last successful backup.</summary>
    public static bool IsDue(BackupSchedule schedule, DateTimeOffset? lastBackup, DateTimeOffset now)
    {
        if (schedule == BackupSchedule.Off)
        {
            return false;
        }

        if (lastBackup is not { } last)
        {
            return true;
        }

        var next = schedule switch
        {
            BackupSchedule.Daily => last.AddDays(1),
            BackupSchedule.Weekly => last.AddDays(7),
            _ => last.AddMonths(1),
        };
        return now >= next;
    }

    private void ApplyRetention(BackupSettings settings)
    {
        var automatic = new DirectoryInfo(settings.Directory)
            .GetFiles($"{FilePrefix}*{AutomaticSuffix}.db")
            .OrderByDescending(f => f.CreationTimeUtc)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(settings.RetentionCount);
        foreach (var file in automatic)
        {
            try
            {
                file.Delete();
            }
            catch (IOException ex)
            {
                LogRetentionFailed(_logger, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogRetentionFailed(_logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup created (automatic: {IsAutomatic}).")]
    private static partial void LogBackupCreated(ILogger logger, bool isAutomatic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An old automatic backup could not be removed.")]
    private static partial void LogRetentionFailed(ILogger logger, Exception exception);
}
