using Detangle.Core.Editing;
using Detangle.Core.Graph;
using Detangle.Core.Linking;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.Core.Tests;

/// <summary>
/// Tests for light editing (plan.md section 6.5): atomic saves, external-change
/// detection, and creating the note a broken link was pointing at.
/// </summary>
public class EditingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-editing-" + Guid.NewGuid().ToString("n")[..8]);

    public EditingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AWriteThatSucceedsLeavesNoTemporaryFileBehind()
    {
        string path = Path.Combine(_root, "page.md");

        Assert.Null(AtomicFile.Write(path, "hello\n"));
        Assert.Equal("hello\n", File.ReadAllText(path));
        Assert.False(File.Exists(path + AtomicFile.TemporarySuffix));
    }

    [Fact]
    public void AWriteCreatesTheFoldersItNeeds()
    {
        string path = Path.Combine(_root, "deep", "deeper", "page.md");

        Assert.Null(AtomicFile.Write(path, "hello\n"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SavingWritesTheEditedText()
    {
        EditSession session = OpenFile("page.md", "# One\n");

        SaveResult result = DocumentEditor.Save(session, "# Two\n");

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.True(result.IsSuccess);
        Assert.Equal("# Two\n", File.ReadAllText(session.Path));
    }

    [Fact]
    public void SavingUnchangedTextWritesNothing()
    {
        EditSession session = OpenFile("page.md", "# One\n");
        DateTime before = File.GetLastWriteTimeUtc(session.Path);

        SaveResult result = DocumentEditor.Save(session, "# One\n");

        Assert.Equal(SaveOutcome.Unchanged, result.Outcome);
        Assert.Equal(before, File.GetLastWriteTimeUtc(session.Path));
    }

    [Fact]
    public void SavingOverAFileSomethingElseChangedIsRefused()
    {
        EditSession session = OpenFile("page.md", "# One\n");

        File.WriteAllText(session.Path, "# Written by something else\n");

        SaveResult result = DocumentEditor.Save(session, "# Two\n");

        Assert.Equal(SaveOutcome.Conflict, result.Outcome);
        Assert.Contains("changed on disk", result.Message, StringComparison.Ordinal);
        Assert.Equal("# Written by something else\n", File.ReadAllText(session.Path));
    }

    [Fact]
    public void TheConflictCanBeOverriddenDeliberately()
    {
        EditSession session = OpenFile("page.md", "# One\n");

        File.WriteAllText(session.Path, "# Written by something else\n");

        SaveResult result = DocumentEditor.Save(session, "# Two\n", overwriteExternalChanges: true);

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Equal("# Two\n", File.ReadAllText(session.Path));
    }

    [Fact]
    public void SavingTwiceInARowWorksBecauseTheSessionMovesForward()
    {
        EditSession session = OpenFile("page.md", "# One\n");

        SaveResult first = DocumentEditor.Save(session, "# Two\n");

        Assert.NotNull(first.Session);

        SaveResult second = DocumentEditor.Save(first.Session!, "# Three\n");

        Assert.Equal(SaveOutcome.Saved, second.Outcome);
        Assert.Equal("# Three\n", File.ReadAllText(session.Path));
    }

    [Fact]
    public void AFileRewrittenWithIdenticalBytesIsNotAnExternalChange()
    {
        EditSession session = OpenFile("page.md", "# One\n");

        // A sync client that rewrites a file byte for byte moves its timestamp without
        // changing anything; warning about that trains the reader to ignore warnings.
        File.WriteAllText(session.Path, "# One\n");

        Assert.False(DocumentEditor.HasChangedOnDisk(session));
    }

    [Fact]
    public void AFileRewrittenWithDifferentBytesIsAnExternalChange()
    {
        EditSession session = OpenFile("page.md", "# One\n");

        File.WriteAllText(session.Path, "# Other\n");

        Assert.True(DocumentEditor.HasChangedOnDisk(session));
    }

    [Fact]
    public void ReloadingAbandonsTheEditorsCopy()
    {
        EditSession session = OpenFile("page.md", "# One\n");

        File.WriteAllText(session.Path, "# Other\n");

        Assert.Equal("# Other\n", DocumentEditor.Reload(session)?.Content);
    }

    [Fact]
    public void OpeningAMissingFileFailsQuietly()
    {
        VaultDocument document = TestVault.CreateDocument("gone.md", "# Gone\n");

        Assert.Null(DocumentEditor.Open(document));
    }

    [Fact]
    public void ABrokenLinkDraftsTheNoteItWasPointingAt()
    {
        VaultSnapshot vault = WriteVault(("notes/page.md", "See [[Missing Concept]].\n"));
        NoteDraft draft = Draft(vault, "notes/page.md");

        // No slash in the target, so the new note goes next to the page that wanted it.
        Assert.Equal("notes/Missing Concept.md", draft.RelativePath);
        Assert.Equal("Missing Concept", draft.Title);
        Assert.Contains("title: Missing Concept", draft.Content, StringComparison.Ordinal);
        Assert.Contains("# Missing Concept", draft.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void ATargetWithAFolderInItSaysWhereItWantsToLive()
    {
        VaultSnapshot vault = WriteVault(("notes/page.md", "See [[concepts/attention]].\n"));

        Assert.Equal("concepts/attention.md", Draft(vault, "notes/page.md").RelativePath);
    }

    [Fact]
    public void AnAliasedLinkTitlesTheNoteByItsAlias()
    {
        VaultSnapshot vault = WriteVault(("page.md", "See [[missing-concept|Attention]].\n"));

        NoteDraft draft = Draft(vault, "page.md");

        Assert.Equal("Attention", draft.Title);
        Assert.Equal("missing-concept.md", draft.RelativePath);
    }

    [Fact]
    public void ATargetThatCannotBeAFilenameIsMadeIntoOne()
    {
        VaultSnapshot vault = WriteVault(("page.md", "See [[what: is this?]].\n"));

        Assert.Equal("what- is this-.md", Draft(vault, "page.md").RelativePath);
    }

    [Fact]
    public void AVaultTemplateIsUsedWhenThereIsOne()
    {
        Directory.CreateDirectory(Path.Combine(_root, "templates"));
        File.WriteAllText(
            Path.Combine(_root, "templates", "note.md"),
            "---\ntitle: {{title}}\ntype: concept\ncreated: {{date}}\n---\n\n# {{title}}\n\nTODO\n");

        VaultSnapshot vault = WriteVault(("page.md", "See [[Missing]].\n"));
        NoteDraft draft = Draft(vault, "page.md");

        Assert.Equal("templates/note.md", draft.TemplatePath);
        Assert.Contains("type: concept", draft.Content, StringComparison.Ordinal);
        Assert.Contains("title: Missing", draft.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", draft.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatingANoteWritesItAndThenRefusesToDoItAgain()
    {
        VaultSnapshot vault = WriteVault(("page.md", "See [[Missing]].\n"));
        NoteDraft draft = Draft(vault, "page.md");

        Assert.Null(NoteFactory.Create(draft));
        Assert.True(File.Exists(draft.AbsolutePath));

        // The second attempt would destroy a page in order to fix a link.
        Assert.Contains("already exists", NoteFactory.Create(draft), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCreatedNoteResolvesTheLinkThatAskedForIt()
    {
        VaultSnapshot vault = WriteVault(("page.md", "See [[Missing Concept]].\n"));

        NoteFactory.Create(Draft(vault, "page.md"));

        LinkGraph rebuilt = LinkGraph.Build(VaultScanner.Scan(_root));

        Assert.DoesNotContain(rebuilt.Resolutions, r => r.IsUnresolved);
    }

    [Fact]
    public void NormalizingRewritesEveryResolvedLinkToItsCanonicalTarget()
    {
        const string Content = "See [[My Target]] and [[note]] and [[nowhere]].\n";

        VaultSnapshot vault = WriteVault(
            (".gitkeep.md", "# Keep\n"),
            ("page.md", Content),
            ("concepts/my-target.md", "# My Target\n"),
            ("concepts/note.md", "# Note\n"));

        VaultDocument document = vault.Index.ByRelativePath("page.md").Single();
        NormalizeResult result = MarkdownNormalizer.Normalize(
            Content, document, vault.CreateResolver().ResolveAll(document));

        Assert.Equal(2, result.Rewritten);
        Assert.Equal(1, result.Unresolved);
        Assert.Contains("[[concepts/my-target]]", result.Content, StringComparison.Ordinal);
        Assert.Contains("[[concepts/note]]", result.Content, StringComparison.Ordinal);

        // A link that resolves to nothing stays exactly as the author wrote it.
        Assert.Contains("[[nowhere]]", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizingKeepsAliasesAnchorsAndEmbedMarkers()
    {
        const string Content = "![[My Target#Section|300]] and [[My Target|the target]]\n";

        VaultSnapshot vault = WriteVault(
            ("page.md", Content),
            ("concepts/my-target.md", "# My Target\n\n## Section\n"));

        VaultDocument document = vault.Index.ByRelativePath("page.md").Single();
        NormalizeResult result = MarkdownNormalizer.Normalize(
            Content, document, vault.CreateResolver().ResolveAll(document));

        Assert.Contains("![[concepts/my-target#Section|300]]", result.Content, StringComparison.Ordinal);
        Assert.Contains("[[concepts/my-target|the target]]", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizingAMarkdownLinkEscapesWhatAUrlMust()
    {
        const string Content = "See [the target](My%20Target).\n";

        VaultSnapshot vault = WriteVault(
            ("page.md", Content),
            ("concepts/my target.md", "# My Target\n"));

        VaultDocument document = vault.Index.ByRelativePath("page.md").Single();
        NormalizeResult result = MarkdownNormalizer.Normalize(
            Content, document, vault.CreateResolver().ResolveAll(document));

        Assert.Contains("(concepts/my%20target.md)", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void NoteRelativeFormWalksUpOutOfTheFolder()
    {
        Assert.Equal("../concepts/a.md", MarkdownNormalizer.RelativePath("notes", "concepts/a.md"));
        Assert.Equal("a.md", MarkdownNormalizer.RelativePath("notes", "notes/a.md"));
        Assert.Equal("deep/a.md", MarkdownNormalizer.RelativePath("notes", "notes/deep/a.md"));
        Assert.Equal("notes/a.md", MarkdownNormalizer.RelativePath(string.Empty, "notes/a.md"));
        Assert.Equal("../../a.md", MarkdownNormalizer.RelativePath("one/two", "a.md"));
    }

    [Fact]
    public void NormalizingLeavesExternalLinksAlone()
    {
        const string Content = "See [docs](https://example.com/docs) and [[note]].\n";

        VaultSnapshot vault = WriteVault(("page.md", Content), ("wiki/note.md", "# Note\n"));

        VaultDocument document = vault.Index.ByRelativePath("page.md").Single();
        NormalizeResult result = MarkdownNormalizer.Normalize(
            Content, document, vault.CreateResolver().ResolveAll(document));

        Assert.Contains("https://example.com/docs", result.Content, StringComparison.Ordinal);
        Assert.Contains("[[wiki/note]]", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizedTextStillResolvesToTheSameFiles()
    {
        // The round trip that matters: rewriting the vault must not lose a link.
        VaultSnapshot vault = WriteVault(
            ("page.md", "See [[My Target]], [[note]] and [Doc](My%20Target).\n"),
            ("concepts/my target.md", "# My Target\n"),
            ("concepts/note.md", "# Note\n"));

        VaultDocument document = vault.Index.ByRelativePath("page.md").Single();
        LinkResolver resolver = vault.CreateResolver();

        List<LinkResolution> before = [.. resolver.ResolveAll(document)];
        NormalizeResult normalized = MarkdownNormalizer.Normalize(
            File.ReadAllText(document.AbsolutePath), document, before);

        File.WriteAllText(document.AbsolutePath, normalized.Content);

        VaultSnapshot rescanned = VaultScanner.Scan(_root);
        VaultDocument rewritten = rescanned.Index.ByRelativePath("page.md").Single();

        List<LinkResolution> after = [.. rescanned.CreateResolver().ResolveAll(rewritten)];

        Assert.Equal(
            before.Select(r => r.Target?.RelativePath),
            after.Select(r => r.Target?.RelativePath));

        // And every one of them now resolves by exact path rather than by a fallback.
        Assert.All(after, r => Assert.Equal(ResolutionRule.ExactVaultPath, r.Rule));
    }

    private NoteDraft Draft(VaultSnapshot vault, string sourcePath)
    {
        VaultDocument source = vault.Index.ByRelativePath(sourcePath).Single();

        LinkResolution unresolved = vault.CreateResolver()
            .ResolveAll(source)
            .First(r => r.IsUnresolved);

        NoteDraft? draft = NoteFactory.Draft(vault, unresolved, new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.NotNull(draft);

        return draft;
    }

    private EditSession OpenFile(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        File.WriteAllText(path, content);

        EditSession? session = DocumentEditor.Open(
            new VaultDocument
            {
                RelativePath = relativePath,
                AbsolutePath = path,
                Stem = Path.GetFileNameWithoutExtension(relativePath),
                Extension = ".md",
                DirectoryPath = string.Empty,
            });

        Assert.NotNull(session);

        return session;
    }

    private VaultSnapshot WriteVault(params (string Path, string Content)[] files)
    {
        foreach ((string path, string content) in files)
        {
            string full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        return VaultScanner.Scan(_root);
    }
}
