using Detangle.Core.Linking;
using Detangle.Core.Parsing;
using Detangle.Core.Vault;

namespace Detangle.Core.Tests;

/// <summary>
/// Builds a vault in memory from (path, content) pairs. Some edge cases cannot exist on
/// disk on every platform — case-only duplicate filenames are impossible on Windows and
/// macOS — so the resolver has to be testable without one.
/// </summary>
public sealed class TestVault
{
    private TestVault(VaultIndex index, VaultProfile profile)
    {
        Index = index;
        Profile = profile;
        Resolver = new LinkResolver(index, profile);
    }

    /// <summary>Indexes over the synthetic documents.</summary>
    public VaultIndex Index { get; }

    /// <summary>The profile in force.</summary>
    public VaultProfile Profile { get; }

    /// <summary>A resolver over this vault.</summary>
    public LinkResolver Resolver { get; }

    /// <summary>Builds a generic-flavor vault.</summary>
    public static TestVault Build(params (string Path, string Content)[] files) =>
        Build(VaultFlavor.Generic, files);

    /// <summary>Builds a vault with an explicit flavor.</summary>
    public static TestVault Build(VaultFlavor flavor, params (string Path, string Content)[] files)
    {
        var documents = new List<VaultDocument>(files.Length);

        foreach ((string path, string content) in files)
        {
            documents.Add(CreateDocument(path, content));
        }

        return new TestVault(VaultIndex.Build(documents), VaultProfile.For(flavor));
    }

    /// <summary>Parses one file into a document without touching the filesystem.</summary>
    public static VaultDocument CreateDocument(string relativePath, string content)
    {
        int slash = relativePath.LastIndexOf('/');
        string fileName = slash < 0 ? relativePath : relativePath[(slash + 1)..];

        ParsedDocument parsed = LinkNormalizer.HasMarkdownExtension(relativePath)
            ? DocumentParser.Parse(relativePath, content)
            : ParsedDocument.Empty;

        return new VaultDocument
        {
            RelativePath = relativePath,
            AbsolutePath = $"/synthetic/{relativePath}",
            Stem = Path.GetFileNameWithoutExtension(fileName),
            Extension = Path.GetExtension(fileName).ToLowerInvariant(),
            DirectoryPath = slash < 0 ? string.Empty : relativePath[..slash],
            Frontmatter = parsed.Frontmatter,
            Headings = parsed.Headings,
            BlockAnchors = parsed.BlockAnchors,
            Links = parsed.Links,
        };
    }

    /// <summary>Resolves every link in one of this vault's documents.</summary>
    public IReadOnlyList<LinkResolution> ResolveLinksOf(string relativePath)
    {
        VaultDocument document = Index.ByRelativePath(relativePath).Single();

        return Resolver.ResolveAll(document);
    }

    /// <summary>Resolves the single link written in a synthetic source document.</summary>
    public LinkResolution ResolveOnly(string sourcePath) => ResolveLinksOf(sourcePath).Single();
}
