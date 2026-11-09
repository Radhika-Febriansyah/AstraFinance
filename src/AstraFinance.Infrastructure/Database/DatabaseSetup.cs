using AstraFinance.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace AstraFinance.Infrastructure.Database;

public static class DatabaseSetup
{
    public static string BuildConnectionString(string databaseFile) => new SqliteConnectionStringBuilder
    {
        DataSource = databaseFile,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private,
        ForeignKeys = true,
        Pooling = true,
    }.ToString();

    public static void Configure(DbContextOptionsBuilder options, string databaseFile)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.UseSqlite(BuildConnectionString(databaseFile), sqlite => sqlite.MigrationsAssembly(typeof(AstraDbContext).Assembly.FullName))
               .AddInterceptors(new SqlitePragmaInterceptor());
    }

    /// <summary>Registers the context factory (short-lived contexts per operation, safe for WPF).</summary>
    public static IServiceCollection AddAstraDatabase(this IServiceCollection services, IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        services.AddDbContextFactory<AstraDbContext>(options => Configure(options, paths.DatabaseFile));
        return services;
    }
}

/// <summary>Used only by the dotnet-ef tooling to create migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AstraDbContext>
{
    public AstraDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AstraDbContext>();
        DatabaseSetup.Configure(options, Path.Combine(Path.GetTempPath(), "astra-design.db"));
        return new AstraDbContext(options.Options);
    }
}
