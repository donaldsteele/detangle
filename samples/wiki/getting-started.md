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

## What's next?

Read [[Attention Is All You Need]] to see resolution provenance on a real page, then open
[[wiki/schema]] for the DBML renderer.

The heading above is linked from the index as `[[getting started#What's next?]]` — lower
case, with the punctuation intact. It resolves.
