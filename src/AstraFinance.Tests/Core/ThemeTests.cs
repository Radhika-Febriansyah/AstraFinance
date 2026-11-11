using System.Text.RegularExpressions;
using System.Xml.Linq;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Navigation;
using AstraFinance.Core.Services;

namespace AstraFinance.Tests.Core;

public partial class ThemeTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static HashSet<string> DictionaryKeys(string relativePath)
    {
        var doc = XDocument.Load(Path.Combine(RepoPaths.App, relativePath));
        return doc.Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Keys provided at runtime by ThemeService.BuildAccentDictionary.
    private static readonly string[] AccentKeys =
    [
        "Color.Accent", "Brush.Accent", "Brush.Accent.Hover", "Brush.Accent.Pressed",
        "Brush.Accent.Subtle", "Brush.Accent.Text", "Brush.Accent.Foreground", "Brush.Focus",
        "Color.Accent.GradientStart", "Brush.Accent.Gradient",
    ];

    [Theory]
    [InlineData(AppThemeMode.Light, null, ResolvedTheme.Light)]
    [InlineData(AppThemeMode.Dark, ResolvedTheme.Light, ResolvedTheme.Dark)]
    [InlineData(AppThemeMode.System, ResolvedTheme.Dark, ResolvedTheme.Dark)]
    [InlineData(AppThemeMode.System, ResolvedTheme.Light, ResolvedTheme.Light)]
    [InlineData(AppThemeMode.System, null, ResolvedTheme.Light)]
    public void Resolves_theme(AppThemeMode mode, ResolvedTheme? system, ResolvedTheme expected) =>
        Assert.Equal(expected, ThemeResolver.Resolve(mode, system));

    [Theory]
    [InlineData(ResolvedTheme.Light)]
    [InlineData(ResolvedTheme.Dark)]
    public void Royal_blue_brand_accent_is_the_default(ResolvedTheme theme)
    {
        Assert.Equal(AccentColor.Blue, new AstraFinance.Core.Models.UserPreferences().Accent);

        var swatch = AccentPalette.Get(AccentColor.Blue, theme);
        Assert.Equal(AccentPalette.BrandRoyal, swatch.Accent);
        Assert.Equal(RgbColor.Parse("#FFFFFF"), swatch.Foreground); // white text on the brand gradient
        if (theme == ResolvedTheme.Light)
        {
            Assert.Equal(AccentPalette.BrandNavy, swatch.GradientStart); // Deep Navy → Royal Blue
        }
    }

    [Fact]
    public void Light_and_dark_palettes_define_identical_keys()
    {
        var light = DictionaryKeys("Themes/Colors.Light.xaml");
        var dark = DictionaryKeys("Themes/Colors.Dark.xaml");
        Assert.Empty(light.Except(dark));
        Assert.Empty(dark.Except(light));
    }

    [Fact]
    public void Every_dynamic_color_resource_used_in_xaml_is_defined()
    {
        var defined = DictionaryKeys("Themes/Colors.Light.xaml");
        defined.UnionWith(DictionaryKeys("Themes/Tokens.xaml")); // theme-independent brand brushes
        defined.UnionWith(AccentKeys);
        defined.Add("Padding.Page.Current");

        var used = XamlFiles()
            .SelectMany(f => DynamicResource().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var missing = used.Where(k => !defined.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Undefined dynamic resources: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_icon_referenced_is_defined()
    {
        var icons = DictionaryKeys("Themes/Icons.xaml");
        var used = XamlFiles().Concat(Directory.EnumerateFiles(RepoPaths.App, "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(f))
            .SelectMany(f => IconKey().Matches(File.ReadAllText(f)).Select(m => m.Value))
            .Concat(PageCatalog.All.Select(p => p.IconKey))
            .Concat(AstraFinance.Core.Services.DefaultCategories.All.Select(c => c.IconKey))
            .ToHashSet(StringComparer.Ordinal);

        var missing = used.Where(k => !icons.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Undefined icons: " + string.Join(", ", missing));
    }

    /// <summary>
    /// A StaticResource must be defined in the same file or in an app-wide dictionary (App.xaml, Themes);
    /// a key that only exists in another page's resources fails at runtime when the page is shown.
    /// </summary>
    [Fact]
    public void Every_static_resource_is_defined_where_it_is_used()
    {
        var globalFiles = XamlFiles().Where(f =>
            Path.GetFileName(f) == "App.xaml"
            || f.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var global = globalFiles.SelectMany(f => XKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var file in XamlFiles())
        {
            var text = File.ReadAllText(file);
            var local = XKey().Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            missing.AddRange(StaticResource().Matches(text)
                .Select(m => m.Groups[1].Value)
                .Where(k => !local.Contains(k) && !global.Contains(k))
                .Select(k => $"{Path.GetFileName(file)}: {k}"));
        }

        Assert.True(missing.Count == 0, "Undefined static resources: " + string.Join(", ", missing.Distinct()));
    }

    [Theory]
    [InlineData(ResolvedTheme.Dark)]
    [InlineData(ResolvedTheme.Light)]
    public void Accent_presets_meet_wcag_aa_contrast(ResolvedTheme theme)
    {
        var background = theme == ResolvedTheme.Dark ? AccentPalette.DarkBackground : AccentPalette.LightBackground;
        var surface = theme == ResolvedTheme.Dark ? AccentPalette.DarkSurface : AccentPalette.LightSurface;

        foreach (var accent in Enum.GetValues<AccentColor>())
        {
            var swatch = AccentPalette.Get(accent, theme);
            Assert.True(ColorMath.ContrastRatio(swatch.Text, background) >= 4.5, $"{accent}/{theme}: accent text on background");
            Assert.True(ColorMath.ContrastRatio(swatch.Text, surface) >= 4.5, $"{accent}/{theme}: accent text on surface");
            Assert.True(ColorMath.ContrastRatio(swatch.Foreground, swatch.Accent) >= 4.5, $"{accent}/{theme}: text on accent fill");
            Assert.True(ColorMath.ContrastRatio(swatch.Foreground, swatch.GradientStart) >= 4.5, $"{accent}/{theme}: text on gradient start");

            var selected = ColorMath.Composite(swatch.Subtle, background);
            Assert.True(ColorMath.ContrastRatio(swatch.Text, selected) >= 4.5, $"{accent}/{theme}: accent text on selected row");
        }
    }

    [Fact]
    public void Contrast_ratio_matches_wcag_reference_values()
    {
        Assert.Equal(21, ColorMath.ContrastRatio(RgbColor.Parse("#000000"), RgbColor.Parse("#FFFFFF")), 2);
        Assert.Equal(1, ColorMath.ContrastRatio(RgbColor.Parse("#777777"), RgbColor.Parse("#777777")), 3);
        Assert.Equal("#FFF4806B", RgbColor.Parse("F4806B").ToHex());
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(RepoPaths.App, "*.xaml", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [GeneratedRegex(@"DynamicResource\s+([A-Za-z0-9_.]+)\}")]
    private static partial Regex DynamicResource();

    [GeneratedRegex(@"StaticResource\s+([A-Za-z0-9_.]+)\}")]
    private static partial Regex StaticResource();

    [GeneratedRegex(@"x:Key=""([^""]+)""")]
    private static partial Regex XKey();

    [GeneratedRegex(@"(?<![\w.])Icon\.[A-Z][A-Za-z]+\b")]
    private static partial Regex IconKey();
}
