using System.Globalization;
using System.Text;

namespace Detangle.Core.Linking;

/// <summary>
/// Reimplements github-slugger, which is what GitHub, Obsidian and every static site
/// generator in plan.md section 3.1 use to turn a heading into an anchor. The detail
/// that bites (section 3.2) is that it <em>deletes</em> punctuation rather than
/// replacing it: "Don't?" becomes "dont", not "don-t". Duplicate headings get
/// "-1"/"-2" counters in document order, which is why this type is stateful.
/// </summary>
public sealed class HeadingSlugger
{
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    /// <summary>Slugs a heading, appending a dedup counter if it has been seen before.</summary>
    public string Slug(string headingText)
    {
        string slug = SlugCore(headingText);

        if (_seen.TryGetValue(slug, out int count))
        {
            _seen[slug] = count + 1;
            return $"{slug}-{count}";
        }

        _seen[slug] = 1;
        return slug;
    }

    /// <summary>Forgets previously issued slugs; call once per document.</summary>
    public void Reset() => _seen.Clear();

    /// <summary>
    /// The stateless half: lowercase, delete punctuation and symbols, then map each
    /// remaining space to a hyphen. Runs are not collapsed — github-slugger replaces
    /// spaces one for one, so "A &amp; B" becomes "a--b" once the ampersand is deleted, and
    /// collapsing here would miss every anchor GitHub actually emits. Hyphen and
    /// underscore survive; everything else punctuation or symbol does not.
    /// </summary>
    public static string SlugCore(string headingText)
    {
        if (string.IsNullOrWhiteSpace(headingText))
        {
            return string.Empty;
        }

        string text = headingText.Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant();
        var builder = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                builder.Append('-');
                continue;
            }

            if (c is not '-' and not '_' && IsRemovable(c))
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static bool IsRemovable(char c)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);

        return category is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation
            or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol;
    }
}
