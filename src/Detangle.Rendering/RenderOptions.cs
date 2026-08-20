using Detangle.Core.Vault;

namespace Detangle.Rendering;

/// <summary>Reads a document's text. Abstracted so the renderer is testable without a disk.</summary>
public interface IDocumentContentReader
{
    /// <summary>Returns the document's text, or null when it cannot be read.</summary>
    string? Read(VaultDocument document);
}

/// <summary>Reads document text from the filesystem.</summary>
public sealed class FileDocumentContentReader : IDocumentContentReader
{
    /// <summary>A shared instance; the reader holds no state.</summary>
    public static FileDocumentContentReader Instance { get; } = new();

    /// <inheritdoc />
    public string? Read(VaultDocument document)
    {
        try
        {
            return File.ReadAllText(document.AbsolutePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Knobs for building a render model.</summary>
public sealed class RenderOptions
{
    /// <summary>The defaults.</summary>
    public static RenderOptions Default { get; } = new();

    /// <summary>
    /// How deep an embed chain may go before it is cut off. A note that embeds a note
    /// that embeds a note is legitimate; ten levels is a runaway, and the depth cap plus
    /// cycle detection is what keeps a malformed vault from hanging the reader.
    /// </summary>
    public int MaxTransclusionDepth { get; init; } = 4;

    /// <summary>Whether to prepend the frontmatter properties card.</summary>
    public bool IncludeProperties { get; init; } = true;

    /// <summary>
    /// Fence languages the diagram renderer claims, mapped to the language they are.
    /// A fence whose language is not here renders as code.
    /// </summary>
    public IReadOnlyDictionary<string, DiagramKind> DiagramLanguages { get; init; } =
        new Dictionary<string, DiagramKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["mermaid"] = DiagramKind.Mermaid,
            ["dbml"] = DiagramKind.Dbml,
        };

    /// <summary>
    /// The backend diagram fences are rendered with. Null renders them as source, which
    /// is what a headless caller — an export, a test of the link graph — wants.
    /// </summary>
    public IDiagramRenderer? DiagramRenderer { get; init; }

    /// <summary>Which palette diagrams are rendered against.</summary>
    public DiagramTheme DiagramTheme { get; init; } = DiagramTheme.Light;
}
