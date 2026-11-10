using PdfSharp.Fonts;

namespace AstraFinance.Infrastructure.Export;

/// <summary>
/// Supplies Segoe UI (the app's font) to PDFsharp from the Windows Fonts folder, falling back to Arial.
/// Fonts are embedded (subset) in the PDF, so any character Segoe UI supports is shown correctly.
/// </summary>
internal sealed class WindowsFontResolver : IFontResolver
{
    public const string Family = "Segoe UI";

    private static readonly string FontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    // face name → candidate files (first existing one wins)
    private static readonly Dictionary<string, string[]> Faces = new(StringComparer.Ordinal)
    {
        ["SegoeUI-Regular"] = ["segoeui.ttf", "arial.ttf"],
        ["SegoeUI-Bold"] = ["segoeuib.ttf", "arialbd.ttf"],
        ["SegoeUI-Italic"] = ["segoeuii.ttf", "ariali.ttf"],
        ["SegoeUI-BoldItalic"] = ["segoeuiz.ttf", "arialbi.ttf"],
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        // Every family used by the exporter maps to Segoe UI.
        var face = (bold, italic) switch
        {
            (true, true) => "SegoeUI-BoldItalic",
            (true, false) => "SegoeUI-Bold",
            (false, true) => "SegoeUI-Italic",
            _ => "SegoeUI-Regular",
        };
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName)
    {
        if (!Faces.TryGetValue(faceName, out var files))
        {
            return null;
        }

        foreach (var file in files)
        {
            var path = Path.Combine(FontsFolder, file);
            if (File.Exists(path))
            {
                return File.ReadAllBytes(path);
            }
        }

        return null;
    }
}
