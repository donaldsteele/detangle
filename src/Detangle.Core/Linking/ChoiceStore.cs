using System.Globalization;
using Detangle.Core.Vault;

namespace Detangle.Core.Linking;

/// <summary>One ambiguity a person settled.</summary>
/// <param name="SourceDirectory">The folder the link was written in, vault-relative.</param>
/// <param name="RawTarget">The link's target, exactly as written.</param>
/// <param name="TargetPath">The document it was decided to mean.</param>
/// <param name="Note">The trailing comment, when the file carried one.</param>
public sealed record SettledChoice(
    string SourceDirectory,
    string RawTarget,
    string TargetPath,
    string Note = "")
{
    /// <summary>The key the resolver looks this up by.</summary>
    public string Key => LinkResolver.ChoiceKey(SourceDirectory, RawTarget);
}

/// <summary>
/// The thirteenth rung: which document an ambiguous link was decided to mean (plan.md
/// section 15.2).
/// <para>
/// Every other rung of the chain is a rule. This one is a person, and it is the only part
/// of the resolution a vault cannot re-derive — which is exactly why it has to travel with
/// the vault rather than live inside one application's private state. Until it did, the
/// desktop app that settled an ambiguity and the CLI, the exporter, the demo and every
/// test resolved the same link differently, and no two people could share a decision.
/// </para>
/// <para>
/// The format is one line per decision, sorted, comment-carrying, so it diffs cleanly and
/// commits beside the markdown:
/// </para>
/// <code>
/// wiki/concepts | Transformer -> wiki/entities/transformer.md  # was ambiguous between 3
/// </code>
/// <para>
/// Not JSON, for the same reason: this is a file people read in a pull request, and the
/// question "why does this link mean that page" should be answerable from the diff.
/// </para>
/// </summary>
public sealed class ChoiceStore
{
    /// <summary>The file's name at the vault root.</summary>
    public const string FileName = ".detangle-choices";

    private const string Arrow = "->";

    private readonly Dictionary<string, SettledChoice> _choices;
    private readonly string? _path;

    private Dictionary<string, string>? _resolverView;

    private ChoiceStore(Dictionary<string, SettledChoice> choices, string? path)
    {
        _choices = choices;
        _path = path;
    }

    /// <summary>Where the choices live for a vault.</summary>
    public static string PathFor(string vaultRoot) => Path.Combine(vaultRoot, FileName);

    /// <summary>
    /// A store with nothing in it that can never be written. What the browser head gets:
    /// a decision made there lasts as long as the tab, and says so rather than claiming a
    /// save into a filesystem that disappears with it.
    /// </summary>
    public static ChoiceStore Detached() => new([], null);

    /// <summary>
    /// Reads the choices beside a vault. Never throws: a file that cannot be read costs
    /// some decisions, not a vault.
    /// </summary>
    /// <param name="vaultRoot">The vault's root directory.</param>
    /// <param name="path">A file to read instead of the vault's own, for a CLI override.</param>
    public static ChoiceStore Open(string vaultRoot, string? path = null)
    {
        string file = path ?? PathFor(vaultRoot);
        var choices = new Dictionary<string, SettledChoice>(StringComparer.Ordinal);

        try
        {
            if (File.Exists(file))
            {
                foreach (string line in File.ReadAllLines(file))
                {
                    if (Parse(line) is { } choice)
                    {
                        choices[choice.Key] = choice;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or PathTooLongException)
        {
            choices.Clear();
        }

        return new ChoiceStore(choices, file);
    }

    /// <summary>
    /// The decisions, in the form the resolver takes them. Rebuilt when one changes rather
    /// than on every read: the graph asks for this once per rebuild, but a rebuild happens
    /// on every save, every rescan and every settled link.
    /// </summary>
    public IReadOnlyDictionary<string, string> ForResolver => _resolverView ??=
        _choices.ToDictionary(c => c.Key, c => c.Value.TargetPath, StringComparer.Ordinal);

    /// <summary>Every decision, by the folder and link they were made for.</summary>
    public IReadOnlyList<SettledChoice> All => [.. _choices.Values
        .OrderBy(c => c.SourceDirectory, StringComparer.Ordinal)
        .ThenBy(c => c.RawTarget, StringComparer.Ordinal)];

    /// <summary>How many decisions are recorded.</summary>
    public int Count => _choices.Count;

    /// <summary>True when a decision made now will still be here next time.</summary>
    public bool IsPersistent => _path is not null;

    /// <summary>
    /// Records which document a link was decided to mean, and returns true when the record
    /// will outlive the session.
    /// </summary>
    /// <param name="source">The document the link was written in.</param>
    /// <param name="rawTarget">The link's target, exactly as written.</param>
    /// <param name="chosen">The document that was picked.</param>
    /// <param name="candidates">How many documents the target matched, for the note.</param>
    public bool Settle(VaultDocument source, string rawTarget, VaultDocument chosen, int candidates = 0)
    {
        string note = candidates > 1
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"settled {DateTime.UtcNow:yyyy-MM-dd}, was ambiguous between {candidates}")
            : string.Create(CultureInfo.InvariantCulture, $"settled {DateTime.UtcNow:yyyy-MM-dd}");

        var choice = new SettledChoice(source.DirectoryPath, rawTarget, chosen.RelativePath, note);

        _choices[choice.Key] = choice;
        _resolverView = null;

        return Write();
    }

    /// <summary>Undoes one decision, and returns true when that will stick.</summary>
    /// <param name="choice">The decision to revoke.</param>
    public bool Forget(SettledChoice choice)
    {
        _choices.Remove(choice.Key);
        _resolverView = null;

        return Write();
    }

    /// <summary>Parses one line, or null when it is a comment, blank, or malformed.</summary>
    /// <param name="line">The line as written.</param>
    public static SettledChoice? Parse(string line)
    {
        string text = line.Trim();

        if (text.Length == 0 || text.StartsWith('#'))
        {
            return null;
        }

        int arrow = text.IndexOf(Arrow, StringComparison.Ordinal);
        int pipe = text.IndexOf('|', StringComparison.Ordinal);

        // A line missing either separator is a line somebody mistyped. Skipped rather than
        // thrown over: one unreadable decision should not cost the reader the rest of them.
        if (arrow < 0 || pipe < 0 || pipe > arrow)
        {
            return null;
        }

        string directory = text[..pipe].Trim();
        string rawTarget = text[(pipe + 1)..arrow].Trim();
        string tail = text[(arrow + Arrow.Length)..];
        string note = string.Empty;

        // The comment is looked for only after the arrow, and only where two spaces
        // introduce it. A bare hash is not a comment marker here: "[[Setup#Install]]" is a
        // perfectly ordinary ambiguous link, and eating everything after its anchor would
        // silently record a decision about a different link.
        int comment = tail.IndexOf("  #", StringComparison.Ordinal);

        if (comment >= 0)
        {
            note = tail[(comment + 3)..].Trim();
            tail = tail[..comment];
        }

        string target = tail.Trim();

        return rawTarget.Length == 0 || target.Length == 0
            ? null
            // "." is how the vault root is written, so a line for a page at the root is
            // not a line whose first field is empty and looks truncated.
            : new SettledChoice(directory == "." ? string.Empty : directory, rawTarget, target, note);
    }

    /// <summary>The file's text, which is what makes the format testable without a disk.</summary>
    public string Serialize()
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine("# Which document each ambiguous link was decided to mean.");
        text.AppendLine("# Written by Detangle; edit or delete a line to change the decision.");
        text.AppendLine();

        foreach (SettledChoice choice in All)
        {
            text.Append(choice.SourceDirectory.Length == 0 ? "." : choice.SourceDirectory);
            text.Append(" | ");
            text.Append(choice.RawTarget);
            text.Append(' ');
            text.Append(Arrow);
            text.Append(' ');
            text.Append(choice.TargetPath);

            if (choice.Note is { Length: > 0 } note)
            {
                text.Append("  # ");
                text.Append(note);
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private bool Write()
    {
        if (_path is not { } path)
        {
            return false;
        }

        try
        {
            // An empty store deletes the file rather than leaving a header with nothing
            // under it, so revoking the last decision leaves the vault as it was found.
            if (_choices.Count == 0)
            {
                File.Delete(path);

                return true;
            }

            string temporary = path + ".tmp";

            File.WriteAllText(temporary, Serialize());
            File.Move(temporary, path, overwrite: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
