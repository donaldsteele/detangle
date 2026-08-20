namespace Detangle.Core.Linking;

/// <summary>
/// The ordered fallback chain from plan.md section 5.3. The resolver stops at the
/// first step that hits and records which one fired, because that provenance is the
/// product: the UI decorates the link by rule and the Link Doctor groups by it.
/// Numeric values match the step numbers in the plan and are part of the sidecar
/// database schema, so they must not be renumbered.
/// </summary>
public enum ResolutionRule
{
    /// <summary>Not resolved and not attempted — external or self-referential links.</summary>
    NotAttempted = 0,

    /// <summary>1. Exact vault-relative path, with or without the extension.</summary>
    ExactVaultPath = 1,

    /// <summary>2. Exact note-relative path ("./", "../", or bare beside the linking file).</summary>
    NoteRelativePath = 2,

    /// <summary>3. Exact filename stem, case-sensitive and unique in the vault.</summary>
    CaseSensitiveStem = 3,

    /// <summary>4. Path-suffix match — Foam's minimum-identifier rule, handles [[folder/note]].</summary>
    PathSuffix = 4,

    /// <summary>5. Case-insensitive stem match.</summary>
    CaseInsensitiveStem = 5,

    /// <summary>6. Normalized N() match — absorbs separator, encoding and case drift at once.</summary>
    NormalizedName = 6,

    /// <summary>7. Alias, frontmatter title, or first H1.</summary>
    Alias = 7,

    /// <summary>8. Identifier — frontmatter id, or a filename prefix match for Zettel timestamps.</summary>
    Identifier = 8,

    /// <summary>9. Folder index — index.md, README.md, readme.md, _index.md, &lt;foldername&gt;.md.</summary>
    FolderIndex = 9,

    /// <summary>10. Encoding variants — Logseq "___", "%2F", legacy "." to "/", Dendron dot-paths.</summary>
    EncodingVariant = 10,

    /// <summary>11. Extension probe — basename search anywhere in the vault, for attachments.</summary>
    ExtensionProbe = 11,

    /// <summary>
    /// 12. Fuzzy nearest match. Offered as a suggestion in the UI only; the resolver
    /// never navigates on this rule by itself.
    /// </summary>
    FuzzyNearest = 12,

    /// <summary>13. Nothing matched: a placeholder with a "create note" affordance.</summary>
    Placeholder = 13,

    /// <summary>
    /// A disambiguation the user made previously, replayed from the sidecar database.
    /// It outranks the whole chain (plan.md section 5.4).
    /// </summary>
    RememberedChoice = 14,
}

/// <summary>
/// How much decoration a resolved link earns in the reader (plan.md section 5.5).
/// Derived from the rule that fired, so the UI never has to know the chain.
/// </summary>
public enum ResolutionConfidence
{
    /// <summary>Steps 1-3: the link was written correctly. No decoration.</summary>
    Exact,

    /// <summary>Steps 4-8: matched after normalization. Dotted underline with a hover explanation.</summary>
    Normalized,

    /// <summary>Steps 9-11: matched by a structural or encoding rule. Dotted underline plus icon.</summary>
    Heuristic,

    /// <summary>Step 12: a suggestion only; never navigated to automatically.</summary>
    Suggestion,

    /// <summary>Step 13: unresolved.</summary>
    Unresolved,
}
