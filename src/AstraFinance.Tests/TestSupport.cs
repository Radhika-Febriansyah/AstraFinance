using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;

namespace AstraFinance.Tests;

internal sealed class FakeClock : IClock
{
    public DateTime LocalNow { get; set; } = new(2026, 11, 9, 9, 30, 0, DateTimeKind.Local);

    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 9, 2, 30, 0, TimeSpan.Zero);
}

internal sealed class InMemoryPreferencesStore : IPreferencesStore
{
    public UserPreferences? Saved { get; private set; }

    public int SaveCount { get; private set; }

    public bool FailSaves { get; set; }

    public PreferencesLoadResult LoadResult { get; set; } = new(UserPreferences.Default, false, IsFirstRun: true);

    public Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(LoadResult);

    public Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        if (FailSaves)
        {
            throw new IOException("Disk full (simulated).");
        }

        Saved = preferences;
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSystemTheme : ISystemThemeProvider
{
    public ResolvedTheme? Theme { get; set; } = ResolvedTheme.Light;

    public event EventHandler? SystemThemeChanged;

    public ResolvedTheme? GetSystemTheme() => Theme;

    public void Raise() => SystemThemeChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeFolderPicker : AstraFinance.App.Services.IFolderPicker
{
    public string? NextResult { get; set; }

    public string? PickFolder(string title, string? initialDirectory) => NextResult;
}

internal sealed class FakeFileSavePicker : AstraFinance.App.Services.IFileSavePicker
{
    public string? NextResult { get; set; }

    public string? PickSaveFile(string title, string defaultFileName, string filter) => NextResult;
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AstraFinanceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class RepoPaths
{
    /// <summary>Locates the repository root (the folder containing AstraFinance.sln).</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "AstraFinance.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    public static string App => System.IO.Path.Combine(Root, "src", "AstraFinance.App");

    public static string Core => System.IO.Path.Combine(Root, "src", "AstraFinance.Core");
}

internal sealed class FakeWindowsNotifier : AstraFinance.App.Services.IWindowsNotifier
{
    public List<(string Title, string Body)> Shown { get; } = [];

    public bool IsAppInForeground { get; set; }

    public void Show(string title, string body) => Shown.Add((title, body));
}

internal sealed class FakeFileOpenPicker : AstraFinance.App.Services.IFileOpenPicker
{
    public string? NextResult { get; set; }

    public string? PickOpenFile(string title, string filter, string? initialDirectory) => NextResult;
}
