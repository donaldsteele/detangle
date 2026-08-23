---
title: Editing and Export
type: guide
updated: 2026-08-20
---

# Editing and Export

Detangle is a reader that can write, not an editor. Everything here is deliberately small.

## Editing

`Ctrl+E` splits the source beside the preview. `Ctrl+S` saves. There is no autosave.

Every write goes to a temporary file which is then moved over the original, so a crash or
a full disk cannot leave a vault holding half a document.

Before overwriting, Detangle compares the file's current **content** against what it read
when the editor opened. A wiki is usually also being written by something else — a model,
a sync client, another editor — so that question is asked every time. A file rewritten
byte-for-byte is not an external change: warning about that teaches you to click through
the warning that matters.

If the file did change, the save is refused and you keep your text. Reload to take the
other version, or save again to keep yours.

## Export

| Shape | Produces |
|---|---|
| Static site | One HTML file per page, navigation, prebuilt search index, inline SVG diagrams |
| Single HTML file | The vault, or one page, in one self-contained file |
| PDF | The vault or one page, with a linked table of contents |
| Normalized markdown | The vault rewritten in place, every link canonical |

Exports go through the same render model the app draws from. That is the whole point: a
link resolved by an alias or a normalized name comes out as a working anchor. A generator
that re-parses the markdown drops exactly the links Detangle exists to find.

The exported site has no network dependency — the stylesheet and the search script are
written out, diagrams are inline SVG, and every link is relative. It opens from a `file://`
URL.

PDF is drawn through Skia's PDF backend rather than converted, so there is no browser
involved. Diagrams go in as vectors; links between exported pages become internal jumps.
Math is written as its TeX source and the export says so.

### Normalizing

**Normalize links in the vault** rewrites every resolved link to its canonical target — the
one export that changes your files. Aliases, anchors, embed markers and size specs are
preserved exactly, and a link that resolves to nothing is left as you wrote it. Inventing a
target for a broken link would turn a visible problem into a silent one.

Detangle can follow `[[Attention Is All You Need]]`; nothing else can. Normalizing writes
that answer down, so the vault keeps working after it leaves.

Like **Fix all safe**, it asks first: choosing it opens the Link Doctor with a card listing
every file it would rewrite and how many links in each. Nothing is touched until you
confirm, and there is no undo afterwards.

See also [[link-doctor]].
