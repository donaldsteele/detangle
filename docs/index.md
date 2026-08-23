---
title: Detangle Documentation
type: index
updated: 2026-08-20
---

# Detangle Documentation

Detangle reads a folder of markdown as a wiki. Its one real trick is that it follows
links that do not match a filename exactly — which is how wikis written by language
models are almost always written.

This folder is also the app's built-in help: open it in Detangle and it reads as a vault.

## Guides

- [[link-resolution]] — the thirteen-step chain, and how to read a link's provenance
- [[formats]] — the thirteen wiki conventions Detangle recognises
- [[diagrams]] — Mermaid and DBML, rendered offline
- [[search]] — the query syntax
- [[link-doctor]] — every broken link in one list, and the fixes that are safe
- [[regeneration]] — marking a baseline, and failing a pipeline only when links got worse
- [[graph]] — the graph view and what its shapes mean
- [[editing-and-export]] — the split editor, atomic saves, and the four export shapes
- [[keyboard]] — every shortcut
- [[faq]] — what Detangle does not do, and why

## The short version

Point it at a folder. Nothing is imported, nothing is converted, and no account is
involved. The folder stays exactly as it was — Detangle keeps its index in a `.detangle/`
sidecar you can delete at any time.
