# Detangle — Product & Implementation Plan

> **Detangle** · `detangle.dev` (registered) · repo `detangle` · binary `detangle` · wordmark `de`**`tangle`**
> Positioning: *LLM wikis are a tangle of half-right links. Detangle reads them anyway.*

---

## 1. Context

LLM-generated wikis are now a common artifact: Karpathy's April 2026 "LLM Wiki" pattern, DeepWiki repo wikis, Obsidian vaults maintained by agents, MkDocs trees emitted by doc-generation tools. They all land on disk as a directory of Markdown files. There is no good way to *read* one.

Current options all fail in a specific way:

- **Obsidian** — heavyweight, editor-first, assumes its own vault conventions, poor DBML/ER support, closed source.
- **VS Code preview** — no wikilink resolution, no backlinks, no graph, no cross-file navigation.
- **MkDocs / Quartz / Docusaurus** — require a build step, a config file, and a web server. You cannot just point them at a folder.
- **GitHub web view** — renders Mermaid, but no wikilinks, no graph, requires a push.

The specific pain: **link syntax drift**. An LLM writes `[[Attention Is All You Need]]`; the file on disk is `attention-is-all-you-need.md`. It writes `[[Getting Started#What's next?]]`; the anchor slug is `whats-next`. It writes `[Setup](setup)` with no extension. It writes `![[diagram.png|300]]` where the pipe is a size, not an alias. Every viewer resolves some subset of these and silently drops the rest. Research (§3) confirms this is systemic across all 13 wiki formats surveyed — even the reference `llm-wiki` linter's own regex swallows anchors.

**Intended outcome:** point the app at *any* directory of Markdown, have it auto-detect the flavor, resolve links the way a human would (with an audit trail when it guesses), and render Mermaid + DBML diagrams natively — offline, instantly, cross-platform, free.

---

## 2. Decisions locked

| Decision | Choice |
|---|---|
| Render core | **Native Avalonia** for the whole document (Markdig AST → control tree) |
| Diagram core | **Mermaider** (pure .NET → SVG) primary; WebView is an opt-in fidelity mode |
| Runtime deps | **Zero** in the default configuration — no WebKitGTK, no WebView2 |
| Editing | **Viewer + light edits** (inline page edit, link fix write-back) |
| Link resolution | **Aggressive multi-stage fallback**, per-flavor profiles, visible audit badge |
| Flavor detection | **Auto-detect + manual override**, persisted per-directory |
| v1 features | Graph view, full-text search, Link Doctor, Export |
| Extras | Math (KaTeX), callouts (both dialects), frontmatter panel + tag browser, transclusion/embeds |
| Scale target | ~5,000 files instant |
| AI features | **None in v1** — fully offline, no network, no keys |
| Repo | **Single monorepo** |
| DBML | **Own C# parser → Mermaid `erDiagram`** |
| UI shell | **3-pane + command palette** |
| Website | Landing + docs + **live WASM demo** |
| Distribution | Free, **MIT**, GitHub Releases |
| WASM | `IDiagramRenderer` with in-process, WebView, and browser-JS backends |

---

## 3. Research findings that shape the design

### 3.1 Formats to support (13 surveyed)

| Format | Layout | Link syntax | Identifier | Resolution rule |
|---|---|---|---|---|
| **LLM Wiki / OmegaWiki** | `wiki/{sources,entities,concepts,synthesis}/`, `index.md`, `log.md`, `raw/` | `[[slug]]`, `[[slug\|alias]]`; frontmatter bare slugs | lowercase-hyphen stem | Global stem map; duplicates are a lint error |
| **Obsidian** | Free-form vault, `attachments/` | `[[…]]`, `[[…\|…]]`, `[[…#h]]`, `[[…#^b]]`, `![[…]]`, `[x](y%20z.md)` | stem, alias, or path | shortest-path / relative / absolute (per-vault); basename search for attachments |
| **Foam** | Free-form workspace | `[[…]]`, `[[folder/file]]`, `[[./rel]]`, `[[/root]]` | any unique **path suffix** | Suffix match → alphabetical tiebreak + warning; misses become placeholders |
| **Dendron** | Flat dir, dot-hierarchy filenames | `[[a.b.c]]`, `[[Label\|a.b.c]]`, `[[a.b.c#h]]` | dot-path filename; UUID `id` | Exact dot-name; display via frontmatter `title` |
| **Logseq** | `pages/`, `journals/`, `assets/` | `[[Page]]`, `#tag`, `((uuid))`, `{{embed …}}` | page **title** (file name is escaped) | `___` / `%2F` / `.` → `/`, URL-decode; blocks via `id::` |
| **Quartz** | Content tree, `index.md` | `[[…]]` + md links | slugified path (**case-preserving**) | spaces→`-`, `&`→`-and-`, `%`→`-percent`, drop `?#` |
| **Zettelkasten** | Flat dir | `[[202604201530]]`, `[d][§id]` | timestamp / Folgezettel ID | Filename **prefix** match |
| **MkDocs / Material** | `docs/`, `mkdocs.yml` `nav:` | relative `.md` | path under `docs/` | `index.md` > `README.md`; `use_directory_urls` shifts depth |
| **Docusaurus** | `docs/`, `sidebars.ts` | relative `.md`/`.mdx` | frontmatter `id` (default stem), `slug` overrides | Build-time relative; sidebar by id |
| **GitBook** | `SUMMARY.md`, folder `README.md` | md links | path | `README.md` = folder index |
| **docsify** | `_sidebar.md` per dir | md links | path | Path, runtime |
| **mdBook** | `src/SUMMARY.md` | md links, strict nested list | path | SUMMARY defines the book |
| **DeepWiki** | `.devin/wiki.json` + md | title-keyed tree; `file.py#L10-L20` cites | page **title** | Title match; code cites resolve into the repo |

Folder-index precedence differs per system. Accept in order: `index.md` → `README.md` → `readme.md` → `_index.md` (Hugo) → `<foldername>.md` (Dendron/Foam sibling style).

### 3.2 The 23 link edge cases that actually bite

Case drift · separator drift (space/`-`/`_`/`.`) · `%20`/`%2F`/`%3F` and double-encoding · missing or wrong extension (`.md`/`.markdown`/`.mdx`/`.html`) · slugified-vs-raw titles (bidirectional) · anchors with punctuation (github-slugger *deletes* `'` and `?`, doesn't replace) · duplicate headings → `-1`/`-2` counters · nested heading paths `[[Note#H1#H2]]` · block refs `#^id` vs `((uuid))` (match `#^` before `#`) · links to folders · ambiguous basenames across dirs · Windows `\` separators and vault-absolute leading `/` · **the `|` overload** (`![[img.png|300]]` is a size; `[[note|label]]` is an alias — disambiguate by target extension) · embeds vs links · empty/self targets `[[#Heading]]`, `[[|alias]]` · escaped `\[\[…\]\]` and links inside code fences / inline code / HTML comments / frontmatter strings (must be excluded from the graph) · trailing punctuation · Unicode NFC vs NFD (macOS filenames are NFD) · Dendron dot-names (never treat last dot as extension) · MkDocs `use_directory_urls` off-by-one · `#L10-L20` code cites are not wiki pages · `notes/index.md` vs `notes.md` both claiming folder index · case-only duplicate files.

### 3.3 Frontmatter key union to normalize

`title` · `aliases`/`alias`/`aka` · `tags`/`tag`/`keywords`/`categories` (string, CSV, or list; nested `a/b`; strip leading `#`) · `id`/`uid`/`zettel-id`/`permalink` · `type`/`kind` · `status`/`state` · `created`/`date`/`dateCreated` (ISO, epoch-s, **epoch-ms** for Dendron) · `updated`/`modified`/`last_modified_at` · `sources`/`related`/`links`/`see-also`/`refs`/`parent`/`up` (**resolve as links — bare slugs, not wikilinks**) · `authors`/`author` · `url` · `raw` · `draft` vs `publish` (**inverted polarity**) · `sidebar_position`/`order`/`weight`/`nav_order` · `cssclasses` · LLM-Wiki nested `graph:` block.

Delimiters to accept: `---` YAML, `+++` TOML, `;;;` JSON, and Logseq/Dataview `key:: value` first-block properties. Tolerate a leading BOM and a leading blank line before `---` — LLMs emit both.

---

## 4. Architecture

```
detangle/                         (monorepo, MIT)
├── src/
│   ├── Detangle.Core/            net10.0 — NO UI, NO platform deps
│   │   ├── Vault/                VaultScanner, FlavorDetector, VaultProfile, FileWatcher
│   │   ├── Parsing/              MarkdigPipeline, FrontmatterReader, WikiLinkExtension
│   │   ├── Linking/              LinkResolver (13-step chain), NameIndex, AliasIndex, PathIndex, AnchorResolver
│   │   ├── Graph/                LinkGraph, Backlinks, UnlinkedMentions, GraphLayout
│   │   ├── Search/               SqliteFtsIndex, QueryParser (type:/tag:/updated>)
│   │   ├── Dbml/                 DbmlLexer, DbmlParser, DbmlModel, MermaidErdEmitter
│   │   └── Diagnostics/          LinkDoctor findings (broken/ambiguous/orphan/stale/dup-slug/oversized)
│   ├── Detangle.Rendering/       Markdig AST -> Avalonia control tree; IDiagramRenderer contract;
│   │                             MermaiderDiagramRenderer (default backend)
│   ├── Detangle.App/             Avalonia shared UI (views, viewmodels, theming) — no platform code
│   ├── Detangle.Desktop/         net10.0 entrypoint; optional WebViewDiagramRenderer
│   └── Detangle.Browser/         net10.0-browser WASM entrypoint; BrowserJsDiagramRenderer
├── tests/
│   ├── Detangle.Core.Tests/      resolver golden tests, DBML parser tests
│   └── fixtures/vaults/          one synthetic vault per format (13) + a torture vault
├── samples/                      demo wiki shipped with the WASM demo
├── site/                         static HTML/CSS website (no framework)
├── docs/                         markdown docs, also the app's built-in help vault
└── .github/workflows/            ci.yml, release.yml, site.yml
```

**Rule:** `Detangle.Core` has zero Avalonia references. It must be usable as a headless library and testable without a UI. This is what makes the resolver golden-testable and what would later enable a `detangle lint` CLI.

### 4.1 Diagram rendering abstraction

One contract, **source string in → SVG string out**. The app never hosts a live WebView per diagram; it renders once, caches, and draws the SVG with Avalonia's Skia SVG control. Cache key = `sha256(kind + source + theme + backend)`, stored in the sidecar DB.

```
IDiagramRenderer
  Task<DiagramResult> RenderAsync(DiagramKind kind, string source, DiagramTheme theme, CancellationToken ct)
  // DiagramResult = { Svg, Width, Height, Diagnostics[] }

[1] MermaiderDiagramRenderer   DEFAULT. Mermaider 0.12.2, pure .NET, in-process,
                               ~23 µs for a simple flowchart, 24 diagram types,
                               15 themes, always-on SVG sanitization, AOT-clean.
                               Zero runtime dependencies.
[2] WebViewDiagramRenderer     OPT-IN "high-fidelity mode". One shared offscreen
                               Avalonia.Controls.WebView; mermaid.min.js 11.16.0
                               bundled as an embedded resource, served over a custom
                               app:// scheme. Exact mermaid.js/GitHub parity.
[3] BrowserJsDiagramRenderer   WASM demo only. [JSImport] to mermaid on the host page.
```

**Why Mermaider leads.** Avalonia 12's `Avalonia.Controls.WebView` is now MIT and first-party, so a WebView is *available* — but on Linux it requires `libwebkit2gtk-4.1-0` + `libsoup-3.0-0`, which breaks AppImage portability and is the single largest packaging risk in this plan. Leading with an in-process renderer means the default build has zero runtime deps on all three OSes, starts instantly, and stays Native-AOT-viable. The WebView backend is then a genuine feature ("render exactly like GitHub") rather than a load-bearing dependency, and it degrades gracefully: if the WebKitGTK probe fails at startup, the setting is disabled with an explanatory note rather than a crash.

**Accepted trade-off.** Mermaider uses Sugiyama layout, not dagre/ELK, so node placement differs visually from mermaid.js. It is also pre-1.0 (0.12.2), C4 `Rel_U`/`Rel_D` direction hints are ignored, and there is no diagram interactivity. Pin the version; keep a conformance corpus of diagrams under `tests/fixtures/diagrams/` rendered by both backends so divergence is visible in review.

### 4.2 DBML pipeline

```
```dbml fence
   -> DbmlLexer/DbmlParser  (Table, Ref, Enum, Project, TableGroup, Note, indexes, [pk] [unique] [not null] [default:] [ref:>])
   -> DbmlModel
   -> MermaidErdEmitter      (erDiagram; cardinality from > < - <>)
   -> IDiagramRenderer       (same path as ```mermaid)
```

Write the parser rather than depending on `Ivy.Dbml.Parser` (1.2.0, MIT, zero deps, but only ~1.5K downloads / 17 commits, with enums, `TableGroup`, sticky notes and `TablePartial` unverified). The DBML grammar is small — roughly 600–900 lines of hand-rolled recursive descent. Use Ivy's source as a reference and build a conformance suite from the spec's own examples first. This removes a bus-factor dependency from a core feature.

**Mermaid ERD is lossy** and that is fine if handled explicitly. Cardinality maps cleanly (`>` → `}o--||`, `<` → `||--o{`, `-` → `||--||`, `<>` → `}o--o{`), but enums, table groups, index definitions, colors/headers, sticky notes and default values have no `erDiagram` equivalent. Render those in a **table-detail side panel** beside the diagram rather than forcing them into it — clicking a table in the diagram focuses its full definition in the panel.

Also accept `.dbml` files as first-class documents in the tree — open one and it renders as a full-page ER diagram plus the detail panel. Parse errors render as an inline error card with line/column and the offending source line, never a blank block.

---

## 5. The link resolver (the centerpiece)

### 5.1 Indexes built at scan time

- **PathIndex** — normalized full vault-relative path → file
- **NameIndex** — normalized stem → [files]
- **AliasIndex** — frontmatter `aliases` + `title` + `id` + first H1 → [files]
- **AnchorIndex** — per file: raw heading text, github-slugger slug (with `-1`/`-2` dedup), `^blockid` markers, Logseq `id::` uuids

### 5.2 Normalization `N(s)`

Unicode-NFC → URL-decode (repeat while changing, max 2) → trim → strip known extension → `\`→`/` → lowercase → collapse `[\s_.]+` → `-` → collapse repeated `-` → trim `-`.

### 5.3 Ordered fallback chain

Stop at first hit. Each step records **which rule fired** into the resolution result.

1. Exact vault-relative path, as written (with and without `.md`)
2. Exact note-relative path (`./`, `../`, or bare relative to the linking file's dir)
3. Exact filename stem, case-sensitive, unique in vault
4. **Path-suffix match** — target matches trailing path segments of exactly one file (Foam's minimum-identifier rule; handles `[[folder/note]]`)
5. Case-insensitive stem match
6. **Normalized `N()` match** — absorbs spaces↔dashes↔underscores↔dots, `%20`, case in one step
7. Alias / title / H1 match
8. ID match — frontmatter `id`, or filename **prefix** match for Zettel timestamps
9. **Folder index** — `index.md` → `README.md` → `readme.md` → `_index.md` → `<foldername>.md`
10. Encoding variants — Logseq `___`→`/`, `%2F`→`/`, legacy `.`→`/`; Dendron dot-path; re-encode and retry
11. Extension probe — non-`.md` targets: basename search anywhere in vault (Obsidian attachment behavior)
12. Fuzzy / nearest (Levenshtein ≤2, or trigram) — **suggestion only in the UI, never automatic navigation**
13. **Placeholder** — distinctly-styled unresolved link with inbound-count and a "create note" affordance

### 5.4 Ambiguity policy

When a step yields >1 candidate: pick deterministically (**shortest path, then alphabetical** — matching Foam), navigate, and surface a persistent inline warning listing all candidates. Never silently drop. The user's disambiguation choice is remembered per-vault in the sidecar DB and takes priority on subsequent resolutions.

### 5.5 Resolution audit UI

Every rendered link carries a resolution provenance:

- **Steps 1–3** — normal link, no decoration
- **Steps 4–8** — small dotted underline; hover shows *"resolved by normalized-name match → concepts/attention.md"*
- **Steps 9–11** — dotted underline + subtle icon
- **Ambiguous** — warning icon, click opens a disambiguation picker with "remember this choice"
- **Step 13** — unresolved styling, click offers "create note" or "search for similar"

A status-bar counter shows `1,284 files · 3 broken · 7 ambiguous`, clicking opens the Link Doctor.

### 5.6 Anchors

After the file resolves: (a) exact raw heading text, case-insensitive, trimmed (Obsidian) → (b) github-slugger slug with `-1`/`-2` counters → (c) `^blockid` trailing-marker scan → (d) `((uuid))` via `id::` → (e) `#L10-L20` line range → (f) `#page=N` / `#height=N` for PDFs. **Anchor failure still navigates to the file, with a warning** — it must never fail the whole link.

### 5.7 Flavor profiles

Sniffer looks for `.obsidian/` · `mkdocs.yml` · `dendron.yml` · `logseq/config.edn` · `SUMMARY.md` · `src/SUMMARY.md` + `book.toml` · `_sidebar.md` · `docusaurus.config.*` · `.devin/wiki.json` · `wiki/SCHEMA.md` + `raw/` (LLM Wiki) · `quartz.config.ts` · flat timestamp-prefixed filenames (Zettel) · flat dot-hierarchy filenames (Dendron). Falls back to **Generic** (all steps enabled).

The profile controls which chain steps are enabled and their priority, plus the display-name rule (Dendron shows frontmatter `title`; Logseq de-escapes the filename), the callout dialect, and the nav source. Persisted in `.detangle/config.json` inside the vault (gitignore-friendly), overridable from the status bar. The sidecar SQLite DB (index, render cache, disambiguation choices) lives beside it as `.detangle/cache.db`.

---

## 6. Feature set

### 6.1 Reading

- 3-pane shell: left rail (Files / Tags / Search / Link Doctor / Graph), center document with tabs, right rail (Outline / Backlinks / Properties)
- Nav source per flavor: `mkdocs.yml nav:`, `SUMMARY.md`, `_sidebar.md`, `sidebars.ts`, `.devin/wiki.json` pages tree, LLM-Wiki `index.md` — falls back to the filesystem tree
- **Backlinks + unlinked mentions** (pages that name this page's title/alias without linking)
- **Hover page preview** popover on any wikilink
- Transclusion `![[note]]`, `![[note#heading]]`, `![[note#^block]]` — inlined, with a source chip and cycle detection
- Callouts: **both** `> [!note]` (Obsidian) and `!!! note` / `??? note` (MkDocs) dialects
- Math: `$inline$` and `$$block$$` via KaTeX
- Frontmatter rendered as a typed properties card; `key:: value` inline fields surfaced too
- Tag browser with nested `a/b` hierarchy
- Syntax-highlighted code fences; images/PDF/attachment resolution
- Back/forward navigation history, breadcrumbs, reading position memory per file
- Light/dark themes following the app palette (§9)

### 6.2 Search

SQLite FTS5 sidecar index. Live-as-you-type. Field-scoped query syntax: `type:concept`, `tag:llm/agents`, `updated>2026-06-01`, `path:wiki/entities/`, `"exact phrase"`. Results show heading context and highlight the term on the target page. Index built in background on first open, incrementally maintained by the file watcher.

### 6.3 Link Doctor

A dedicated panel — this is the differentiating feature. Findings, each with one-click actions:

| Finding | Action |
|---|---|
| Broken link | Fix to best fuzzy candidate · create the missing note · ignore |
| Ambiguous link | Pick canonical target · remember for vault |
| Orphan page (no inbound links) | Jump to page |
| Duplicate slug | Show all colliding files |
| Stale page (`updated` >90d with ≥3 inbound) | Jump |
| Oversized page (>400 lines soft, >800 hard) | Jump |
| Frontmatter issue (missing `title`, bad date, unknown `type`) | Jump |
| Non-canonical link (resolved by step ≥4) | Rewrite to the canonical form |

"Fix all safe" bulk action rewrites only step-4-through-8 resolutions to their canonical target, with a preview diff. Findings mirror the `llm-wiki` linter's categories so the app is a drop-in visual replacement for `wiki_lint.py`.

### 6.4 Graph view

Full-tab force-directed graph over the link graph. Node size = inbound count, color = frontmatter `type`. Local-graph mode (N hops from current page). Filters by tag/type/folder; orphans and broken-target placeholders shown distinctly. LOD/clustering above ~1,500 visible nodes so 5k files stays interactive. Click navigates; hover previews.

### 6.5 Light editing

Read-only by default. `Ctrl+E` toggles a split source/preview for the current page. Saves are explicit (`Ctrl+S`), atomic (write temp + move), and detect external modification since load. Link Doctor write-backs go through the same path. No note creation UI beyond "create missing note from a broken link" (which stubs a file with frontmatter from the vault's template, if one exists).

### 6.6 Export

- **Static HTML site** — self-contained, diagrams pre-rendered to inline SVG, prebuilt search index, all links rewritten to relative paths. This is a genuinely useful "publish my LLM wiki" button.
- **Single-file HTML** — one page or the whole vault inlined
- **PDF** — current page or a selected subtree, with a generated TOC
- **Markdown (normalized)** — rewrite every link to canonical relative paths; useful for handing a vault to another tool

---

## 7. Non-goals for v1

Sync, collaboration, plugins, mobile, AI/RAG features, full WYSIWYG editing, note creation workflows, templates engine, canvas, spaced repetition.

---

## 8. Delivery phases

| Phase | Deliverable | Exit criteria |
|---|---|---|
| **0 — Skeleton** | Repo, solution, CI green on 3 OSes, MIT license, empty Avalonia window opens | `dotnet build` + `dotnet test` pass in Actions matrix |
| **1 — Core & resolver** | `Detangle.Core` scan/parse/frontmatter/index; the 13-step chain; flavor sniffer; golden tests over 13 fixture vaults + torture vault | Resolver test suite green; every §3.2 edge case has a named test |
| **2 — Render** | Markdig→Avalonia renderer; code highlighting; callouts (both dialects); math; images/attachments; transclusion | Torture vault renders correctly end-to-end |
| **3 — Diagrams** | `IDiagramRenderer` + Mermaider backend + SVG display + render cache; DBML parser (conformance suite first) + Mermaid ERD emitter + table-detail panel; optional WebView backend behind a setting | Mermaid + DBML fences render with the network disabled and zero runtime deps; theme-aware; both backends agree on the conformance corpus |
| **4 — Shell** | 3-pane UI, tabs, nav sources, outline, backlinks, unlinked mentions, properties card, tag browser, hover previews, history, command palette, themes | Usable daily on a real LLM wiki |
| **5 — Search & Doctor** | FTS5 index, field query syntax, file watcher, Link Doctor panel with fix actions | 5k-file vault: cold index <5s, keystroke→results <50ms |
| **6 — Graph** | Force-directed graph, local mode, filters, LOD | 5k nodes interactive at 30fps+ |
| **7 — Editing & export** | Split edit, atomic save, external-change detection; HTML/PDF/static-site/normalized-md export | Round-trip a vault through export without link loss |
| **8 — Package & release** | Velopack installers + auto-update, AppImage, notarized `.app`, signed Windows build, release workflow | One tag produces all six RID artifacts; in-app update round-trips |
| **9 — Website & WASM demo** | `site/` landing + docs + changelog on `detangle.dev`; `Detangle.Browser` demo loading `samples/` | Deployed; demo loads and renders a Mermaid + DBML page in-browser |

| **10 — Ladder made actionable** | Two known defects (§14) fixed; Anchor Doctor, Settle, Triage deck (§15.1–15.3); the `.detangle` state store they share | A finding can be reviewed, previewed and applied one at a time; an ambiguous link remembers the reader's choice across a restart; `RememberedChoice` and `BrokenAnchor` both appear in the resolution audit |
| **11 — Ladder over time and off-app** | Regeneration Diff and `detangle lint` (§15.4–15.5) | Rescan a regenerated vault and get an accurate rule-delta; `detangle lint` produces the same findings as the panel, as JSON, with a settable failure severity |

Phases 1–3 are the load-bearing risk. Phase 6 and 9 are the marketing payload. Phases 0–9
shipped in v0.1.0. Phases 10 and 11 are complete on `main` and unreleased: both defects in
§14 are fixed and all five features in §15 are built, with the "shipped" note under each
recording what the specification got wrong.

---

## 9. Website

Deliberately modeled on **sendwire.dev** — hand-written static HTML + one commented CSS file, no framework, no build step, no third-party JS, strict CSP. That aesthetic *is* the positioning for an offline, local-first, zero-dependency tool.

### 9.1 Design system (adapted from the teardown)

```css
:root{
  --bg:#0a0c11; --panel:#15181f; --elevated:#1c212b; --raised:#232935; --sunken:#070910;
  --border:#20252f; --border-strong:#323a4a;
  --text:#e6e9ef; --dim:#8a92a3; --faint:#5c6473;
  --accent:<pick>; --accent-hover:<lighter>;
  --green:#3fb950; --orange:#d29922; --red:#f85149; --purple:#a371f7;
  --shadow:rgba(0,0,0,.55); --glow:rgba(<accent-rgb>,.16);
  --radius:10px; --page:1120px;
}
```
Light theme mirrors it (`--bg:#f6f7f9`, `--panel:#fff`, `--text:#1b1f27`, `--border:#dfe3ea`). Semantic colors are GitHub Primer values. **Use a different accent than sendwire's `#4a8cf7`** so it reads as a sibling, not a clone — a warm amber or a teal against the same near-black.

Typography: **zero webfonts**, system stacks only (`-apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, …` / `ui-monospace, "SF Mono", "JetBrains Mono", Menlo, Consolas, …`). Variable weights **550 / 630 / 660** rather than 500/600/700. `h1: clamp(42px,6.6vw,78px)/1.04, -.035em, 660`. `h2: clamp(28px,3.8vw,41px)/1.15, -.025em, 630`. Body `16px/1.6`. Mono labels get `+.09em` tracking and uppercase; display type gets negative tracking scaled to size; body copy untracked.

Spacing scale `8/14/18/22/40/58/76/88`; sections `padding:76px 0` separated only by `border-top:1px solid var(--border)` — no background alternation. Radii 10px cards, 12px hero shot, 8px buttons, 999px pills.

Motion: **no keyframes, no scroll reveals, no parallax.** Only `.15s` transitions on `border-color`, `transform:translateY(-2px)` card hover, and button background. Global `prefers-reduced-motion` override. Depth comes from static radial `--glow` gradients behind the hero and screenshot, plus the screenshot bottom-fade (`.shot::after` → `linear-gradient(transparent, var(--bg))` over the bottom 22%). Sticky 60px header with `color-mix(in srgb, var(--bg) 88%, transparent)` + `backdrop-filter: blur(12px)`.

### 9.2 Page structure

```
header (sticky, blurred, anchors + theme toggle; no hamburger below 720px)
hero        centered, max-width 780px:
              eyebrow pill  "Markdown wiki viewer · macOS, Windows, Linux"
              h1            "The wiki reader that <em>untangles</em> your links."
              capability    "Mermaid, DBML, backlinks and a graph — offline, from any folder."
              lede          "An LLM writes [[Attention Is All You Need]]; the file is
                             attention-is-all-you-need.md. Detangle resolves it, tells you
                             which rule it used, and lists every link it couldn't."
              [Download ▾] then micro-line: Free · Offline · No account · MIT
              full-bleed screenshot (max-width 1240px, wider than --page)
#links      "Your links don't have to be perfect."          <- the differentiator, first
#diagrams   "Mermaid and DBML, rendered offline."
#formats    "It already knows what kind of wiki this is."   <- 13-format grid
#graph      "See the shape of what the model wrote."
#search     "Find it before you finish typing."
#doctor     "Every broken link, in one list."
#demo       "Try it without installing anything."           <- live WASM demo embed
#craft      "Built like the tool it is."                    <- specs strip: no network,
                                                               no telemetry, no account,
                                                               your wiki is just files
#download   closing CTA + illustration bleeding off the bottom edge
footer      one row: logo · © · Docs / GitHub / Releases
```

Every section: `.section-head` (h2 + one paragraph on a 660px left rail) then a `.split` (terminal panel + `.points` list), `.cards` 3-col, or `.specs` 4-cell hairline stat strip. Hero centered, everything below left-aligned — deliberate asymmetry.

### 9.3 Elements to steal specifically

- **Eyebrow pill carries the SEO category**, h1 carries the positioning — neither compromises
- **One accent `<em>` word in the h1**, matching the accent in the wordmark
- **Micro-line under the CTA**: `Free · Offline · No account · MIT` — middot objection-handling in one line
- **Copy-command panel**: fake window chrome (three 9px dots + a title), `margin-left:auto` Copy button, `user-select:none` on the `$ ` prompt span, ~12 lines of JS, swaps to "Copied" for 1500ms, fully functional with JS disabled
- **OS panels that work with JS off** — macOS/Linux/Windows stack with `border-top` dividers; a `.js-tabs` class converts them to tabs only when JS runs. Install command first, direct download second, footnotes (`chmod +x`, `sudo apt install ./…`, flatpak) third
- **"Read `install.sh` before you run it"** link beside any `curl | sh` line
- **Figure captions naming a checkable number visible in the screenshot**, not restating the heading
- **`.specs` strip whose values aren't all numbers** — "No network", "13 formats", "5,000 files", "0 accounts"
- **`.doclink` grid** — nine plain links to real doc pages, so skeptics can evaluate depth before downloading
- **Theme toggle done right** — pre-paint inline script kills FOUC, `localStorage` key, *no stored value means follow OS* via `:root:not([data-theme])` inside the media query, listens to `mq.change`

Deliberately absent, matching the reference: pricing table, testimonials, logo wall, newsletter capture, cookie banner, hamburger menu, animated gradients, grid background, scroll reveals, chat widget, "trusted by".

### 9.4 The terminal-panel content

Instead of a wire log, the signature panel shows **resolution provenance** — the product's actual output:

```
[[Attention Is All You Need]]     -> entities/attention-is-all-you-need.md   normalized-name
[[getting started#What's next?]]  -> docs/getting-started.md#whats-next      case + slugified-anchor
[Setup](setup)                    -> docs/setup/index.md                      folder-index
![[diagram.png|300]]              -> assets/diagram.png  (width 300)          basename probe
[[transformer]]                   -> AMBIGUOUS: concepts/…, entities/…        needs a choice
[[dose-response]]                 -> unresolved                               3 inbound
```

Color-coded per resolution class, exactly the way sendwire colors DNS/TCP/TLS/send/recv. This single panel communicates the whole product.

### 9.5 Hosting

Static, served from `site/` via GitHub Pages (or Cloudflare Pages) with a `_headers`/CSP config: `default-src 'none'; script-src 'self' <sha256 hashes>; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; base-uri 'none'; frame-action 'none'; frame-ancestors 'none'` + HSTS + permissions-policy. The WASM demo relaxes `script-src`/`wasm-unsafe-eval` on its own path only. Download buttons resolve the latest release via a build-time-baked version string (no runtime GitHub API call, keeping CSP `connect-src 'none'`).

---

## 10. CI/CD

`.github/workflows/`:

- **`ci.yml`** — on PR/push: matrix `{windows-latest, ubuntu-latest, macos-latest}` × `dotnet build` + `dotnet test`. Format check (`dotnet format --verify-no-changes`). Resolver golden tests are the gate.
- **`release.yml`** — on tag `v*`, matrix by RID **on the matching runner** (macOS artifacts must be built on macOS to codesign/notarize):

  | RID | Runner | Artifact |
  |---|---|---|
  | `win-x64`, `win-arm64` | `windows-latest` | Velopack installer `.exe` + portable zip |
  | `linux-x64`, `linux-arm64` | `ubuntu-latest` | `.deb` + **AppImage** + tar.gz |
  | `osx-x64` | `macos-13` (Intel) | signed+notarized `.app` in a `.dmg` |
  | `osx-arm64` | `macos-latest` (Apple Silicon) | signed+notarized `.app` in a `.dmg` |

  Publish flags: `-c Release -r <rid> --self-contained /p:PublishSingleFile=true /p:PublishTrimmed=true`. macOS signing imports the p12 into a temp keychain (`apple-actions/import-codesign-certs`), then `codesign --deep --options runtime` → `xcrun notarytool submit --wait` → `xcrun stapler staple`. An aggregate job collects artifacts, generates the changelog from conventional commits, publishes checksums, and creates the release via `softprops/action-gh-release@v2`. Velopack also publishes the update feed consumed by the in-app updater.

- **`site.yml`** — on push to `main` touching `site/**` or `src/Detangle.Browser/**`: build the WASM demo, assemble `site/`, deploy to Pages. Bakes the current release version into the download buttons at build time so the page needs no runtime network access (keeps `connect-src 'none'`).

Ongoing cost: **$99/yr** Apple Developer Program + **$9.99/mo** Azure Trusted Signing. Linux is free. Everything else in the stack is $0.

---

## 11. Stack

All MIT/BSD except where noted. Versions verified August 2026.

| Layer | Choice | Version | License |
|---|---|---|---|
| Runtime | **.NET 10 LTS** (rel. Nov 2025, support to Nov 10 2028) | 10.0.11 | MIT |
| UI | **Avalonia** (targets .NET 10, SkiaSharp 3) | 12.1.1 | MIT |
| Markdown parse | **Markdig** — explicit pipeline, not `UseAdvancedExtensions()` | 1.3.2 | BSD-2 |
| Document render | Markdig AST → **Avalonia control tree** (in-house) | — | — |
| Diagrams (default) | **Mermaider** — pure .NET Mermaid → SVG, AOT-verified, 24 types | 0.12.2 | MIT |
| SVG display | **Svg.Controls.Skia.Avalonia** (⚠️ *not* the deprecated `Avalonia.Svg.Skia`) | 12.0.0.15 | MIT |
| Diagrams (opt-in) | **Avalonia.Controls.WebView** + bundled **mermaid.min.js** | 12.1.0 / 11.16.0 | MIT |
| Code highlighting | **TextMateSharp** + `TextMateSharp.Grammars` (VS Code grammars/themes, ~200 langs) | 2.0.4 | MIT |
| Edit pane | **Avalonia.AvaloniaEdit** + `AvaloniaEdit.TextMate` | 12.0.0 | MIT |
| DBML | **Own recursive-descent parser** → Mermaid `erDiagram` (Ivy.Dbml.Parser 1.2.0 as reference) | — | MIT |
| Search | **SQLite FTS5** via `Microsoft.Data.Sqlite` (`e_sqlite3` ships FTS5) | 10.0.11 | MIT |
| Math | **KaTeX**, lazy-loaded — via Markdig `UseMathematics()` + render pass | current | MIT |
| File watch | `FileSystemWatcher` + debounce + periodic reconcile | BCL | — |
| Packaging + update | **Velopack** — Win/macOS/Linux installers *and* delta auto-update, one tool | 1.2.0 | MIT |
| Linux extra | **AppImage** via `kuiperzone/Publish-AppImage` | — | — |
| CI | GH Actions matrix + `softprops/action-gh-release@v2` | — | — |

**Explicit rejections, with reasons:**

- **Markdown.Avalonia** — Avalonia 12 support is *alpha only* (12.0.0-a3); ships its own non-Markdig regex parser (double maintenance); no Mermaid; single-maintainer; `.Html` variant uses HtmlAgilityPack and is AOT-hostile.
- **Lucene.NET** — stable is 3.0.3 from *2012*; the usable 4.8.0 has been in beta since 2014, still blocked on ICU4N. Not a serious option.
- **ColorCode.Core / ColorCode-Universal** — dormant since July 2023, ~15 languages, weak grammars. `Markdown.ColorCode.Avalonia` does not exist on NuGet.
- **CefGlue.Avalonia** — bundles full CEF, +150–250 MB per platform.
- **WebViewControl-Avalonia** (OutSystems) — maintainers state no intention to support Linux.
- **mermaid-cli (mmdc)** — requires Node + Puppeteer + Chromium (~180 MB).
- **Naiad** — MIT source but an Open Source Maintenance Fee EULA applies to binary use in revenue-generating activity *and all government use*. A licensing landmine; Mermaider is plain MIT.
- **Avalonia Parcel** — Community edition is non-commercial-only, current-platform-only, GUI-only, 200 MB cap; **the CLI you'd need for CI is paid**. Velopack covers the same ground for free.
- **Native AOT in v1** — Avalonia's XAML/binding layer is reflection-heavy; AOT failures are *silent* (blank windows). Ship self-contained + `PublishTrimmed` + `PublishSingleFile` instead (~35–45 MB). Revisit AOT once dependencies are frozen — Mermaider being AOT-clean keeps that door open.

**Mandatory hygiene from day one:**
- `AvaloniaUseCompiledBindingsByDefault=true` and `x:CompileBindings="True"` everywhere — required for any future trimming/AOT, and it catches binding typos at compile time.
- Delete the template's default `ViewLocator.cs` (`Activator.CreateInstance` is trim-hostile) — use an explicit switch.
- Markdig `DisableHtml()` or an allowlist sanitizer on untrusted vault content. If the WebView backend is ever enabled: `securityLevel:'strict'`, `default-src 'none'` CSP, block all remote navigation, never load from a CDN.

### 11.1 Known risks and mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| **Mermaider is pre-1.0**; Sugiyama layout differs visually from mermaid.js/GitHub | Medium | Pin the version; dual-render conformance corpus in `tests/fixtures/diagrams/`; WebView fidelity mode as the escape hatch; contribute upstream |
| Avalonia **Wayland is private preview** → XWayland fractional-scaling blur on modern Linux desktops | Medium | Ship X11, document it, track upstream |
| Linux **WebKitGTK/libsoup-3** deps if the WebView backend is enabled | Medium | Not on the default path; runtime probe at startup, disable the setting with an explanatory note if missing; declare deps in `.deb`, never in the AppImage |
| **Avalonia 12 breaking changes** (`netstandard2.0` dropped, `SystemDecorations`→`WindowDecorations`, `BinaryFormatter` removed) strand third-party controls | Medium | Audit every dependency for a 12.x release before adding it |
| **inotify watch limits** on Linux (`max_user_watches` default 8,192) blow up on a large vault | Medium | Non-recursive per-subdir watches, skip `.git`/`node_modules`, catch the error and surface the exact `sysctl -w fs.inotify.max_user_watches=524288` remedy |
| **macOS `FileSystemWatcher` is kqueue-based**, one fd per file → "too many open files" | Medium | Debounce + periodic reconcile now; P/Invoke `FSEventStreamCreate` later if latency complaints arrive |
| `FileSystemWatcher` **silent event loss** (Windows buffer overflow, macOS coalescing) | Medium | `InternalBufferSize = 64KB`; handle the `Error` event by recreating the watcher and full-rescanning against the SQLite `mtime`+`hash` table; 30–60s reconcile sweep regardless |
| macOS **notarization is a hard $99/yr gate** — unsigned `.app` is effectively unlaunchable | Medium | Budget it; automate `notarytool` in CI from day one, not at release time |
| Windows SmartScreen reputation with a non-EV cert | Low | Azure Trusted Signing ($9.99/mo); reputation accrues over releases |
| Own DBML parser diverges from `@dbml/core` semantics | Low | Conformance suite built from the spec's examples before writing the parser |

---

## 12. Brand

**Detangle** — `detangle.dev`, registered.

The name does real work: it states the differentiator (link resolution) rather than the category (markdown viewer), it's a verb so it implies action, and it sets up the entire copy voice. Everything else follows from it.

| Asset | Value |
|---|---|
| Wordmark | `de`**`tangle`** — the second half in `--accent`, matching the sendwire `<em>` treatment |
| Repo / binary / CLI | `detangle` |
| .NET root namespace | `Detangle.*` |
| Sidecar directory | `.detangle/` (`config.json`, `cache.db`) |
| Domain | `detangle.dev` |
| Tagline | *Read the wiki the model actually wrote.* |
| One-liner | *A markdown wiki viewer that resolves the links your generator got wrong.* |

**Mark:** a knot resolving into a straight line, or a tangle of edges where one path is highlighted clean — reads at 20px in the header tile and scales to the favicon. Deliberately not another document/page icon.

**Voice, inherited from the sendwire teardown:** headlines are complete sentences with a period; concessive framing (name what everyone else does, then reverse it); specificity as the proof mechanism — `13 formats`, `5,000 files`, `0 network calls`, `23 edge cases`, never "blazing fast". The product's own resolution log is the hero visual (§9.4).

Accent color: pick a warm amber or teal against the near-black so it reads as a sibling of sendwire's palette, not a clone of its `#4a8cf7`. Amber suits "untangling" better than blue and is uncommon in this category.

---

## 13. Verification

- **Resolver golden tests** — 13 fixture vaults, one per format, plus a "torture vault" containing every §3.2 edge case. Each case asserts the resolved path *and the rule that fired*. This suite is the project's spine; nothing merges if it's red.
- **DBML conformance tests** — written *before* the parser, from the spec's own examples (tables, aliases, all column settings, `Ref` forms and cardinalities, `Enum`, `Indexes`, `TableGroup`, `Note`, sticky notes, multi-line strings, `TablePartial`), plus malformed input asserting graceful error cards with line/column.
- **Diagram conformance corpus** — `tests/fixtures/diagrams/` rendered by both Mermaider and the WebView backend; divergence is reviewed, not silently accepted. Guards the pre-1.0 dependency.
- **Zero-dependency assertion** — a CI job installs the Linux artifact on a minimal container *without* `libwebkit2gtk-4.1-0` and asserts the app launches and renders a Mermaid diagram. This is the check that keeps the packaging story honest.
- **Render snapshot tests** — Markdig AST → expected control tree for callouts (both dialects), transclusion, math, tables, embeds.
- **Performance gates in CI** — generate a synthetic 5,000-file vault; assert cold scan+index <5s, search keystroke→results <50ms, graph frame time at 5k nodes.
- **Manual cross-platform smoke** — open a real Obsidian vault, a real LLM Wiki (`wiki/` + `raw/`), and an MkDocs tree on Windows, Linux, and macOS; confirm flavor detection, diagram rendering offline (with the network disabled), and export round-trip.
- **Website** — Lighthouse ≥95 on all four axes; verify with JS disabled that OS panels, copy commands, and all nav still work; verify CSP with no console violations.

---

## 14. Known defects

Found by a review pass after v0.1.0, verified against the source. Neither is speculative;
both have a named reproduction. Fixed as the first work of phase 10, because two of the
phase-10 features build directly on top of them.

### 14.1 `ApplyRewrite` rewrites the wrong link when a line has two links to the same target

`LinkDoctor.ApplyRewrite` (`src/Detangle.Core/Diagnostics/LinkDoctor.cs:385`) locates the
link to replace with `line.IndexOf(resolution.Link.RawTarget)` — the *first* occurrence on
the line. A line such as

```markdown
See [the spec](spec) and, for the older revision, [the spec](spec).
```

produces two findings on the same line with the same `RawTarget`, and both rewrites land
on the first link. The second link is never fixed and the first is rewritten twice.

The fix is already paid for: `LinkReference.Column`
(`src/Detangle.Core/Linking/LinkReference.cs:69`) is the 0-based column where the link
starts, set from the Markdig source offset at `DocumentParser.cs:115` and `:140`, and
`ApplyRewrite` ignores it. Search from `Column` rather than from 0, and treat a target that
is not found at or after that column as the "not where expected" case the method already
returns null for. Test: two same-target links on one line, assert the second finding
rewrites the second link.

This ships in v0.1.0 and fails silently — "fix all safe" reports the count it intended to
write, not the count it wrote correctly.

### 14.2 The command palette and search results have no keyboard path

`ShellView` wires `PaletteList.SelectionChanged` and `SearchList.SelectionChanged` straight
to invoke (`src/Detangle.App/ShellView.axaml.cs:582` and `:591`). Selecting an entry *is*
running it, so arrow-key navigation cannot exist: moving the highlight would fire the first
entry the highlight touched.

In practice the lists are mouse-only. `Ctrl+K` focuses `PaletteBox`
(`ShellView.axaml.cs:330`); arrow keys there move the caret inside the `TextBox` and never
reach the `ListBox`, and there is no `Enter` handler on either the box or the list —
`OnWindowKeyDown` (`ShellView.axaml.cs:637`) handles `Escape` for the palette and nothing
else. A palette you must click is not a command palette.

The fix is a highlight/commit split: keep `PaletteBox` focused, handle `Up`/`Down`/`Enter`
on the box by moving `PaletteList.SelectedIndex`, and invoke only on `Enter` or on
`PointerReleased`; drop the `SelectionChanged` handlers. The same shape applies to
`SearchList`. Any new shortcut must be gated on `IsKeyboardFocusWithin` for its panel;
`OnWindowKeyDown` fires window-wide and would otherwise eat keystrokes in `SearchBox` and in
the `Ctrl+E` editor.

---

## 15. Post-1.0 — the resolution ladder, made actionable

v1 shipped a 14-step resolution chain that can say *how* every link resolved. Nothing else
in the category has that, because every competitor's link is binary: it works or it does
not, so none of them can represent a link that still works but works *worse*. Everything in
this section is that same claim applied at a different range — below the page, at the
reader's judgment, at the review surface, over time, and at the machine that wrote the wiki.
They share plumbing deliberately: **one** JSON writer in `Detangle.Core`, **one** `.detangle`
state store, **one** finding pipeline.

Constraints unchanged from section 4: offline by default, must build for the WebAssembly
head, trimming-safe with `TreatWarningsAsErrors`, no JavaScript host, and no
reflection-based `JsonSerializer` (it fails the browser head's `TrimMode=full` build).
`Detangle.Core` stays Avalonia-free.

### 15.1 Anchor Doctor — section-level link integrity

When a page links to a heading that does not exist, name the heading it probably meant
instead of landing the reader at the top of the page.

The diagnosis already exists and is reported nowhere: `AnchorResolver`
(`src/Detangle.Core/Linking/AnchorResolver.cs:89`, `:109`) builds
`new AnchorResolution(AnchorRule.Unresolved, Warning: "No heading or block …")` and no
consumer reads `Warning`. Section 5.6 says an unresolved anchor still navigates to the file,
which stays true — this makes the silent part visible.

- Add `BrokenAnchor` (Warning) and `AnchorDialectDrift` (Info) to the **tail** of
  `FindingKind` (`LinkDoctor.cs:8`). There is no exhaustive `switch` over it in `src/` or in
  the axaml, so appending is safe.
- An `ExamineAnchors` pass reads `LinkResolution.Anchor` (populated at `LinkResolver.cs:508`)
  and scores the fragment against `VaultDocument.Headings` using the **static**
  `HeadingSlugger.SlugCore` — not the instance `Slug`, which mutates dedup state — plus
  `VaultIndex.EditDistance`.
- Self-references need no special handling, contrary to the review that proposed this:
  `[[#Heading]]` resolves to its own document with `Rule = ExactVaultPath`, so it arrives
  in the pass with a target like any other link. A page-local heading typo — the
  commonest case of this defect — is caught without reordering anything.
- Do **not** widen `SafeToFix` (`LinkDoctor.cs:402`) to include anchor guesses. An
  edit-distance-2 heading match is not a one-correct-answer rewrite, so the guess is
  carried on its own `Finding.SuggestedAnchor` rather than on `SuggestedRewrite`, and
  applying it waits for the per-finding action in 15.3.
- `ApplyRewrite` will need an anchor-aware branch when that action lands: the fragment is
  not part of `RawTarget`, and for a self-reference `RawTarget` is `""`. Section 14.1 has
  already made the empty case refuse rather than splice at column 0.

Effort S. Risk: dialect-drift false positives. `AnchorResolver` tries `RawHeading` before
`HeadingSlug`, so `#overview` against `## Overview` already resolves; the drift rule must
require that the fragment is *not* already a valid slug for the matched heading, or it will
"fix" links that work everywhere. Ship `BrokenAnchor` first, `AnchorDialectDrift` behind it.

**Shipped.** Both kinds report, `SuggestFix` names the nearest heading on demand, and the
Doctor panel shows the guess. `FixAllSafe` also had to learn to rewrite a line's links
right to left: with the destination now located from its recorded column, fixing a
left-hand link first moves every column to its right, and a canonical target shorter than
what it replaced left the next link unfixed.

### 15.2 Settle — the click that makes an ambiguous link yours

When a link could mean three files, show the three and remember which one the reader picked,
for this vault. Section 5.4 promises ambiguity is surfaced rather than guessed; this is where
the reader's judgment enters the chain.

The Core half is built and dead. `LinkResolver` short-circuits on a remembered choice
(`LinkResolver.cs:83`), `ResolutionRule.RememberedChoice = 14` exists with its explanation
string at `LinkResolution.cs:144`, and both `LinkGraph.Build(vault, rememberedChoices)`
(`Graph/LinkGraph.cs:51`) and `RenderModelBuilder` take the dictionary. Nothing in
`Detangle.App` ever supplies one.

- A `ChoiceStore` keyed with `LinkResolver.ChoiceKey(sourceDirectory, rawTarget)` — **not**
  raw target alone. The key is `{DirectoryPath} {NormalizePath(target)}`
  (`LinkResolver.cs:44`, `:83`), and `LinkResolution` only carries `Link.SourcePath`, so the
  store must derive `VaultDocument.DirectoryPath` the way `VaultScanner.cs:164` does, or look
  the source up in the index. Get this wrong and the store silently never hits.
- A `Func<LinkResolution, Control?> ActionFactory` hook beside the existing `PreviewFactory`
  (`Rendering/Controls/DocumentRenderer.cs:66`), supplied by `ShellView.axaml.cs` next to
  `BuildPreview`, so `Detangle.Rendering` stays App-free. Open it from the existing `Click`
  handler in `LinkControl` (`DocumentRenderer.cs:961`), gated on non-exact resolutions. Do
  not set `Button.Flyout` — that breaks navigation on every ambiguous link.
- Thread the store through **all** construction sites: `ShellViewModel.cs:244`, `:252`,
  `:487`, `:524`. `ApplyTheme` rebuilds `_builder` but not `_graph`, and `OnVaultChanged`
  rebuilds `_graph` with no choices, so a watcher rescan would drop every choice.

Effort L standalone, M once the store in 15.4 is shared. Risk: where the state lives.
`<vault>/.detangle/choices.json` mildly bends the read-only stance — though
`.detangle/cache.db` is already written there (`Search/SearchIndex.cs:51`) and `.detangle` is
scan-ignored (`VaultScanner.cs:20`). On a browser vault or a `StorageVaultImporter` copy
(`IsDetachedCopy`, writes refused at `ShellEditing.cs:89`) the choice must visibly degrade to
session-only rather than claim a save that dies with the tab.

**Shipped.** `ChoiceStore` writes `.detangle/choices.json` whole-then-move, reads it with
`JsonDocument` rather than the reflection serializer the browser head's `TrimMode=full`
would reject, and treats an unreadable file as no choices rather than a broken vault. The
picker is a small count marker rendered *after* an ambiguous link rather than a flyout on
the link itself, so the link still navigates. `RebuildBuilder` exists because the theme
switch and a settled choice both rebuild the render model and only one of them used to
carry the choices. A detached copy remembers for the session and says so.

### 15.3 Triage deck — the Link Doctor as a review you finish

One finding at a time, showing the exact before/after line it would write, with fix, skip, or
"this one is fine, stop telling me". This is the surface the rest of the section acts on:
15.1's anchor findings, 15.2's picker ("pick canonical target · remember for vault" belongs
on the same card), and 15.4's "since last run" filter.

This is unbuilt spec, not a new idea — section 6.3 already specifies per-finding
fix/create/ignore and a preview diff for "fix all safe". What shipped is a flat `ListBox` and
two global buttons (`ShellView.axaml:176`), where `FixAllButton` writes immediately and
reports a count afterwards (`ShellView.axaml.cs:617`, `ShellViewModel.cs:598`).

Everything needed is public, tested and unused: `LinkDoctor.SuggestFix` (`:340`),
`ApplyRewrite` (`:365` — pure, content in and content out, so the diff preview is free),
`CanonicalTargetFor` (`:328`), `Finding.SuggestedRewrite`.

- A two-level model behind a `TreeView`. Avalonia has no `ICollectionView` grouping, so
  grouping by kind is hand-rolled.
- A card in the panel footer showing source line vs rewritten line, computed lazily per
  selected finding against `ReadContent`.
- An ignore list keyed on **relative path + `Resolution.Link.RawTarget` + kind, never on line
  number**. Line numbers move on every regeneration, which is precisely this product's corpus.
- Gate every new shortcut on `FindingList.IsKeyboardFocusWithin` — see section 14.2.

Effort L. Risk: it writes to a corpus the product promises not to touch, so it must be
visibly opt-in and refuse on detached copies the way `ShellEditing.cs:89` already refuses
saves. Expect the diff card to expose 14.1 immediately; fix that first and write the test.

**Shipped.** The panel is a `TreeView` over `FindingGroup`, one group per kind, worst
severity first, with an action card docked to the bottom rather than a dialog — triage is
look, decide, next, and a dialog puts a click between every finding and the one after it.
`PreviewFix` returns the before and after line straight out of `ApplyRewrite`, so the
preview *is* the fix. `ApplyFix` writes one finding and still refuses anything with no
suggested rewrite, so a near-miss guess cannot be applied by accident. The ignore list
lives in the same `.detangle/state.json` as the remembered choices — `ChoiceStore` became
`VaultState` rather than growing a second sidecar file, which is the unification 15.4
needs anyway — and is keyed on path, kind, target and fragment, never on the line number.
A test regenerates the file with every line moved and asserts the ignore holds.

### 15.4 Regeneration Diff — the ladder gets a time axis

After the generator runs again, say which links that used to resolve cleanly now resolve only
because the chain rescued them. "12 links that resolved by `ExactVaultPath` now resolve by
`FuzzyNearest`" is a sentence no other tool in the category can produce. In the app it is a
"since last run" filter on the Triage deck and a delta on the status bar's link summary.

- New `src/Detangle.Core/History/VaultSnapshotRecord.cs` plus
  `VaultDelta.Compare(previous, current)`. The record stays small: per document a
  `RelativePath` and a normalized content hash (NFC + line endings — mtime and size cannot
  carry "rewritten", a generator stamps fresh mtimes on byte-identical files, and a Windows
  git checkout would otherwise report the whole vault rewritten), plus a per-link tuple of
  `(Line, RawTarget, ResolutionRule, Target.RelativePath)` taken straight off
  `LinkGraph.Resolutions` (`Graph/LinkGraph.cs:47`).
- Persist through an `IVaultStateStore` in Core, with a JSON-file implementation under
  `.detangle/` and an in-memory one for WASM and tests. **This store is the single home for
  15.2's remembered choices, 15.3's ignore list, the baseline snapshot, and the unsaved
  `VaultProfile.IsUserOverride` flag** — four promised-but-missing pieces of per-vault state
  landing once.
- Core needs its own JSON writer *and* a parser that never throws on a truncated or foreign
  file. `JsonText` is `internal` to `Detangle.Rendering` (`Export/JsonText.cs:16`) and only
  quotes strings; move it down into Core (Rendering already references Core) rather than
  copying it. The repo's only JSON-reading precedent is `JsonDocument.Parse`
  (`NavigationTree.cs:268`). Reflection-based `JsonSerializer` will fail the browser head's
  build.

Effort L. Risk: **nothing tells Detangle a generation run happened.** `VaultWatcher` debounces
at 250 ms and will fire dozens of times mid-run, so auto-snapshotting on rescan diffs against
a half-written corpus. This needs an explicit baseline moment — a "mark current state" action,
or a quiet-period threshold — designed before any code is written. Secondary: identity across
renames must match on frontmatter `id`, then normalized stem, never on path, and it can still
be wrong.

**Shipped**, with the baseline moment answered the blunt way: a "Mark this state" button, and
no automatic snapshot at all. A quiet-period threshold was rejected — it guesses at when a
generator finished, and a guess that is wrong produces a diff against half a corpus, which
is worse than no diff. `VaultSnapshotRecord` and `VaultDelta` live in `Detangle.Core/History`;
the baseline is a third section of `.detangle/state.json` rather than a fourth sidecar file,
so `VaultState` is now the single home the section called for. Documents are compared by a
SHA-256 of the text with line endings and Unicode composition normalized, since a generator
stamps fresh times on files it rewrote identically and a Windows checkout of a Linux-written
repository differs in every line ending. With no baseline the delta is empty rather than
"the whole vault is new". `ShowOnlyChanged` is the "since I last marked" filter on the triage
deck.

### 15.5 `detangle lint` — the report the generator can read

Run the same Link Doctor from a terminal or CI and get machine-readable, rule-attributed
findings back. This moves the audience from the human reading the wiki to the agent that
wrote it.

The differentiating payload is *not* "broken links found" — `mkdocs --strict` and Docusaurus'
`onBrokenLinks: 'throw'` have done that for years, and both `lychee` and `markdownlint-cli2`
already emit JSON and SARIF. It is the `NonCanonicalLink` set plus a histogram of
`ResolutionRule` over `LinkGraph.Resolutions`, grouped by source folder: "every link in `raw/`
needed step-10 encoding-variant rescue." That is feedback a generator can act on, and it
requires the 14-step ladder to exist.

- `LinkDoctor.Examine` is already a pure function of `(LinkGraph, contentReader, options)` and
  `Detangle.Core` has zero Avalonia references — `Detangle.Core.csproj:5` says in-file that
  this is "so a `detangle lint` CLI stays possible later". Compose `VaultScanner.Scan`,
  `LinkGraph.Build`, `LinkDoctor.Examine`.
- Put the serializer in Core as `Diagnostics/FindingsReport.cs`, so the WASM head can produce
  the identical report from `ShellViewModel.Findings`.
- Name: `Detangle.Desktop.csproj:6` already claims `<AssemblyName>detangle</AssemblyName>` and
  `release.yml` ships it, so either name the new head `detangle-lint` or put `lint` behind the
  existing binary beside `--export-site`. A real console head is worth having regardless:
  `Detangle.Desktop` is `OutputType=WinExe`, so `Console.Error` from `HeadlessExport` goes
  nowhere on Windows.
- Exit codes: only `BrokenLink` is `FindingSeverity.Error` (`LinkDoctor.cs:154`), so "exit 1
  on Error" means "exit 1 on broken links only" and the interesting finding is advisory. Ship
  `--fail-on <severity>` or nobody will ever gate on it.

Effort M. Risk: trim surface. Core drags `YamlDotNet` and `Microsoft.Data.Sqlite` for code
paths lint never touches, and `Detangle.Desktop.csproj:36` already documents having to set
`ILLinkTreatWarningsAsErrors=false` and root `YamlDotNet` whole. Promise a trimmable console
app, not AOT, or the first `dotnet publish` fails. Resist scope creep toward export and graph
dump in v1.

**Shipped** as `src/Detangle.Lint`, a real console head named `detangle-lint`, because
`Detangle.Desktop` is `OutputType=WinExe` and anything it wrote to `Console.Error` on
Windows would go nowhere. `FindingsReport` lives in Core so the browser head can produce
the identical document, and writes through `Utf8JsonWriter` with the relaxed encoder — no
`JsonText` move was needed, and the strict encoder's `\uXXXX` escaping turns a readable
report into noise for no benefit in a file. `--fail-on` takes `error` (default), `warning`,
`info` or `never`; the default alone would never notice the interesting findings, since
`BrokenLink` is the only `Error`. Every finding is suggested, unlike in the panel, because
a report has no reader who will open one later. It rides in the portable archive rather
than the installers: those are for the reader, this is for the pipeline.

### 15.6 Considered and deliberately left out

- **Hint mode** (two-letter tags on every link, Vimium-style). The differentiation claim is
  false: `obsidian-jump-to-link`, `azim58/link-hints` and Vimium all ship this, and the WASM
  head is a Skia canvas where a browser extension cannot help — the opposite of a moat. What
  was real inside it is section 14.2, filed as a defect.
- **Page Map** (marker rail beside the scrollbar). The scrollspy half is shipped by Logseq,
  MkDocs Material and Docusaurus, and the rail is a decade-old VS Code idiom. Its one useful
  increment — click a Doctor finding, land on it — belongs to 15.3 and gets built there.
- **Twin Finder** (near-duplicate page detection by shingling). The strongest omission and the
  closest call: a real failure of agent-written corpora, and no competitor ships an offline
  duplicate *audit*. Deferred because it is L, the whole feature is threshold tuning against
  heavily templated generated text, it re-reads the vault a second time on a doctor pass
  already held to a 30 s budget, and — decisively — it is the one candidate that does not
  touch the resolution ladder, so it compounds with nothing else here. Revisit after 15.4.
