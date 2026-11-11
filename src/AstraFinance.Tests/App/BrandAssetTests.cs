using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AstraFinance.App.Services;

namespace AstraFinance.Tests.App;

/// <summary>Guards the brand assets generated from assets/branding/source/astra-symbol.svg.</summary>
public partial class BrandAssetTests
{
    private static string MasterSymbolPath()
    {
        var svg = XDocument.Load(Path.Combine(RepoPaths.Root, "assets", "branding", "source", "astra-symbol.svg"));
        XNamespace ns = "http://www.w3.org/2000/svg";
        return (string)svg.Descendants(ns + "path").Single(e => (string?)e.Attribute("id") == "astra-symbol").Attribute("d")!;
    }

    private static double[] Numbers(string pathData) =>
        NumberPattern().Matches(pathData).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();

    private static string Commands(string pathData) => new(pathData.Where(char.IsLetter).ToArray());

    [Fact]
    public void App_brand_symbol_matches_the_vector_master()
    {
        var icons = XDocument.Load(Path.Combine(RepoPaths.App, "Themes", "Icons.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var appPath = icons.Root!.Elements().Single(e => (string?)e.Attribute(x + "Key") == "Brand.Symbol").Value;
        var master = MasterSymbolPath();

        Assert.Equal(Commands(master), Commands(appPath));
        var expected = Numbers(master).Select(v => v * 0.24).ToArray(); // 100-unit grid → 24-unit icon grid
        var actual = Numbers(appPath);
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) < 0.001, $"Coordinate {i}: expected {expected[i]}, got {actual[i]}");
        }
    }

    [Fact]
    public void Application_icon_contains_every_required_size()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoPaths.App, "Resources", "astra.ico"));
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0)); // reserved
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2)); // type: icon
        var count = BitConverter.ToUInt16(bytes, 4);

        var sizes = Enumerable.Range(0, count).Select(i => bytes[6 + 16 * i] is 0 ? 256 : bytes[6 + 16 * i]).ToHashSet();
        foreach (var required in new[] { 16, 32, 48, 64, 128, 256 })
        {
            Assert.Contains(required, sizes);
        }
    }

    [Fact]
    public void Brand_asset_set_is_complete()
    {
        var branding = Path.Combine(RepoPaths.Root, "assets", "branding");
        string[] names =
        [
            "astra-symbol-light", "astra-symbol-dark",
            "astra-logo-horizontal-light", "astra-logo-horizontal-dark",
            "astra-logo-vertical-light", "astra-logo-vertical-dark",
            "astra-app-icon", "astra-installer-banner",
        ];
        foreach (var name in names)
        {
            Assert.True(File.Exists(Path.Combine(branding, "svg", name + ".svg")), $"svg/{name}.svg");
            Assert.True(File.Exists(Path.Combine(branding, "png", name + "-512.png")), $"png/{name}-512.png");
            Assert.True(File.Exists(Path.Combine(branding, "png", name + "-1024.png")), $"png/{name}-1024.png");
        }
    }

    [Fact]
    public void Cached_notification_icon_is_refreshed_only_when_the_app_icon_changes()
    {
        var dir = Directory.CreateTempSubdirectory("astra-icon-");
        try
        {
            var path = Path.Combine(dir.FullName, "notification-icon.ico");
            Assert.True(WindowsToastNotifier.WriteIfChanged(path, [1, 2, 3]));   // missing → written
            Assert.False(WindowsToastNotifier.WriteIfChanged(path, [1, 2, 3]));  // identical → untouched
            Assert.True(WindowsToastNotifier.WriteIfChanged(path, [9, 9]));      // new branding → replaced
            Assert.Equal([9, 9], File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [GeneratedRegex(@"-?\d+(?:\.\d+)?")]
    private static partial Regex NumberPattern();
}
