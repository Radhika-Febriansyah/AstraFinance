using AstraFinance.Core.Enums;
using AstraFinance.Core.Models;
using AstraFinance.Infrastructure.Preferences;
using AstraFinance.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstraFinance.Tests.Infrastructure;

public sealed class PreferencesStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly AppPaths _paths;
    private readonly JsonPreferencesStore _store;

    public PreferencesStoreTests()
    {
        _paths = new AppPaths(_dir.Path);
        _store = new JsonPreferencesStore(_paths, NullLogger<JsonPreferencesStore>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public async Task Missing_file_is_first_run_with_defaults()
    {
        var result = await _store.LoadAsync();
        Assert.True(result.IsFirstRun);
        Assert.False(result.WasRecovered);
        Assert.Equal(UserPreferences.Default, result.Preferences);
    }

    [Fact]
    public async Task Round_trips_and_leaves_no_temp_file()
    {
        var prefs = UserPreferences.Default with
        {
            Language = AppLanguage.English, ThemeMode = AppThemeMode.Dark, Accent = AccentColor.Violet, IsSidebarExpanded = false,
        };

        await _store.SaveAsync(prefs);
        var result = await _store.LoadAsync();

        Assert.Equal(prefs, result.Preferences);
        Assert.False(result.IsFirstRun);
        Assert.False(File.Exists(_paths.PreferencesFile + ".tmp"));
        Assert.Contains("\"Dark\"", await File.ReadAllTextAsync(_paths.PreferencesFile));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"ThemeMode\": \"Neon\"}")]
    [InlineData("{\"Language\": 7}")]
    [InlineData("null")]
    public async Task Unreadable_file_is_set_aside_and_defaults_are_used(string content)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        await File.WriteAllTextAsync(_paths.PreferencesFile, content);

        var result = await _store.LoadAsync();

        Assert.True(result.WasRecovered);
        Assert.Equal(UserPreferences.Default, result.Preferences);
        Assert.False(File.Exists(_paths.PreferencesFile));
        var aside = Assert.Single(Directory.GetFiles(_paths.DataDirectory, "preferences.json.unreadable-*"));
        Assert.Equal(content, await File.ReadAllTextAsync(aside));
    }

    [Fact]
    public async Task Save_overwrites_previous_file_atomically()
    {
        await _store.SaveAsync(UserPreferences.Default with { Accent = AccentColor.Blue });
        await _store.SaveAsync(UserPreferences.Default with { Accent = AccentColor.Amber });

        var result = await _store.LoadAsync();
        Assert.Equal(AccentColor.Amber, result.Preferences.Accent);
    }
}
