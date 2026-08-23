namespace Detangle.App;

/// <summary>One file a proposed write would change.</summary>
/// <param name="Path">The document, vault-relative.</param>
/// <param name="Changes">How many links in it would be rewritten.</param>
public sealed record PendingWriteFile(string Path, int Changes)
{
    /// <summary>The count as the confirm card shows it.</summary>
    public string Summary => Changes == 1 ? "1 link" : $"{Changes} links";
}

/// <summary>
/// A vault-wide rewrite that has been worked out but not performed.
/// <para>
/// Both writers in this application used to run straight off a click: "Fix all safe" and
/// "Normalize links in place" each rewrote every markdown file in the vault with no
/// confirmation, no list of what would change, and no undo — in a product whose stated
/// promise is that it is non-destructive by default.
/// </para>
/// <para>
/// This is the plan, held between the click and the write, so the reader sees the whole
/// set before any of it happens. It is not a dialog: it docks in the Link Doctor's action
/// card, where triage already happens.
/// </para>
/// </summary>
/// <param name="Title">What the write is called, in the reader's terms.</param>
/// <param name="Files">Every file it would touch.</param>
/// <param name="Apply">Performs the write, and returns how many files were changed.</param>
public sealed record PendingWrite(
    string Title,
    IReadOnlyList<PendingWriteFile> Files,
    Func<int> Apply)
{
    /// <summary>How many files would change.</summary>
    public int FileCount => Files.Count;

    /// <summary>How many links would be rewritten.</summary>
    public int ChangeCount => Files.Sum(f => f.Changes);

    /// <summary>The sentence over the list.</summary>
    public string Summary =>
        $"{Describe(ChangeCount, "link", "links")} in {Describe(FileCount, "file", "files")} "
        + "will be rewritten. This cannot be undone.";

    private static string Describe(int count, string one, string many) =>
        $"{count:N0} {(count == 1 ? one : many)}";
}
