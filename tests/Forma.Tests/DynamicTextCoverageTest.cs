// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Linq;


namespace Forma.Tests
{
    [TestFixture]
    public sealed class DynamicTextCoverageTest
    {
        private static UIFontFace Inter() => UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");

        private static UIFontFace Cjk() => UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansCJK_Subset.ttf");

        private static int KanjiOnlyIn(UIFontFace cjk, UIFontFace latin) =>
            cjk.GetSupportedCodePoints().First(codePoint => codePoint >= 0x4E00 && !latin.SupportsCharacter(codePoint));

        [Test]
        public void ACoveredString_HasNoMissingGlyphs()
        {
            using var face = Inter();

            Assert.That(DynamicTextCoverage.FindMissingGlyphs(new[] { face }, new[] { "Save and Exit", "Açúcar é doce" }), Is.Empty);
        }

        [Test]
        public void AUncoveredCharacter_IsReportedWithItsString_OncePerCodePoint()
        {
            using var face = Inter();
            using var cjk = Cjk();
            var kanji = char.ConvertFromUtf32(KanjiOnlyIn(cjk, face));

            var missing = DynamicTextCoverage.FindMissingGlyphs(new[] { face }, new[] { "Map " + kanji, kanji + kanji });

            Assert.Multiple(() =>
            {
                Assert.That(face.SupportsCharacter(char.ConvertToUtf32(kanji, 0)), Is.False);
                Assert.That(missing, Has.Count.EqualTo(1));
                Assert.That(missing[0].CodePoint, Is.EqualTo(char.ConvertToUtf32(kanji, 0)));
                Assert.That(missing[0].Text, Is.EqualTo("Map " + kanji));
                Assert.That(missing[0].ToString(), Does.Contain("U+" + missing[0].CodePoint.ToString("X4")));
            });
        }

        [Test]
        public void AFallbackFace_RestoresCoverage()
        {
            using var inter = Inter();
            using var noto = Cjk();
            var text = "Map " + char.ConvertFromUtf32(KanjiOnlyIn(noto, inter));

            var withoutFallback = DynamicTextCoverage.FindMissingGlyphs(new[] { inter }, new[] { text });
            var withFallback = DynamicTextCoverage.FindMissingGlyphs(new[] { inter, noto }, new[] { text });
            var font = new DynamicUIFont(inter, 16, UIFontHinting.Default, System.Array.Empty<UIFontVariationCoordinate>(), noto);

            Assert.Multiple(() =>
            {
                Assert.That(withoutFallback, Is.Not.Empty);
                Assert.That(withFallback, Is.Empty);
                Assert.That(DynamicTextCoverage.FindMissingGlyphs(font, new[] { text }), Is.Empty);
            });
        }

        [Test]
        public void ControlAndFormatCharacters_AreIgnored()
        {
            using var face = Inter();

            Assert.That(DynamicTextCoverage.FindMissingGlyphs(new[] { face }, new[] { "a\tb\nc\u200Bd\u202Be" }), Is.Empty);
        }
    }
}
