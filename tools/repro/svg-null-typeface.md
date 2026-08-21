# `FontManagerTypefaceProvider` returns null on single-font platforms, and text then draws with zero advance

**Status: draft. Not filed anywhere.** Written for wieslawsoltes/Svg.Skia.

> **Two corrections against earlier drafts of this note, both from measurement.** The first
> claimed the collapse was specific to a family arriving through a CSS rule, and named this file
> after that claim; a sixteen-cell matrix measured in the browser shows a presentation attribute
> and an inline `style=` attribute collapsing exactly as far as a `<style>` block does. The
> second guessed the cause was HarfBuzz returning zero advances; it is not, and the section that
> said so has been replaced by a measured mechanism. The file was renamed from
> `svg-css-font-family.md` once the cause was settled, since the old name asserted the wrong one.

## Summary

Under `browser-wasm`, `<text>` renders with zero glyph advance — every glyph of the string is
painted at the same x — whenever the document names a font family. The same document with no
family named renders correctly, and every variant renders correctly on desktop. Nothing throws,
no font fails to resolve, and `MeasureText` returns correct numbers on the same code path that
draws wrong.

The cause is not the drawing. Both built-in typeface providers resolve the face, apply an
acceptance test that no single-font platform can pass, and return null — and a null typeface is
what stacks the glyphs. Inserting one provider that always answers fixes it completely, with
the same face, on the same platform.

Two elements reproduce it.

## Minimal reproduction

Broken (`browser-wasm` only) — and equally broken with the family delivered as
`font-family="sans-serif"` on the element, or as `style="font-family: sans-serif"`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" width="64" height="32">
  <style>text { font-family: sans-serif; }</style>
  <text x="2" y="24" font-size="24" fill="#ffffff">MM</text>
</svg>
```

Correct everywhere — the same document with the family removed:

```svg
<svg xmlns="http://www.w3.org/2000/svg" width="64" height="32">
  <text x="2" y="24" font-size="24" fill="#ffffff">MM</text>
</svg>
```

The only difference is whether a family is named.

## Measured evidence

Each variant is rendered through `SKSvg` into an `SKBitmap` and the horizontal span of inked
columns measured. The measurement is a *ratio*, not a pixel count: each cell draws `M` and
`MMMM` in one style and divides the second span by the first, so the test calibrates against a
single glyph in its own style and stays comparable across sizes. Four advancing letters score
about 4; four letters stacked at one position score about 1. Nothing lands in between.

Measured in the browser, headless, Edge 151.0.4129.93 — and reproduced identically on
Chrome 151.0.7922.170, so it is not one vendor's quirk:

| family delivered via | size | weight | `M` span | `MMMM` span | ratio | verdict |
|---|---|---|---|---|---|---|
| none | 12 | 400 | 6 | 27 | 4.50 | advancing |
| none | 12 | 700 | 6 | 28 | 4.67 | advancing |
| none | 24 | 400 | 11 | 55 | 5.00 | advancing |
| none | 24 | 700 | 13 | 56 | 4.31 | advancing |
| presentation attribute | 12 | 400 | 6 | 6 | 1.00 | **stacked** |
| presentation attribute | 12 | 700 | 6 | 8 | 1.33 | **stacked** |
| presentation attribute | 24 | 400 | 11 | 12 | 1.09 | **stacked** |
| presentation attribute | 24 | 700 | 13 | 14 | 1.08 | **stacked** |
| `style=` attribute | 12/24, 400/700 | | | | 1.00 / 1.33 / 1.09 / 1.08 | **stacked** |
| `<style>` block | 12/24, 400/700 | | | | 1.00 / 1.33 / 1.09 / 1.08 | **stacked** |

Four of sixteen variants draw advancing glyphs, and they are exactly the four that name no
family. The three deliveries produce **byte-identical spans** at every size and weight.

The same binary, same source, same rasteriser on desktop (`net10.0`, win-x64): 16 of 16
advancing, ratios 4.33 to 5.00.

An earlier bisect, before the matrix existed, measured absolute spans on a longer string and
saw 122 px collapse to 7 px. That is the same finding at one cell's worth of resolution.

## Already ruled out, and how

| hypothesis | how it was ruled out |
|---|---|
| it is the CSS cascade | the presentation attribute and the `style=` attribute collapse to identical spans — see the table above |
| the font does not resolve | the WebAssembly runtime resolves `sans-serif` to a real face — 897 glyphs — and reports it by name |
| metrics are wrong | `SKFont.MeasureText("Attention")` returns 72 px in the same runtime, correctly |
| the SVG document differs between platforms | the wasm build generates a **byte-identical** SVG to the desktop build — 2526 bytes, viewBox 439.76 x 146.4 — so no upstream layout code is involved |
| the HarfBuzz managed/native version gap | `HarfBuzzSharp.NativeAssets.WebAssembly` floated to 8.3.1.3 against managed `HarfBuzzSharp` 14.2.0. A real mismatch, and it has been pinned to 14.2.0 — but pinning it did not change the rendering |
| no fallback font registered | registering a bundled font with the SVG renderer as a fallback did not change the rendering |
| an exception being swallowed | nothing throws at any point in the parse, the `SKPicture` build or the draw |

## Only browser-wasm

Desktop (`win-x64`, .NET 10) renders every variant above correctly. The same source, the same
package versions and the same `SKSvg` call produce the collapse only when the RID is
`browser-wasm`. That is the whole shape of the bug: it is not a document problem, it is not a
font-resolution problem, and it does not exist off the web target.

## Mechanism

Two parts. The first is settled; the second is a strong reading of the source that has **not**
been observed running under WebAssembly, and should be read as a lead rather than a diagnosis.

### Settled: there is no CSS code path to blame

Svg erases the cascade into presentation attributes during parsing, before the model is built.
`SvgElement.FlushStyles` walks the collected styles and calls
`SvgElementFactory.SetPropertyValue(..., isStyle: true)`, which lands in the ordinary setter
`FontFamily { set { Attributes["font-family"] = value; } }`. Parsing both spellings and dumping
the result confirms it: a `<style>` rule and a presentation attribute both end up as
`text.FontFamily == "sans-serif"` read back out of `Attributes`, not out of the style
collection.

Dumping the intermediate ShimSkiaSharp model for six variants — style block, presentation
attribute, inline `style=`, generic and named families — produces the identical single command
in all of them:

```
DrawText text="MM" x=2 y=36 paint=[face=Arial size=24 align=Left enc=Utf16 ...]
```

with an identical 37 px ink span. By the time the renderer looks, the CSS is gone. This is
what makes the browser matrix's result inevitable in hindsight, and it is also why rewriting
CSS declarations into presentation attributes is not a workaround: the parser already does
exactly that rewrite, one layer earlier.

The only model-level difference between "no family" and "some family" is the resolved face
name in the paint — a string, on the same command at the same coordinates.

### Settled: both built-in typeface providers return null for every family

`SKSvgSettings` installs `FontManagerTypefaceProvider` and `DefaultTypefaceProvider`, in that
order, and a lookup takes the first non-null answer. Both apply the same acceptance test to the
face they resolved:

```csharp
val = fontManager.MatchFamily(text, style);        // or SKTypeface.FromFamilyName(...)
if (val != null)
{
    bool flag  = defaultFamilyName.Equals(text, OrdinalIgnoreCase);        // asked for the default by name
    bool flag2 = val.FamilyName.Equals(text, OrdinalIgnoreCase);           // got what was asked for
    bool flag3 = defaultFamilyName.Equals(val.FamilyName, OrdinalIgnoreCase); // got the default back
    bool flag4 = IsGenericFamilyName(text);                                // "sans-serif", "monospace", ...

    if (flag2 || (flag & flag3) || (flag4 && !flag3)) break;               // accept
    val.Dispose();
    val = null;                                                            // reject
}
```

On a platform with exactly one font, that test can never pass, and the generic-name escape
hatch is what closes it. Under `browser-wasm` every family resolves to the single embedded
face, Noto Mono:

- `flag2` is false — "Noto Mono" is not "sans-serif".
- `flag` is false — the document did not ask for "Noto Mono" by name.
- `flag4` is true, but `flag3` is *also* true, because the resolved face **is** the platform
  default. `flag4 && !flag3` is therefore false.

So both providers reject and return null, and the drawing that follows a null typeface is the
one that paints every glyph at the same point. Nothing throws; the face resolves; `MeasureText`
returns correct numbers on the same face. That is why every cheaper diagnostic answered wrongly.

The rule `flag4 && !flag3` reads as "a generic name is satisfied by anything except the
default", which is a reasonable preference on a system with many fonts and an impossibility on
a system with one. The last resort of a generic lookup should be the default face, not nothing.

### Measured, in the browser

Same face — "Noto Mono", 897 glyphs, 107,848-byte stream — across three paths. Ratio is the ink
span of `MMMM` over the ink span of `M`: about 4 when glyphs advance, about 1 when they stack.

| path | ratio | verdict |
|---|---|---|
| `SKCanvas.DrawText` directly, no SVG | 4.82 | advancing |
| SVG, `font-family="sans-serif"`, stock providers | 1.09 | **stacked** |
| SVG, same document, one permissive provider inserted at index 0 | 5.00 | advancing |

The third row is the proof: it changes nothing but the lookup, keeps the same face, and draws
correctly. It was also run as the very first drawing of the process, to rule out its passing
because an earlier lookup had already disposed the shared typeface.

Across the full sixteen-cell matrix — four deliveries x two sizes x two weights — stock
providers draw 4 of 16 correctly (only the rows that name no family at all) and the permissive
provider draws 16 of 16.

### Suggested fix

In both providers, accept the resolved face when the requested name is generic and nothing
better was found, rather than requiring it to differ from the default. Equivalently: never
return null from the last provider in the chain when a face was resolved. A caller that gets
null has no typeface, and no typeface is not a degraded rendering — it is an unreadable one.

The complete downstream workaround is one provider:

```csharp
public SKTypeface? FromFamilyName(string fontFamily, SKFontStyleWeight weight,
    SKFontStyleWidth width, SKFontStyleSlant slant)
{
    foreach (string name in fontFamily.Split(',').Select(x => x.Trim().Trim(''', '"')))
    {
        if (SKTypeface.FromFamilyName(name, weight, width, slant) is { } typeface) return typeface;
    }

    return SKTypeface.FromFamilyName(null, weight, width, slant) ?? SKTypeface.Default;
}
```

## Exact versions

From `Directory.Packages.props` (central package management; these are the resolved versions,
not floating ranges):

| package | version |
|---|---|
| `Svg.Skia` | 5.2.1 |
| `Svg.Controls.Skia.Avalonia` | 12.0.0.15 |
| `SkiaSharp` | 4.148.0 |
| `SkiaSharp.NativeAssets.WebAssembly` | 4.148.0 |
| `HarfBuzzSharp` | 14.2.0 (transitive, via Svg.Skia) |
| `HarfBuzzSharp.NativeAssets.WebAssembly` | 14.2.0 |
| `Avalonia` / `Avalonia.Browser` | 12.1.1 |

### Why these versions and not others

`Avalonia.Browser` 12.1.1 declares `SkiaSharp 3.119.4` and `HarfBuzzSharp 8.3.1.3` in its
nuspec. `Svg.Skia` 5.2.1 declares `SkiaSharp 4.148.0` and `HarfBuzzSharp 14.2.0` in its nuspec.
NuGet resolves the higher of each, so the managed libraries in the app are SkiaSharp 4.148.0
and HarfBuzzSharp 14.2.0. The WebAssembly native assets are then pinned to match, because a
mismatch there is not a compile error but a `wasm-ld` failure with undefined native symbols.

So the SkiaSharp 4.148.0 + Svg.Skia 5.2.1 pairing is not an unusual choice — it is exactly the
pairing `Svg.Skia` 5.2.1's own nuspec declares, on every one of the five target frameworks it
ships. What is unusual is running that pairing on `browser-wasm` underneath Avalonia 12:
Svg.Skia's own CI (`.github/workflows/build.yml`) has no WebAssembly leg, and its only
wasm-targeting sample (`samples/UnoSvgSkiaSample`, `net10.0-browserwasm`) takes its native
assets from the Uno SDK rather than from an explicit pin. Svg.Skia's nuspec also ships
HarfBuzzSharp native assets for Linux, Win32 and macOS only — the WebAssembly consumer has to
supply that themselves, which is the link-step forcing described above.

Newer releases exist that have not been tried: SkiaSharp and
`SkiaSharp.NativeAssets.WebAssembly` are at 4.151.1, and
`HarfBuzzSharp.NativeAssets.WebAssembly` at 14.2.1.2.

## Environment

- .NET SDK 10.0.302
- Target framework `net10.0-browser`, `RuntimeIdentifier` `browser-wasm`,
  SDK `Microsoft.NET.Sdk.WebAssembly`, `wasm-tools` workload installed
- Host that built it: Windows 11
- Desktop comparison: same solution, `win-x64`, .NET 10, identical package versions
- Renderer entry point in both cases: `Svg.Skia.SKSvg.FromSvg(string)` then
  `SKCanvas.DrawPicture`

## How to reproduce it in one command

The downstream project automated this, because the check that nobody re-runs is the check that
stops being true — which is most of why the defect survived as long as it did:

```
python tools/wasm-selftest.py                  # publish, serve, drive a headless Chromium, report
python tools/wasm-selftest.py --no-build       # reuse the last publish, seconds not minutes
python tools/wasm-selftest.py --keep-serving   # same, then leaves the server up to look yourself
```

It installs nothing: the browser is whichever Edge or Chrome is already on the machine, driven
over the DevTools Protocol by Node's built-in WebSocket. Exit code 0 means every variant drew
advancing glyphs, 1 means at least one collapsed — this defect — and 2 means the demo never
reported at all.

## Smallest program that shows it

Renders the probe and prints the ink span. Run once on desktop and once in the browser; the
desktop print is a two-glyph span, the browser print is a one-glyph span.

```csharp
using SkiaSharp;
using Svg.Skia;

const string Probe =
    """
    <svg xmlns="http://www.w3.org/2000/svg" width="64" height="32">
    <text x="2" y="24" font-size="24" fill="#ffffff" font-family="sans-serif">MM</text>
    </svg>
    """;

using var svg = new SKSvg();
svg.FromSvg(Probe);

using var bitmap = new SKBitmap(64, 32);
using (var canvas = new SKCanvas(bitmap))
{
    canvas.Clear(SKColors.Black);
    canvas.DrawPicture(svg.Picture);
}

int first = -1, last = -1;
for (int x = 0; x < bitmap.Width; x++)
{
    for (int y = 0; y < bitmap.Height; y++)
    {
        if (bitmap.GetPixel(x, y).Red > 100)
        {
            if (first < 0) first = x;
            last = x;
            break;
        }
    }
}

Console.WriteLine($"ink span: {last - first}px");
```

Delete the `font-family` attribute and the browser number becomes the desktop number. Moving
that family into a `<style>` block or a `style=` attribute changes nothing in either place.

## Related upstream issues

Searched wieslawsoltes/Svg.Skia, mono/SkiaSharp and AvaloniaUI/Avalonia, open and closed, via
the GitHub search API and the web. **No existing report describes this symptom** — zero glyph
advance under wasm with a resolved family. The nearest neighbours:

- mono/SkiaSharp **#1902** — *SKTypeface.FromFamilyName does not change the font or text style
  in SkiaSharp.Views.Blazor* (open since 2021, 14 comments). Explains why `sans-serif` resolves
  to an unexpected face under wasm: Skia's font manager has no system fonts there. It is about
  the *wrong face*, not about *zero advances*, so it is background rather than a duplicate.
- mono/SkiaSharp **#3841** — *Uno Gallery sample: Uno SDK injects SkiaSharp 3.x native assets,
  causing WASM link errors and Resizetizer crash* (open). Same class of link-step version
  forcing described above, from the other side.
- wieslawsoltes/Svg.Skia **#544** — *Update SkiaSharp to 4.148.0 and HarfBuzzSharp to 14.2.0*
  (merged 2026-07-01, shipped before v5.2.1 on 2026-08-12). The change that moved the renderer
  from `SKPaint` to `SKFont` as the font carrier, and rebuilt HarfBuzz shaping, measurement and
  advance caching on `SKFont` — the subsystem this report lands in. It also notes that
  SkiaSharp 4 "on some platforms ignores the requested style when a font family is
  null/empty/**generic**".
- wieslawsoltes/Svg.Skia **#543** — *Live text renders with tighter glyph spacing than browser*
  (closed 2026-08-05). Desktop, not wasm, and a spacing difference rather than a collapse — but
  the same subsystem, and fixed in the release line this report is filed against.
- wieslawsoltes/Svg.Skia **#511** — *Fix empty CSS declarations overriding presentation
  attributes* (closed). Cited in the first draft as evidence the two paths can disagree; the
  matrix above shows they do not disagree here.
- wieslawsoltes/Svg.Skia **#251** — *get custom typeface is no ok in blazor wasm environment*
  (closed). Typeface resolution under wasm; not a spacing report.
- AvaloniaUI/Avalonia **#15683** — *Custom font family is not working in WebAssembly* (closed
  2024-05-22). Avalonia's own text stack, not Svg.Skia's, and about shaping of Arabic/Persian.

## Downstream resolution

The permissive provider above is installed ahead of the built-ins for every diagram, which
fixes the rendering outright and keeps the requested typeface. The earlier workaround — strip
`font-family` and let the renderer draw with no face — is retained only as a fallback for a
platform where that does not take, because it costs the diagram its typeface. Nothing currently
measures as needing it.
