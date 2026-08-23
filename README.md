# Detangle

**Read the wiki the model actually wrote.**

A markdown wiki viewer that resolves the links your generator got wrong.

An LLM writes `[[Attention Is All You Need]]`; the file on disk is
`attention-is-all-you-need.md`. It writes `[[Getting Started#What's next?]]`; the anchor
slug is `whats-next`. It writes `[Setup](setup)` with no extension. Every other viewer
resolves some subset of these and silently drops the rest.

Detangle resolves the link, tells you which rule it used, and lists every link it
couldn't.

**[Try it in your browser](https://detangle.dev/demo/)** ·
[Documentation](https://detangle.dev/docs/) ·
[Site](https://detangle.dev)

## Status

All nine phases of [plan.md](plan.md) are implemented, and the site and demo are
deployed. Nothing has been released yet: the release workflow has never run against a tag,
so the installers and the in-app updater are written but unproven.

## What it does

- **Thirteen wiki formats**, auto-detected: LLM Wiki (Karpathy pattern), Obsidian, Foam,
  Dendron, Logseq, Quartz, Zettelkasten, MkDocs, Docusaurus, GitBook, docsify, mdBook,
  DeepWiki.
- **A thirteen-step link resolver** with visible provenance — every link shows which
  rule resolved it, ambiguity opens a picker, nothing is silently dropped.
- **Mermaid and DBML diagrams**, rendered offline with no runtime dependencies.
- **Backlinks, unlinked mentions, graph view, full-text search, find in page.**
- **Link Doctor** — every broken, ambiguous, orphaned and stale page in one list. Fixes
  show you every file they would rewrite before touching one, and there is no undo, so the
  list comes first.
- **Portable decisions.** The one resolution rung a person writes — which page an ambiguous
  link means — lives in `.detangle-choices` beside your markdown. Commit it and the desktop
  app, the CLI and the exported site all agree.
- **A regeneration gate.** Mark a baseline, and `detangle-lint --fail-on-regression` fails a
  pipeline only when a link broke *or fell down the chain* — not on the ones that were
  already broken before the run.
- **Offline by design.** No network calls, no telemetry, no account, no API keys.

`detangle-lint` reports the same findings as JSON, and `--emit-patch` plans the repair as a
unified diff — naming the rung that resolved each link above its hunk — without ever
writing to the vault.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build Detangle.slnx
dotnet test Detangle.slnx
dotnet run --project src/Detangle.Desktop -- samples
```

A self-contained single-file build needs a runtime identifier, which is what switches
those publish settings on:

```
dotnet publish src/Detangle.Desktop -c Release -r win-x64
```

Publishing a vault needs no window:

```
dotnet run --project src/Detangle.Desktop -- --export-site docs out --title "Detangle Docs"
```

The WebAssembly demo is not in the solution — it needs the `wasm-tools` workload, which
would break `dotnet build` for anybody who has not installed it:

```
dotnet workload install wasm-tools
dotnet publish src/Detangle.Browser
```

## Layout

| Path | Contents |
|---|---|
| `src/Detangle.Core` | Vault scanning, parsing, link resolution, graph, search. No UI, no Avalonia. |
| `src/Detangle.Rendering` | Markdig AST to Avalonia controls; the `IDiagramRenderer` contract. |
| `src/Detangle.App` | Shared Avalonia UI. |
| `src/Detangle.Desktop` | Windows, macOS and Linux entry point, and the headless exporter. |
| `src/Detangle.Browser` | The WebAssembly demo. Not in the solution; see above. |
| `tests/Detangle.Core.Tests` | Resolver golden tests and DBML conformance tests. |
| `docs/` | The documentation, which is also the app's built-in help vault. |
| `samples/` | A small wiki written the way a model writes one; the demo ships it. |
| `site/` | The website: hand-written HTML, one stylesheet, no build step. |

`Detangle.Core` must never reference Avalonia — it stays headless-testable so the
resolver can be golden-tested without a UI.

## License

MIT. See [LICENSE](LICENSE).
