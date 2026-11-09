using AstraFinance.Core.Interfaces;

namespace AstraFinance.Infrastructure.Services;

/// <summary>
/// Per-user application data locations. Data lives under %LOCALAPPDATA%\AstraFinance,
/// which Windows restricts to the current user profile by default, and is never stored
/// in the installation directory.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public const string FolderName = "AstraFinance";

    public AppPaths()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName))
    {
    }

    public AppPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string DataDirectory { get; }

    public string PreferencesFile => Path.Combine(DataDirectory, "preferences.json");

    public string LogsDirectory => Path.Combine(DataDirectory, "Logs");

    public string DatabaseFile => Path.Combine(DataDirectory, "astra.db");

    public string BackupsDirectory => Path.Combine(DataDirectory, "Backups");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
