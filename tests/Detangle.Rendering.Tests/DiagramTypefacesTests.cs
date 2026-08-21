using Detangle.Rendering.Diagrams;
using SkiaSharp;
using Svg.Skia;
using Xunit;

namespace Detangle.Rendering.Tests;

/// <summary>
/// Tests for the diagram font lookup.
/// <para>
/// The defect it fixes cannot be reproduced here — it needs a platform with one font,
/// which no desktop is. What can be checked on any platform is the property the fix rests
/// on: this lookup answers every family with a face, where Svg.Skia's own answers some
/// families with nothing. A provider that returns null for a name is the whole bug.
/// </para>
/// </summary>
public class DiagramTypefacesTests
{
    [Theory]
    [InlineData("sans-serif")]
    [InlineData("monospace")]
    [InlineData("Inter, 'Segoe UI', sans-serif")]
    [InlineData("Nothing By This Name Exists")]
    [InlineData("")]
    public void TheLookupAlwaysResolvesToAFace(string family)
    {
        SKTypeface? resolved = Resolve(family);

        Assert.NotNull(resolved);

        // A face that reports no glyphs would satisfy "not null" and still draw nothing,
        // which is the failure mode this exists to prevent rather than a milder version
        // of it.
        Assert.True(resolved.GlyphCount > 0, $"\"{family}\" resolved to a face with no glyphs.");
    }

    [Fact]
    public void TheLookupGoesInFrontOfTheOnesThatCanAnswerNothing()
    {
        var settings = new SKSvgSettings();

        int built_in = settings.TypefaceProviders?.Count ?? 0;

        DiagramTypefaces.Install(settings);

        // Order is the point. Svg.Skia's providers return null rather than declining, and
        // a chain stops at the first non-null answer — so a provider added after them
        // would never be reached for exactly the families that need it.
        Assert.Equal(built_in + 1, settings.TypefaceProviders?.Count);
        Assert.NotNull(Resolve("sans-serif", settings.TypefaceProviders![0]));
    }

    [Fact]
    public void InstallingTwiceLeavesOneOfThem()
    {
        var settings = new SKSvgSettings();

        DiagramTypefaces.Install(settings);
        int installed = settings.TypefaceProviders!.Count;
        DiagramTypefaces.Install(settings);

        // The render path installs before every diagram rather than at startup, because a
        // head that forgets a startup call fails silently and only in a browser.
        Assert.Equal(installed, settings.TypefaceProviders.Count);
    }

    [Fact]
    public void EveryDeliveryOfAFamilyDrawsAdvancingGlyphs()
    {
        // On a healthy platform this passes with or without the fix, so it is not evidence
        // that the fix works — tools/wasm-selftest.py is, and it reports 16 of 16 in the
        // browser where the unfixed renderer reports 4. What this pins is that installing
        // the lookup does not break the platform that was never broken.
        IReadOnlyList<SvgTextSelfTest.Sample> samples = SvgTextSelfTest.Run(DiagramTypefaces.Install);

        Assert.All(samples, sample => Assert.Equal("advancing", sample.Verdict));
    }

    private static SKTypeface? Resolve(string family, Svg.Skia.TypefaceProviders.ITypefaceProvider? provider = null)
    {
        if (provider is null)
        {
            var settings = new SKSvgSettings();

            DiagramTypefaces.Install(settings);
            provider = settings.TypefaceProviders![0];
        }

        return provider.FromFamilyName(
            family,
            SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
    }
}
