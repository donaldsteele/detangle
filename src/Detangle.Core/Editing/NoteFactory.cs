using Detangle.Core.Linking;
using Detangle.Core.Vault;

namespace Detangle.Core.Editing;

/// <summary>A note that was, or would be, created for a broken link.</summary>
/// <param name="RelativePath">Where it goes, vault-relative with "/" separators.</param>
/// <param name="AbsolutePath">Where it goes on disk.</param>
/// <param name="Title">The title written into the frontmatter and the first heading.</param>
/// <param name="Content">The file's proposed content.</param>
/// <param name="TemplatePath">The vault template it was built from, when there was one.</param>
public sealed record NoteDraft(
    string RelativePath,
    string AbsolutePath,
    string Title,
    string Content,
    string? TemplatePath);

/// <summary>
/// Creates the note a broken link was pointing at (plan.md section 6.5).
/// <para>
/// This is the only note-creation path in v1, and it exists because it is the natural
/// end of the Link Doctor: the reader has just been shown a link that resolves to
/// nothing, and the honest options are to fix the link or to write the page. If the
/// vault has a template, the new file is stubbed from it, so a note created here looks
/// like the notes around it rather than like something a different tool made.
/// </para>
/// </summary>
public static class NoteFactory
{
    private static readonly string[] TemplateFolders =
        ["templates", "_templates", ".obsidian/templates", "_template"];

    private static readonly string[] TemplateNames =
        ["note", "default", "new-note", "page", "template"];

    /// <summary>
    /// Works out what note a broken link wants, without writing anything.
    /// </summary>
    /// <param name="vault">The vault the note goes into.</param>
    /// <param name="resolution">The unresolved link.</param>
    /// <param name="now">The creation date written into the stub.</param>
    /// <returns>The draft, or null when the link names nothing that can be a file.</returns>
    public static NoteDraft? Draft(VaultSnapshot vault, LinkResolution resolution, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(resolution);

        LinkReference link = resolution.Link;

        if (link.IsExternal || link.IsSelfReference || link.RawTarget.Trim().Length == 0)
        {
            return null;
        }

        string relativePath = PathFor(vault, link);

        if (relativePath.Length == 0)
        {
            return null;
        }

        string title = TitleFor(link);
        (string? templatePath, string? template) = FindTemplate(vault);

        return new NoteDraft(
            relativePath,
            Path.Combine(vault.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            title,
            Compose(template, title, now ?? DateTimeOffset.Now),
            templatePath);
    }

    /// <summary>
    /// Writes a drafted note, refusing to touch a file that already exists.
    /// </summary>
    /// <param name="draft">What to write.</param>
    /// <returns>Null on success, or why it failed.</returns>
    public static string? Create(NoteDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        // Creating a note is meant to fill a gap. If something is already there, the link
        // was not broken for the reason the reader thought, and overwriting it would
        // destroy a page to fix a link.
        return File.Exists(draft.AbsolutePath)
            ? $"{draft.RelativePath} already exists."
            : AtomicFile.Write(draft.AbsolutePath, draft.Content);
    }

    /// <summary>
    /// Where a link's note belongs. A target with a slash in it is already saying where
    /// it wants to live; one without goes next to the page that linked to it, which keeps
    /// a folder's notes together without asking the reader to decide.
    /// </summary>
    public static string PathFor(VaultSnapshot vault, LinkReference link)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(link);

        string target = link.RawTarget.Trim().Replace('\\', '/').Trim('/');

        if (target.Length == 0)
        {
            return string.Empty;
        }

        string[] segments = [.. target.Split('/').Select(Sanitize).Where(s => s.Length > 0)];

        if (segments.Length == 0)
        {
            return string.Empty;
        }

        segments[^1] = LinkNormalizer.StripKnownExtension(segments[^1]) + ".md";

        if (target.Contains('/', StringComparison.Ordinal))
        {
            return string.Join('/', segments);
        }

        string folder = FolderOf(vault, link);

        return folder.Length == 0 ? segments[^1] : $"{folder}/{segments[^1]}";
    }

    /// <summary>The title a link implies: its alias if it wrote one, else its target.</summary>
    public static string TitleFor(LinkReference link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (link.Label is { Length: > 0 } label)
        {
            return label.Trim();
        }

        string target = link.RawTarget.Trim().Replace('\\', '/').TrimEnd('/');
        int slash = target.LastIndexOf('/');

        return LinkNormalizer.StripKnownExtension(slash < 0 ? target : target[(slash + 1)..]);
    }

    /// <summary>The vault's note template, when it has one.</summary>
    public static (string? Path, string? Content) FindTemplate(VaultSnapshot vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        foreach (string folder in TemplateFolders)
        {
            string directory = Path.Combine(
                vault.RootPath, folder.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(directory))
            {
                continue;
            }

            string[] candidates;

            try
            {
                candidates = Directory.GetFiles(directory, "*.md");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (candidates.Length == 0)
            {
                continue;
            }

            // A vault with several templates is not asking a question here; take the one
            // whose name says "an ordinary note", else the first in a stable order.
            string chosen = candidates
                .OrderBy(c => Rank(Path.GetFileNameWithoutExtension(c)))
                .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                .First();

            try
            {
                return (Path.GetRelativePath(vault.RootPath, chosen).Replace('\\', '/'), File.ReadAllText(chosen));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (null, null);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Fills a template's placeholders, or writes a minimal stub when there is no
    /// template. The placeholder set is the intersection of what Obsidian, Templater and
    /// Jekyll-style vaults use, because a vault written by an LLM will have copied
    /// whichever one its training data favoured.
    /// </summary>
    public static string Compose(string? template, string title, DateTimeOffset now)
    {
        if (template is not { Length: > 0 })
        {
            return $"---\ntitle: {title}\ncreated: {now:yyyy-MM-dd}\n---\n\n# {title}\n";
        }

        string date = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string time = now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        string filled = template
            .Replace("{{title}}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{{date}}", date, StringComparison.OrdinalIgnoreCase)
            .Replace("{{time}}", time, StringComparison.OrdinalIgnoreCase)
            .Replace("{{tp.file.title}}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{{date:YYYY-MM-DD}}", date, StringComparison.OrdinalIgnoreCase);

        return filled.EndsWith('\n') ? filled : filled + "\n";
    }

    private static string FolderOf(VaultSnapshot vault, LinkReference link)
    {
        VaultDocument? source = vault.Index.ByRelativePath(link.SourcePath).FirstOrDefault();

        return source?.DirectoryPath ?? string.Empty;
    }

    private static int Rank(string name)
    {
        int index = Array.FindIndex(
            TemplateNames, n => name.Equals(n, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? TemplateNames.Length : index;
    }

    /// <summary>
    /// Makes one path segment safe to create. A link target is arbitrary text, and a
    /// wiki full of them will eventually contain a colon, a question mark, or a run of
    /// dots — none of which can be a filename on Windows.
    /// </summary>
    private static string Sanitize(string segment)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(segment.Length);

        foreach (char c in segment.Trim())
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 || c is ':' or '?' or '*' or '"' or '<' or '>' or '|'
                ? '-'
                : c);
        }

        // "." and ".." are directory navigation rather than names, and a trailing dot or
        // space is silently dropped by Windows, which makes the file unopenable.
        string cleaned = builder.ToString().TrimEnd('.', ' ');

        return cleaned is "." or ".." ? string.Empty : cleaned;
    }
}
