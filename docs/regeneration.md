---
title: Regeneration
type: guide
updated: 2026-08-22
---

# Regeneration

A wiki written by a model is not edited. It is regenerated — the whole corpus, or a large
part of it, rewritten in one run. That changes what "is this wiki healthy" means.

Every link checker asks the absolute question: *are there broken links right now?* For a
corpus somebody edits by hand, that is the right question, because the answer starts at
zero and every regression is visible. For a corpus a generator rewrites wholesale, the
answer is almost never zero, so the run that made things worse looks exactly like the
twenty runs that did not.

Detangle asks the other question: *is anything worse than it was?*

## Marking a baseline

Nothing tells Detangle that a generation run has finished. The file watcher sees dozens of
changes while one is under way, and a baseline taken automatically would as often as not be
taken halfway through. So marking is explicit.

In the application, the Link Doctor panel has a **Mark this state** button. From a
terminal:

```
$ detangle-lint wiki/ --mark
```

Both write the same file: `.detangle-baseline.json`, at the vault root.

It sits at the root rather than in `.detangle/` on purpose. `.detangle/` is a cache — the
search index and the diagram renders — and the [FAQ](faq.md) says you may delete it at any
time. A baseline is the opposite: it is the thing a repository commits so the next
generation run can be measured against it, and it belongs where `git status` shows it.

```
$ git add .detangle-baseline.json
$ git commit -m "chore: mark the wiki's link baseline"
```

## Asking what changed

```
$ detangle-lint wiki/ --since
```

The report grows a `delta` block:

```json
{
  "delta": {
    "pages": { "added": 4, "removed": 0, "renamed": 1, "rewritten": 22 },
    "links": { "broke": 2, "healed": 5, "degraded": 7, "improved": 1, "retargeted": 0 },
    "regressed": true,
    "regressions": [
      { "source": "wiki/concepts/attention.md", "target": "Vaswani et al",
        "change": "broke", "was": "alias", "now": "unresolved", "resolvedTo": "" },
      { "source": "wiki/index.md", "target": "Getting Started",
        "change": "degraded", "was": "exactStem", "now": "fuzzyNearest",
        "resolvedTo": "wiki/getting-started.md" }
    ]
  }
}
```

Five words carry the whole idea, and the panel's change summary uses the same five:

| Word | Means |
|---|---|
| `broke` | It resolved before. It resolves to nothing now. |
| `healed` | It resolved to nothing before. It resolves now. |
| `degraded` | It still resolves, but a later rung of the chain had to rescue it. |
| `improved` | It still resolves, and now by an earlier rung than before. |
| `retargeted` | Same rung, different page. |

`degraded` is the one no other tool can report. Every competitor's link is binary — it
works or it does not — so none of them can represent a link that still works but works
worse. Detangle's [thirteen-rung chain](link-resolution.md) is what makes the difference
visible: "twelve links that resolved by an exact stem now resolve by a fuzzy nearest match"
is a sentence about a naming convention that drifted, and it is actionable *before* those
twelve links break.

Only the regressions are listed. The healed and improved ones are counted, because a report
that listed every link that moved in either direction would bury the ones somebody has to
act on.

## Gating a pipeline

```
$ detangle-lint wiki/ --fail-on-regression
```

Exit 1 when anything broke or degraded, whatever the absolute counts are. It implies
`--since`; there is nothing to fail on without a comparison.

```yaml
- name: Regenerate the wiki
  run: ./generate-wiki.sh

- name: Fail if any link got worse
  run: ./detangle-lint wiki/ --fail-on-regression --output findings.json

- name: Keep the report
  if: always()
  uses: actions/upload-artifact@v4
  with: { name: link-findings, path: findings.json }
```

An already-broken wiki that did not get worse passes. That is the point.

`--fail-on-regression` and `--fail-on <severity>` are independent and compose: the first
asks whether this run made things worse, the second whether the wiki is above some absolute
bar. A pipeline that wants both gets an exit 1 from either.

## Accepting the new state

When a regression is deliberate — a page was renamed on purpose, a section was cut — mark
again and commit the new baseline:

```
$ detangle-lint wiki/ --mark
$ git commit -am "chore: accept the link changes from the rename"
```

The commit is the record of somebody having looked.

## When there is no baseline

`--since` against a vault that has never been marked reports a `delta` with everything at
zero and `regressed: false`, rather than reporting the whole vault as new. Without the flag
there is no `delta` block at all — "nothing regressed" and "nothing was compared" are
different answers, and a gate reading the report has to be able to tell them apart.
