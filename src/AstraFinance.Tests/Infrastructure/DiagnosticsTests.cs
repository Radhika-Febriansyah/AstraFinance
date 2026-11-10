using AstraFinance.App.Services;
using AstraFinance.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace AstraFinance.Tests.Infrastructure;

/// <summary>Repeated errors and the log file can never grow without bound.</summary>
public class DiagnosticsTests
{
    [Fact]
    public void Repeated_identical_errors_are_reported_once_per_window()
    {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var filter = new RepeatedErrorFilter(TimeSpan.FromMinutes(1), () => now);
        var layoutError = new InvalidOperationException("Outer", new InvalidOperationException("Cannot find resource named 'X'."));

        Assert.True(filter.ShouldReport(layoutError));
        Assert.False(filter.ShouldReport(new InvalidOperationException("Outer", new InvalidOperationException("Cannot find resource named 'X'."))));
        Assert.True(filter.ShouldReport(new ArgumentException("Something else")));

        now = now.AddSeconds(61);
        Assert.True(filter.ShouldReport(layoutError));
    }

    [Fact]
    public void Daily_log_file_stops_at_its_size_limit_with_one_notice()
    {
        using var dir = new TempDirectory();
        using var provider = new FileLoggerProvider(dir.Path, LogLevel.Information, maxFileBytes: 4_000);
        var logger = provider.CreateLogger("Test");

        for (var i = 0; i < 500; i++)
        {
#pragma warning disable CA1848, CA2254, CA1873 // plain calls are fine in this test
            logger.LogInformation("Entry number " + i);
#pragma warning restore CA1848, CA2254, CA1873
        }

        var text = File.ReadAllText(provider.CurrentLogFile);
        Assert.True(new FileInfo(provider.CurrentLogFile).Length < 4_200);
        Assert.Contains("Entry number 0", text, StringComparison.Ordinal);
        Assert.Single(text.Split('\n'), l => l.Contains("Daily log size limit reached", StringComparison.Ordinal));
        Assert.DoesNotContain("Entry number 499", text, StringComparison.Ordinal);
    }
}
