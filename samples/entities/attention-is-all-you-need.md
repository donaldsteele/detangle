---
title: Attention Is All You Need
type: paper
tags: [llm/architecture, foundational]
authors: [Vaswani, Shazeer, Parmar]
created: 2017-06-12
updated: 2026-08-20
sources:
  - vaswani
  - concepts/self-attention
---

# Attention Is All You Need

The 2017 paper that introduced the [[Transformer]] and removed recurrence from sequence
transduction entirely. Written by [[Vaswani]] and colleagues at Google Brain.

The whole model is [[self attention]] plus position-wise feedforward layers, stacked.

## The core claim

Recurrent models process a sequence one position at a time, which forbids parallelism
within a training example. Attention lets every position see every other position in one
step, so the sequence length stops being a serial bottleneck.

$$
\text{Attention}(Q, K, V) = \text{softmax}\left(\frac{QK^\top}{\sqrt{d_k}}\right)V
$$

The $\sqrt{d_k}$ term is not decoration: without it the dot products grow with dimension
and push the softmax into regions where its gradient vanishes.

!!! note "Why this page exists"
    It is the page an LLM is most likely to link to as `[[Attention Is All You Need]]`
    while saving the file as `attention-is-all-you-need.md`. That mismatch is the whole
    reason Detangle exists.

## What followed

- [[concepts/self-attention]] — the mechanism itself
- [[Dose Response]] — a page this wiki refers to but never wrote
- [[wiki/getting-started#What's next?]] — where to go from here
