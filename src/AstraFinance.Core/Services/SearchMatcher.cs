using System.Globalization;
using System.Text;

namespace AstraFinance.Core.Services;

/// <summary>
/// Case- and diacritic-insensitive text matching used by search providers.
/// Returns a score where higher is better and 0 means no match.
/// </summary>
public static class SearchMatcher
{
    public const int ExactScore = 100;
    public const int PrefixScore = 80;
    public const int WordPrefixScore = 60;
    public const int ContainsScore = 40;

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Scores <paramref name="candidate"/> against an already-normalized query.</summary>
    public static int Score(string normalizedQuery, string? candidate)
    {
        ArgumentNullException.ThrowIfNull(normalizedQuery);
        if (normalizedQuery.Length == 0)
        {
            return 0;
        }

        var text = Normalize(candidate);
        if (text.Length == 0)
        {
            return 0;
        }

        if (text == normalizedQuery)
        {
            return ExactScore;
        }

        if (text.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            return PrefixScore;
        }

        var index = text.IndexOf(normalizedQuery, StringComparison.Ordinal);
        if (index < 0)
        {
            return 0;
        }

        return char.IsLetterOrDigit(text[index - 1]) ? ContainsScore : WordPrefixScore;
    }
}
