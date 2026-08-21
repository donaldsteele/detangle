---
title: Formats
type: guide
updated: 2026-08-20
---

# Formats

Detangle sniffs what kind of wiki a folder is before it reads it, because the conventions
genuinely differ — the same link text means different things in different tools.

## What is recognised

| Convention | Detected by | What changes |
|---|---|---|
| Obsidian | `.obsidian/` | Wikilinks, `^blockid` anchors, `\|` aliases and sizes |
| Logseq | `logseq/` | `key:: value` properties, `((uuid))` block references, `#tag` as a page |
| MkDocs | `mkdocs.yml` | Navigation read from the config; `!!!` admonitions |
| Docusaurus | `docusaurus.config.*` | `slug:` overrides the path; `sidebars.js` orders the tree |
| Dendron | `dendron.yml` | Dot hierarchies — `a.b.c.md` is one filename, not an extension |
| Foam | `.vscode/foam.json` | Wikilinks over a VS Code workspace |
| Quartz | `quartz.config.ts` | Obsidian conventions, published |
| Hugo | `config.toml`, `hugo.toml` | TOML frontmatter, `content/` roots, page bundles |
| Jekyll | `_config.yml` | `_posts/` date-prefixed filenames |
| MDBook | `book.toml` | `SUMMARY.md` is the navigation |
| Zettelkasten | `NNNNNNNNNNNN-` filenames | Identifier links |
| LLM Wiki | `wiki/` beside `raw/` | Frontmatter references treated as links |
| Plain markdown | nothing in particular | Everything above, best-effort |

## Flavor profiles

A profile decides which chain steps are enabled and which syntaxes are parsed. Dendron
prefers the frontmatter title over its dot-path filename; Logseq treats a `#tag` as a link
to a page; Hugo reads TOML between `+++` fences.

The detected flavor is shown in the status bar. Nothing is hidden behind it: if the
sniffer is wrong, the worst case is that a rule you did not need was enabled.

## Frontmatter

Four delimiter styles are read — YAML between `---`, TOML between `+++`, JSON between
`;;;`, and Logseq's `key:: value` lines — and their keys are folded into one set, because
the same concept appears under half a dozen spellings across these tools.

`aliases`/`alias`/`aka` become one list. `tags`/`tag`/`keywords`/`categories` become one
list. `updated`/`modified`/`last_modified_at` become one date. `sources`/`related`/`links`/
`see-also`/`refs`/`parent`/`up` become links — those are the ones other viewers drop,
which is why their graphs are smaller than the wiki really is.

Anything not recognised is kept verbatim and shown in the properties card.

See also [[link-resolution]].
