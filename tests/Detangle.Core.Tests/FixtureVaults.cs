using System.Collections.Concurrent;
using Detangle.Core.Vault;

namespace Detangle.Core.Tests;

/// <summary>
/// Locates and caches the fixture vaults under tests/fixtures. Scanning is not free and
/// every test in the suite wants the same snapshots, so each vault is scanned once per
/// test run.
/// </summary>
public static class FixtureVaults
{
    private static readonly ConcurrentDictionary<string, VaultSnapshot> Cache = new(StringComparer.Ordinal);

    /// <summary>The names of the thirteen format fixtures plus the torture vault.</summary>
    public static readonly string[] All =
    [
        "llm-wiki", "obsidian", "foam", "dendron", "logseq", "quartz", "zettelkasten",
        "mkdocs", "docusaurus", "gitbook", "docsify", "mdbook", "deepwiki", "torture",
    ];

    /// <summary>Absolute path to tests/fixtures, found by walking up from the test binary.</summary>
    public static string FixturesRoot { get; } = FindFixturesRoot();

    /// <summary>Scans a fixture vault by directory name, caching the result.</summary>
    public static VaultSnapshot Load(string name) =>
        Cache.GetOrAdd(name, static key => VaultScanner.Scan(Path.Combine(FixturesRoot, "vaults", key)));

    /// <summary>Absolute path to a fixture vault directory.</summary>
    public static string PathTo(string name) => Path.Combine(FixturesRoot, "vaults", name);

    private static string FindFixturesRoot()
    {
        // The test binary sits under tests/<project>/bin/<config>/<tfm>, and the depth of
        // that path is a build detail — walking up for the marker directory survives it.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"tests/fixtures was not found above {AppContext.BaseDirectory}.");
    }
}
