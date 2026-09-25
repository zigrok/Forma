using System;
using System.Linq;
using NUnit.Framework;

namespace Forma.Tests;

[TestFixture]
public class FontSynthesisTest
{
    private static int Ink(UIFontGlyphBitmap bitmap) => bitmap.Pixels.ToArray().Sum(pixel => pixel);

    [Test]
    public void SyntheticBoldAndObliqueChangeRealGlyphOutlines()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        var glyph = face.GetGlyphId('l');
        var regular = face.RasterizeGlyph(glyph, 48);
        var bold = face.RasterizeGlyph(glyph, 48, 1, UIFontHinting.Default, null, UIFontSynthesis.Bold);
        var oblique = face.RasterizeGlyph(glyph, 48, 1, UIFontHinting.Default, null, UIFontSynthesis.Oblique);
        Assert.Multiple(() =>
        {
            Assert.That(Ink(bold), Is.GreaterThan(Ink(regular) * 1.2));
            Assert.That(bold.Width, Is.GreaterThan(regular.Width));
            Assert.That(oblique.Width, Is.GreaterThan(regular.Width * 2));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                face.RasterizeGlyph(glyph, 48, 1, UIFontHinting.Default, null, (UIFontSynthesis)4));
        });
    }

    [Test]
    public void SynthesizedFontsHaveDistinctIdentityAndSurviveResizing()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        var regular = new DynamicUIFont(face, 24);
        var both = regular.WithSynthesis(UIFontSynthesis.Bold | UIFontSynthesis.Oblique);
        var resized = (DynamicUIFont)both.WithSize(36);
        Assert.Multiple(() =>
        {
            Assert.That(regular.WithSynthesis(UIFontSynthesis.None), Is.SameAs(regular));
            Assert.That(both.Identity, Is.Not.EqualTo(regular.Identity));
            Assert.That(both.Weight, Is.EqualTo(UIFontWeight.Bold));
            Assert.That(both.Style, Is.EqualTo(UIFontStyle.Oblique));
            Assert.That(resized.Synthesis, Is.EqualTo(both.Synthesis));
            Assert.That(resized.Identity, Is.EqualTo(both.Identity));
        });
    }
}
