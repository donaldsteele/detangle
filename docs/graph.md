---
title: Graph View
type: guide
updated: 2026-08-20
---

# Graph View

`Ctrl+G` swaps the reading pane for a force-directed picture of the whole vault.

## Reading it

- **Node size** is inbound links. The big ones are what the wiki is actually about.
- **Node colour** is the frontmatter `type`, hashed to a stable colour so `concept` stays
  the same colour between sessions.
- **A hollow node** is an orphan: nothing links to it.
- **A dashed outline** is a page that does not exist — something links to it, and nobody
  wrote it. In a wiki a model produced, those are the shape of the work left to do.
- **Dashed edges** are the links pointing at them.

Hovering a node lights up its own links and puts its counts in the status bar. Clicking
opens the page; dragging moves it and lets the simulation settle around it.

## Filters

Filter by frontmatter type, by tag (a tag filter matches its descendants, so `llm` keeps
`llm/architecture`), and by folder. Orphans and missing pages can each be hidden.

**Local** mode draws only the neighbourhood of the page you were reading — N hops out,
walked in both directions, because a page's backlinks are as much its neighbourhood as
its outbound links.

## Above 1,500 nodes

The graph folds to one node per top-level folder. A five thousand node hairball is not a
picture of anything, and drawing it at ten frames a second would be worse than useless.
Clicking a folder filters to it and the pages inside come back individually.

## Performance

Repulsion goes through a Barnes-Hut quadtree rather than comparing every pair — the naive
form is twenty-five million distance computations per frame at five thousand nodes — and
the pass is spread across cores. Measured on a 5,000-page vault: about 11 ms per
simulation step, inside a 33 ms frame with room for drawing.

Layout is seeded deterministically, so the same vault lays out the same way every time.

See also [[search]].
