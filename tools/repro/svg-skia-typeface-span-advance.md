# Text collapses to one point when character fallback finds no font

**Status: draft, filed nowhere.** Written for [wieslawsoltes/Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia).
A patch and a failing test are in `svg-skia-typeface-span-advance.patch`, and a runnable
reproduction is in `SvgTextWasmRepro/`.

> **This note has been wrong twice, and both corrections came from measuring rather than
> reading.** The first draft blamed a family arriving through a CSS rule; a sixteen-cell
> matrix in the browser showed a presentation attribute and an inline `style=` collapsing
> identically. The second blamed HarfBuzz returning zero advances, then Svg.Skia's typeface
> providers returning null; instrumenting the renderer showed the typeface is the *same
> object* in the working and broken cases. What follows is the third answer, and unlike the
> first two it is the one the instrumentation actually points at.

## Summary

`SkiaSvgAssetLoader.FindTypefaces` clears the running font's typeface whenever per-character
fallback finds no match:

```csharp
typeface = MatchCharacterForSpan(GetCodepoint(text, i, ch), out var familyOverride);
matchedShimTypeface = ToShimTypeface(typeface, requestedWeight, familyOverride);
// ...
runningFont.Typeface = typeface;     // typeface may be null
```

A font with no typeface has no metrics, so the `GetTextAdvance` call in
`YieldCurrentTypefaceText` returns `0` for every span produced from that point on. The
callers in `SvgSceneTextCompiler` position consecutive spans by accumulating exactly those
advances:

```csharp
canvas.DrawText(typefaceSpan.Text, currentX, anchorY, paint);
currentX += typefaceSpan.Advance;    // 0
```

So every span of the run is drawn at the same `x`, and the text renders as one dense mark.

Nothing throws, no font fails to resolve, and the typeface Svg.Skia resolves for the run is
correct — which is why every cheaper diagnostic answered wrongly.

## Reproduction, without a browser

This fails on Windows against `main`, and is included in the patch:

```csharp
var assetLoader = new SkiaSvgAssetLoader(new SkiaModel(new SKSvgSettings()));
var paint = new SKPaint { TextSize = 24f, Typeface = SKTypeface.FromFamilyName("sans-serif", ...) };

var spans = assetLoader.FindTypefaces("A\uE000B", paint);

Assert.All(spans, span => Assert.True(span.Advance > 0f));
// Error: span "" reported an advance of 0
```

`U+E000` is a private-use codepoint no installed font claims, so character fallback returns
null for it. Any such codepoint will do.

## Reproduction, where it is total

Under `browser-wasm` there is exactly one embedded face — Noto Mono — and the font manager
matches no family and no character query. So *every* character of *every* label takes the
path above, and every label in every document collapses. That is how this was found: Mermaid
diagram labels in a WebAssembly build rendered as single marks while the same code rendered
correctly on desktop.

`SvgTextWasmRepro/` is a self-contained reproduction: no Avalonia, no application code, just
SkiaSharp and Svg.Skia and a `Main` that prints a table.

```
dotnet publish tools/repro/SvgTextWasmRepro -c Release -o out
# serve out/wwwroot, open it, read the console
```

Each row draws `M` and `MMMM` in one style and reports the ratio of the horizontal extent of
the ink. Glyphs that advance score about four; glyphs painted on top of each other score
about one.

### Against Svg.Skia 5.2.1 and against `main` at `9ccef3e` — identical

```
case                                 M    MMMM   ratio   verdict
no font-family named                  11      55    5.00   advancing
font-family="sans-serif"              11      12    1.09   GLYPHS STACKED
font-family="Inter"                   11      12    1.09   GLYPHS STACKED
font-family="Inter, sans-serif"       11      55    5.00   advancing
SKCanvas.DrawText, no SVG             11      53    4.82   advancing
```

### With the patch

```
no font-family named                  11      55    5.00   advancing
font-family="sans-serif"              11      55    5.00   advancing
font-family="Inter"                   11      55    5.00   advancing
font-family="Inter, sans-serif"       11      55    5.00   advancing
SKCanvas.DrawText, no SVG             11      53    4.82   advancing
```

## How it was localised

Printing from inside the renderer, in the browser, for `font-family="Inter"`:

```
[probe] FindTypefaces("MMMM") shimFamily="Inter" runningTypefaceNull=False
        family="Noto Mono" handle=15172520 glyphs=897 size=24     <- healthy on entry
[probe] span "M" advance=0 runningFace=""                          <- and cleared by here
[probe] span "M" advance=0 runningFace=""
[probe] span "M" advance=0 runningFace=""
[probe] span "M" advance=0 runningFace=""
[probe] draw "M" via SHAPED blob at x=2 y=36, width=14.390625, face="Noto Mono"
[probe] draw "M" via SHAPED blob at x=2 y=36, width=14.390625, face="Noto Mono"
[probe] draw "M" via SHAPED blob at x=2 y=36, width=14.390625, face="Noto Mono"
[probe] draw "M" via SHAPED blob at x=2 y=36, width=14.390625, face="Noto Mono"
```

Four draws, one per character, every one at `x=2`. Each knows its own width — 14.39 — and
none of them is offset by it, because the advance the *caller* accumulates comes from the
spans, and those were measured with a font that had been emptied.

For contrast, `font-family="Inter, sans-serif"` yields one span with `advance=57.5625` and
draws once.

## Ruled out, each by measurement rather than by reading

- **The font.** The same face draws correctly through `SKCanvas.DrawText` with no SVG
  involved (ratio 4.82), and `SKFont.MeasureText` returns correct numbers on it.
- **HarfBuzz shaping.** The working and broken cases both take the shaped-blob branch, and
  the shaped width is correct in both — 14.39 for one `M`.
- **The resolved typeface differing between cases.** Instrumenting `CacheTypefaceResolution`
  shows the identical object in both: `"Noto Mono" handle=15172520 glyphs=897 upem=2048`.
- **`SKTypeface.Default` being disposed by a rejected lookup.** Drawing directly, and drawing
  a family-free document, both still advance correctly *after* a collapsing draw.
- **The CSS cascade.** `SvgElement.FlushStyles` writes cascaded declarations into
  `Attributes["font-family"]` during parsing, so a `<style>` rule and a presentation
  attribute are the same string by the time the renderer sees them, and they measure
  identically.

## A second, smaller thing

Both `FontManagerTypefaceProvider` and `DefaultTypefaceProvider` return null for *every*
family under `browser-wasm`, including `sans-serif`. The acceptance test cannot pass on a
single-font platform:

```csharp
var requestedGenericFamily = IsGenericFamilyName(fontFamilyName);   // true for "sans-serif"
var resolvedExplicitDefault = defaultName.Equals(skTypeface.FamilyName, ...);  // also true
if (!resolvedRequestedFamily &&
    !(requestedExplicitDefault && resolvedExplicitDefault) &&
    !(requestedGenericFamily && !resolvedExplicitDefault))   // generic escape hatch closed
{
    skTypeface.Dispose();
    skTypeface = null;
}
```

The rule reads as "a generic name is satisfied by anything except the default", which is a
reasonable preference where other fonts exist and an impossibility where they do not.

This is **not** what causes the collapse — the patch fixes the rendering without touching
these — so it is mentioned only because a maintainer looking at the area may want to keep the
preference while keeping the candidate as a last resort rather than discarding it. Happy to
send that separately if it is wanted.

## Versions

- Svg.Skia `5.2.1`, and `main` at `9ccef3e` — same behaviour
- SkiaSharp `4.148.0`, `SkiaSharp.NativeAssets.WebAssembly` `4.148.0`
- HarfBuzzSharp `14.2.0`, `HarfBuzzSharp.NativeAssets.WebAssembly` `14.2.0`
- .NET SDK `10.0.302`, `net10.0-browser` / `browser-wasm`
- Desktop repro: Windows 11, .NET 10, no WebAssembly involved

## Verification of the patch

- `dotnet build Svg.Skia.slnx -c Release` — 0 errors
- `dotnet test Svg.Skia.slnx -c Release` — all suites pass, 1112 in `Svg.Skia.UnitTests`
  against 1111 before, the extra one being the regression test
- `dotnet format Svg.Skia.slnx --no-restore` — clean
- The WebAssembly reproduction goes from 2 of 5 cases collapsed to 0 of 5
