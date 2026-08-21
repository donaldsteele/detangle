# Third-party notices

Detangle is MIT licensed. It redistributes the fonts below, which carry their own terms.

Package dependencies are not listed here — they are declared in
`Directory.Packages.props` and resolved from nuget.org rather than vendored into this
repository.

## Fonts

Both faces are compiled into `Detangle.Rendering` as Avalonia resources, in
`src/Detangle.Rendering/Assets/Fonts/`. They are bundled rather than looked up by name
because the WebAssembly build has no system fonts to search, and because the faces that
ship with desktop systems are missing much of the mathematical notation a wiki contains —
Segoe UI, Arial and Cascadia all draw an empty box for the transpose sign.

### DejaVu Sans Mono

Used for code, file paths and every other identifier.

- Upstream: <https://dejavu-fonts.github.io/>
- Licence: Bitstream Vera Fonts Copyright, with DejaVu changes released into the public
  domain. <https://dejavu-fonts.github.io/License.html>
- Copyright (c) 2003 by Bitstream, Inc. All Rights Reserved. Bitstream Vera is a
  trademark of Bitstream, Inc.

### DejaVu Math TeX Gyre

Used for mathematics.

- Upstream: <https://dejavu-fonts.github.io/>, derived from the TeX Gyre project
- Licence: Bitstream Vera Fonts Copyright; DejaVu changes are in the public domain, and
  the mathematical extensions derive from TeX Gyre under the GUST Font Licence.
  <https://dejavu-fonts.github.io/License.html>
- Copyright (c) 2003 by Bitstream, Inc. All Rights Reserved.

### Inter

Not vendored: it arrives through the `Avalonia.Fonts.Inter` package and is used for body
prose.

- Upstream: <https://rsms.me/inter/>
- Licence: SIL Open Font License 1.1

## Before publishing a release

The Bitstream Vera licence requires its full text to accompany redistribution. Add
`LICENSE-DejaVu.txt` beside the fonts, copied from the upstream project, and reference it
here. The summaries above are attribution, not a substitute for the licence text.
