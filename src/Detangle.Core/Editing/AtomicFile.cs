namespace Detangle.Core.Editing;

/// <summary>
/// Writes files by replacing them.
/// <para>
/// Every write in this app goes through here: a reader's own notes are the one thing
/// Detangle cannot afford to damage, and a crash or a full disk part-way through an
/// in-place write leaves a vault holding half a document. The temporary file is written
/// first and then moved over the original, which on every platform this ships to is the
/// closest thing to an atomic replace the filesystem offers.
/// </para>
/// </summary>
public static class AtomicFile
{
    /// <summary>The suffix the in-flight temporary file carries.</summary>
    public const string TemporarySuffix = ".detangle-tmp";

    /// <summary>Writes text to a path, replacing whatever was there.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="content">The new content.</param>
    /// <returns>Null on success, or why it failed.</returns>
    public static string? Write(string path, string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string temporary = path + TemporarySuffix;

        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Discard(temporary);

            return ex.Message;
        }
    }

    /// <summary>True when a path is one of this app's in-flight temporary files.</summary>
    public static bool IsTemporary(string path) =>
        path.EndsWith(TemporarySuffix, StringComparison.Ordinal);

    private static void Discard(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The original file is untouched either way; a stray temporary is not worth
            // failing a save the caller already knows failed.
        }
    }
}
