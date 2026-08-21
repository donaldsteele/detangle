---
title: Link Doctor
type: guide
updated: 2026-08-20
---

# Link Doctor

One list of everything wrong with a wiki's links, and the subset of it that is safe to fix
automatically.

## What it reports

| Finding | Severity | Means |
|---|---|---|
| Broken link | Error | Nothing in the vault matches, after all thirteen rules |
| Ambiguous link | Warning | More than one file matched; one was chosen |
| Non-canonical link | Info | Resolved, but by a fallback rule rather than by path |
| Orphan page | Info | Nothing links here |
| Stale page | Warning | Well-linked and long unchanged |
| Duplicate slug | Info | Two files normalize to the same name |
| Oversized page | Info | Long enough that it probably wants splitting |
| Frontmatter issue | Info | Unterminated block, or no title |

Opening a broken link finds its nearest match by edit distance. That search is not run
for every finding — a wiki with a thousand broken links would pay for a thousand
edit-distance sweeps over every name in the vault, and only the finding you are looking at
needs one.

## Fixing

**Fix all safe** rewrites the non-canonical links — the ones that resolved by rules 4
through 8 and have exactly one correct answer — to their canonical target. Ambiguous and
broken links are never touched, because neither has one right answer.

Rewrites are surgical: only the exact link on the recorded line is changed, and a file
that no longer matches what the finding recorded is skipped rather than rewritten blind.
Each file is written atomically.

**Create the missing note** writes the page a broken link was pointing at, stubbed from the
vault's own template if it has one, so the new file looks like the files around it.

To rewrite every link in the vault at once rather than only the safe subset, see
[[editing-and-export]].

See also [[link-resolution]].
