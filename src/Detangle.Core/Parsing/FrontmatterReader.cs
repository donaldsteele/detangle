using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Detangle.Core.Parsing;

/// <summary>
/// Reads the frontmatter block at the top of a document and folds the key union from
/// plan.md section 3.3 into <see cref="DocumentFrontmatter"/>.
/// <para>
/// Three tolerances are deliberate, because generated wikis trip all three: a leading
/// UTF-8 BOM, blank lines before the opening fence, and an unterminated block. None of
/// them is an error — a document with unreadable frontmatter still renders, it just
/// carries a diagnostic.
/// </para>
/// </summary>
public static class FrontmatterReader
{
    private static readonly string[] AliasKeys = ["aliases", "alias", "aka"];
    private static readonly string[] TagKeys = ["tags", "tag", "keywords", "categories"];
    private static readonly string[] IdKeys = ["id", "uid", "zettel-id", "zettelid", "permalink"];
    private static readonly string[] TypeKeys = ["type", "kind"];
    private static readonly string[] StatusKeys = ["status", "state"];
    private static readonly string[] CreatedKeys = ["created", "date", "datecreated", "created_at"];
    private static readonly string[] UpdatedKeys = ["updated", "modified", "last_modified_at", "updated_at"];
    private static readonly string[] AuthorKeys = ["authors", "author"];
    private static readonly string[] OrderKeys = ["sidebar_position", "order", "weight", "nav_order"];

    /// <summary>
    /// Keys whose values are links even though they carry no brackets. Missing these is
    /// the most common way a viewer under-reports an LLM wiki's graph.
    /// </summary>
    private static readonly string[] ReferenceKeys =
        ["sources", "related", "links", "see-also", "see_also", "seealso", "refs", "parent", "up"];

    /// <summary>Parses the leading frontmatter block, if any.</summary>
    /// <param name="content">The full document text.</param>
    public static DocumentFrontmatter Read(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return DocumentFrontmatter.Empty;
        }

        string text = content.TrimStart('﻿');
        string[] lines = text.Split('\n');

        int start = 0;
        while (start < lines.Length && lines[start].Trim().Length == 0)
        {
            start++;
        }

        if (start >= lines.Length)
        {
            return DocumentFrontmatter.Empty;
        }

        string fence = lines[start].TrimEnd('\r').Trim();

        FrontmatterKind kind = fence switch
        {
            "---" => FrontmatterKind.Yaml,
            "+++" => FrontmatterKind.Toml,
            ";;;" => FrontmatterKind.Json,
            _ => FrontmatterKind.None,
        };

        if (kind == FrontmatterKind.None)
        {
            // Logseq and Dataview put "key:: value" properties in the first block with no
            // fence at all, so a missing fence is not the same as missing frontmatter.
            return ReadDoubleColonBlock(lines, start);
        }

        int end = -1;
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r').Trim() == fence)
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            return new DocumentFrontmatter
            {
                Kind = FrontmatterKind.None,
                Diagnostics = [$"Unterminated {kind} frontmatter block: no closing \"{fence}\"."],
            };
        }

        string body = string.Join('\n', lines[(start + 1)..end]);
        int lineCount = end + 1;

        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();

        switch (kind)
        {
            case FrontmatterKind.Yaml:
            case FrontmatterKind.Json:
                // YamlDotNet parses JSON too: JSON is a YAML subset, and the ";;;" form is
                // rare enough that a second parser would be all risk and no benefit.
                ReadYaml(body, values, diagnostics);
                break;
            case FrontmatterKind.Toml:
                ReadToml(body, values, diagnostics);
                break;
        }

        return Build(kind, values, diagnostics, lineCount);
    }

    private static DocumentFrontmatter ReadDoubleColonBlock(string[] lines, int start)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int consumed = start;

        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Trim().Length == 0)
            {
                break;
            }

            int marker = line.IndexOf("::", StringComparison.Ordinal);
            if (marker <= 0)
            {
                break;
            }

            string key = line[..marker].TrimStart('-', ' ', '\t').Trim();
            string value = line[(marker + 2)..].Trim();

            if (key.Length == 0)
            {
                break;
            }

            AddValues(values, key, SplitScalar(value));
            consumed = i + 1;
        }

        return values.Count == 0
            ? DocumentFrontmatter.Empty
            : Build(FrontmatterKind.DoubleColon, values, [], consumed - start);
    }

    private static void ReadYaml(string body, Dictionary<string, List<string>> values, List<string> diagnostics)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(body));

            if (stream.Documents.Count == 0
                || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return;
            }

            foreach (KeyValuePair<YamlNode, YamlNode> entry in root.Children)
            {
                if (entry.Key is not YamlScalarNode { Value: { Length: > 0 } key })
                {
                    continue;
                }

                AddValues(values, key, Flatten(entry.Value, key));
            }
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException)
        {
            diagnostics.Add($"YAML frontmatter could not be parsed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the flat subset of TOML that frontmatter actually uses: scalars, inline
    /// arrays, and dotted keys. Tables are recorded as a diagnostic rather than parsed,
    /// which keeps a full TOML dependency out of Core for a format used by one flavor.
    /// </summary>
    private static void ReadToml(string body, Dictionary<string, List<string>> values, List<string> diagnostics)
    {
        foreach (string rawLine in body.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                diagnostics.Add($"TOML table \"{line}\" in frontmatter was not read; only flat keys are supported.");
                continue;
            }

            int separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim().Trim('"');
            string value = line[(separator + 1)..].Trim();

            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                List<string> items = [.. value[1..^1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(TrimQuotes)
                    .Where(v => v.Length > 0)];
                AddValues(values, key, items);
                continue;
            }

            AddValues(values, key, [TrimQuotes(value)]);
        }
    }

    /// <summary>Flattens a YAML node to strings; nested maps are recorded as "path: value".</summary>
    private static List<string> Flatten(YamlNode node, string key)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                return SplitScalar(scalar.Value ?? string.Empty);

            case YamlSequenceNode sequence:
                return [.. sequence.Children.SelectMany(child => Flatten(child, key))];

            case YamlMappingNode mapping:
                // The LLM Wiki "graph:" block is nested. Keeping a flattened "sub: value"
                // form means the properties card can still show it without a schema.
                var flattened = new List<string>();
                foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
                {
                    string childKey = (entry.Key as YamlScalarNode)?.Value ?? string.Empty;
                    foreach (string value in Flatten(entry.Value, childKey))
                    {
                        flattened.Add(childKey.Length > 0 ? $"{childKey}: {value}" : value);
                    }
                }

                return flattened;

            default:
                return [];
        }
    }

    /// <summary>
    /// Splits a scalar that is carrying a list. Frontmatter writers use CSV where the
    /// schema says sequence often enough that treating "a, b" as one tag is wrong more
    /// often than it is right — but only when the value has no spaces around a comma-free
    /// phrase, so titles keep their commas.
    /// </summary>
    private static List<string> SplitScalar(string value)
    {
        string trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            return [];
        }

        // "[[a]]" is a wikilink the reference keys are allowed to carry, not a
        // single-element inline list; stripping its brackets would corrupt the target.
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']')
            && !trimmed.StartsWith("[[", StringComparison.Ordinal))
        {
            return [.. trimmed[1..^1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(TrimQuotes)
                .Where(v => v.Length > 0)];
        }

        return [trimmed];
    }

    private static void AddValues(Dictionary<string, List<string>> values, string key, List<string> items)
    {
        if (!values.TryGetValue(key, out List<string>? existing))
        {
            existing = [];
            values[key] = existing;
        }

        existing.AddRange(items);
    }

    private static DocumentFrontmatter Build(
        FrontmatterKind kind,
        Dictionary<string, List<string>> values,
        List<string> diagnostics,
        int lineCount)
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "title", "slug", "url", "cssclasses", "draft", "publish", "published" };

        List<string> Collect(string[] keys)
        {
            var result = new List<string>();
            foreach (string key in keys)
            {
                claimed.Add(key);
                if (values.TryGetValue(key, out List<string>? found))
                {
                    result.AddRange(found);
                }
            }

            return result;
        }

        string? First(string[] keys) => Collect(keys).FirstOrDefault(v => v.Length > 0);

        List<string> tags = [.. Collect(TagKeys)
            .SelectMany(tag => tag.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(tag => tag.TrimStart('#'))
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        // draft and publish say the same thing with opposite polarity (section 3.3).
        bool isDraft = ReadBoolean(values, "draft") == true
            || ReadBoolean(values, "publish") == false
            || ReadBoolean(values, "published") == false;

        // Every recognised key has to be read before the leftovers are collected: Collect
        // is what marks a key as claimed, so gathering "extra" first would leave every
        // field below in it as well, and the properties card would list "type" twice.
        List<string> aliases = [.. Collect(AliasKeys).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        string? id = First(IdKeys);
        string? type = First(TypeKeys);
        string? status = First(StatusKeys);
        DateTimeOffset? created = ParseTimestamp(First(CreatedKeys));
        DateTimeOffset? updated = ParseTimestamp(First(UpdatedKeys));
        List<string> references = [.. Collect(ReferenceKeys).Where(v => v.Length > 0)];
        List<string> authors = [.. Collect(AuthorKeys).Where(v => v.Length > 0)];
        double? order = ParseNumber(First(OrderKeys));

        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, List<string>> entry in values)
        {
            if (!claimed.Contains(entry.Key) && entry.Value.Count > 0)
            {
                extra[entry.Key] = string.Join(", ", entry.Value);
            }
        }

        return new DocumentFrontmatter
        {
            Kind = kind,
            Title = values.TryGetValue("title", out List<string>? title) ? title.FirstOrDefault() : null,
            Aliases = aliases,
            Tags = tags,
            Id = id,
            Slug = values.TryGetValue("slug", out List<string>? slug) ? slug.FirstOrDefault() : null,
            Type = type,
            Status = status,
            Created = created,
            Updated = updated,
            References = references,
            Authors = authors,
            Url = values.TryGetValue("url", out List<string>? url) ? url.FirstOrDefault() : null,
            IsDraft = isDraft,
            Order = order,
            CssClasses = values.TryGetValue("cssclasses", out List<string>? css) ? css : [],
            Extra = extra,
            Diagnostics = diagnostics,
            LineCount = lineCount,
        };
    }

    /// <summary>
    /// Accepts ISO 8601, epoch seconds, and epoch milliseconds. Dendron writes epoch
    /// milliseconds, which read as the year 56000 if treated as seconds — the threshold
    /// below is what separates the two.
    /// </summary>
    internal static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string text = TrimQuotes(value.Trim());

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long epoch))
        {
            const long MillisecondThreshold = 100_000_000_000L;

            return epoch >= MillisecondThreshold
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                : DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        if (DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? ParseNumber(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number
            : null;

    private static bool? ReadBoolean(Dictionary<string, List<string>> values, string key)
    {
        if (!values.TryGetValue(key, out List<string>? found) || found.Count == 0)
        {
            return null;
        }

        string text = found[0].Trim();

        return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" ? true
            : text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0" ? false
            : null;
    }

    private static string TrimQuotes(string value)
    {
        string trimmed = value.Trim();

        return trimmed.Length >= 2
            && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\''))
                ? trimmed[1..^1]
                : trimmed;
    }
}
