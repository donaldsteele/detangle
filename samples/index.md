---
title: A Wiki an LLM Wrote
type: index
tags: [demo]
updated: 2026-08-20
---

# A Wiki an LLM Wrote

This is a small, deliberately imperfect wiki — the kind a language model produces when
you ask it to write one. Its links are written the way a person would say them out loud,
not the way the files are named on disk.

Every link below resolves. Hover one to see which rule got it there.

## Start here

- [[Attention Is All You Need]] — a paper page whose file is `attention-is-all-you-need.md`
- [[Transformer]] — a name that matches **two** files, so Detangle asks
- [[getting started#What's next?]] — a heading anchor written in prose
- [Setup](wiki/setup) — a folder, not a file
- [[Dose Response]] — a page nobody has written yet

## What to look at

| Section | What it shows |
|---|---|
| [[concepts/self-attention]] | Mermaid rendered offline |
| [[wiki/schema]] | DBML parsed here, drawn as an ER diagram |
| [[wiki/getting-started]] | Callouts in both dialects, math, task lists |
| [[entities/vaswani]] | Frontmatter references as real links |
| [[How These Links Resolved]] | Every link syntax, and the rule that answered each one |
| [[What Happens When You Open a Folder]] | A state diagram, and the resolution chain drawn |
| [[Attention Variants]] | A sequence diagram, a table, mathematics and task lists together |

## Set in place, not fetched

Diagrams, code and mathematics are all rendered in this process. No browser, no Node, no
CDN — scaled dot-product attention divides by $\sqrt{d_k}$, and that radical is drawn here:

$$
\text{Attention}(Q, K, V) = \text{softmax}\left(\frac{QK^\top}{\sqrt{d_k}}\right)V
$$

> [!tip] The point
> Nothing in this folder was cleaned up for the demo. The link text is what the model
> wrote; the filenames are what it saved. Detangle reconciles the two and tells you how.

> [!note] If you are adding a page here
> Fenced code is deliberately unhighlighted in the browser demo. The 150 TextMate
> grammars are 6.7 MB of the download and this wiki uses none of them, so the browser
> build ships without them and the desktop build ships with them. A `csharp` fence added
> to this folder will therefore look plain in the demo and coloured on the desktop, and
> nothing will fail to warn you.
