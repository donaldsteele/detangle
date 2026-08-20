---
title: Reader torture
type: fixture
tags: [rendering, callouts]
related:
  - anchor-host
---

# Reader torture

Everything the reader has to draw, on one page.

## Callouts, both dialects

> [!note]
> An Obsidian callout with no title.

> [!warning] Mind the gap
> One with a title, and a [[case-drift]] link inside it.

> [!tip]- Folded by default
> Hidden until the reader opens it.

!!! note
    A MkDocs admonition.

!!! danger "Named admonition"
    With a title, a list:

    - one
    - two

??? question "Collapsed admonition"
    Only visible once expanded.

> Just an ordinary quotation, not a callout.

## Tables

| Left | Center | Right |
|:-----|:------:|------:|
| a | b | c |
| [[case-drift]] | `code` | **bold** |

## Code

```csharp
var greeting = "hello";
Console.WriteLine(greeting);
```

```mermaid
graph TD;
  A-->B;
```

```
An unlabelled fence.
```

## Math

Inline $E = mc^2$ inside a sentence.

$$
\int_0^1 x^2 \, dx = \frac{1}{3}
$$

## Lists

- [ ] an unchecked task
- [x] a checked task
- a plain item
  - a nested item

3. three
4. four

## Definitions

Term
:   Its definition.

## Footnotes

A claim that needs support[^evidence].

[^evidence]: The supporting note.

## Transclusion

![[anchor-host#Duplicate]]

![[folder/index]]

![[nowhere-at-all]]

## Attachments

![[extension-probe.png|200]]

![Alt text](extension-probe.png)

## Emphasis

**bold**, *italic*, ~~struck~~, ==highlighted==, ^super^, ~sub~, `inline code`.
