namespace Detangle.Core.Vault;

/// <summary>
/// The wiki formats Detangle can recognise on disk. Each flavor selects which
/// steps of the link-resolution chain are enabled and in what priority, plus the
/// display-name rule, callout dialect, and navigation source.
/// See plan.md sections 3.1 and 5.7.
/// </summary>
public enum VaultFlavor
{
    /// <summary>No format markers found; every resolution step is enabled.</summary>
    Generic,

    /// <summary>Karpathy-pattern LLM Wiki: wiki/{sources,entities,concepts,synthesis} plus raw/.</summary>
    LlmWiki,

    /// <summary>Obsidian vault, identified by a .obsidian directory.</summary>
    Obsidian,

    /// <summary>Foam workspace.</summary>
    Foam,

    /// <summary>Dendron: flat directory, dot-hierarchy filenames.</summary>
    Dendron,

    /// <summary>Logseq: pages/, journals/, logseq/config.edn.</summary>
    Logseq,

    /// <summary>Quartz static site.</summary>
    Quartz,

    /// <summary>Zettelkasten: flat directory, timestamp or Folgezettel filename IDs.</summary>
    Zettelkasten,

    /// <summary>MkDocs or MkDocs Material, identified by mkdocs.yml.</summary>
    MkDocs,

    /// <summary>Docusaurus, identified by docusaurus.config.*.</summary>
    Docusaurus,

    /// <summary>GitBook, identified by SUMMARY.md with README.md folder indexes.</summary>
    GitBook,

    /// <summary>docsify, identified by _sidebar.md.</summary>
    Docsify,

    /// <summary>mdBook, identified by book.toml plus src/SUMMARY.md.</summary>
    MdBook,

    /// <summary>DeepWiki export, identified by .devin/wiki.json.</summary>
    DeepWiki,
}
