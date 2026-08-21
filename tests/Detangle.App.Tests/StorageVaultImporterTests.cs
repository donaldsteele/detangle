using System.Text;
using Detangle.App;
using Detangle.Core.Vault;
using Xunit;

namespace Detangle.App.Tests;

/// <summary>
/// Tests for opening a folder that has no path.
/// <para>
/// A browser hands back a folder it will read for you rather than a location on disk, and
/// three separate features have now failed the same way on it: the reader granted
/// permission and nothing happened at all. The picker itself is a native dialog no test
/// can drive, so what is tested here is the part that was actually wrong — which files are
/// taken in, which are skipped, and what happens when one of them will not open.
/// </para>
/// </summary>
public class StorageVaultImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "detangle-import-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AFolderIsCopiedDeeplyEnoughToScan()
    {
        StorageVaultImporter.Result result = await Import(
            File("index.md", "# Index\n\n[[notes/alpha]]"),
            Folder("notes", File("alpha.md", "# Alpha")));

        Assert.Equal(2, result.Files);
        Assert.False(result.Truncated);

        // The point of the copy is that the scanner can then walk it, so that is what is
        // asserted rather than a file count that proves only that bytes moved.
        VaultSnapshot vault = VaultScanner.Scan(_root);

        Assert.Equal(2, vault.Documents.Count(d => d.IsMarkdown));
        Assert.Contains(vault.Documents, d => d.RelativePath == "notes/alpha.md");
    }

    [Fact]
    public async Task FilesThatAreNotPartOfAWikiAreLeftBehind()
    {
        StorageVaultImporter.Result result = await Import(
            File("page.md", "# Page"),
            File("video.mp4", "not really a video"),
            File("program.exe", "not really a program"));

        Assert.Equal(1, result.Files);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "page.md")));
        Assert.False(System.IO.File.Exists(Path.Combine(_root, "video.mp4")));
    }

    [Fact]
    public async Task TheDirectoriesTheScannerIgnoresAreNotEvenCopied()
    {
        StorageVaultImporter.Result result = await Import(
            File("page.md", "# Page"),
            Folder("node_modules", File("readme.md", "# Dependency")),
            Folder(".git", File("config.json", "{}")));

        Assert.Equal(1, result.Files);
        Assert.False(Directory.Exists(Path.Combine(_root, "node_modules")));
    }

    [Fact]
    public async Task AnUnreadableFileDoesNotAbandonTheFolder()
    {
        StorageVaultImporter.Result result = await Import(
            File("good.md", "# Good"),
            Unreadable("bad.md"),
            File("also-good.md", "# Also good"));

        Assert.Equal(2, result.Files);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "also-good.md")));
    }

    [Fact]
    public async Task AnEmptyFolderReportsNothingRatherThanFailing()
    {
        StorageVaultImporter.Result result = await Import();

        Assert.Equal(0, result.Files);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task ImagesComeAcrossBecausePagesEmbedThem()
    {
        StorageVaultImporter.Result result = await Import(
            File("page.md", "![](diagram.png)"),
            File("diagram.png", "pretend PNG"));

        Assert.Equal(2, result.Files);
    }

    [Fact]
    public async Task AFolderThatCannotBeListedSaysWhy()
    {
        // A browser throws its own interop exception type, which is none of the framework's
        // IO exceptions. Swallowing it made granting permission to a folder look exactly
        // like picking an empty one: nothing happened and nothing was said.
        StorageVaultImporter.Result result = await StorageVaultImporter.ImportAsync(
            () => throw new InvalidOperationException("the folder handle went away"), _root);

        Assert.Equal(0, result.Files);
        Assert.NotNull(result.Failure);
        Assert.Contains("the folder handle went away", result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFolderThatIsMerelyEmptyBlamesNobody()
    {
        StorageVaultImporter.Result result = await Import();

        Assert.Equal(0, result.Files);
        Assert.Null(result.Failure);
    }

    private Task<StorageVaultImporter.Result> Import(params StorageVaultImporter.Entry[] entries) =>
        StorageVaultImporter.ImportAsync(() => Enumerate(entries), _root);

    private static StorageVaultImporter.Entry File(string name, string content) =>
        new(name, () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content))), null);

    private static StorageVaultImporter.Entry Unreadable(string name) =>
        new(name, () => throw new IOException($"{name} cannot be read."), null);

    private static StorageVaultImporter.Entry Folder(string name, params StorageVaultImporter.Entry[] children) =>
        new(name, null, () => Enumerate(children));

    private static async IAsyncEnumerable<StorageVaultImporter.Entry> Enumerate(
        StorageVaultImporter.Entry[] entries)
    {
        foreach (StorageVaultImporter.Entry entry in entries)
        {
            yield return entry;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
