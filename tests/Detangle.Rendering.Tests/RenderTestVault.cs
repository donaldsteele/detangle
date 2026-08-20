using Detangle.Core.Linking;
using Detangle.Core.Parsing;
using Detangle.Core.Vault;
using Detangle.Rendering.Model;

namespace Detangle.Rendering.Tests;

/// <summary>Serves document text from a dictionary, so tests never touch a disk.</summary>
public sealed class DictionaryContentReader(IReadOnlyDictionary<string, string> files) : IDocumentContentReader
{
    /// <inheritdoc />
    public string? Read(VaultDocument document) =>
        files.TryGetValue(document.RelativePath, out string? content) ? content : null;
}

/// <summary>
/// Builds a vault and a render model from (path, content) pairs. Rendering decisions are
/// about content, not about the filesystem, so the whole suite runs in memory.
/// </summary>
public sealed class RenderTestVault
{
    private RenderTestVault(VaultSnapshot vault, RenderModelBuilder builder)
    {
        Vault = vault;
        Builder = builder;
    }

    /// <summary>The synthetic vault.</summary>
    public VaultSnapshot Vault { get; }

    /// <summary>A builder over it.</summary>
    public RenderModelBuilder Builder { get; }

    /// <summary>Builds a vault with the generic profile.</summary>
    public static RenderTestVault Build(params (string Path, string Content)[] files) =>
        Build(VaultFlavor.Generic, RenderOptions.Default, files);

    /// <summary>Builds a vault with an explicit flavor and options.</summary>
    public static RenderTestVault Build(
        VaultFlavor flavor, RenderOptions options, params (string Path, string Content)[] files)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        var documents = new List<VaultDocument>(files.Length);

        foreach ((string path, string content) in files)
        {
            contents[path] = content;
            documents.Add(CreateDocument(path, content));
        }

        var vault = new VaultSnapshot
        {
            RootPath = "/synthetic",
            Profile = VaultProfile.For(flavor),
            Index = VaultIndex.Build(documents),
        };

        return new RenderTestVault(
            vault, new RenderModelBuilder(vault, new DictionaryContentReader(contents), options));
    }

    /// <summary>Renders one of this vault's documents.</summary>
    public RenderDocument Render(string relativePath) =>
        Builder.Build(Vault.Index.ByRelativePath(relativePath).Single());

    /// <summary>Renders a document and returns its blocks, skipping the properties card.</summary>
    public IReadOnlyList<RenderBlock> Body(string relativePath) =>
        [.. Render(relativePath).Blocks.Where(b => b is not PropertiesRenderBlock)];

    /// <summary>Renders a single-document vault whose one file is "page.md".</summary>
    public static IReadOnlyList<RenderBlock> BodyOf(string markdown) =>
        Build(("page.md", markdown)).Body("page.md");

    private static VaultDocument CreateDocument(string relativePath, string content)
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
}
