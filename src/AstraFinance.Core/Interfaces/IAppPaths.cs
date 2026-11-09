namespace AstraFinance.Core.Interfaces;

/// <summary>Stable per-user locations used by the application, outside the installation directory.</summary>
public interface IAppPaths
{
    /// <summary>Root data directory, e.g. %LOCALAPPDATA%\AstraFinance.</summary>
    string DataDirectory { get; }

    string PreferencesFile { get; }

    string LogsDirectory { get; }

    /// <summary>The SQLite database file.</summary>
    string DatabaseFile { get; }

    /// <summary>Default directory for safety copies (pre-migration, pre-restore) and manual backups.</summary>
    string BackupsDirectory { get; }

    /// <summary>Creates the directories if they do not exist.</summary>
    void EnsureCreated();
}
