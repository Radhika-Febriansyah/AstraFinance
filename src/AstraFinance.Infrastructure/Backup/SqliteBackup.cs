using Microsoft.Data.Sqlite;

namespace AstraFinance.Infrastructure.Backup;

/// <summary>
/// Consistent copies of a live SQLite database using SQLite's online backup API. Unlike a file
/// copy, this includes committed data still in the WAL file and never captures a half-written page.
/// </summary>
public static class SqliteBackup
{
    /// <summary>Copies <paramref name="sourceFile"/> to <paramref name="destinationFile"/> (overwritten) and verifies the copy.</summary>
    public static async Task BackupAsync(string sourceFile, string destinationFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationFile))!);

        var temp = destinationFile + ".partial";
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        await Task.Run(
            () =>
            {
                using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
                using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
                {
                    source.Open();
                    destination.Open();
                    SetBusyTimeout(source);
                    SetBusyTimeout(destination);
                    BackupWithRetry(source, destination);

                    // The copied header inherits WAL mode from the live database; make the backup a
                    // self-contained single file so it can be verified, moved and opened read-only.
                    using var command = destination.CreateCommand();
                    command.CommandText = "PRAGMA journal_mode = DELETE;";
                    command.ExecuteNonQuery();
                }

                if (!IntegrityCheck(temp))
                {
                    File.Delete(temp);
                    throw new InvalidDataException("The backup copy failed its integrity check.");
                }

                File.Move(temp, destinationFile, overwrite: true);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>How long a backup waits for a lock held by another connection before giving up.</summary>
    public static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    /// <summary>
    /// The backup needs a brief shared lock on the live database. If the app is writing at that moment the
    /// busy timeout lets SQLite wait; if the lock is still held, the copy is retried a few times.
    /// </summary>
    private static void BackupWithRetry(SqliteConnection source, SqliteConnection destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                source.BackupDatabase(destination);
                return;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked && attempt < 5)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }

    private static void SetBusyTimeout(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout = {(int)LockWait.TotalMilliseconds};";
        command.ExecuteNonQuery();
    }

    /// <summary>Runs PRAGMA integrity_check; true when the database reports "ok".</summary>
    public static bool IntegrityCheck(string databaseFile)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal);
    }
}
