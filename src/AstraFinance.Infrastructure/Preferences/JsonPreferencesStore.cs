using System.Text.Json;
using System.Text.Json.Serialization;
using AstraFinance.Core.Interfaces;
using AstraFinance.Core.Models;
using Microsoft.Extensions.Logging;

namespace AstraFinance.Infrastructure.Preferences;

/// <summary>
/// Stores UI preferences as JSON. Writes go to a temporary file which then atomically replaces
/// the previous file, so an interrupted write never leaves a half-written preferences file.
/// An unreadable file is renamed aside (never deleted) and defaults are returned.
/// </summary>
public sealed partial class JsonPreferencesStore : IPreferencesStore, IDisposable
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<JsonPreferencesStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonPreferencesStore(IAppPaths paths, ILogger<JsonPreferencesStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<PreferencesLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.PreferencesFile;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return new PreferencesLoadResult(UserPreferences.Default, WasRecovered: false, IsFirstRun: true);
            }

            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                var loaded = await JsonSerializer.DeserializeAsync<UserPreferences>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (loaded is null || !IsValid(loaded))
                {
                    throw new JsonException("Preferences file contains invalid values.");
                }

                return new PreferencesLoadResult(loaded, WasRecovered: false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                LogUnreadable(_logger, ex);
                SetAside(path);
                return new PreferencesLoadResult(UserPreferences.Default, WasRecovered: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file exists but cannot be opened right now; keep it untouched and use defaults.
                LogUnreadable(_logger, ex);
                return new PreferencesLoadResult(UserPreferences.Default, WasRecovered: false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var path = _paths.PreferencesFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, preferences with { SchemaVersion = UserPreferences.CurrentSchemaVersion }, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static bool IsValid(UserPreferences p) =>
        Enum.IsDefined(p.Language) && Enum.IsDefined(p.ThemeMode) && Enum.IsDefined(p.Accent);

    private void SetAside(string path)
    {
        try
        {
            var aside = $"{path}.unreadable-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, aside, overwrite: true);
        }
        catch (IOException ex)
        {
            LogSetAsideFailed(_logger, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogSetAsideFailed(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Preferences file was unreadable; defaults restored.")]
    private static partial void LogUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not set aside unreadable preferences file.")]
    private static partial void LogSetAsideFailed(ILogger logger, Exception exception);
}
