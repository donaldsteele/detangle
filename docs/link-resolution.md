---
title: Link Resolution
type: guide
updated: 2026-08-20
---

# Link Resolution

A language model writes `[[Attention Is All You Need]]`. The file it saved is
`attention-is-all-you-need.md`. Every other viewer treats those as different things and
shows you a dead link.

Detangle tries thirteen rules in order and stops at the first that matches. The order
matters: exact answers are always preferred over clever ones.

## The chain

| # | Rule | Matches |
|---|---|---|
| 1 | Exact vault path | `[[wiki/setup/index]]` → `wiki/setup/index.md` |
| 2 | Note-relative path | a sibling file, from the linking page's own folder |
| 3 | Case-sensitive filename | `[[setup]]` → `setup.md` anywhere in the vault |
| 4 | Path suffix | `[[setup/index]]` → `docs/setup/index.md` |
| 5 | Case-insensitive filename | `[[Setup]]` → `setup.md` |
| 6 | Normalized name | `[[Attention Is All You Need]]` → `attention-is-all-you-need.md` |
| 7 | Alias or title | a name from `aliases:`, `title:`, or the first H1 |
| 8 | Identifier | `id:`, `uid:`, `permalink:` or `slug:` from frontmatter |
| 9 | Folder index | `[Setup](wiki/setup)` → `wiki/setup/index.md` |
| 10 | Encoding variant | percent-encoding, and Unicode composed against decomposed |
| 11 | Extension probe | `![[diagram]]` → `assets/diagram.png` |
| 12 | Nearest name | offered as a suggestion, never followed automatically |
| 13 | Placeholder | nothing matched; the link is reported, not hidden |

Markdown links swap steps 1 and 2: `[text](setup.md)` in a CommonMark file means the file
beside this one, and every other tool in the world reads it that way.

## Normalization

Steps 5 onward compare normalized names. Normalizing means: Unicode NFC, percent-decode,
trim, drop a known extension, backslashes to forward slashes, lowercase, then collapse
every run of spaces, underscores and dots into a single hyphen.

So `My Note.md`, `my_note`, `My%20Note` and `my.note` are all the same name.

## Provenance

Every resolved link carries the rule that resolved it, and the reader shows it:

- **Exact** — rules 1, 2, 3. Drawn as an ordinary link.
- **Normalized** — rules 4 through 8. Drawn with a dotted underline; hovering says which.
- **Heuristic** — rules 9 through 11. Same, with a stronger hint.
- **Suggestion** — rule 12. Never navigated to; offered in the [[link-doctor]].
- **Unresolved** — rule 13. Drawn as a placeholder, and counted in the status bar.

A viewer that guesses without telling you is worse than one that fails loudly. The whole
design here is that a guess is always visible as a guess.

## Ambiguity

When a rule matches more than one file, Detangle picks the shortest path, then
alphabetical order, and marks the link ambiguous. Every candidate is listed, and choosing
one is remembered for that vault.

### Where the decision lives

Every rung above is a rule. This one is a person, and it is the only part of the
resolution the vault cannot re-derive — so it travels with the vault, in
`.detangle-choices` at the root:

```
wiki/concepts | Transformer -> wiki/entities/transformer.md  # settled 2026-08-22, was ambiguous between 3
```

One line per decision, sorted, with the reason as a comment. It is a text file rather than
JSON on purpose: this is a file people read in a pull request, and "why does this link mean
that page" should be answerable from the diff. Commit it, and everyone who checks the wiki
out — and `detangle-lint`, and the static site export — resolves that link the way you
decided rather than the way the chain would have guessed.

To change your mind, edit or delete the line; or open the **Link Doctor** panel, where
**settled links** lists every decision with a **Revoke** beside it.

`detangle-lint --choices <path>` reads a different file, for a pipeline that keeps its
decisions somewhere else. In the browser demo there is no file: a decision made there lasts
as long as the tab, and the app says so rather than claiming a save.

## Anchors

A fragment is matched against the raw heading text first (case-insensitively, which is the
Obsidian rule), then the github-slugger slug including its `-1`/`-2` dedup counters, then
`^blockid` markers, then Logseq `id::` uuids, then `#L10-L20` line ranges, then `#page=N`.

An anchor that matches nothing never fails the link — you still land on the page, with a
warning.

See also [[formats]] and [[link-doctor]].
