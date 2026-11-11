using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AstraFinance.App.Interop;

/// <summary>
/// Tints the native Windows title bar to match the active Astra theme. Keeping the native
/// caption preserves Snap Layouts, accessibility and system behaviors. Attributes that the
/// running Windows version does not support are ignored by DWM.
/// </summary>
internal static partial class DwmTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwcpRound = 2;

    public static void Apply(Window window, bool isDark, Color caption, Color text, Color border)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        SetInt(handle, DwmwaUseImmersiveDarkMode, isDark ? 1 : 0);
        SetInt(handle, DwmwaWindowCornerPreference, DwmwcpRound);
        SetInt(handle, DwmwaCaptionColor, ToColorRef(caption));
        SetInt(handle, DwmwaTextColor, ToColorRef(text));
        SetInt(handle, DwmwaBorderColor, ToColorRef(border));
    }

    /// <summary>Rounded corners for a borderless window (Windows 11; ignored elsewhere).</summary>
    public static void RoundCorners(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetInt(handle, DwmwaWindowCornerPreference, DwmwcpRound);
        }
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static void SetInt(IntPtr hwnd, int attribute, int value)
    {
        // Failure (e.g. unsupported attribute on older Windows builds) is non-fatal.
        _ = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
