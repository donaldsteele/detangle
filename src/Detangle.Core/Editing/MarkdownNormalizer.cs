using Detangle.Core.Linking;
using Detangle.Core.Vault;

namespace Detangle.Core.Editing;

/// <summary>How a normalized link should be written.</summary>
public enum LinkForm
{
    /// <summary>Vault-relative, the form most wiki tools index by.</summary>
    VaultRelative,

    /// <summary>Relative to the linking document, which survives the vault being moved.</summary>
    NoteRelative,
}

/// <summary>What a normalization pass changed.</summary>
/// <param name="Content">The rewritten text.</param>
/// <param name="Rewritten">How many links were rewritten.</param>
/// <param name="Unresolved">How many links were left alone because they resolve to nothing.</param>
public sealed record NormalizeResult(string Content, int Rewritten, int Unresolved);

/// <summary>
/// Rewrites every link in a document to its canonical target (plan.md section 6.6).
/// <para>
/// This is the export that matters most for handing a vault to another tool. Detangle
/// can follow "[[Attention Is All You Need]]" to attention-is-all-you-need.md through
/// the resolution chain; nothing else can. Normalizing writes the answer down, so the
/// vault keeps working after it leaves.
/// </para>
/// <para>
/// Only the target is touched. Aliases, anchors, embed markers and size specifications
/// are preserved exactly, and a link that resolves to nothing is left as the author
/// wrote it — inventing a target for it would turn a visible problem into a silent one.
/// </para>
/// </summary>
public static class MarkdownNormalizer
{
    /// <summary>Rewrites a document's links.</summary>
    /// <param name="content">The document's current text.</param>
    /// <param name="document">The document being rewritten.</param>
    /// <param name="resolutions">Its resolutions, in document order.</param>
    /// <param name="form">Whether targets come out vault-relative or note-relative.</param>
    public static NormalizeResult Normalize(
        string content,
        VaultDocument document,
        IEnumerable<LinkResolution> resolutions,
        LinkForm form = LinkForm.VaultRelative)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolutions);

        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int rewritten = 0;
        int unresolved = 0;

        // Later columns first: rewriting from the end of a line keeps every earlier
        // link's recorded column valid, the same reason the Link Doctor fixes files from
        // the bottom up.
        foreach (LinkResolution resolution in resolutions
            .OrderByDescending(r => r.Link.Line)
            .ThenByDescending(r => r.Link.Column))
        {
            LinkReference link = resolution.Link;

            if (link.IsExternal || link.IsSelfReference || link.Syntax == LinkSyntax.Tag)
            {
                continue;
            }

            if (resolution.Target is not { } target)
            {
                unresolved++;
                continue;
            }

            // Frontmatter references are bare slugs in a YAML list rather than links in
            // the body; they are rewritten by the same rule but they are not wiki syntax,
            // so the same replacement works without any bracket handling.
            string replacement = CanonicalTarget(document, target, form, link);

            if (string.Equals(replacement, link.RawTarget, StringComparison.Ordinal))
            {
                continue;
            }

            int index = link.Line - 1;

            if (index < 0 || index >= lines.Length)
            {
                continue;
            }

            string line = lines[index];
            int position = FindTarget(line, link);

            if (position < 0)
            {
                continue;
            }

            lines[index] = string.Concat(
                line.AsSpan(0, position), replacement, line.AsSpan(position + link.RawTarget.Length));

            rewritten++;
        }

        string result = string.Join('\n', lines);

        return new NormalizeResult(
            content.Contains("\r\n", StringComparison.Ordinal)
                ? result.Replace("\n", "\r\n", StringComparison.Ordinal)
                : result,
            rewritten,
            unresolved);
    }

    /// <summary>
    /// The canonical way to write a link to a target. Markdown links keep their file
    /// extension and percent-encode what a URL must; wiki links drop the extension,
    /// because that is the form every wiki tool indexes by.
    /// </summary>
    public static string CanonicalTarget(
        VaultDocument source, VaultDocument target, LinkForm form, LinkReference link)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(link);

        string path = form == LinkForm.NoteRelative
            ? RelativePath(source.DirectoryPath, target.RelativePath)
            : target.RelativePath;

        if (link.Syntax != LinkSyntax.Markdown)
        {
            return target.IsMarkdown ? LinkNormalizer.StripKnownExtension(path) : path;
        }

        // A markdown link is a URL: a space in a path has to be escaped or the link stops
        // being one, and half the tooling in this space writes them unescaped.
        return string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }

    /// <summary>The path from one vault directory to a vault-relative file.</summary>
    public static string RelativePath(string fromDirectory, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(fromDirectory);
        ArgumentNullException.ThrowIfNull(targetPath);

        string[] from = fromDirectory.Length == 0
            ? []
            : fromDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string[] to = targetPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        int shared = 0;

        while (shared < from.Length && shared < to.Length - 1
            && string.Equals(from[shared], to[shared], StringComparison.OrdinalIgnoreCase))
        {
            shared++;
        }

        var segments = new List<string>();

        for (int i = shared; i < from.Length; i++)
        {
            segments.Add("..");
        }

        segments.AddRange(to[shared..]);

        return string.Join('/', segments);
    }

    /// <summary>
    /// Finds the link's target text on its line. The recorded column is used as a
    /// starting point rather than as the answer: one line can carry the same target
    /// twice, and inline formatting between the two is exactly the sort of thing that
    /// shifts a column by one.
    /// </summary>
    private static int FindTarget(string line, LinkReference link)
    {
        if (link.RawTarget.Length == 0)
        {
            return -1;
        }

        int from = Math.Clamp(link.Column, 0, Math.Max(0, line.Length - 1));
        int found = line.IndexOf(link.RawTarget, from, StringComparison.Ordinal);

        return found >= 0 ? found : line.IndexOf(link.RawTarget, StringComparison.Ordinal);
    }
}
