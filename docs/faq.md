---
title: FAQ
type: reference
updated: 2026-08-20
---

# FAQ

## Does it phone home?

No. Detangle makes no network requests at all — not for diagrams, not for fonts, not for
telemetry, not for update checks unless you ask for one. Diagram rendering, search and
export all run in process.

## Does it change my files?

Only when you tell it to: saving in the editor, applying a Link Doctor fix, creating a
missing note, or normalizing links. Everything else is read-only.

The two commands that rewrite the whole vault — **Fix all safe** and **Normalize links in
place** — ask first. Each shows how many links in how many files it would change, lists
every file, and does nothing until you confirm; Cancel is the default. There is no undo
afterwards, which is why the list comes first.

Its index lives in a `.detangle/` folder inside the vault and can be deleted at any time.
Two files sit outside it, at the vault root, because they are decisions rather than cache
and are meant to be committed: `.detangle-choices` records which page an ambiguous link was
decided to mean, and `.detangle-baseline.json` records the state later regenerations are
measured against. Deleting either loses a decision; deleting `.detangle/` loses nothing.

## What does it not do?

Sync, collaboration, plugins, mobile, AI features, WYSIWYG editing, note-creation
workflows, a template engine, canvas, and spaced repetition. All deliberate. There are good
tools for each of those; this one reads wikis whose links do not line up.

## Why not just fix the links?

You can — that is what normalizing does, and it is one menu item. But a wiki being
generated continuously will keep producing new ones, and rewriting somebody's files should
be a decision rather than the price of reading them.

## Is my wiki locked in?

There is nothing to lock in to. A vault is a folder of markdown before Detangle opens it
and after it closes. The export that rewrites links writes plain markdown; the static site
export writes plain HTML.

## Which formats does it read?

Thirteen conventions, detected automatically — see [[formats]].

## Why does my system warn me when I open it?

Because the build is not signed. Code signing certificates are a recurring cost — Apple
charges ninety-nine dollars a year, and a Windows certificate is a monthly fee — and this
project does not take money, so it does not pay them.

Nothing is wrong with the download. The warning means the operating system cannot identify
who published it, not that it found a problem.

- **macOS**: right-click the app and choose Open, then confirm. Or run
  `xattr -dr com.apple.quarantine /Applications/Detangle.app`.
- **Windows**: SmartScreen shows "Windows protected your PC". Choose More info, then Run
  anyway.
- **Linux**: no warning; make the AppImage executable and run it.

Every release ships a `SHA256SUMS` file, so you can verify a download matches what the
build produced. The source is public and the build is one command, which is a stronger
guarantee than a certificate anyway.

## Why is the download 60 MB?

It is self-contained: the .NET runtime, Skia, the syntax-highlighting grammars and the
Mermaid renderer are all inside the binary. Nothing to install first, no runtime version
to match, and no dependency that can go missing.

## Native AOT?

Not yet. Avalonia's XAML and binding layer is reflection-heavy and AOT failures there are
silent — a blank window rather than an error. Trimmed self-contained builds are the
compromise until the dependency set is frozen.

## What about Wayland?

Detangle ships X11 and runs under XWayland, where fractional scaling can look soft.
Avalonia's Wayland backend is a private preview; this will change when it does.

See also [[index]].
