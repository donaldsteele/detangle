---
title: Getting Started
type: guide
tags: [demo]
updated: 2026-08-20
---

# Getting Started

Point Detangle at any folder of markdown. There is no import, no database to build first,
and no account. The folder stays exactly as it was.

## What happens on open

1. The folder is scanned and its flavor is sniffed — Obsidian, MkDocs, Docusaurus, Logseq,
   Dendron, plain markdown, and eight more.
2. Every link is resolved through a thirteen-step chain, exact matches first.
3. Anything that did not resolve cleanly is listed rather than dropped.

- [x] Open a folder
- [x] Read a page
- [ ] Find out which links were guesses

> [!note] Both callout dialects
> This is Obsidian's blockquote form.

!!! tip "And this one"
    This is the MkDocs form. Vaults written by a model mix them freely, often on the same
    page, so both are supported.

## Inline detail

Euler's identity, $e^{i\pi} + 1 = 0$, inline. A code span like `--filter-query` keeps its
punctuation. A term list:

Vault
: Any folder of markdown files.

Flavor
: Which wiki convention the folder appears to follow.

## In the browser, some of this is missing

If you are reading this at detangle.dev, you are running the whole reader compiled to
WebAssembly. It resolves links, renders diagrams, searches, and draws the graph exactly as
the desktop application does. Four things are not the same, and none of them are bugs:

Editing
: A folder you open here is copied into the tab, not opened in place, so saving is refused
  rather than writing somewhere that disappears when you close it.

Static site export
: A site is a folder of files and a browser will not let this build write one. Export a
  single HTML file instead, or use the desktop application.

Mathematics in exports
: Rendered here, but written as its TeX source in an exported PDF or HTML file.

Fonts and highlighting
: A PDF exported here is set in the one typeface WebAssembly has, and fenced code is not
  syntax highlighted, because the grammars are 6.7 MB the demo does not download.

## What's next?

Read [[Attention Is All You Need]] to see resolution provenance on a real page, then open
[[wiki/schema]] for the DBML renderer.

The heading above is linked from the index as `[[getting started#What's next?]]` — lower
case, with the punctuation intact. It resolves.
