using System.Text.Json;
using System.Text.RegularExpressions;
using Detangle.Core.Linking;
using Detangle.Core.Parsing;

namespace Detangle.Core.Vault;

/// <summary>Where a navigation tree came from.</summary>
public enum NavigationSource
{
    /// <summary>The directory tree itself.</summary>
    FileSystem,

    /// <summary>The "nav:" key of mkdocs.yml.</summary>
    MkDocsNav,

    /// <summary>SUMMARY.md, as used by GitBook and mdBook.</summary>
    Summary,

    /// <summary>_sidebar.md, as used by docsify.</summary>
    Sidebar,

    /// <summary>The pages tree of .devin/wiki.json.</summary>
    DeepWikiPages,

    /// <summary>The vault's index.md, as used by an LLM wiki.</summary>
    IndexPage,
}

/// <summary>One entry in the navigation tree.</summary>
/// <param name="Title">What the reader sees.</param>
/// <param name="Document">The document it opens, or null for a grouping node.</param>
/// <param name="Children">Nested entries.</param>
public sealed record NavigationNode(
    string Title,
    VaultDocument? Document,
    IReadOnlyList<NavigationNode> Children)
{
    /// <summary>True when this node only groups others.</summary>
    public bool IsGroup => Document is null;
}

/// <summary>
/// Builds the left rail's tree.
/// <para>
/// A wiki almost always states its own order somewhere — an mkdocs nav, a SUMMARY.md, a
/// sidebar, an index page — and that stated order is far more useful than alphabetical
/// filenames (plan.md section 6.1). Detangle reads whichever one the flavor implies and
/// falls back to the filesystem only when there is nothing to read. Documents the nav
/// source omits are appended under "Not in navigation" rather than hidden, because a
/// file the author forgot to list is exactly the kind of thing this app should surface.
/// </para>
/// </summary>
public static partial class NavigationTreeBuilder
{
    /// <summary>The tree, and where it came from.</summary>
    /// <param name="Source">Which navigation source produced it.</param>
    /// <param name="Roots">Top-level entries.</param>
    public sealed record Result(NavigationSource Source, IReadOnlyList<NavigationNode> Roots);

    /// <summary>Builds a navigation tree for a vault.</summary>
    /// <param name="vault">The scanned vault.</param>
    /// <param name="contentReader">Reads a document's text; nav sources are files too.</param>
    public static Result Build(VaultSnapshot vault, Func<VaultDocument, string?> contentReader)
    {
        Result? stated = vault.Profile.Flavor switch
        {
            VaultFlavor.MkDocs => FromMkDocs(vault),
            VaultFlavor.GitBook or VaultFlavor.MdBook => FromSummary(vault, contentReader),
            VaultFlavor.Docsify => FromSidebar(vault, contentReader),
            VaultFlavor.DeepWiki => FromDeepWiki(vault),
            VaultFlavor.LlmWiki => FromIndexPage(vault, contentReader),
            _ => null,
        };

        if (stated is { Roots.Count: > 0 })
        {
            return stated with { Roots = [.. stated.Roots, .. Remainder(vault, stated.Roots)] };
        }

        return new Result(NavigationSource.FileSystem, FromFileSystem(vault));
    }

    /// <summary>Groups documents by directory, folders first, then names.</summary>
    public static IReadOnlyList<NavigationNode> FromFileSystem(VaultSnapshot vault)
    {
        var byDirectory = new Dictionary<string, List<VaultDocument>>(StringComparer.OrdinalIgnoreCase);

        foreach (VaultDocument document in vault.Documents.Where(d => d.IsMarkdown))
        {
            if (!byDirectory.TryGetValue(document.DirectoryPath, out List<VaultDocument>? documents))
            {
                documents = [];
                byDirectory[document.DirectoryPath] = documents;
            }

            documents.Add(document);
        }

        return BuildDirectory(string.Empty, byDirectory);
    }

    private static IReadOnlyList<NavigationNode> BuildDirectory(
        string directory, Dictionary<string, List<VaultDocument>> byDirectory)
    {
        var children = new List<NavigationNode>();

        IEnumerable<string> subdirectories = byDirectory.Keys
            .Where(d => d.Length > directory.Length
                && d.StartsWith(directory.Length == 0 ? string.Empty : directory + "/", StringComparison.OrdinalIgnoreCase)
                && !d[(directory.Length == 0 ? 0 : directory.Length + 1)..].Contains('/', StringComparison.Ordinal))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);

        foreach (string subdirectory in subdirectories)
        {
            string name = subdirectory[(subdirectory.LastIndexOf('/') + 1)..];

            children.Add(new NavigationNode(name, null, BuildDirectory(subdirectory, byDirectory)));
        }

        if (byDirectory.TryGetValue(directory, out List<VaultDocument>? documents))
        {
            children.AddRange(documents
                .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(d => new NavigationNode(d.DisplayName, d, [])));
        }

        return children;
    }

    /// <summary>
    /// Reads the "nav:" key of mkdocs.yml. The file is YAML but the nav is a list of
    /// single-key maps, so it is read line by line off its indentation rather than
    /// through a schema that would reject the many shapes real config files take.
    /// </summary>
    private static Result? FromMkDocs(VaultSnapshot vault)
    {
        string configPath = Path.Combine(vault.RootPath, "mkdocs.yml");

        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(vault.RootPath, "mkdocs.yaml");
        }

        string[] lines;

        try
        {
            lines = File.ReadAllLines(configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        int start = Array.FindIndex(lines, l => l.TrimEnd().StartsWith("nav:", StringComparison.OrdinalIgnoreCase));

        if (start < 0)
        {
            return null;
        }

        var stack = new List<(int Indent, List<NavigationNode> Children)>
        {
            (-1, []),
        };

        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.Trim().Length == 0)
            {
                continue;
            }

            int indent = line.Length - line.TrimStart().Length;
            string trimmed = line.Trim();

            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (indent == 0)
                {
                    break;
                }

                continue;
            }

            string entry = trimmed[2..].Trim();
            string title;
            string? target = null;

            int colon = entry.IndexOf(':', StringComparison.Ordinal);

            if (colon >= 0)
            {
                title = entry[..colon].Trim().Trim('"', '\'');
                string value = entry[(colon + 1)..].Trim().Trim('"', '\'');
                target = value.Length > 0 ? value : null;
            }
            else
            {
                title = entry.Trim('"', '\'');
                target = title;
            }

            while (stack.Count > 1 && stack[^1].Indent >= indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            VaultDocument? document = target is null ? null : FindByPath(vault, target);
            var children = new List<NavigationNode>();

            stack[^1].Children.Add(new NavigationNode(title, document, children));
            stack.Add((indent, children));
        }

        return new Result(NavigationSource.MkDocsNav, stack[0].Children);
    }

    /// <summary>Reads SUMMARY.md, whose nesting is carried by list indentation.</summary>
    private static Result? FromSummary(VaultSnapshot vault, Func<VaultDocument, string?> contentReader)
    {
        VaultDocument? summary = vault.Index.ByRelativePath("SUMMARY.md").FirstOrDefault()
            ?? vault.Index.ByRelativePath("src/SUMMARY.md").FirstOrDefault();

        return summary is null
            ? null
            : new Result(NavigationSource.Summary, ReadMarkdownList(vault, summary, contentReader));
    }

    /// <summary>Reads docsify's _sidebar.md.</summary>
    private static Result? FromSidebar(VaultSnapshot vault, Func<VaultDocument, string?> contentReader)
    {
        VaultDocument? sidebar = vault.Index.ByRelativePath("_sidebar.md").FirstOrDefault();

        return sidebar is null
            ? null
            : new Result(NavigationSource.Sidebar, ReadMarkdownList(vault, sidebar, contentReader));
    }

    /// <summary>Reads the pages array of a DeepWiki export.</summary>
    private static Result? FromDeepWiki(VaultSnapshot vault)
    {
        string path = Path.Combine(vault.RootPath, ".devin", "wiki.json");

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));

            if (!json.RootElement.TryGetProperty("pages", out JsonElement pages)
                || pages.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var roots = new List<NavigationNode>();

            foreach (JsonElement page in pages.EnumerateArray())
            {
                string? title = page.ValueKind == JsonValueKind.String
                    ? page.GetString()
                    : page.TryGetProperty("title", out JsonElement titleElement) ? titleElement.GetString() : null;

                if (title is not { Length: > 0 })
                {
                    continue;
                }

                roots.Add(new NavigationNode(title, FindByTitle(vault, title), []));
            }

            return new Result(NavigationSource.DeepWikiPages, roots);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads an LLM wiki's index.md, whose links are its table of contents.</summary>
    private static Result? FromIndexPage(VaultSnapshot vault, Func<VaultDocument, string?> contentReader)
    {
        VaultDocument? index = vault.Index.ByRelativePath("wiki/index.md").FirstOrDefault()
            ?? vault.Index.ByRelativePath("index.md").FirstOrDefault();

        return index is null
            ? null
            : new Result(NavigationSource.IndexPage, ReadMarkdownList(vault, index, contentReader));
    }

    /// <summary>
    /// Reads a nested markdown list of links into nav nodes. Both SUMMARY.md and a
    /// sidebar are exactly that, and so is a generated index page.
    /// </summary>
    private static IReadOnlyList<NavigationNode> ReadMarkdownList(
        VaultSnapshot vault, VaultDocument source, Func<VaultDocument, string?> contentReader)
    {
        string? content = contentReader(source);

        if (content is null)
        {
            return [];
        }

        LinkResolver resolver = vault.CreateResolver();
        var stack = new List<(int Indent, List<NavigationNode> Children)> { (-1, []) };

        foreach (string rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            Match item = ListItemPattern().Match(rawLine);

            if (!item.Success)
            {
                continue;
            }

            int indent = item.Groups["indent"].Value.Replace("\t", "    ", StringComparison.Ordinal).Length;
            string text = item.Groups["text"].Value.Trim();

            (string title, string? target) = ReadEntry(text);

            VaultDocument? document = null;

            if (target is { Length: > 0 })
            {
                LinkReference? reference = LinkFactory.FromMarkdownLink(
                    source.RelativePath, target, title, isImage: false, line: 1, column: 0);

                document = reference is null ? null : resolver.Resolve(reference).Target;
            }

            while (stack.Count > 1 && stack[^1].Indent >= indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var children = new List<NavigationNode>();
            stack[^1].Children.Add(new NavigationNode(title, document, children));
            stack.Add((indent, children));
        }

        return stack[0].Children;
    }

    /// <summary>Pulls the title and target out of a nav list item.</summary>
    private static (string Title, string? Target) ReadEntry(string text)
    {
        Match markdown = MarkdownLinkPattern().Match(text);

        if (markdown.Success)
        {
            return (markdown.Groups["label"].Value.Trim(), markdown.Groups["url"].Value.Trim());
        }

        Match wiki = WikiLinkPattern().Match(text);

        if (wiki.Success)
        {
            string body = wiki.Groups["body"].Value;
            int pipe = body.IndexOf('|', StringComparison.Ordinal);

            return pipe >= 0 ? (body[(pipe + 1)..].Trim(), body[..pipe].Trim()) : (body.Trim(), body.Trim());
        }

        return (text, null);
    }

    /// <summary>Documents the stated navigation left out, so nothing is silently hidden.</summary>
    private static IReadOnlyList<NavigationNode> Remainder(
        VaultSnapshot vault, IReadOnlyList<NavigationNode> roots)
    {
        var listed = new HashSet<string>(StringComparer.Ordinal);

        void Collect(IReadOnlyList<NavigationNode> nodes)
        {
            foreach (NavigationNode node in nodes)
            {
                if (node.Document is { } document)
                {
                    listed.Add(document.RelativePath);
                }

                Collect(node.Children);
            }
        }

        Collect(roots);

        List<NavigationNode> missing =
        [
            .. vault.Documents
                .Where(d => d.IsMarkdown && !listed.Contains(d.RelativePath))
                .OrderBy(d => d.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(d => new NavigationNode(d.DisplayName, d, [])),
        ];

        return missing.Count == 0 ? [] : [new NavigationNode("Not in navigation", null, missing)];
    }

    private static VaultDocument? FindByPath(VaultSnapshot vault, string target)
    {
        string cleaned = target.Split('#')[0].Trim();

        // MkDocs paths are relative to the docs directory, which is not the vault root.
        return vault.Index.ByRelativePath(cleaned).FirstOrDefault()
            ?? vault.Index.ByRelativePath($"docs/{cleaned}").FirstOrDefault()
            ?? vault.Index.ByNormalizedPath(LinkNormalizer.NormalizePath(cleaned)).FirstOrDefault()
            ?? vault.Index.ByNormalizedPath(LinkNormalizer.NormalizePath($"docs/{cleaned}")).FirstOrDefault();
    }

    private static VaultDocument? FindByTitle(VaultSnapshot vault, string title) =>
        vault.Index.ByAlias(LinkNormalizer.Normalize(title)).FirstOrDefault()
        ?? vault.Index.ByNormalizedStem(LinkNormalizer.Normalize(title)).FirstOrDefault();

    [GeneratedRegex(@"^(?<indent>[ \t]*)[-*+]\s+(?<text>.+)$")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"\[(?<label>[^\]]*)\]\((?<url>[^)]*)\)")]
    private static partial Regex MarkdownLinkPattern();

    [GeneratedRegex(@"\[\[(?<body>[^\]]+)\]\]")]
    private static partial Regex WikiLinkPattern();
}
