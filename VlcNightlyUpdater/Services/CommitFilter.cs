using System.Text.RegularExpressions;

namespace VlcNightlyUpdater.Services;

/// <summary>
/// The Changes tab's filter: free-text search plus "hide merges and translations". Pure logic, no UI.
/// "Noise" is a best-effort guess from the commit title and author, so it can miss one or hide an odd one;
/// it is off by default and never changes the "commits behind" count. Adjust the patterns here.
/// </summary>
public static class CommitFilter
{
    private static readonly Regex TranslationTitle = new(
        @"^(update|add|fix)\b.*\btranslations?\b|^(po|l10n|i18n)\b\s*[:/\-]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] TranslationAuthors = ["weblate", "transifex", "translation"];

    public static bool IsMerge(CommitInfo c) => c.Title.StartsWith("Merge ", StringComparison.OrdinalIgnoreCase);

    public static bool IsTranslation(CommitInfo c) =>
        TranslationTitle.IsMatch(c.Title) ||
        TranslationAuthors.Any(a => c.Author.Contains(a, StringComparison.OrdinalIgnoreCase));

    public static bool IsNoise(CommitInfo c) => IsMerge(c) || IsTranslation(c);

    /// <summary>Every word typed must appear somewhere in the title, author or short id (any case).</summary>
    public static bool Matches(CommitInfo c, string? text, bool hideNoise)
    {
        if (hideNoise && IsNoise(c)) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var haystack = $"{c.Title} {c.Author} {c.ShortId}";
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsActive(string? text, bool hideNoise) => hideNoise || !string.IsNullOrWhiteSpace(text);
}
