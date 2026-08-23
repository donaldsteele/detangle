---
title: Keyboard
type: reference
updated: 2026-08-20
---

# Keyboard

On macOS, `Cmd` works everywhere `Ctrl` is listed.

| Keys | Does |
|---|---|
| `Ctrl+K` | Command palette — pages, headings and actions |
| `Ctrl+Shift+P` | The same palette |
| `Ctrl+F` | Find in the open page |
| `Ctrl+G` | Graph view |
| `Ctrl+E` | Split editor on the open page |
| `Ctrl+S` | Save, while editing |
| `Ctrl+B` | Navigation rail |
| `Ctrl+Shift+B` | Outline and backlinks panel |
| `Ctrl+W` | Close the open tab |
| `Alt+Left` | Back |
| `Alt+Right` | Forward |
| `Esc` | Close the palette, or the find bar |
| `Shift+F10`, `Menu` | The context menu for whatever is selected |

## Find in page

`Ctrl+F` opens a bar under the link ledger. `Enter` and `Shift+Enter` step forward and
back, `Aa` distinguishes case, and `Esc` closes it and clears the highlight.

It searches the page as rendered rather than the markdown behind it, so it finds a
wikilink by the label you can see rather than by the target you cannot. With the split
editor focused, `Ctrl+F` opens the editor's own search instead, so the shortcut never
means two things at once.

The palette matches pages by path and display name, headings in the open page, and a short
list of actions. It is deliberately short: a palette that lists every command in the app is
a menu with worse discoverability.

See also [[graph]] and [[editing-and-export]].
