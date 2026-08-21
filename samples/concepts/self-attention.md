---
title: Self-Attention
type: concept
tags: [llm/architecture]
updated: 2026-08-20
---

# Self-Attention

Every position in a sequence attends to every other position, including itself. There is
no recurrence and no convolution — just three projections and a weighted sum.

```mermaid
graph LR
  X[Input embeddings] --> Q[Query]
  X --> K[Key]
  X --> V[Value]
  Q --> S["Q · Kᵀ / √d"]
  K --> S
  S --> A[softmax]
  A --> O[Weighted sum]
  V --> O
  O --> Y[Output]
```

That diagram is rendered here, in process, with no browser and no network. The fence is
plain Mermaid; nothing was fetched to draw it.

## Multi-head

Splitting the projections into $h$ heads lets the model attend to several relationships
at once — one head tracking syntax while another tracks coreference, in the usual
telling.

```mermaid
sequenceDiagram
  participant T as Token
  participant H1 as Head 1
  participant H2 as Head 2
  participant C as Concat + project
  T->>H1: project to d/h
  T->>H2: project to d/h
  H1->>C: attended values
  H2->>C: attended values
  C->>T: output
```

## Cost

Attention is $O(n^2 d)$ in sequence length, which is what every long-context method since
has been trying to get around.

> [!warning] A link that goes nowhere
> This page links to [[Dose Response]], which does not exist. It is left broken on
> purpose: open the Link Doctor and it is the first thing listed, with the nearest
> filename Detangle can find.

Back to [[Attention Is All You Need]] · see also [[Transformer]].
