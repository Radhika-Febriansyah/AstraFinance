using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AstraFinance.Infrastructure.Logging;

/// <summary>
/// Minimal local diagnostic log (one file per day, 14 days retained).
/// Privacy rule: callers must never log PINs, balances, amounts, descriptions or other
/// financial content. Only technical events and exception details are written.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const int RetentionDays = 14;

    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly Lock _writeLock = new();

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(directory);
        PurgeOldLogs();
    }

    public string CurrentLogFile => Path.Combine(_directory, $"astra-{DateTime.UtcNow:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    public void Dispose() => _loggers.Clear();

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var builder = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
            .Append(category).Append(": ").AppendLine(message);
        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(CurrentLogFile, builder.ToString(), Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never crash the application.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void PurgeOldLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "astra-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
