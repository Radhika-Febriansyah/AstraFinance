using System.Globalization;
using AstraFinance.Core.Interfaces;
using AstraFinance.Infrastructure.Backup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AstraFinance.Infrastructure.Database;

/// <summary>The database was created by a newer version of Astra Finance; opening it could lose data.</summary>
public sealed class DatabaseNewerThanApplicationException : Exception
{
    public DatabaseNewerThanApplicationException()
    {
    }

    public DatabaseNewerThanApplicationException(string message)
        : base(message)
    {
    }

    public DatabaseNewerThanApplicationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The database failed SQLite's integrity check.</summary>
public sealed class DatabaseIntegrityException : Exception
{
    public DatabaseIntegrityException()
    {
    }

    public DatabaseIntegrityException(string message)
        : base(message)
    {
    }

    public DatabaseIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Opens the database at startup: refuses databases from newer app versions, takes a safety copy
/// before applying migrations to an existing database, migrates, verifies integrity, and seeds
/// built-in categories.
/// </summary>
public sealed partial class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<AstraDbContext> _factory;
    private readonly IAppPaths _paths;
    private readonly ICategoryService _categories;
    private readonly IClock _clock;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDbContextFactory<AstraDbContext> factory,
        IAppPaths paths,
        ICategoryService categories,
        IClock clock,
        ILogger<DatabaseInitializer> logger)
    {
        _factory = factory;
        _paths = paths;
        _categories = categories;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DatabaseInitResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        var file = _paths.DatabaseFile;
        var createdNew = !File.Exists(file) || new FileInfo(file).Length == 0;
        string? backupPath = null;

        var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var unknown = applied.Where(m => !known.Contains(m)).ToList();
            if (unknown.Count > 0)
            {
                throw new DatabaseNewerThanApplicationException(
                    "The database was created by a newer version of Astra Finance. Unknown migrations: " + string.Join(", ", unknown));
            }

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (!createdNew && pending.Count > 0)
            {
                backupPath = Path.Combine(
                    _paths.BackupsDirectory,
                    string.Create(CultureInfo.InvariantCulture, $"pre-migration-{_clock.UtcNow:yyyyMMdd-HHmmss}.db"));
                await SqliteBackup.BackupAsync(file, backupPath, cancellationToken).ConfigureAwait(false);
                LogPreMigrationBackup(_logger, pending.Count);
            }

            if (pending.Count > 0)
            {
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }

            var check = await db.Database.SqlQueryRaw<string>("PRAGMA quick_check").ToListAsync(cancellationToken).ConfigureAwait(false);
            if (check.Count != 1 || check[0] != "ok")
            {
                throw new DatabaseIntegrityException("The database failed its integrity check.");
            }

            await _categories.EnsureDefaultsAsync(cancellationToken).ConfigureAwait(false);
            LogOpened(_logger, createdNew, pending.Count);
            return new DatabaseInitResult(createdNew, pending, backupPath);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Safety copy taken before applying {Count} migration(s).")]
    private static partial void LogPreMigrationBackup(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database opened (new: {CreatedNew}, migrations applied: {Count}).")]
    private static partial void LogOpened(ILogger logger, bool createdNew, int count);
}
