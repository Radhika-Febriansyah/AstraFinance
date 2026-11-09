using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Localization;
using AstraFinance.Core.Models;
using AstraFinance.Core.Navigation;

namespace AstraFinance.Tests.Core;

public partial class LocalizationTests
{
    private static HashSet<string> Keys(CultureInfo culture)
    {
        var set = LocalizationService.Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                  ?? throw new InvalidOperationException($"No resource set for {culture}.");
        return set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void English_and_Indonesian_define_exactly_the_same_keys()
    {
        var english = Keys(CultureInfo.InvariantCulture);
        var indonesian = Keys(CultureInfo.GetCultureInfo("id"));

        Assert.Empty(english.Except(indonesian));
        Assert.Empty(indonesian.Except(english));
        Assert.True(english.Count > 50);
    }

    [Fact]
    public void Format_placeholders_match_between_languages()
    {
        var en = new LocalizationService(AppLanguage.English);
        var id = new LocalizationService(AppLanguage.Indonesian);
        foreach (var key in Keys(CultureInfo.InvariantCulture))
        {
            var enArgs = Placeholder().Matches(en.GetString(key)).Select(m => m.Value).Order().ToList();
            var idArgs = Placeholder().Matches(id.GetString(key)).Select(m => m.Value).Order().ToList();
            Assert.True(enArgs.SequenceEqual(idArgs), $"Placeholder mismatch for '{key}'.");
        }
    }

    [Fact]
    public void Every_page_and_search_category_has_strings()
    {
        var service = new LocalizationService(AppLanguage.English);
        var keys = Keys(CultureInfo.InvariantCulture);
        foreach (var page in PageCatalog.All)
        {
            Assert.Contains(page.TitleKey, keys);
            Assert.Contains(page.DescriptionKey, keys);
        }

        foreach (var category in Enum.GetValues<SearchCategory>())
        {
            Assert.Contains($"SearchCategory.{category}", keys);
        }

        foreach (var accent in Enum.GetValues<AccentColor>())
        {
            Assert.Contains($"Accent.{accent}", keys);
        }

        Assert.Equal("Dashboard", service.GetString("Nav.Dashboard"));
    }

    [Fact]
    public void Every_localization_key_used_in_source_exists()
    {
        var keys = Keys(CultureInfo.InvariantCulture);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(RepoPaths.App, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        foreach (var file in files)
        {
            foreach (Match m in LocUsage().Matches(File.ReadAllText(file)))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        var missing = used.Where(k => !keys.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Missing keys: " + string.Join(", ", missing));
        Assert.NotEmpty(used);
    }

    [Fact]
    public void Every_validation_code_and_default_category_has_strings()
    {
        var keys = Keys(CultureInfo.InvariantCulture);
        var codes = typeof(AstraFinance.Core.Validation.ValidationCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!);
        foreach (var code in codes)
        {
            Assert.Contains($"Validation.{code}", keys);
        }

        foreach (var category in AstraFinance.Core.Services.DefaultCategories.All)
        {
            Assert.Contains($"Category.{category.Key}", keys);
        }
    }

    [Fact]
    public void Switching_language_changes_text_and_raises_event_once()
    {
        var service = new LocalizationService(AppLanguage.English);
        var raised = 0;
        service.LanguageChanged += (_, _) => raised++;

        service.SetLanguage(AppLanguage.Indonesian);
        service.SetLanguage(AppLanguage.Indonesian);

        Assert.Equal(1, raised);
        Assert.Equal("Dasbor", service.GetString("Nav.Dashboard"));
        Assert.Equal("id-ID", service.Culture.Name);
    }

    [Fact]
    public void Missing_key_is_visible_rather_than_blank()
    {
        var service = new LocalizationService(AppLanguage.English);
        Assert.Equal("[Does.Not.Exist]", service.GetString("Does.Not.Exist"));
    }

    [Theory]
    [InlineData("id-ID", AppLanguage.Indonesian)]
    [InlineData("en-US", AppLanguage.English)]
    [InlineData("ja-JP", AppLanguage.English)]
    public void Default_language_follows_windows_culture(string culture, AppLanguage expected) =>
        Assert.Equal(expected, LanguageCultures.FromCulture(CultureInfo.GetCultureInfo(culture)));

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{loc:Loc\s+([A-Za-z0-9_.]+)\}")]
    private static partial Regex LocUsage();
}
