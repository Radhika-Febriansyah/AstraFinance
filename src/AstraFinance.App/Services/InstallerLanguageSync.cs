using System.IO;
using System.Security;
using AstraFinance.Core.Enums;
using Microsoft.Win32;

namespace AstraFinance.App.Services;

/// <summary>
/// Keeps the language that Setup remembers for this installation ("Inno Setup: Language" in Astra Finance's own
/// per-user uninstall entry) in step with the language chosen in the app. The next upgrade Setup — and the
/// uninstaller it installs — then speak the user's language. Nothing happens when the app was not installed with
/// Setup (no such entry), and failures are ignored: this only affects the installer's language.
/// </summary>
public static class InstallerLanguageSync
{
    /// <summary>Must match AppId in installer\AstraFinance.iss (Inno appends "_is1").</summary>
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6E1B2C5A-7F43-4D8E-9A21-3C5F0B8D7E14}_is1";

    internal const string LanguageValue = "Inno Setup: Language";

    /// <summary>The [Languages] name used by the installer for an app language.</summary>
    internal static string InstallerLanguageName(AppLanguage language) =>
        language == AppLanguage.Indonesian ? "indonesian" : "english";

    public static void Apply(AppLanguage language)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: true);
            if (key is null)
            {
                return; // not installed with Setup (development build or portable copy)
            }

            var name = InstallerLanguageName(language);
            if (!string.Equals(key.GetValue(LanguageValue) as string, name, StringComparison.Ordinal))
            {
                key.SetValue(LanguageValue, name, RegistryValueKind.String);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            // Cosmetic for the installer only.
        }
    }
}
