using System.Text.Json;
using Detangle.Core.Linking;
using Detangle.Core.Vault;

namespace Detangle.App;

/// <summary>
/// The disambiguation choices a reader has made in one vault (plan.md section 15.2).
/// <para>
/// The resolver has always been able to take these — <see cref="LinkResolver"/> short
/// circuits on one before running the chain, and <see cref="ResolutionRule.RememberedChoice"/>
/// has an explanation string waiting for it — and nothing has ever supplied any. This is
/// the missing half: the only point in the whole chain where the reader's judgment
/// outranks the rules.
/// </para>
/// <para>
/// Choices live in <c>.detangle/choices.json</c> beside the search cache, which the
/// scanner already ignores. A vault that cannot be written to — the browser build's
/// detached copy, a read-only share — still remembers choices for as long as the tab is
/// open, and says so rather than claiming a save that will not survive.
/// </para>
/// </summary>
internal sealed class ChoiceStore
{
    private readonly Dictionary<string, string> _choices;
    private readonly string? _path;

    private ChoiceStore(Dictionary<string, string> choices, string? path)
    {
        _choices = choices;
        _path = path;
    }

    /// <summary>A store with nothing in it that can never be written. Used before a vault is open.</summary>
    public static ChoiceStore Empty { get; } = new([], null);

    /// <summary>The choices, in the form the resolver takes them.</summary>
    public IReadOnlyDictionary<string, string> Choices => _choices;

    /// <summary>True when a choice made now will still be here next time.</summary>
    public bool IsPersistent => _path is not null;

    /// <summary>
    /// Opens the store for a vault. Never throws: a store that cannot be read is an empty
    /// one, because failing to open a vault over a sidecar file nobody asked for would be
    /// a worse answer than forgetting some choices.
    /// </summary>
    /// <param name="vaultRoot">The vault's root directory, or null when there is no local one.</param>
    public static ChoiceStore Open(string? vaultRoot)
    {
        if (vaultRoot is not { Length: > 0 })
        {
            return new ChoiceStore([], null);
        }

        string path = Path.Combine(vaultRoot, ".detangle", "choices.json");

        return new ChoiceStore(Read(path), path);
    }

    /// <summary>
    /// Records which document a link was meant to reach, and returns true when the record
    /// will outlive the session.
    /// </summary>
    /// <param name="source">The document the link was written in.</param>
    /// <param name="rawTarget">The link's target, exactly as written.</param>
    /// <param name="chosen">The document the reader picked.</param>
    public bool Remember(VaultDocument source, string rawTarget, VaultDocument chosen)
    {
        _choices[LinkResolver.ChoiceKey(source.DirectoryPath, rawTarget)] = chosen.RelativePath;

        return Write();
    }

    /// <summary>Forgets one choice, so the chain decides again.</summary>
    public bool Forget(VaultDocument source, string rawTarget)
    {
        _choices.Remove(LinkResolver.ChoiceKey(source.DirectoryPath, rawTarget));

        return Write();
    }

    private static Dictionary<string, string> Read(string path)
    {
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(path))
            {
                return choices;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return choices;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { Length: > 0 } target)
                {
                    choices[property.Name] = target;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A truncated, foreign or unreadable file means no choices, not a broken
            // vault. JsonDocument is used rather than the serializer on purpose: the
            // browser head publishes at TrimMode=full, where reflection-based
            // deserialization fails the build.
            choices.Clear();
        }

        return choices;
    }

    private bool Write()
    {
        if (_path is not { } path)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();

                // Ordinal order so the file does not churn between saves; it sits in a
                // vault someone may well have under version control.
                foreach ((string key, string value) in _choices.OrderBy(c => c.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(key, value);
                }

                writer.WriteEndObject();
            }

            // Written whole and moved into place: a half-written choices file read on the
            // next launch would silently drop every choice after the truncation.
            string temporary = path + ".tmp";

            File.WriteAllBytes(temporary, stream.ToArray());
            File.Move(temporary, path, overwrite: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
