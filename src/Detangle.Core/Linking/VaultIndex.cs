using Detangle.Core.Vault;

namespace Detangle.Core.Linking;

/// <summary>
/// The lookup tables the resolver walks, built once per scan (plan.md section 5.1).
/// Every table is keyed by a form the chain asks for directly, so each of the thirteen
/// steps is a dictionary hit rather than a scan — that is what keeps resolution of a
/// 5,000-file vault interactive.
/// <para>
/// Candidate lists are pre-sorted by the ambiguity policy from section 5.4 (shortest
/// path, then alphabetical), so "first candidate" is always the deterministic winner
/// and the rest are the disambiguation menu.
/// </para>
/// </summary>
public sealed class VaultIndex
{
    private readonly Dictionary<string, List<VaultDocument>> _byRelativePath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VaultDocument>> _byNormalizedPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VaultDocument>> _byStem = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VaultDocument>> _byStemIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<VaultDocument>> _byNormalizedStem = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VaultDocument>> _byAlias = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VaultDocument>> _byIdentifier = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<VaultDocument>> _byFileName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<VaultDocument>> _byBlockAnchor = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    private VaultIndex(IReadOnlyList<VaultDocument> documents)
    {
        Documents = documents;
    }

    /// <summary>Every scanned file, markdown and attachments alike.</summary>
    public IReadOnlyList<VaultDocument> Documents { get; }

    /// <summary>Normalized directory paths present in the vault, for folder-index resolution.</summary>
    public IReadOnlyCollection<string> Directories => _directories;

    /// <summary>Builds all indexes from a scanned document set.</summary>
    public static VaultIndex Build(IReadOnlyList<VaultDocument> documents)
    {
        var index = new VaultIndex(documents);

        foreach (VaultDocument document in documents)
        {
            index.Add(document);
        }

        index.SortCandidates();

        return index;
    }

    /// <summary>Files whose vault-relative path matches exactly, case-sensitively.</summary>
    public IReadOnlyList<VaultDocument> ByRelativePath(string path) => Lookup(_byRelativePath, path);

    /// <summary>Files whose normalized path matches, absorbing case, separator and encoding drift.</summary>
    public IReadOnlyList<VaultDocument> ByNormalizedPath(string path) => Lookup(_byNormalizedPath, path);

    /// <summary>Files whose stem matches exactly, case-sensitively.</summary>
    public IReadOnlyList<VaultDocument> ByStem(string stem) => Lookup(_byStem, stem);

    /// <summary>Files whose stem matches ignoring case.</summary>
    public IReadOnlyList<VaultDocument> ByStemIgnoreCase(string stem) => Lookup(_byStemIgnoreCase, stem);

    /// <summary>Files whose normalized stem matches.</summary>
    public IReadOnlyList<VaultDocument> ByNormalizedStem(string stem) => Lookup(_byNormalizedStem, stem);

    /// <summary>Files carrying a matching alias, frontmatter title, slug, or first H1.</summary>
    public IReadOnlyList<VaultDocument> ByAlias(string alias) => Lookup(_byAlias, alias);

    /// <summary>Files carrying a matching frontmatter identifier.</summary>
    public IReadOnlyList<VaultDocument> ByIdentifier(string id) => Lookup(_byIdentifier, id);

    /// <summary>
    /// Files carrying a block anchor with this id. Logseq's "((uuid))" addresses a block
    /// that usually lives on another page, so this has to be a vault-wide index rather
    /// than a per-document scan.
    /// </summary>
    public IReadOnlyList<VaultDocument> ByBlockAnchor(string id) => Lookup(_byBlockAnchor, id);

    /// <summary>Files whose filename including extension matches, anywhere in the vault.</summary>
    public IReadOnlyList<VaultDocument> ByFileName(string fileName) => Lookup(_byFileName, fileName);

    /// <summary>
    /// Files whose normalized path ends with the given normalized segments — Foam's
    /// minimum-identifier rule, which is what makes "[[folder/note]]" work without a
    /// full path. Candidates are narrowed by last segment first, so this stays a
    /// dictionary hit plus a short filter rather than a vault scan.
    /// </summary>
    public IReadOnlyList<VaultDocument> ByPathSuffix(string normalizedSuffix)
    {
        string[] segments = normalizedSuffix.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return [];
        }

        IReadOnlyList<VaultDocument> candidates = ByNormalizedStem(segments[^1]);

        if (segments.Length == 1 || candidates.Count == 0)
        {
            return candidates;
        }

        var matches = new List<VaultDocument>();

        foreach (VaultDocument candidate in candidates)
        {
            string[] path = LinkNormalizer.NormalizePath(candidate.RelativePath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (path.Length < segments.Length)
            {
                continue;
            }

            bool matched = true;
            for (int i = 1; i <= segments.Length; i++)
            {
                if (!string.Equals(path[^i], segments[^i], StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                matches.Add(candidate);
            }
        }

        return matches;
    }

    /// <summary>
    /// Files whose stem starts with the given identifier — the Zettelkasten rule, where
    /// "[[202604201530]]" addresses "202604201530-on-attention.md".
    /// </summary>
    public IReadOnlyList<VaultDocument> ByIdentifierPrefix(string prefix)
    {
        if (prefix.Length == 0)
        {
            return [];
        }

        var matches = new List<VaultDocument>();

        foreach (VaultDocument document in Documents)
        {
            if (document.IsMarkdown
                && document.Stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(document);
            }
        }

        return Sort(matches);
    }

    /// <summary>True when the normalized path names a directory in the vault.</summary>
    public bool IsDirectory(string normalizedPath) => _directories.Contains(normalizedPath);

    /// <summary>
    /// Chain step 12: documents whose normalized stem is within an edit distance of two
    /// of the target. These are suggestions for the unresolved-link UI only — the
    /// resolver never navigates on them, because a wrong automatic jump is worse than an
    /// honest placeholder.
    /// </summary>
    /// <param name="normalizedTarget">N() of the target stem.</param>
    /// <param name="limit">Maximum suggestions to return.</param>
    public IReadOnlyList<VaultDocument> NearestByName(string normalizedTarget, int limit = 5)
    {
        if (normalizedTarget.Length < 3)
        {
            // Below three characters an edit distance of two matches almost anything.
            return [];
        }

        var scored = new List<(int Distance, VaultDocument Document)>();

        foreach ((string key, List<VaultDocument> candidates) in _byNormalizedStem)
        {
            if (Math.Abs(key.Length - normalizedTarget.Length) > 2)
            {
                continue;
            }

            int distance = EditDistance(key, normalizedTarget, maxDistance: 2);

            if (distance <= 2)
            {
                scored.AddRange(candidates.Select(candidate => (distance, candidate)));
            }
        }

        return
        [
            .. scored
                .OrderBy(entry => entry.Distance)
                .ThenBy(entry => entry.Document.RelativePath.Count(c => c == '/'))
                .ThenBy(entry => entry.Document.RelativePath, StringComparer.Ordinal)
                .Select(entry => entry.Document)
                .Take(limit),
        ];
    }

    /// <summary>
    /// Levenshtein distance with an early exit: once every cell in a row exceeds the
    /// cap the answer cannot come back down, so the remaining rows are wasted work.
    /// </summary>
    internal static int EditDistance(string left, string right, int maxDistance)
    {
        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];

        for (int j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            int rowMinimum = current[0];

            for (int j = 1; j <= right.Length; j++)
            {
                int cost = left[i - 1] == right[j - 1] ? 0 : 1;

                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                rowMinimum = Math.Min(rowMinimum, current[j]);
            }

            if (rowMinimum > maxDistance)
            {
                return maxDistance + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    /// <summary>
    /// Orders candidates by the section 5.4 ambiguity policy: fewest path segments
    /// first, then shortest path, then ordinal — deterministic across platforms, which
    /// matters because the choice is persisted per vault.
    /// </summary>
    public static List<VaultDocument> Sort(List<VaultDocument> candidates)
    {
        candidates.Sort(static (left, right) =>
        {
            int leftDepth = left.RelativePath.Count(c => c == '/');
            int rightDepth = right.RelativePath.Count(c => c == '/');

            if (leftDepth != rightDepth)
            {
                return leftDepth.CompareTo(rightDepth);
            }

            if (left.RelativePath.Length != right.RelativePath.Length)
            {
                return left.RelativePath.Length.CompareTo(right.RelativePath.Length);
            }

            return string.CompareOrdinal(left.RelativePath, right.RelativePath);
        });

        return candidates;
    }

    private void Add(VaultDocument document)
    {
        string path = document.RelativePath;

        Index(_byRelativePath, path, document);
        Index(_byRelativePath, LinkNormalizer.StripKnownExtension(path), document);
        Index(_byNormalizedPath, LinkNormalizer.NormalizePath(path), document);
        Index(_byFileName, Path.GetFileName(path), document);

        Index(_byStem, document.Stem, document);
        Index(_byStemIgnoreCase, document.Stem, document);
        Index(_byNormalizedStem, LinkNormalizer.Normalize(document.Stem), document);

        for (string directory = document.DirectoryPath;
            directory.Length > 0;
            directory = ParentOf(directory))
        {
            _directories.Add(LinkNormalizer.NormalizePath(directory));
        }

        foreach (BlockAnchor anchor in document.BlockAnchors)
        {
            Index(_byBlockAnchor, anchor.Id, document);
        }

        DocumentFrontmatterIndex(document);
    }

    private void DocumentFrontmatterIndex(VaultDocument document)
    {
        foreach (string alias in document.Frontmatter.Aliases)
        {
            Index(_byAlias, LinkNormalizer.Normalize(alias), document);
        }

        if (document.Frontmatter.Title is { Length: > 0 } title)
        {
            Index(_byAlias, LinkNormalizer.Normalize(title), document);
        }

        if (document.Frontmatter.Slug is { Length: > 0 } slug)
        {
            Index(_byAlias, LinkNormalizer.Normalize(slug), document);
        }

        if (document.FirstHeading is { Length: > 0 } heading)
        {
            Index(_byAlias, LinkNormalizer.Normalize(heading), document);
        }

        // The identifier is deliberately not an alias: step 7 and step 8 are separate
        // rules, and folding one into the other would report every id hit as an alias.
        if (document.Frontmatter.Id is { Length: > 0 } id)
        {
            Index(_byIdentifier, id.Trim(), document);
        }
    }

    private void SortCandidates()
    {
        foreach (Dictionary<string, List<VaultDocument>> table in
            new[]
            {
                _byRelativePath, _byNormalizedPath, _byStem, _byStemIgnoreCase,
                _byNormalizedStem, _byAlias, _byIdentifier, _byFileName, _byBlockAnchor,
            })
        {
            foreach (List<VaultDocument> candidates in table.Values)
            {
                Sort(candidates);
            }
        }
    }

    private static void Index(
        Dictionary<string, List<VaultDocument>> table, string key, VaultDocument document)
    {
        if (key.Length == 0)
        {
            return;
        }

        if (!table.TryGetValue(key, out List<VaultDocument>? candidates))
        {
            candidates = [];
            table[key] = candidates;
        }

        if (!candidates.Contains(document))
        {
            candidates.Add(document);
        }
    }

    private static IReadOnlyList<VaultDocument> Lookup(
        Dictionary<string, List<VaultDocument>> table, string key) =>
        key.Length > 0 && table.TryGetValue(key, out List<VaultDocument>? candidates) ? candidates : [];

    private static string ParentOf(string directory)
    {
        int slash = directory.LastIndexOf('/');
        return slash <= 0 ? string.Empty : directory[..slash];
    }
}
