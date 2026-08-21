---
title: Transformer (architecture)
type: concept
tags: [llm/architecture]
updated: 2026-08-20
---

# Transformer (architecture)

The encoder-decoder stack from [[Attention Is All You Need]]: N identical layers, each
one [[self attention]] followed by a position-wise feedforward network, with residual
connections and layer normalization around both.

There is a second file in this wiki called `transformer.md`, in `entities/`. A link
written as `[[Transformer]]` matches both, so Detangle picks the shortest path, marks the
link ambiguous, and offers the other candidate rather than choosing silently.
