# Detangle

**Read the wiki the model actually wrote.**

A markdown wiki viewer that resolves the links your generator got wrong.

An LLM writes `[[Attention Is All You Need]]`; the file on disk is
`attention-is-all-you-need.md`. It writes `[[Getting Started#What's next?]]`; the anchor
slug is `whats-next`. It writes `[Setup](setup)` with no extension. Every other viewer
resolves some subset of these and silently drops the rest.

Detangle resolves the link, tells you which rule it used, and lists every link it
couldn't.

## Status

Pre-alpha. Phase 0 of 9 — see [plan.md](plan.md) for the full design.

## What it does

- **Thirteen wiki formats**, auto-detected: LLM Wiki (Karpathy pattern), Obsidian, Foam,
  Dendron, Logseq, Quartz, Zettelkasten, MkDocs, Docusaurus, GitBook, docsify, mdBook,
  DeepWiki.
- **A thirteen-step link resolver** with visible provenance — every link shows which
  rule resolved it, ambiguity opens a picker, nothing is silently dropped.
- **Mermaid and DBML diagrams**, rendered offline with no runtime dependencies.
- **Backlinks, unlinked mentions, graph view, full-text search.**
- **Link Doctor** — every broken, ambiguous, orphaned and stale page in one list, with
  one-click fixes.
- **Offline by design.** No network calls, no telemetry, no account, no API keys.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build Detangle.slnx
dotnet test Detangle.slnx
dotnet run --project src/Detangle.Desktop
```

## Layout

| Path | Contents |
|---|---|
| `src/Detangle.Core` | Vault scanning, parsing, link resolution, graph, search. No UI, no Avalonia. |
| `src/Detangle.Rendering` | Markdig AST to Avalonia controls; the `IDiagramRenderer` contract. |
| `src/Detangle.App` | Shared Avalonia UI. |
| `src/Detangle.Desktop` | Windows, macOS and Linux entry point. |
| `tests/Detangle.Core.Tests` | Resolver golden tests and DBML conformance tests. |

`Detangle.Core` must never reference Avalonia — it stays headless-testable so the
resolver can be golden-tested without a UI.

## License

MIT. See [LICENSE](LICENSE).
