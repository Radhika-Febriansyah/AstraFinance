using System.Globalization;
using AstraFinance.Core.Enums;

namespace AstraFinance.Core.Localization;

public static class LanguageCultures
{
    public static CultureInfo For(AppLanguage language) => language switch
    {
        AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
        AppLanguage.Indonesian => CultureInfo.GetCultureInfo("id-ID"),
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    /// <summary>Picks a sensible default language from a Windows UI culture.</summary>
    public static AppLanguage FromCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName.Equals("id", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Indonesian
            : AppLanguage.English;
    }
}
