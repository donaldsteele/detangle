using Detangle.Core.Linking;
using Detangle.Core.Parsing;

namespace Detangle.Core.Vault;

/// <summary>What a scan is allowed to look at.</summary>
public sealed class VaultScanOptions
{
    /// <summary>The defaults: skip tooling directories, read files up to 8 MB.</summary>
    public static VaultScanOptions Default { get; } = new();

    /// <summary>
    /// Directory names skipped anywhere in the tree. The marker directories stay
    /// readable to the flavor sniffer — they are excluded from the document set, not
    /// from the listing detection runs over.
    /// </summary>
    public IReadOnlySet<string> IgnoredDirectories { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".svn", ".hg", "node_modules", ".detangle", ".obsidian",
            ".trash", ".vscode", ".idea", "bin", "obj",
        };

    /// <summary>
    /// Largest file the scanner will read into memory. Oversized files are still indexed
    /// as link targets — they just carry no parsed content, and the Link Doctor reports
    /// them.
    /// </summary>
    public long MaxFileSizeInBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Override the sniffed flavor; null lets detection decide.</summary>
    public VaultFlavor? ForcedFlavor { get; init; }
}

/// <summary>
/// One scanned vault: its documents, its indexes, and the profile that governs
/// resolution. Immutable — the file watcher produces a new snapshot rather than
/// mutating this one, so a render in flight never sees a half-updated index.
/// </summary>
public sealed class VaultSnapshot
{
    /// <summary>Absolute path to the vault root.</summary>
    public required string RootPath { get; init; }

    /// <summary>The detected or forced flavor profile.</summary>
    public required VaultProfile Profile { get; init; }

    /// <summary>Lookup tables over <see cref="Documents"/>.</summary>
    public required VaultIndex Index { get; init; }

    /// <summary>Files the scan read, markdown and attachments.</summary>
    public IReadOnlyList<VaultDocument> Documents => Index.Documents;

    /// <summary>Problems hit during the scan; a scan never throws on one bad file.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>Creates a resolver over this snapshot.</summary>
    public LinkResolver CreateResolver(IReadOnlyDictionary<string, string>? rememberedChoices = null) =>
        new(Index, Profile, rememberedChoices);
}

/// <summary>
/// Walks a directory into a <see cref="VaultSnapshot"/>: list, sniff the flavor, parse each
/// markdown file, build the indexes. Scanning is deliberately separate from resolving
/// so that both halves stay testable — the resolver golden tests build documents by
/// hand and never touch a disk.
/// </summary>
public static class VaultScanner
{
    /// <summary>Scans a directory tree.</summary>
    public static VaultSnapshot Scan(string rootPath, VaultScanOptions? options = null)
    {
        options ??= VaultScanOptions.Default;

        string root = Path.GetFullPath(rootPath);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Vault directory not found: {root}");
        }

        var diagnostics = new List<string>();
        List<string> relativePaths = [.. ListFiles(root, options, diagnostics)];

        VaultFlavor flavor = options.ForcedFlavor ?? FlavorDetector.Detect(relativePaths);

        VaultProfile profile = options.ForcedFlavor is null
            ? VaultProfile.For(flavor)
            : new VaultProfile
            {
                Flavor = flavor,
                EnabledRules = VaultProfile.For(flavor).EnabledRules,
                IsUserOverride = true,
            };

        var documents = new List<VaultDocument>(relativePaths.Count);

        foreach (string relativePath in relativePaths)
        {
            if (relativePath.EndsWith('/') || IsIgnored(relativePath, options))
            {
                continue;
            }

            VaultDocument? document = ReadDocument(root, relativePath, options, diagnostics);

            if (document is not null)
            {
                documents.Add(document);
            }
        }

        return new VaultSnapshot
        {
            RootPath = root,
            Profile = profile,
            Index = VaultIndex.Build(documents),
            Diagnostics = diagnostics,
        };
    }

    /// <summary>Reads and parses a single file into a document.</summary>
    public static VaultDocument? ReadDocument(
        string rootPath, string relativePath, VaultScanOptions? options = null, List<string>? diagnostics = null)
    {
        options ??= VaultScanOptions.Default;

        string absolutePath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        FileInfo info;
        try
        {
            info = new FileInfo(absolutePath);
            if (!info.Exists)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics?.Add($"{relativePath}: {ex.Message}");
            return null;
        }

        int slash = relativePath.LastIndexOf('/');
        string fileName = slash < 0 ? relativePath : relativePath[(slash + 1)..];
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        var document = new VaultDocument
        {
            RelativePath = relativePath,
            AbsolutePath = absolutePath,
            // Dendron stems are dot hierarchies, but GetFileNameWithoutExtension only
            // strips the final extension, so "a.b.c.md" correctly yields "a.b.c".
            Stem = Path.GetFileNameWithoutExtension(fileName),
            Extension = extension,
            DirectoryPath = slash < 0 ? string.Empty : relativePath[..slash],
            LastModified = info.LastWriteTimeUtc,
            SizeInBytes = info.Length,
        };

        if (!document.IsMarkdown)
        {
            // Attachments are index entries, not documents: they are link targets and
            // nothing more.
            return document;
        }

        if (info.Length > options.MaxFileSizeInBytes)
        {
            diagnostics?.Add(
                $"{relativePath}: {info.Length / 1024 / 1024} MB exceeds the scan limit; indexed without content.");
            return document;
        }

        string content;
        try
        {
            content = File.ReadAllText(absolutePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics?.Add($"{relativePath}: {ex.Message}");
            return document;
        }

        ParsedDocument parsed = DocumentParser.Parse(relativePath, content);

        return new VaultDocument
        {
            RelativePath = document.RelativePath,
            AbsolutePath = document.AbsolutePath,
            Stem = document.Stem,
            Extension = document.Extension,
            DirectoryPath = document.DirectoryPath,
            LastModified = document.LastModified,
            SizeInBytes = document.SizeInBytes,
            Frontmatter = parsed.Frontmatter,
            Headings = parsed.Headings,
            BlockAnchors = parsed.BlockAnchors,
            Links = parsed.Links,
        };
    }

    /// <summary>
    /// Lists every file under the root as a vault-relative path with "/" separators.
    /// Marker directories are listed even when they are excluded from the document set,
    /// because that listing is what the flavor sniffer reads.
    /// </summary>
    private static IEnumerable<string> ListFiles(
        string root, VaultScanOptions options, List<string> diagnostics)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            string[] entries;
            try
            {
                entries = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"{Relative(root, directory)}: {ex.Message}");
                continue;
            }

            foreach (string file in entries)
            {
                yield return Relative(root, file);
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"{Relative(root, directory)}: {ex.Message}");
                continue;
            }

            foreach (string child in children)
            {
                string name = Path.GetFileName(child);

                // ".obsidian" and friends are read one level deep for their marker files
                // only; nothing below them is walked.
                if (options.IgnoredDirectories.Contains(name))
                {
                    // Not walked, but its own files are still listed: ".obsidian/app.json"
                    // and ".vscode/foam.json" are how two flavors identify themselves, and
                    // IsIgnored keeps them out of the document set regardless.
                    yield return Relative(root, child) + "/";

                    foreach (string marker in SafeGetFiles(child, root, diagnostics))
                    {
                        yield return Relative(root, marker);
                    }

                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static string[] SafeGetFiles(string directory, string root, List<string> diagnostics)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"{Relative(root, directory)}: {ex.Message}");
            return [];
        }
    }

    private static bool IsIgnored(string relativePath, VaultScanOptions options)
    {
        foreach (string segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[..^1])
        {
            if (options.IgnoredDirectories.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
