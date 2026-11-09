using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AstraFinance.Infrastructure.Database;

/// <summary>
/// Applies durability and integrity settings to every SQLite connection:
/// foreign keys on, WAL journal (readers never block the writer) and synchronous=FULL so a
/// committed financial record survives power loss.
/// </summary>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string CommonPragmas = "PRAGMA foreign_keys = ON; PRAGMA synchronous = FULL; PRAGMA busy_timeout = 5000;";

    // Changing the journal mode writes to the file, so it is only issued on writable connections
    // (EF Core opens read-only connections, e.g. to test whether the database exists).
    private const string WritablePragmas = "PRAGMA journal_mode = WAL; " + CommonPragmas;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = connection.CreateCommand();
        command.CommandText = PragmasFor(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = PragmasFor(connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string PragmasFor(DbConnection connection) =>
        new SqliteConnectionStringBuilder(connection.ConnectionString).Mode == SqliteOpenMode.ReadOnly
            ? CommonPragmas
            : WritablePragmas;
}
