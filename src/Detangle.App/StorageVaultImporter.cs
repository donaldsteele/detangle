using Detangle.Core.Vault;

namespace Detangle.App;

/// <summary>
/// Copies a folder chosen through a file picker into a directory the scanner can walk.
/// <para>
/// A browser hands back a folder it will read on your behalf, not a path: its
/// <c>TryGetLocalPath</c> is null. The reader granted permission and then nothing happened,
/// because the shell had nowhere to point the scanner. Copying the folder in is what makes
/// the browser build able to open a wiki at all.
/// </para>
/// <para>
/// It stays a copy. Nothing is uploaded — the bytes go to the runtime's own in-memory
/// filesystem — and nothing is written back, which is why the shell marks such a vault as
/// detached and refuses to save into it.
/// </para>
/// </summary>
public static class StorageVaultImporter
{
    /// <summary>
    /// Enough for a large wiki and not enough to exhaust a browser tab. A folder chosen
    /// from a picker can be anything, including a home directory.
    /// </summary>
    public const int MaxFiles = 5000;

    /// <summary>Largest total taken in.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>Largest single file; a wiki's pages are far below this.</summary>
    public const long MaxFileBytes = 8L * 1024 * 1024;

    /// <summary>What an import took in.</summary>
    /// <param name="Files">How many files were copied.</param>
    /// <param name="Bytes">How many bytes they came to.</param>
    /// <param name="Truncated">True when a limit stopped the copy early.</param>
    public sealed record Result(int Files, long Bytes, bool Truncated);

    /// <summary>
    /// One thing inside a picked folder: a file that can be opened, or a folder that can
    /// be listed.
    /// <para>
    /// Avalonia's storage interfaces are sealed against implementation outside the
    /// framework, so a test cannot present a folder to this. Taking its own shape instead
    /// keeps the part that was actually broken - which files are taken, which are skipped,
    /// and what happens when one will not open - testable without a file picker.
    /// </para>
    /// </summary>
    /// <param name="Name">The file or folder name, without a path.</param>
    /// <param name="Open">Opens a file for reading, or null for a folder.</param>
    /// <param name="Children">Lists a folder's contents, or null for a file.</param>
    public sealed record Entry(
        string Name,
        Func<Task<Stream>>? Open,
        Func<IAsyncEnumerable<Entry>>? Children)
    {
        /// <summary>True when this entry is a folder to descend into.</summary>
        public bool IsFolder => Children is not null;
    }

    /// <summary>Copies everything readable under <paramref name="entries"/> into <paramref name="root"/>.</summary>
    /// <param name="entries">Lists the picked folder's contents.</param>
    /// <param name="root">A directory to copy into; created if missing.</param>
    public static async Task<Result> ImportAsync(Func<IAsyncEnumerable<Entry>> entries, string root)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        Directory.CreateDirectory(root);

        var state = new State();

        await CopyFolder(entries, root, state).ConfigureAwait(false);

        return new Result(state.Files, state.Bytes, state.Truncated);
    }

    /// <summary>
    /// The files worth carrying in: the wiki itself, the configuration that says which
    /// flavor it is, and the pictures its pages embed.
    /// </summary>
    public static bool IsReadable(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is
            ".md" or ".markdown" or ".mdx" or ".txt"
            or ".yml" or ".yaml" or ".json" or ".toml"
            or ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp";

    /// <summary>
    /// True for a directory the scanner would skip anyway. Copying node_modules into
    /// memory to then not scan it would be the slowest possible way to read nothing.
    /// </summary>
    public static bool IsIgnored(string name) =>
        VaultScanOptions.Default.IgnoredDirectories.Contains(name);

    private static async Task CopyFolder(Func<IAsyncEnumerable<Entry>> entries, string destination, State state)
    {
        List<Entry> items;

        try
        {
            var collected = new List<Entry>();

            await foreach (Entry entry in entries().ConfigureAwait(false))
            {
                collected.Add(entry);
            }

            items = collected;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return;
        }

        foreach (Entry entry in items)
        {
            if (state.IsFull)
            {
                state.Truncated = true;

                return;
            }

            if (entry is { IsFolder: false, Open: { } open } && IsReadable(entry.Name))
            {
                await CopyFile(open, Path.Combine(destination, entry.Name), state).ConfigureAwait(false);
            }
            else if (entry is { Children: { } children } && !IsIgnored(entry.Name))
            {
                string nested = Path.Combine(destination, entry.Name);

                Directory.CreateDirectory(nested);
                await CopyFolder(children, nested, state).ConfigureAwait(false);
            }
        }
    }

    private static async Task CopyFile(Func<Task<Stream>> open, string destination, State state)
    {
        try
        {
            await using Stream source = await open().ConfigureAwait(false);

            if (source.CanSeek && source.Length > MaxFileBytes)
            {
                return;
            }

            long written;

            await using (Stream target = File.Create(destination))
            {
                await source.CopyToAsync(target).ConfigureAwait(false);

                written = target.Length;
            }

            state.Files++;
            state.Bytes += written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // One unreadable file is not a reason to abandon the folder.
        }
    }

    private sealed class State
    {
        internal int Files { get; set; }

        internal long Bytes { get; set; }

        internal bool Truncated { get; set; }

        internal bool IsFull => Files >= MaxFiles || Bytes >= MaxBytes;
    }
}
