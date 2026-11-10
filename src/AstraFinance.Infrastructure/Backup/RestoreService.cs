using System.Globalization;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Services;
using AstraFinance.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AstraFinance.Infrastructure.Backup;

/// <summary>A backup file can't be restored; <see cref="Code"/> is one of <see cref="RestoreProblems"/>.</summary>
public sealed class InvalidBackupException : Exception
{
    public InvalidBackupException()
    {
        Code = RestoreProblems.NotADatabase;
    }

    public InvalidBackupException(string code)
        : base("The backup can't be restored: " + code)
    {
        Code = code;
    }

    public InvalidBackupException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = RestoreProblems.Damaged;
    }

    public string Code { get; }
}

/// <summary>
/// Backup history and safe restore (see <see cref="IRestoreService"/>). The current data is replaced with
/// SQLite's online backup API, which is all-or-nothing on the destination, so an interrupted restore never
/// leaves half-replaced data. A safety copy of the current data is always taken first.
/// </summary>
public sealed partial class RestoreService : IRestoreService
{
    public const string PreRestorePrefix = "pre-restore-";
    public const string PreMigrationPrefix = "pre-migration-";

    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    private static readonly string[] RequiredTables = ["__EFMigrationsHistory", "Accounts", "Transactions", "Settings"];

    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IAppPaths _paths;
    private readonly IBackupService _backups;
    private readonly IDatabaseInitializer _initializer;
    private readonly IClock _clock;
    private readonly ILogger<RestoreService> _logger;

    public RestoreService(
        IDbContextFactory<AstraDbContext> factory,
        IAppPaths paths,
        IBackupService backups,
        IDatabaseInitializer initializer,
        IClock clock,
        ILogger<RestoreService> logger)
    {
        _factory = factory;
        _paths = paths;
        _backups = backups;
        _initializer = initializer;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _backups.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var folders = new[] { settings.Directory, _paths.BackupsDirectory }
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var entries = new List<BackupEntry>();
        foreach (var folder in folders)
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.db"))
            {
                var kind = KindOf(Path.GetFileName(file));
                if (kind is null)
                {
                    continue;
                }

                var info = new FileInfo(file);
                entries.Add(new BackupEntry(file, info.Name, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length, kind.Value));
            }
        }

        return entries.OrderByDescending(e => e.CreatedAt).ToList();
    }

    public Task<BackupInspection> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Inspect(path), cancellationToken);

    public async Task<RestoreResult> RestoreAsync(string path, CancellationToken cancellationToken = default)
    {
        // 1. Validate the selected backup.
        var inspection = await InspectAsync(path, cancellationToken).ConfigureAwait(false);
        if (!inspection.IsValid)
        {
            throw new InvalidBackupException(inspection.ProblemCode ?? RestoreProblems.NotADatabase);
        }

        // 2. Safety copy of the current data (also from the online backup API, so WAL content is included).
        var stamp = _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var safety = Path.Combine(_paths.BackupsDirectory, $"{PreRestorePrefix}{stamp}.db");
        await SqliteBackup.BackupAsync(_paths.DatabaseFile, safety, cancellationToken).ConfigureAwait(false);

        // 3. A verified, self-contained working copy of the backup (never the user's file itself).
        var staged = Path.Combine(_paths.DataDirectory, $"restore-{stamp}.tmp.db");
        await SqliteBackup.BackupAsync(path, staged, cancellationToken).ConfigureAwait(false);
        try
        {
            // 4. Replace the current data. Pooled connections are released first; the copy is one transaction
            //    on the live database, so a failure leaves it unchanged.
            SqliteConnection.ClearAllPools();
            await Task.Run(() => CopyInto(staged, _paths.DatabaseFile), cancellationToken).ConfigureAwait(false);

            // 5–6. Reopen: upgrade an older backup if needed and verify integrity.
            SqliteConnection.ClearAllPools();
            DatabaseInitResult opened;
            try
            {
                opened = await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DatabaseIntegrityException or SqliteException or InvalidOperationException or IOException)
            {
                // The restored data could not be opened: put the previous data back.
                LogRestoreRolledBack(_logger, ex.GetType().Name);
                SqliteConnection.ClearAllPools();
                CopyInto(safety, _paths.DatabaseFile);
                SqliteConnection.ClearAllPools();
                await _initializer.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
                throw new InvalidBackupException(RestoreProblems.Damaged);
            }

            LogRestored(_logger, opened.AppliedMigrations.Count);
            return new RestoreResult(safety, opened.AppliedMigrations);
        }
        finally
        {
            TryDelete(staged);
        }
    }

    private BackupInspection Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return BackupInspection.Invalid(RestoreProblems.NotFound);
        }

        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_paths.DatabaseFile), StringComparison.OrdinalIgnoreCase))
        {
            return BackupInspection.Invalid(RestoreProblems.SameFile);
        }

        if (!HasSqliteHeader(path))
        {
            return BackupInspection.Invalid(RestoreProblems.NotADatabase);
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            if (!string.Equals(Scalar(connection, "PRAGMA integrity_check;") as string, "ok", StringComparison.Ordinal))
            {
                return BackupInspection.Invalid(RestoreProblems.Damaged);
            }

            var tables = new HashSet<string>(StringComparer.Ordinal);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            if (!RequiredTables.All(tables.Contains))
            {
                return BackupInspection.Invalid(RestoreProblems.NotAstra);
            }

            var applied = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    applied.Add(reader.GetString(0));
                }
            }

            using var db = _factory.CreateDbContext();
            var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            if (applied.Any(m => !known.Contains(m)))
            {
                return BackupInspection.Invalid(RestoreProblems.NewerVersion);
            }

            var accounts = Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM Accounts;"), CultureInfo.InvariantCulture);
            var transactions = Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM Transactions WHERE IsDeleted = 0;"), CultureInfo.InvariantCulture);
            var lastDate = Scalar(connection, "SELECT MAX(Date) FROM Transactions WHERE IsDeleted = 0;") as string;
            var completed = Scalar(connection, $"SELECT Value FROM Settings WHERE Key = '{Phase3SettingKeys.OnboardingCompleted}';") as string;
            return new BackupInspection(
                true,
                null,
                accounts,
                transactions,
                DateOnly.TryParseExact(lastDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                string.Equals(completed, "true", StringComparison.OrdinalIgnoreCase),
                known.Any(m => !applied.Contains(m)));
        }
        catch (SqliteException)
        {
            return BackupInspection.Invalid(RestoreProblems.Damaged);
        }
    }

    /// <summary>Copies <paramref name="source"/> over <paramref name="destination"/> with SQLite's backup API (one transaction).</summary>
    private static void CopyInto(string source, string destination)
    {
        using var from = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        from.Open();
        to.Open();
        using (var timeout = to.CreateCommand())
        {
            timeout.CommandText = $"PRAGMA busy_timeout = {(int)SqliteBackup.LockWait.TotalMilliseconds};";
            timeout.ExecuteNonQuery();
        }

        from.BackupDatabase(to);
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private static bool HasSqliteHeader(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[SqliteHeader.Length];
            return stream.Read(buffer, 0, buffer.Length) == buffer.Length && buffer.AsSpan().SequenceEqual(SqliteHeader);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static BackupKind? KindOf(string fileName)
    {
        if (fileName.StartsWith(BackupService.FilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return fileName.EndsWith(BackupService.AutomaticSuffix + ".db", StringComparison.OrdinalIgnoreCase) ? BackupKind.Automatic : BackupKind.Manual;
        }

        if (fileName.StartsWith(PreMigrationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BackupKind.PreMigration;
        }

        return fileName.StartsWith(PreRestorePrefix, StringComparison.OrdinalIgnoreCase) ? BackupKind.PreRestore : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup restored (migrations applied: {Count}).")]
    private static partial void LogRestored(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restored data could not be opened ({ErrorType}); the previous data was put back.")]
    private static partial void LogRestoreRolledBack(ILogger logger, string errorType);
}
