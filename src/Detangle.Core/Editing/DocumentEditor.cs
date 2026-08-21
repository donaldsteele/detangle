using System.Security.Cryptography;
using System.Text;
using Detangle.Core.Vault;

namespace Detangle.Core.Editing;

/// <summary>What happened when a save was attempted.</summary>
public enum SaveOutcome
{
    /// <summary>The file was written.</summary>
    Saved,

    /// <summary>The content matched what was already on disk; nothing was written.</summary>
    Unchanged,

    /// <summary>Something else changed the file since it was opened.</summary>
    Conflict,

    /// <summary>The write failed.</summary>
    Failed,
}

/// <summary>
/// One document open for editing, with the fingerprint it had when it was read.
/// </summary>
/// <param name="Document">The document being edited.</param>
/// <param name="Content">The text as it was on disk when the session began.</param>
/// <param name="Fingerprint">Hash of that text, which is what conflict detection compares.</param>
public sealed record EditSession(VaultDocument Document, string Content, string Fingerprint)
{
    /// <summary>The file this session writes to.</summary>
    public string Path => Document.AbsolutePath;
}

/// <summary>The result of a save.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Session">The session to keep editing with, when the save succeeded.</param>
/// <param name="Message">What to tell the reader, when there is something to say.</param>
public sealed record SaveResult(SaveOutcome Outcome, EditSession? Session, string? Message = null)
{
    /// <summary>True when the file on disk now holds what the editor showed.</summary>
    public bool IsSuccess => Outcome is SaveOutcome.Saved or SaveOutcome.Unchanged;
}

/// <summary>
/// Light editing (plan.md section 6.5): read the file, edit it, write it back whole.
/// <para>
/// Saves are explicit and atomic, and they refuse to run over a file that changed
/// underneath them. A wiki is usually also being written by something else — an LLM, a
/// sync client, another editor — so "the file I loaded is still the file I am about to
/// overwrite" is a question that has to be asked every single time rather than assumed.
/// </para>
/// </summary>
public static class DocumentEditor
{
    /// <summary>Opens a document for editing.</summary>
    /// <param name="document">The document to read.</param>
    /// <returns>The session, or null when the file could not be read.</returns>
    public static EditSession? Open(VaultDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        string? content = Read(document.AbsolutePath);

        return content is null ? null : new EditSession(document, content, Fingerprint(content));
    }

    /// <summary>
    /// True when the file no longer holds what the session read. Compared by content
    /// rather than by timestamp: a sync client that rewrites a file byte for byte moves
    /// the timestamp without changing anything, and warning about that trains the reader
    /// to click through the warning that matters.
    /// </summary>
    public static bool HasChangedOnDisk(EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        string? current = Read(session.Path);

        return current is not null && !string.Equals(Fingerprint(current), session.Fingerprint, StringComparison.Ordinal);
    }

    /// <summary>Writes the edited text back, refusing a file that changed underneath.</summary>
    /// <param name="session">The session being saved.</param>
    /// <param name="content">The new text.</param>
    /// <param name="overwriteExternalChanges">Save anyway, discarding what the other writer did.</param>
    public static SaveResult Save(EditSession session, string content, bool overwriteExternalChanges = false)
    {
        ArgumentNullException.ThrowIfNull(session);

        string? current = Read(session.Path);
        string fingerprint = Fingerprint(content);

        if (current is not null && string.Equals(Fingerprint(current), fingerprint, StringComparison.Ordinal))
        {
            return new SaveResult(SaveOutcome.Unchanged, session with { Content = content, Fingerprint = fingerprint });
        }

        if (!overwriteExternalChanges
            && current is not null
            && !string.Equals(Fingerprint(current), session.Fingerprint, StringComparison.Ordinal))
        {
            return new SaveResult(
                SaveOutcome.Conflict,
                Session: null,
                $"{session.Document.RelativePath} changed on disk since it was opened.");
        }

        return AtomicFile.Write(session.Path, content) is { } failure
            ? new SaveResult(SaveOutcome.Failed, Session: null, failure)
            : new SaveResult(SaveOutcome.Saved, session with { Content = content, Fingerprint = fingerprint });
    }

    /// <summary>Rereads the file, abandoning whatever the editor held.</summary>
    public static EditSession? Reload(EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return Open(session.Document);
    }

    /// <summary>The content fingerprint two texts are compared by.</summary>
    public static string Fingerprint(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty)));

    private static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
