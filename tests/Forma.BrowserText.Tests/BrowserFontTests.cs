// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Forma.Tests;

public sealed class BrowserFontTests
{
    [Test]
    public void UsesRealFixedWidthAbiWithoutPinnedFontMemoryOrManagedNativePackages()
    {
        Assert.That(DynamicTextBackendRegistry.Backend.Name, Is.EqualTo("FreeType/HarfBuzz (Forma native ABI v1)"));
        var baseline = UIFontFace.NativeHandleCounts;
        for (var i = 0; i < 20; i++)
        {
            var source = File.ReadAllBytes(FontPath("Inter_Regular.ttf"));
            var face = UIFontFace.FromMemory(source);
            source.AsSpan().Clear();
            Assert.That(face.RasterizeCharacter('A', 24).Pixels.ToArray(), Has.Some.GreaterThan((byte)0));
            Assert.That(UIFontFace.NativeHandleCounts, Is.EqualTo((0, baseline.FreeTypeLibraries + 1, baseline.FreeTypeFaces + 1)));
            face.Dispose();
            face.Dispose();
            Assert.That(UIFontFace.NativeHandleCounts, Is.EqualTo(baseline));
            Assert.Throws<ObjectDisposedException>(() => face.Shape("disposed", 20));
        }
        Assert.That(typeof(UIFontFace).Assembly.GetReferencedAssemblies().Select(a => a.Name),
            Does.Not.Contain("FreeTypeSharp").And.Not.Contain("HarfBuzzSharp"));
    }

    [Test]
    public void FixedWidthStructsMatchNativeAbi()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Marshal.SizeOf<BrowserTextNative.FaceInfo>(), Is.EqualTo(40));
            Assert.That(Marshal.SizeOf<BrowserTextNative.Variation>(), Is.EqualTo(8));
            Assert.That(Marshal.SizeOf<BrowserTextNative.Feature>(), Is.EqualTo(8));
            Assert.That(Marshal.SizeOf<BrowserTextNative.Metrics>(), Is.EqualTo(24));
            Assert.That(Marshal.SizeOf<BrowserTextNative.Bitmap>(), Is.EqualTo(28));
            Assert.That(Marshal.SizeOf<BrowserTextNative.Glyph>(), Is.EqualTo(24));
            Assert.That(Marshal.SizeOf<BrowserTextNative.ShapeRequest>(), Is.EqualTo(IntPtr.Size == 8 ? 72 : 40));
            Assert.That(Marshal.SizeOf<BrowserTextNative.ShapeResult>(), Is.EqualTo(IntPtr.Size == 8 ? 16 : 12));
        });
    }

    [TestCase("en", "Hello café")]
    [TestCase("es-419", "¡Hola! ¿Cómo estás?")]
    [TestCase("pt-BR", "Olá, coração!")]
    public void LatinLocalesUseRealShapingAndRasterization(string locale, string text)
    {
        using var face = UIFontFace.FromMemory(File.ReadAllBytes(FontPath("Inter_Regular.ttf")));
        var run = face.Shape(text, 24, locale: locale);
        Assert.That(run.Glyphs.All(g => g.GlyphId != 0), Is.True);
        Assert.That(run.Glyphs.Select(g => face.RasterizeGlyph(g.GlyphId, 24)).Any(b => b.Pixels.Span.ContainsAnyExcept((byte)0)), Is.True);
    }

    [Test]
    public void ArabicBidiGraphemesAndFallbackKeepManagedLayoutPipeline()
    {
        using var latin = UIFontFace.FromMemory(File.ReadAllBytes(FontPath("Inter_Regular.ttf")));
        using var arabic = UIFontFace.FromMemory(File.ReadAllBytes(FontPath("NotoSansArabic_Variable.ttf")));
        var font = new DynamicUIFont(latin, 24, UIFontHinting.None, arabic);
        var layout = new TextLayoutEngine().Layout(font, "Hello مَرْحَبًا A\u0301", new TextLayoutOptions(locale: "ar"));
        Assert.That(layout.Runs.Any(r => r.Direction == TextDirection.RightToLeft), Is.True);
        Assert.That(layout.Runs.Any(r => r.Direction == TextDirection.LeftToRight), Is.True);
        Assert.That(layout.Runs.SelectMany(r => r.Glyphs).All(g => g.GlyphId != 0), Is.True);
    }

    private static string FontPath(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", name);

    [TestCase("A\u0301B", "en")]
    [TestCase("لَا مرحبا", "ar")]
    public void TypewriterRevealUsesAlreadyShapedRuns(string text, string locale)
    {
        using var latin = UIFontFace.FromMemory(File.ReadAllBytes(FontPath("Inter_Regular.ttf")));
        using var arabic = UIFontFace.FromMemory(File.ReadAllBytes(FontPath("NotoSansArabic_Variable.ttf")));
        var font = new DynamicUIFont(latin, 24, UIFontHinting.None, arabic);
        var engine = new TextLayoutEngine();
        var full = engine.Layout(font, text, new TextLayoutOptions(locale: locale));
        var partial = engine.Layout(font, text, new TextLayoutOptions(locale: locale, maxVisibleCharacters: 1));
        Assert.Multiple(() =>
        {
            Assert.That(partial.Runs.SelectMany(r => r.Glyphs).Select(g => (g.GlyphId, g.Position)),
                Is.EqualTo(full.Runs.SelectMany(r => r.Glyphs).Select(g => (g.GlyphId, g.Position))));
            Assert.That(partial.VisibleGlyphs.Count, Is.LessThan(full.VisibleGlyphs.Count));
            Assert.That(partial.VisibleRange.End, Is.GreaterThanOrEqualTo(2));
        });
    }
}
