using System.Text.Json;
using Detangle.Core.Vault;

namespace Detangle.App;

/// <summary>A vault this reader has opened before.</summary>
/// <param name="Path">The folder, as it was opened.</param>
/// <param name="Flavor">The wiki convention that was detected, for the second line.</param>
/// <param name="OpenedAt">When it was last opened, which is what orders the list.</param>
public sealed record RecentVault(string Path, string Flavor, DateTimeOffset OpenedAt)
{
    /// <summary>The folder's own name, which is what a reader recognises.</summary>
    public string Name
    {
        get
        {
            string trimmed = System.IO.Path.TrimEndingDirectorySeparator(Path);

            return System.IO.Path.GetFileName(trimmed) is { Length: > 0 } name ? name : trimmed;
        }
    }

    /// <summary>False when the folder is not there any more.</summary>
    public bool Exists => Directory.Exists(Path);
}

/// <summary>
/// What this reader has set up, kept in their own configuration directory rather than in
/// any vault.
/// <para>
/// Deliberately not beside a vault: the whole point of the recent list is to be readable
/// when no vault is open, and the vault you no longer have in front of you is exactly the
/// one you need listed.
/// </para>
/// <para>
/// Never throws. A settings file that cannot be read is a reader who has to type a path
/// once, not an application that will not start.
/// </para>
/// </summary>
public sealed class AppSettings
{
    /// <summary>How many vaults the list remembers.</summary>
    public const int RecentLimit = 10;

    private readonly List<RecentVault> _recent;
    private readonly string? _path;

    private AppSettings(List<RecentVault> recent, string? path)
    {
        _recent = recent;
        _path = path;
    }

    /// <summary>Settings that remember nothing, for a head with nowhere to write.</summary>
    public static AppSettings None { get; } = new([], null);

    /// <summary>Where the file lives for this reader.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        "detangle",
        "settings.json");

    /// <summary>The vaults opened before, most recent first.</summary>
    public IReadOnlyList<RecentVault> Recent => _recent;

    /// <summary>Reads the settings, or empty ones.</summary>
    /// <param name="path">The file to read; defaults to this reader's own.</param>
    public static AppSettings Open(string? path = null)
    {
        string file = path ?? DefaultPath;
        var recent = new List<RecentVault>();

        try
        {
            if (File.Exists(file))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(file));

                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("recent", out JsonElement stored)
                    && stored.ValueKind == JsonValueKind.Array)
                {
                    recent.AddRange(stored.EnumerateArray().Select(Read).OfType<RecentVault>());
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or PathTooLongException)
        {
            recent.Clear();
        }

        return new AppSettings(recent, file);
    }

    /// <summary>
    /// Records that a vault was opened, moving it to the top of the list.
    /// </summary>
    /// <param name="vault">The vault that was opened.</param>
    /// <param name="openedAt">When, so the caller decides rather than the clock.</param>
    public bool Remember(VaultSnapshot vault, DateTimeOffset openedAt)
    {
        // A store with nowhere to write records nothing, rather than building a list in
        // memory that will never be read: the value of this list is entirely in surviving
        // the session, and None is a shared instance that must not accumulate state.
        if (_path is null)
        {
            return false;
        }

        // Compared as paths rather than as strings: opening the same folder by a slightly
        // different spelling should move the entry, not add a second one.
        _recent.RemoveAll(r => string.Equals(
            Path.TrimEndingDirectorySeparator(r.Path),
            Path.TrimEndingDirectorySeparator(vault.RootPath),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

        _recent.Insert(0, new RecentVault(vault.RootPath, vault.Profile.Flavor.ToString(), openedAt));

        if (_recent.Count > RecentLimit)
        {
            _recent.RemoveRange(RecentLimit, _recent.Count - RecentLimit);
        }

        return Write();
    }

    /// <summary>Takes one vault off the list.</summary>
    /// <param name="vault">The entry to forget.</param>
    public bool Forget(RecentVault vault)
    {
        _recent.RemoveAll(r => string.Equals(r.Path, vault.Path, StringComparison.Ordinal));

        return Write();
    }

    private static RecentVault? Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || Text(element, "path") is not { Length: > 0 } path)
        {
            return null;
        }

        DateTimeOffset opened = DateTimeOffset.TryParse(
            Text(element, "openedAt"),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
                ? parsed
                : DateTimeOffset.MinValue;

        return new RecentVault(path, Text(element, "flavor"), opened);

        static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
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
                writer.WriteStartArray("recent");

                foreach (RecentVault vault in _recent)
                {
                    writer.WriteStartObject();
                    writer.WriteString("path", vault.Path);
                    writer.WriteString("flavor", vault.Flavor);
                    writer.WriteString("openedAt", vault.OpenedAt.ToString("O"));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

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
