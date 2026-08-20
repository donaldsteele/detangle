using Detangle.Core.Linking;

namespace Detangle.Core.Vault;

/// <summary>
/// What a detected flavor changes about resolution (plan.md section 5.7). A profile is
/// data, not behaviour: the resolver reads it, so a user override from the status bar
/// is a value swap rather than a code path.
/// </summary>
public sealed class VaultProfile
{
    /// <summary>Folder-index candidates in the order section 5.3 step 9 accepts them.</summary>
    public static readonly string[] DefaultFolderIndexNames =
        ["index.md", "README.md", "readme.md", "_index.md"];

    /// <summary>The detected or user-selected flavor.</summary>
    public required VaultFlavor Flavor { get; init; }

    /// <summary>
    /// Which chain steps run. Generic enables everything; narrower flavors switch off
    /// the steps that would produce false positives for their own conventions.
    /// </summary>
    public required IReadOnlySet<ResolutionRule> EnabledRules { get; init; }

    /// <summary>Dendron shows the frontmatter title rather than the dot-path filename.</summary>
    public bool PreferTitleAsDisplayName { get; init; }

    /// <summary>
    /// Logseq escapes "/" in page titles as "___" (older vaults use "%2F" or "."), so
    /// its filenames must be de-escaped before they read as page names.
    /// </summary>
    public bool DecodeLogseqFilenames { get; init; }

    /// <summary>
    /// Dendron filenames are dot hierarchies, so the final dot segment is part of the
    /// name and must never be treated as an extension.
    /// </summary>
    public bool DotPathHierarchy { get; init; }

    /// <summary>Zettelkasten links carry a timestamp prefix of the filename, not the whole stem.</summary>
    public bool IdentifierPrefixMatch { get; init; }

    /// <summary>Folder-index filenames, most preferred first.</summary>
    public IReadOnlyList<string> FolderIndexNames { get; init; } = DefaultFolderIndexNames;

    /// <summary>True when this profile was chosen by the user rather than sniffed.</summary>
    public bool IsUserOverride { get; init; }

    /// <summary>Every rule in the chain. Steps 12 and 13 are terminal and always available.</summary>
    private static IReadOnlySet<ResolutionRule> AllRules { get; } =
        new HashSet<ResolutionRule>(Enum.GetValues<ResolutionRule>());

    /// <summary>Builds the default profile for a flavor.</summary>
    public static VaultProfile For(VaultFlavor flavor) => flavor switch
    {
        VaultFlavor.Dendron => new VaultProfile
        {
            Flavor = flavor,
            EnabledRules = AllRules,
            PreferTitleAsDisplayName = true,
            DotPathHierarchy = true,
            FolderIndexNames = ["index.md", "README.md", "readme.md", "_index.md"],
        },

        VaultFlavor.Logseq => new VaultProfile
        {
            Flavor = flavor,
            EnabledRules = AllRules,
            DecodeLogseqFilenames = true,
        },

        VaultFlavor.Zettelkasten => new VaultProfile
        {
            Flavor = flavor,
            EnabledRules = AllRules,
            IdentifierPrefixMatch = true,
        },

        VaultFlavor.GitBook => new VaultProfile
        {
            Flavor = flavor,
            EnabledRules = AllRules,
            // GitBook's folder index is README.md; index.md has no special meaning there.
            FolderIndexNames = ["README.md", "readme.md", "index.md", "_index.md"],
        },

        VaultFlavor.MkDocs or VaultFlavor.MdBook or VaultFlavor.Docusaurus or VaultFlavor.Docsify =>
            new VaultProfile
            {
                Flavor = flavor,
                EnabledRules = AllRules,
                FolderIndexNames = ["index.md", "README.md", "readme.md", "_index.md"],
            },

        _ => new VaultProfile
        {
            Flavor = flavor,
            EnabledRules = AllRules,
        },
    };

    /// <summary>True when a chain step is enabled for this vault.</summary>
    public bool IsEnabled(ResolutionRule rule) => EnabledRules.Contains(rule);
}
