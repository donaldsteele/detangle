using System.Text.RegularExpressions;

namespace Detangle.Core.Vault;

/// <summary>
/// Sniffs which of the thirteen formats a directory holds (plan.md section 5.7).
/// Detection is marker-file first because those are unambiguous; the two shape-based
/// flavors (Zettelkasten and Dendron) are tested last and only when no marker file
/// claimed the vault, since their evidence is statistical rather than definite.
/// </summary>
public static partial class FlavorDetector
{
    /// <summary>Marker paths that identify a flavor outright, in priority order.</summary>
    private static readonly (string Marker, VaultFlavor Flavor)[] Markers =
    [
        (".obsidian", VaultFlavor.Obsidian),
        ("logseq/config.edn", VaultFlavor.Logseq),
        ("dendron.yml", VaultFlavor.Dendron),
        (".devin/wiki.json", VaultFlavor.DeepWiki),
        ("quartz.config.ts", VaultFlavor.Quartz),
        ("mkdocs.yml", VaultFlavor.MkDocs),
        ("mkdocs.yaml", VaultFlavor.MkDocs),
        ("_sidebar.md", VaultFlavor.Docsify),
        ("SUMMARY.md", VaultFlavor.GitBook),
    ];

    /// <summary>
    /// Detects a flavor from vault-relative paths ("/" separated, original case). Taking
    /// paths rather than a directory keeps detection testable without a filesystem and
    /// lets the scanner reuse the listing it already has.
    /// </summary>
    public static VaultFlavor Detect(IEnumerable<string> relativePaths)
    {
        var paths = new List<string>();
        var lookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in relativePaths)
        {
            string normalized = path.Replace('\\', '/').TrimStart('/');
            paths.Add(normalized);
            lookup.Add(normalized);

            // Marker directories are recorded by name so a listing of files alone still
            // reveals ".obsidian/app.json" as a marker.
            int slash = normalized.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                lookup.Add(normalized[..slash]);
            }
        }

        // The LLM Wiki marker is a pair, and it outranks the single-file markers because
        // such a vault often also carries a SUMMARY.md or an .obsidian directory.
        if (lookup.Contains("wiki/SCHEMA.md") && paths.Any(p => p.StartsWith("raw/", StringComparison.OrdinalIgnoreCase)))
        {
            return VaultFlavor.LlmWiki;
        }

        if (paths.Any(p => p.StartsWith("docusaurus.config.", StringComparison.OrdinalIgnoreCase)))
        {
            return VaultFlavor.Docusaurus;
        }

        // mdBook needs both halves; book.toml alone could be any Rust-adjacent project.
        if (lookup.Contains("book.toml") && lookup.Contains("src/SUMMARY.md"))
        {
            return VaultFlavor.MdBook;
        }

        if (lookup.Contains(".vscode/foam.json") || lookup.Contains(".foam"))
        {
            return VaultFlavor.Foam;
        }

        foreach ((string marker, VaultFlavor flavor) in Markers)
        {
            if (lookup.Contains(marker))
            {
                return flavor;
            }
        }

        List<string> markdown =
        [
            .. paths.Where(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)),
        ];

        if (markdown.Count == 0)
        {
            return VaultFlavor.Generic;
        }

        List<string> flat = [.. markdown.Where(p => !p.Contains('/', StringComparison.Ordinal))];

        // Both shape-based flavors are flat directories, so a nested tree rules them out.
        if (flat.Count < markdown.Count * 0.9)
        {
            return VaultFlavor.Generic;
        }

        int zettel = flat.Count(p => ZettelPattern().IsMatch(p));
        int dotted = flat.Count(p => DotHierarchyPattern().IsMatch(p));

        if (zettel >= 3 && zettel >= flat.Count / 2)
        {
            return VaultFlavor.Zettelkasten;
        }

        if (dotted >= 3 && dotted >= flat.Count / 2)
        {
            return VaultFlavor.Dendron;
        }

        return VaultFlavor.Generic;
    }

    /// <summary>Detects a flavor and returns its profile.</summary>
    public static VaultProfile DetectProfile(IEnumerable<string> relativePaths) =>
        VaultProfile.For(Detect(relativePaths));

    /// <summary>A timestamp or Folgezettel identifier prefix: "202604201530-note.md".</summary>
    [GeneratedRegex(@"^\d{8,14}([-_ ].*)?\.md$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ZettelPattern();

    /// <summary>A Dendron dot hierarchy: the stem itself contains a dot.</summary>
    [GeneratedRegex(@"^[^./]+(\.[^./]+)+\.md$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DotHierarchyPattern();
}
