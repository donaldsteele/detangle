---
title: Search
type: guide
updated: 2026-08-20
---

# Search

Full-text search over the whole vault, backed by SQLite FTS5 in a sidecar database. A
five thousand file vault indexes cold in about three seconds and answers a keystroke in
well under fifty milliseconds.

## Query syntax

| Form | Finds |
|---|---|
| `attention` | pages containing the word |
| `"is all you need"` | the exact phrase |
| `attention head` | pages containing both |
| `type:concept` | pages whose frontmatter `type` is concept |
| `tag:llm` | pages tagged `llm`, including `llm/architecture` |
| `path:wiki/` | pages under a folder |
| `updated>2026-06-01` | pages changed since a date |
| `updated<2025-01-01` | pages not changed since a date |

Filters combine with text and with each other: `attention type:paper tag:llm path:entities/`
is one query.

Results are ranked by bm25 and shown with the matching heading and a snippet, so you can
tell which of five similar pages you meant before opening any of them.

## The index

The database lives in `.detangle/cache.db` inside the vault. It is a cache: delete it and
it rebuilds on the next open. Nothing in it is needed to read the vault, and nothing about
the vault is stored anywhere else.

A file watcher keeps it current — debounced, with a periodic reconcile sweep in case the
platform drops an event, which both Windows and macOS do under load.

See also [[link-doctor]].
