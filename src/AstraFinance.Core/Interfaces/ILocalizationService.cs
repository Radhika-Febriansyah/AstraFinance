using System.Globalization;
using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Interfaces;

/// <summary>Provides localized UI strings and supports switching language at runtime.</summary>
public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; }

    /// <summary>Culture used for UI text of the current language (en-US or id-ID).</summary>
    CultureInfo Culture { get; }

    /// <summary>Raised after the language has changed and all strings should be refreshed.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>Returns the localized string, or a visible "[key]" marker when the key does not exist.</summary>
    string this[string key] { get; }

    string GetString(string key);

    string Format(string key, params object?[] args);

    void SetLanguage(AppLanguage language);
}
