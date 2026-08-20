using System.Globalization;
using System.Text.RegularExpressions;

namespace Detangle.Core.Search;

/// <summary>How a field filter compares.</summary>
public enum FieldComparison
{
    /// <summary>Equal, ignoring case.</summary>
    Equals,

    /// <summary>Starts with, for path prefixes and nested tags.</summary>
    StartsWith,

    /// <summary>Strictly after, for dates.</summary>
    After,

    /// <summary>Strictly before, for dates.</summary>
    Before,
}

/// <summary>One "field:value" term of a query.</summary>
/// <param name="Field">The field name, lowercased.</param>
/// <param name="Value">The value as written.</param>
/// <param name="Comparison">How it compares.</param>
public sealed record FieldFilter(string Field, string Value, FieldComparison Comparison);

/// <summary>
/// A parsed search query (plan.md section 6.2).
/// <para>
/// The syntax is the one people already type in issue trackers: bare words are full-text
/// terms, "quoted phrases" are exact, and "field:value" narrows — with "updated&gt;date"
/// and "updated&lt;date" for ranges. Anything the parser does not recognise as a field
/// stays a search term rather than becoming an error, because a query bar that rejects
/// input is worse than one that searches for it.
/// </para>
/// </summary>
public sealed partial record SearchQuery
{
    /// <summary>Fields that narrow rather than search.</summary>
    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "type", "tag", "tags", "path", "title", "status", "author", "id", "updated", "created",
    };

    /// <summary>Free-text terms, in order.</summary>
    public IReadOnlyList<string> Terms { get; init; } = [];

    /// <summary>Quoted phrases, which must match exactly.</summary>
    public IReadOnlyList<string> Phrases { get; init; } = [];

    /// <summary>Field filters.</summary>
    public IReadOnlyList<FieldFilter> Filters { get; init; } = [];

    /// <summary>True when the query asks for nothing.</summary>
    public bool IsEmpty => Terms.Count == 0 && Phrases.Count == 0 && Filters.Count == 0;

    /// <summary>Parses a query string.</summary>
    public static SearchQuery Parse(string query)
    {
        var terms = new List<string>();
        var phrases = new List<string>();
        var filters = new List<FieldFilter>();

        foreach (Match token in TokenPattern().Matches(query ?? string.Empty))
        {
            if (token.Groups["phrase"].Success)
            {
                phrases.Add(token.Groups["phrase"].Value);
                continue;
            }

            string word = token.Groups["word"].Value;

            if (word.Length == 0)
            {
                continue;
            }

            Match field = FieldPattern().Match(word);

            if (field.Success && KnownFields.Contains(field.Groups["field"].Value))
            {
                filters.Add(new FieldFilter(
                    field.Groups["field"].Value.ToLowerInvariant(),
                    field.Groups["value"].Value.Trim('"', '\''),
                    field.Groups["op"].Value switch
                    {
                        ">" => FieldComparison.After,
                        "<" => FieldComparison.Before,
                        _ => field.Groups["field"].Value.Equals("path", StringComparison.OrdinalIgnoreCase)
                            || field.Groups["field"].Value.StartsWith("tag", StringComparison.OrdinalIgnoreCase)
                                ? FieldComparison.StartsWith
                                : FieldComparison.Equals,
                    }));

                continue;
            }

            terms.Add(word);
        }

        return new SearchQuery { Terms = terms, Phrases = phrases, Filters = filters };
    }

    /// <summary>
    /// The FTS5 MATCH expression for the text half of the query, or null when the query
    /// only narrows by field. Terms get a prefix "*" so results appear while typing.
    /// </summary>
    public string? ToMatchExpression()
    {
        var parts = new List<string>();

        foreach (string phrase in Phrases)
        {
            parts.Add($"\"{phrase.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
        }

        foreach (string term in Terms)
        {
            string cleaned = new([.. term.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-')]);

            if (cleaned.Length > 0)
            {
                parts.Add($"{cleaned}*");
            }
        }

        return parts.Count == 0 ? null : string.Join(" AND ", parts);
    }

    /// <summary>Parses a filter value as a date, accepting "2026-06-01" and "2026".</summary>
    public static DateTimeOffset? ParseDate(string value)
    {
        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed))
        {
            return parsed;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year)
            && year is >= 1000 and <= 9999
                ? new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero)
                : null;
    }

    [GeneratedRegex(@"""(?<phrase>[^""]*)""|(?<word>\S+)")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^(?<field>[A-Za-z_]+)(?<op>[:><])(?<value>.*)$")]
    private static partial Regex FieldPattern();
}
