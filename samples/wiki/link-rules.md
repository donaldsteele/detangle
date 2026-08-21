---
title: How These Links Resolved
type: reference
tags: [demo, links]
updated: 2026-08-21
---

# How These Links Resolved

Every link on this page is written the way a person would say it, not the way the file is
named on disk. Hover any of them and the bar underneath the page tells you which rule got
there — that provenance is the whole point of this reader.

## The same target, said five ways

| Written | Resolves by |
|---|---|
| [[Attention Is All You Need]] | normalized name |
| [[entities/attention-is-all-you-need]] | exact vault path |
| [[Attention Is All You Need\|the 2017 paper]] | the same match, aliased |
| [attention](../entities/attention-is-all-you-need.md) | markdown link, relative |
| [[wiki/setup]] | folder index |

None of those are configuration. They are what a language model writes when you ask it for
a wiki, and reconciling them with the filenames it chose is the work.

## Anchors are resolved too, not just pages

A link can point into the middle of a page, and the heading it names is checked as well as
the file:

- [[wiki/getting-started#What happens on open]] — the heading exists, so this is a green link
- [[Self-Attention#Multi-head]] — a heading on another page, reached by the page's title

An anchor that does not exist is reported rather than followed silently, because a link
that lands on the wrong part of the right page is the kind of wrong that survives review.

## When there is more than one answer

[[Transformer]] matches two files — `concepts/transformer.md` and `entities/transformer.md`.
Detangle picks the shorter path and then alphabetical order, and says out loud that it
guessed. The Link Doctor lists it as ambiguous, so the choice is visible rather than
buried.

## When there is no answer

[[Dose Response]] is a page nobody has written. It is drawn in red, counted in the status
bar, and drawn in the graph as a hollow node — in a wiki a model produced, the pages it
referred to but never wrote are the shape of the work left to do.

> [!tip] Read the bar, not the colour
> Colour tells you something resolved. The ledger under the page tells you *why*, which is
> what you need when a link is green and still wrong.
