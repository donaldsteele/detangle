using System.Text.Json;

namespace Detangle.Core.History;

/// <summary>
/// The marked baseline, as a file beside the vault (plan.md section 15.4).
/// <para>
/// One file, read and written by every head. The panel's "Mark this state" button and the
/// CLI's <c>--mark</c> have to mean the same thing, or a team gets a gate in CI that
/// disagrees with the application the person who wrote the wiki is looking at.
/// </para>
/// <para>
/// It sits at the vault root rather than in <c>.detangle/</c>, which the FAQ describes as
/// a cache the reader may delete at any time. A baseline is the opposite of a cache: it is
/// the thing a repository commits so that the next generation run can be measured against
/// it, and it belongs where <c>git status</c> will show it.
/// </para>
/// </summary>
public static class BaselineStore
{
    /// <summary>The file's name at the vault root.</summary>
    public const string FileName = ".detangle-baseline.json";

    /// <summary>Where the baseline lives for a vault.</summary>
    public static string PathFor(string vaultRoot) => Path.Combine(vaultRoot, FileName);

    /// <summary>
    /// Reads the baseline for a vault, or an empty record when there is none. Never
    /// throws: a baseline that cannot be read costs a comparison, not a vault.
    /// </summary>
    /// <param name="vaultRoot">The vault's root directory.</param>
    public static VaultSnapshotRecord Load(string vaultRoot)
    {
        try
        {
            string path = PathFor(vaultRoot);

            if (!File.Exists(path))
            {
                return VaultSnapshotRecord.Empty;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));

            // JsonDocument rather than the serializer, as everywhere else that reads a
            // sidecar: the browser head publishes at TrimMode=full, where reflection-based
            // deserialization fails the build.
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("baseline", out JsonElement baseline)
                    ? VaultSnapshotRecord.Read(baseline)
                    : VaultSnapshotRecord.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return VaultSnapshotRecord.Empty;
        }
    }

    /// <summary>Writes the baseline, and returns true when it will still be there next time.</summary>
    /// <param name="vaultRoot">The vault's root directory.</param>
    /// <param name="record">The state to measure later runs against.</param>
    public static bool Save(string vaultRoot, VaultSnapshotRecord record)
    {
        try
        {
            string path = PathFor(vaultRoot);

            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schema", SchemaVersion);
                record.Write(writer, "baseline");
                writer.WriteEndObject();
            }

            // Written whole and moved into place: this file is committed, and a half-written
            // one read on the next run would silently compare against a truncated vault.
            string temporary = path + ".tmp";

            File.WriteAllBytes(temporary, stream.ToArray());
            File.Move(temporary, path, overwrite: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>The file's schema version, so a consumer can tell what it is reading.</summary>
    public const int SchemaVersion = 1;
}
