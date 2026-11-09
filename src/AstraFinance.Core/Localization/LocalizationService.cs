using System.Globalization;
using System.Resources;
using AstraFinance.Core.Enums;
using AstraFinance.Core.Interfaces;

namespace AstraFinance.Core.Localization;

/// <summary>
/// .resx-backed localization. English is the neutral (fallback) resource set and Indonesian
/// is a satellite resource set, so a key missing from Indonesian falls back to English.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    internal static readonly ResourceManager Resources =
        new("AstraFinance.Core.Resources.Strings", typeof(LocalizationService).Assembly);

    public LocalizationService(AppLanguage initialLanguage = AppLanguage.Indonesian)
    {
        CurrentLanguage = initialLanguage;
        Culture = LanguageCultures.For(initialLanguage);
    }

    public AppLanguage CurrentLanguage { get; private set; }

    public CultureInfo Culture { get; private set; }

    public event EventHandler? LanguageChanged;

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return Resources.GetString(key, Culture) ?? $"[{key}]";
    }

    public string Format(string key, params object?[] args) =>
        string.Format(Culture, GetString(key), args);

    public void SetLanguage(AppLanguage language)
    {
        if (language == CurrentLanguage)
        {
            return;
        }

        CurrentLanguage = language;
        Culture = LanguageCultures.For(language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
