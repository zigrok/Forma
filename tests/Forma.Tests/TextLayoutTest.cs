// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Reflection;
using Forma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Forma.Tests
{
    public class TextLayoutTest
    {
        [TestCase(false)]
        [TestCase(true)]
        public void WrappedLabelMinimumHeightTracksShapedLinesAndResizing(bool dynamicFont)
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            UIFont font = dynamicFont ? new DynamicUIFont(face, 18) : new SpriteFontAdapter(CreateTestFont());
            var label = new Label
            {
                UIFont = font,
                Text = "Speaker\nA long paragraph with several words that must wrap onto multiple lines.",
                AutowrapMode = LabelAutowrapMode.WordSmart,
                Padding = new Thickness(2),
                ParagraphSpacing = 6,
                Size = new Vector2(84, 40)
            };
            var engine = new TextLayoutEngine();
            float Height(float width) => engine.Layout(font, label.Text,
                new TextLayoutOptions(maxWidth: width - 4, wrapping: TextWrapping.Word, paragraphSpacing: 6)).Size.Y + 4;

            var narrow = label.GetMinimumSize();
            Assert.That(narrow.X, Is.EqualTo(4));
            Assert.That(narrow.Y, Is.EqualTo(Height(84)).Within(.01f));
            label.Size = new Vector2(340, 40);
            Assert.That(label.GetMinimumSize().Y, Is.EqualTo(Height(340)).Within(.01f));
            Assert.That(label.GetMinimumSize().Y, Is.LessThan(narrow.Y));
            label.VisibleCharactersBehavior = LabelVisibleCharactersBehavior.CharactersAfterShaping;
            label.VisibleCharacters = 1;
            Assert.That(label.GetMinimumSize().Y, Is.EqualTo(Height(340)).Within(.01f));
            label.ClipText = true;
            Assert.That(label.GetMinimumSize().Y, Is.EqualTo(5));
        }

        [Test]
        public void SpriteFontAdapter_MatchesAsciiMeasurementAtLogicalSize()
        {
            var spriteFont = CreateTestFont();
            var adapter = new SpriteFontAdapter(spriteFont);
            var layout = new TextLayoutEngine().Layout(adapter, "Forma");

            Assert.That(layout.Size, Is.EqualTo(spriteFont.MeasureString("Forma")));
            Assert.That(layout.Lines, Has.Count.EqualTo(1));
            Assert.That(layout.GetCaretPosition(5).X, Is.EqualTo(layout.Size.X));
        }

        [Test]
        public void TextLayoutEngineReportsWarmCacheAndShapeDiagnostics()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var engine = new TextLayoutEngine();
            var font = new DynamicUIFont(face, 18);

            var cold = engine.Layout(font, "Forma diagnostics");
            var warm = engine.Layout(font, "Forma diagnostics");
            var diagnostics = engine.Diagnostics;

            Assert.Multiple(() =>
            {
                Assert.That(warm, Is.SameAs(cold));
                Assert.That(diagnostics.CacheEntries, Is.EqualTo(1));
                Assert.That(diagnostics.CacheMisses, Is.EqualTo(1));
                Assert.That(diagnostics.CacheHits, Is.EqualTo(1));
                Assert.That(diagnostics.CacheHitRate, Is.EqualTo(.5));
                Assert.That(diagnostics.LayoutTime, Is.GreaterThan(TimeSpan.Zero));
                Assert.That(diagnostics.ShapeTime, Is.GreaterThan(TimeSpan.Zero));
            });
        }

        [Test]
        public void SpriteFontAdapter_AppliesLogicalSizeAndWordWrapping()
        {
            var adapter = new SpriteFontAdapter(CreateTestFont(), 32);
            var options = new TextLayoutOptions(maxWidth: 64, wrapping: TextWrapping.Word);
            var layout = new TextLayoutEngine().Layout(adapter, "AA AA", options);

            Assert.That(layout.Lines, Has.Count.EqualTo(2));
            Assert.That(layout.Lines[0].Length, Is.EqualTo(3));
            Assert.That(layout.Lines[0].Size.Y, Is.EqualTo(32));
            Assert.That(layout.Size, Is.EqualTo(new Vector2(48, 64)));
        }

        [Test]
        public void SpriteFontAdapterRetainsSourceWhileTrimmingAtGraphemeAndWordBoundaries()
        {
            var font = new SpriteFontAdapter(CreateTestFont());
            var engine = new TextLayoutEngine();
            var characters = engine.Layout(font, "AB😀CD", new TextLayoutOptions(maxWidth: 40, trimming: TextTrimming.CharacterEllipsis));
            var words = engine.Layout(font, "one two three", new TextLayoutOptions(maxWidth: 64, trimming: TextTrimming.WordEllipsis, ellipsis: "..."));

            Assert.Multiple(() =>
            {
                Assert.That(characters.Text, Is.EqualTo("AB😀CD"));
                Assert.That(characters.Lines[0].VisibleRange, Is.EqualTo(new TextLayoutRange(0, 4)));
                Assert.That(characters.Lines[0].Ellipsis, Is.EqualTo("…"));
                Assert.That(characters.GetCaretPosition(4), Is.EqualTo(characters.GetCaretPosition(characters.Text.Length)));
                Assert.That(characters.GetPreviousGraphemeBoundary(characters.Lines[0].VisibleRange.End), Is.EqualTo(2));
                Assert.That(words.Lines[0].VisibleRange, Is.EqualTo(new TextLayoutRange(0, 3)));
                Assert.That(words.Lines[0].Ellipsis, Is.EqualTo("..."));
                Assert.That(words.Size.X, Is.LessThanOrEqualTo(64));
            });
        }

        [Test]
        public void TextLayout_UsesRetainedCaretsForHitTestingAndSelection()
        {
            var layout = new TextLayoutEngine().Layout(new SpriteFontAdapter(CreateTestFont()), "ABCD");

            Assert.That(layout.HitTest(new Vector2(17, 4)), Is.EqualTo(2));
            Assert.That(layout.GetSelectionRectangles(1, 2), Has.Count.EqualTo(1));
            var selection = layout.GetSelectionRectangles(1, 2)[0];
            Assert.That(new[] { selection.X, selection.Y, selection.Width, selection.Height }, Is.EqualTo(new[] { 8f, 0f, 16f, 16f }));
        }

        [Test]
        public void TextLayoutAdjuster_AppliesLetterSpacingBetweenGraphemeClusters()
        {
            var natural = new TextLayoutEngine().Layout(new SpriteFontAdapter(CreateTestFont()), "ABC");
            var spaced = TextLayoutAdjuster.Apply(natural, 3);

            Assert.That(spaced.Size.X, Is.EqualTo(natural.Size.X + 6));
            Assert.That(spaced.GetCaretPosition(3).X, Is.EqualTo(natural.GetCaretPosition(3).X + 6));
            Assert.That(spaced.GetCaretPosition(1).X, Is.EqualTo(natural.GetCaretPosition(1).X + 3));
        }

        [Test]
        public void TextLayoutAdjuster_ResetsLetterSpacingAtSoftWrappedLines()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 16, UIFontHinting.Light);
            var maxWidth = TextMetrics.Measure(font, "alpha beta g").X;
            var natural = new TextLayoutEngine().Layout(font, "alpha beta gamma", new TextLayoutOptions(maxWidth, TextWrapping.Word));
            var spaced = TextLayoutAdjuster.Apply(natural, .25f);
            var secondLineStart = natural.Lines[1].Start;
            var naturalGlyph = natural.Runs.SelectMany(run => run.Glyphs).Single(glyph => glyph.Utf16Cluster == secondLineStart);
            var spacedGlyph = spaced.Runs.SelectMany(run => run.Glyphs).Single(glyph => glyph.Utf16Cluster == secondLineStart);

            Assert.Multiple(() =>
            {
                Assert.That(natural.Text.Substring(secondLineStart), Is.EqualTo("gamma"));
                Assert.That(spaced.GetCaretPosition(secondLineStart).X, Is.EqualTo(natural.GetCaretPosition(secondLineStart).X).Within(.0001f));
                Assert.That(spacedGlyph.Position.X, Is.EqualTo(naturalGlyph.Position.X).Within(.0001f));
            });
        }

        [Test]
        public void TextBlock_AppliesAbsoluteLineHeightToPlainAndInlineContent()
        {
            var font = new SpriteFontAdapter(CreateTestFont());
            var plain = new TextBlock { UIFont = font, Text = "A\nB", LineHeight = 14, Padding = new Thickness() };
            var inline = new TextBlock { UIFont = font, LineHeight = 14, Padding = new Thickness() };
            inline.Inlines.Add(new Run("A"));
            inline.Inlines.Add(new LineBreak());
            inline.Inlines.Add(new Run("B"));

            Assert.That(plain.GetMinimumSize().Y, Is.EqualTo(28));
            Assert.That(inline.GetMinimumSize().Y, Is.EqualTo(28));
            Assert.Throws<ArgumentOutOfRangeException>(() => plain.LineHeight = -1);
        }

        [Test]
        public void TextBlock_AppliesLetterSpacingToPlainMeasurementAndCharacterBounds()
        {
            var font = new SpriteFontAdapter(CreateTestFont());
            var natural = new TextBlock { UIFont = font, Text = "ABC", Padding = new Thickness() };
            var spaced = new TextBlock { UIFont = font, Text = "ABC", LetterSpacing = 4, Padding = new Thickness(), Size = new Vector2(64, 16) };

            Assert.That(spaced.GetMinimumSize().X, Is.EqualTo(natural.GetMinimumSize().X + 8));
            Assert.That(spaced.GetCharacterBounds(1).X, Is.EqualTo(natural.GetCharacterBounds(1).X + 4));
            Assert.That(spaced.GetCharacterBounds(2).X, Is.EqualTo(natural.GetCharacterBounds(2).X + 8));
        }

        [Test]
        public void TextBlock_SelectsTypedFamilyFaceAndFontSize()
        {
            var normal = new SpriteFontAdapter(CreateTestFont(), 16, UIFontWeight.Normal);
            var semibold = new SpriteFontAdapter(CreateTestFont(), 16, UIFontWeight.SemiBold);
            var text = new TextBlock
            {
                FontFamily = new UIFontFamily(new UIFont[] { normal, semibold }),
                FontWeight = UIFontWeight.SemiBold,
                FontSize = 24,
                Text = "A",
            };

            Assert.That(text.EffectiveUIFont.Weight, Is.EqualTo(UIFontWeight.SemiBold));
            Assert.That(text.EffectiveUIFont.Size, Is.EqualTo(24));
            Assert.That(text.FontStyle, Is.EqualTo(UIFontStyle.Normal));
            Assert.That(text.FontStretch, Is.EqualTo(UIFontStretch.Normal));
        }

        [Test]
        public void TextBlock_InlineCharacterBoundsMatchVisibleBoxes()
        {
            var text = new TextBlock { UIFont = new SpriteFontAdapter(CreateTestFont()), Padding = new Thickness(), Size = new Vector2(64, 32), MaxLinesVisible = 1 };
            text.Inlines.Add(new Run("A"));
            text.Inlines.Add(new InlineImage { Size = new Vector2(12, 7) });
            text.Inlines.Add(new LineBreak());
            text.Inlines.Add(new Run("B"));

            Assert.That(text.GetCharacterBounds(0), Is.Not.EqualTo(Rectangle.Empty));
            Assert.That(text.GetCharacterBounds(1), Is.EqualTo(new Rectangle(8, 4, 12, 7)));
            Assert.That(text.GetCharacterBounds(2), Is.EqualTo(Rectangle.Empty), "Line-break source positions have no visual box.");
            Assert.That(text.GetCharacterBounds(3), Is.EqualTo(Rectangle.Empty), "Rows hidden by MaxLinesVisible must not remain hittable.");
        }

        [Test]
        public void TextLayoutEngine_CachesByValueIdentity()
        {
            var spriteFont = CreateTestFont();
            var firstAdapter = new SpriteFontAdapter(spriteFont, 16);
            var equivalentAdapter = new SpriteFontAdapter(spriteFont, 16);
            var engine = new TextLayoutEngine();

            var first = engine.Layout(firstAdapter, "cached", TextLayoutOptions.Default);
            var second = engine.Layout(equivalentAdapter, string.Concat("ca", "ched"), TextLayoutOptions.Default);

            Assert.That(second, Is.SameAs(first));
            Assert.That(firstAdapter, Is.EqualTo(equivalentAdapter));
            Assert.That(new UIFontFamily(new[] { firstAdapter }), Is.EqualTo(new UIFontFamily(new[] { equivalentAdapter })));
        }

        [Test]
        public void TextLayoutOptionsCopyEllipsisIntoValueIdentity()
        {
            var engine = new TextLayoutEngine();
            var font = new SpriteFontAdapter(CreateTestFont());
            var first = engine.Layout(font, "trim me", new TextLayoutOptions(trimming: TextTrimming.CharacterEllipsis, ellipsis: "..."));
            var equivalent = engine.Layout(font, string.Concat("trim", " me"), new TextLayoutOptions(trimming: TextTrimming.CharacterEllipsis, ellipsis: "..."));
            var different = engine.Layout(font, "trim me", new TextLayoutOptions(trimming: TextTrimming.CharacterEllipsis));

            Assert.Multiple(() =>
            {
                Assert.That(first.Options.Ellipsis, Is.EqualTo("..."));
                Assert.That(equivalent, Is.SameAs(first));
                Assert.That(different, Is.Not.SameAs(first));
                Assert.That(() => new TextLayoutOptions(ellipsis: string.Empty), Throws.ArgumentException);
            });
        }

        [Test]
        public void LegacyAndUIFontProperties_KeepValuesAndUseMostRecentAssignment()
        {
            var legacy = CreateTestFont();
            var dynamicSelection = new SpriteFontAdapter(CreateTestFont(), 24);
            var label = new Label { Font = legacy };
            var cachedLegacyAdapter = label.EffectiveUIFont;

            label.UIFont = dynamicSelection;
            Assert.That(label.Font, Is.SameAs(legacy));
            Assert.That(label.UIFont, Is.SameAs(dynamicSelection));
            Assert.That(label.EffectiveUIFont, Is.SameAs(dynamicSelection));

            label.Font = legacy;
            Assert.That(label.UIFont, Is.SameAs(dynamicSelection));
            Assert.That(label.EffectiveUIFont, Is.SameAs(cachedLegacyAdapter));
        }

        [Test]
        public void TreeItemCustomFonts_KeepValuesAndUseMostRecentAssignment()
        {
            var legacy = CreateTestFont();
            var logical = new SpriteFontAdapter(CreateTestFont(), 24);
            var tree = new Tree { Columns = 1 };
            var item = tree.CreateItem();

            item.SetCustomFont(0, legacy);
            var cachedLegacyAdapter = item.GetEffectiveCustomUIFont(0);
            item.SetCustomUIFont(0, logical);
            Assert.That(item.GetCustomFont(0), Is.SameAs(legacy));
            Assert.That(item.GetCustomUIFont(0), Is.SameAs(logical));
            Assert.That(item.GetEffectiveCustomUIFont(0), Is.SameAs(logical));

            item.SetCustomFont(0, legacy);
            Assert.That(item.GetCustomUIFont(0), Is.SameAs(logical));
            Assert.That(item.GetEffectiveCustomUIFont(0), Is.SameAs(cachedLegacyAdapter));
        }

        [Test]
        public void TreeRowsMeasureDynamicPerCellFontAndSizeOverrides()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var tree = new Tree { Columns = 1, ItemHeight = 12 };
            var item = tree.CreateItem();
            item.SetText(0, "Dynamic tree cell");
            item.SetCustomUIFont(0, new DynamicUIFont(face, 14));
            item.SetCustomFontSize(0, 28);

            Assert.That(tree.GetItemAreaRectangle(item).Height, Is.GreaterThanOrEqualTo(28));
        }

        [Test]
        public void DynamicFontLayoutRetainsShapedGlyphsAndLogicalMetrics()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var font = new DynamicUIFont(face, 24);
            var layout = new TextLayoutEngine().Layout(font, "مرحبا", new TextLayoutOptions(direction: TextDirection.RightToLeft, locale: "ar"));

            Assert.Multiple(() =>
            {
                Assert.That(layout.Runs, Has.Count.EqualTo(1));
                Assert.That(layout.Runs[0].Direction, Is.EqualTo(TextDirection.RightToLeft));
                Assert.That(layout.Runs[0].Glyphs, Is.Not.Empty);
                Assert.That(layout.Runs[0].Glyphs[0].Utf16Cluster, Is.GreaterThan(layout.Runs[0].Glyphs[^1].Utf16Cluster));
                Assert.That(layout.Size.X, Is.GreaterThan(0));
                Assert.That(layout.Lines[0].Baseline, Is.GreaterThan(0));
            });
        }

        [Test]
        public void DynamicLayoutAppliesImmutableOpenTypeFeaturesAndCachesByValue()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 24);
            var source = new[] { new UIFontOpenTypeFeature("kern", 0) };
            var disabledOptions = new TextLayoutOptions(openTypeFeatures: source);
            source[0] = new UIFontOpenTypeFeature("kern", 1);
            var engine = new TextLayoutEngine();
            var enabled = engine.Layout(font, "AV");
            var disabled = engine.Layout(font, "AV", disabledOptions);
            var equivalent = engine.Layout(font, string.Concat("A", "V"), new TextLayoutOptions(openTypeFeatures: new[] { new UIFontOpenTypeFeature("kern", 0) }));

            Assert.Multiple(() =>
            {
                Assert.That(disabled.Options.OpenTypeFeatures, Is.EqualTo(new[] { new UIFontOpenTypeFeature("kern", 0) }));
                Assert.That(disabled.Size.X, Is.GreaterThan(enabled.Size.X));
                Assert.That(disabled, Is.Not.SameAs(enabled));
                Assert.That(equivalent, Is.SameAs(disabled));
                Assert.That(() => new UIFontOpenTypeFeature("bad"), Throws.ArgumentException);
            });
        }

        [Test]
        public void ReloadedIdenticalFontPackDoesNotReuseDisposedPrimaryOrFallbackFaces()
        {
            var engine = new TextLayoutEngine();
            using var firstLatin = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var firstArabic = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var firstFont = new DynamicUIFont(firstLatin, 24, UIFontHinting.None, firstArabic);
            var first = engine.Layout(firstFont, "Luna مرحبا");
            var sameOwners = engine.Layout(new DynamicUIFont(firstLatin, 24, UIFontHinting.None, firstArabic), first.Text);
            Assert.That(sameOwners, Is.SameAs(first));

            firstArabic.Dispose();
            using var nextArabic = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var nextFont = new DynamicUIFont(firstLatin, 24, UIFontHinting.None, nextArabic);
            var fallbackReloaded = engine.Layout(nextFont, first.Text);
            Assert.That(nextFont.Identity, Is.EqualTo(firstFont.Identity));
            Assert.That(fallbackReloaded, Is.Not.SameAs(first));
            foreach (var run in fallbackReloaded.Runs)
                Assert.That(() => ((DynamicUIFont)run.Font).Face.RasterizeGlyph(run.Glyphs[0].GlyphId, 24), Throws.Nothing);

            firstLatin.Dispose();
            using var nextLatin = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var primaryReloaded = engine.Layout(new DynamicUIFont(nextLatin, 24, UIFontHinting.None, nextArabic), first.Text);
            Assert.That(primaryReloaded, Is.Not.SameAs(fallbackReloaded));
            Assert.That(((DynamicUIFont)primaryReloaded.Runs[0].Font).Face, Is.SameAs(nextLatin));
        }

        [Test]
        public void ContextDisposalClearsSharedTextMetricsLayouts()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 24);
            var layout = TextMetrics.Layout(font, "font pack lifetime");
            Assert.That(TextMetrics.Layout(font, layout.Text), Is.SameAs(layout));
            using (var context = new UIContext()) { }
            Assert.That(TextMetrics.Layout(font, layout.Text), Is.Not.SameAs(layout));
            var cleared = TextMetrics.Layout(font, layout.Text);
            TextLayoutEngine.ClearSharedCaches();
            Assert.That(TextMetrics.Layout(font, layout.Text), Is.Not.SameAs(cleared));
        }

        [Test]
        public void DynamicLabelCopiesOpenTypeFeaturesIntoItsLayoutContract()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var label = new Label { UIFont = new DynamicUIFont(face, 24), Text = "AV" };
            var source = new[] { new UIFontOpenTypeFeature("kern", 0) };
            label.SetOpenTypeFeatures(source);
            source[0] = new UIFontOpenTypeFeature("kern", 1);

            Assert.Multiple(() =>
            {
                Assert.That(label.GetOpenTypeFeatures(), Is.EqualTo(new[] { new UIFontOpenTypeFeature("kern", 0) }));
                Assert.That(label.GetMinimumSize().X, Is.GreaterThan(new TextLayoutEngine().Layout(label.UIFont, label.Text).Size.X));
            });
        }

        [Test]
        public void DynamicEmptyLayoutContainsOneLogicalLineAndNoGlyphs()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var layout = new TextLayoutEngine().Layout(new DynamicUIFont(face, 16), string.Empty);
            Assert.Multiple(() =>
            {
                Assert.That(layout.Lines, Has.Count.EqualTo(1));
                Assert.That(layout.Runs, Has.Count.EqualTo(1));
                Assert.That(layout.Runs[0].Glyphs, Is.Empty);
                Assert.That(layout.Size, Is.EqualTo(Vector2.Zero));
            });
        }

        [Test]
        public void DynamicLayoutWrapsAtShapedClusterBoundariesAndReshapesLines()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 18);
            var engine = new TextLayoutEngine();
            var wordWidth = engine.Layout(font, "alpha ").Size.X;
            var wrapped = engine.Layout(font, "alpha beta gamma", new TextLayoutOptions(maxWidth: wordWidth + 1, wrapping: TextWrapping.Word));

            Assert.Multiple(() =>
            {
                Assert.That(wrapped.Lines.Count, Is.GreaterThan(1));
                Assert.That(wrapped.Runs, Has.Count.EqualTo(wrapped.Lines.Count));
                Assert.That(wrapped.Lines.All(line => line.Size.X <= wordWidth + 1.01f), Is.True);
                Assert.That(wrapped.Runs.SelectMany(run => run.Glyphs), Has.All.Property(nameof(TextLayoutGlyph.GlyphId)).GreaterThan(0));
            });
        }

        [Test]
        public void DynamicLayoutRetainsSourceAndShapesSyntheticEllipsisForLtrAndRtl()
        {
            using var latinFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var engine = new TextLayoutEngine();
            var ltr = engine.Layout(new DynamicUIFont(latinFace, 20), "alpha beta gamma", new TextLayoutOptions(maxWidth: 90, trimming: TextTrimming.WordEllipsis, ellipsis: "..."));
            var rtl = engine.Layout(new DynamicUIFont(arabicFace, 20), "مرحبا بالعالم", new TextLayoutOptions(maxWidth: 80, direction: TextDirection.RightToLeft, locale: "ar", trimming: TextTrimming.CharacterEllipsis));

            Assert.Multiple(() =>
            {
                Assert.That(ltr.Text, Is.EqualTo("alpha beta gamma"));
                Assert.That(ltr.Lines[0].IsTrimmed, Is.True);
                Assert.That(ltr.Lines[0].VisibleRange.End, Is.EqualTo(5));
                Assert.That(ltr.VisibleGlyphs.Where(glyph => glyph.IsSynthetic), Is.Not.Empty);
                Assert.That(ltr.Size.X, Is.LessThanOrEqualTo(90.01f));
                Assert.That(ltr.GetCaretPosition(ltr.Lines[0].VisibleRange.End), Is.EqualTo(ltr.GetCaretPosition(ltr.Text.Length)));
                Assert.That(rtl.Lines[0].IsTrimmed, Is.True);
                Assert.That(rtl.VisibleGlyphs.Where(glyph => glyph.IsSynthetic), Is.Not.Empty);
                Assert.That(rtl.VisibleGlyphs.Where(glyph => glyph.IsSynthetic).Max(glyph => glyph.Bounds.Right), Is.LessThanOrEqualTo(rtl.VisibleGlyphs.Where(glyph => !glyph.IsSynthetic).Min(glyph => glyph.Bounds.Left) + 1));
            });
        }

        [Test]
        public void DynamicLayoutSelectsFallbackPerGraphemeAndKeepsRunsMapped()
        {
            using var latinFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var font = new DynamicUIFont(latinFace, 20, UIFontHinting.Default, arabicFace, latinFace);
            var layout = new TextLayoutEngine().Layout(font, "Forma مرحبا", new TextLayoutOptions(locale: "ar"));

            Assert.Multiple(() =>
            {
                Assert.That(font.FallbackFaces, Has.Count.EqualTo(1));
                Assert.That(layout.Runs.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(((DynamicUIFont)layout.Runs[0].Font).Face, Is.SameAs(latinFace));
                Assert.That(layout.Runs.Any(run => ReferenceEquals(((DynamicUIFont)run.Font).Face, arabicFace)), Is.True);
                Assert.That(layout.Runs.SelectMany(run => run.Glyphs), Has.All.Property(nameof(TextLayoutGlyph.GlyphId)).GreaterThan(0));
                Assert.That(layout.Runs.Sum(run => run.Length), Is.EqualTo(layout.Text.Length));
                Assert.That(new DynamicUIFont(latinFace, 20, UIFontHinting.Default, latinFace, arabicFace).Identity, Is.EqualTo(font.Identity));
            });
        }

        [Test]
        public void DynamicLayoutCoversFocusedMultilingualShapingFallbackAndWrapping()
        {
            using var latinFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            using var hebrewFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansHebrew_Subset.ttf");
            using var devanagariFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansDevanagari_Subset.ttf");
            using var thaiFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansThai_Subset.ttf");
            using var cjkFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansCJK_Subset.ttf");
            using var emojiFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoEmoji_Subset.ttf");
            var font = new DynamicUIFont(latinFace, 22, UIFontHinting.Default, arabicFace, hebrewFace, devanagariFace, thaiFace, cjkFace, emojiFace);
            var engine = new TextLayoutEngine();
            var arabic = engine.Layout(font, "مرحبا", new TextLayoutOptions(locale: "ar"));
            var hebrew = engine.Layout(font, "abc שלום");
            var devanagari = engine.Layout(font, "क्ष", new TextLayoutOptions(locale: "hi"));
            var thai = engine.Layout(font, "สวัสดีชาวโลก", new TextLayoutOptions(locale: "th"));
            var emoji = engine.Layout(font, "👩🏽‍💻");
            var combining = engine.Layout(font, "Á");
            var cjkWidth = engine.Layout(font, "你好").Size.X + .01f;
            var cjk = engine.Layout(font, "你好世界日本語", new TextLayoutOptions(maxWidth: cjkWidth, wrapping: TextWrapping.Character));
            var ligature = engine.Layout(new DynamicUIFont(cjkFace, 22), "office");
            var missing = engine.Layout(font, "\u0378");

            Assert.Multiple(() =>
            {
                Assert.That(arabic.Runs.Any(run => ReferenceEquals(((DynamicUIFont)run.Font).Face, arabicFace)), Is.True);
                Assert.That(hebrew.Runs.Select(run => run.Direction), Does.Contain(TextDirection.LeftToRight));
                Assert.That(hebrew.Runs.Select(run => run.Direction), Does.Contain(TextDirection.RightToLeft));
                Assert.That(devanagari.Clusters, Has.Count.EqualTo(1));
                Assert.That(devanagari.Runs.SelectMany(run => run.Glyphs).Count(), Is.EqualTo(1));
                Assert.That(thai.Runs.Any(run => ReferenceEquals(((DynamicUIFont)run.Font).Face, thaiFace)), Is.True);
                Assert.That(thai.Runs.SelectMany(run => run.Glyphs), Has.All.Property(nameof(TextLayoutGlyph.GlyphId)).GreaterThan(0));
                Assert.That(emoji.Clusters, Has.Count.EqualTo(1));
                Assert.That(emoji.Runs.SelectMany(run => run.Glyphs).Count(), Is.EqualTo(1));
                Assert.That(combining.Clusters, Has.Count.EqualTo(1));
                Assert.That(cjk.Lines.Count, Is.GreaterThan(1));
                Assert.That(cjk.Lines.Select(line => line.Size.X), Is.All.LessThanOrEqualTo(cjkWidth + .01f));
                Assert.That(ligature.Runs.SelectMany(run => run.Glyphs).Count(), Is.LessThan(ligature.Text.Length));
                Assert.That(missing.Runs.SelectMany(run => run.Glyphs).Single().GlyphId, Is.Zero);
            });
        }

        [Test]
        public void DynamicLayoutOrdersMixedBidiRunsVisuallyAndRetainsLogicalMappings()
        {
            using var latinFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var font = new DynamicUIFont(latinFace, 20, UIFontHinting.Default, arabicFace);

            var layout = new TextLayoutEngine().Layout(font, "abc مرحبا 123", new TextLayoutOptions(locale: "ar"));

            Assert.Multiple(() =>
            {
                Assert.That(layout.Runs.Select(run => run.Direction), Does.Contain(TextDirection.LeftToRight));
                Assert.That(layout.Runs.Select(run => run.Direction), Does.Contain(TextDirection.RightToLeft));
                Assert.That(layout.Runs.Select(run => run.BidiLevel), Does.Contain((byte?)1));
                Assert.That(layout.Runs.Select(run => run.BidiLevel), Does.Contain((byte?)2));
                Assert.That(layout.Runs.Zip(layout.Runs.Skip(1), (left, right) => left.Start > right.Start).Any(value => value), Is.True);
                Assert.That(layout.Runs.SelectMany(run => run.Glyphs).Select(glyph => glyph.Utf16Cluster), Is.All.InRange(0, layout.Text.Length));
            });
        }

        [Test]
        public void DynamicLayoutHonorsForcedParagraphDirectionAndMirrorsRtlPunctuation()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 20);
            var rtl = new TextLayoutEngine().Layout(font, "(abc)", new TextLayoutOptions(direction: TextDirection.RightToLeft));
            var mirroredOpen = face.Shape(")", 20, TextDirection.LeftToRight).Glyphs[0].GlyphId;

            Assert.Multiple(() =>
            {
                Assert.That(rtl.Runs[0].BidiLevel, Is.EqualTo(1));
                Assert.That(rtl.Runs[1].BidiLevel, Is.EqualTo(2));
                Assert.That(rtl.Runs[^1].BidiLevel, Is.EqualTo(1));
                Assert.That(rtl.Runs[^1].Glyphs[0].GlyphId, Is.EqualTo(mirroredOpen));
            });
        }

        [Test]
        public void DynamicLayoutRetainsInvisibleBidiControlRangesWithoutGlyphs()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var text = "a\u202B1\u202Cb\u2067c\u2069";

            var layout = new TextLayoutEngine().Layout(new DynamicUIFont(face, 20), text);
            var controlRuns = layout.Runs.Where(run => run.Start < text.Length && text[run.Start] is '\u202B' or '\u202C' or '\u2067' or '\u2069').ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(layout.Runs.Sum(run => run.Length), Is.EqualTo(text.Length));
                Assert.That(controlRuns, Has.Length.EqualTo(4));
                Assert.That(controlRuns.SelectMany(run => run.Glyphs), Is.Empty);
                Assert.That(controlRuns.Count(run => run.BidiLevel == null), Is.EqualTo(2));
                Assert.That(layout.Runs.Where(run => !controlRuns.Contains(run)).SelectMany(run => run.Glyphs), Is.Not.Empty);
            });
        }

        [Test]
        public void DynamicLayoutSelectionSplitsAcrossVisualBidiRuns()
        {
            using var latinFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var layout = new TextLayoutEngine().Layout(
                new DynamicUIFont(latinFace, 20, UIFontHinting.Default, arabicFace),
                "abc אבג def");

            var mixed = layout.GetSelectionRectangles(2, 6);
            var rtl = layout.GetSelectionRectangles(4, 3);

            Assert.Multiple(() =>
            {
                Assert.That(mixed, Has.Count.GreaterThan(1));
                Assert.That(mixed, Has.All.Property(nameof(RectangleF.Width)).GreaterThan(0));
                Assert.That(rtl, Has.Count.EqualTo(1));
                Assert.That(rtl[0].Width, Is.GreaterThan(0));
            });
        }

        [Test]
        public void DynamicLayoutExposesImmutableLogicalAndVisualClusterMaps()
        {
            using var latinFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var text = "A😀 אב";
            var layout = new TextLayoutEngine().Layout(new DynamicUIFont(latinFace, 20, UIFontHinting.Default, arabicFace), text);

            Assert.Multiple(() =>
            {
                Assert.That(layout.Clusters.Select(cluster => text.Substring(cluster.Start, cluster.Length)), Is.EqualTo(new[] { "A", "😀", " ", "א", "ב" }));
                Assert.That(layout.Clusters.Select(cluster => cluster.LogicalIndex), Is.EqualTo(Enumerable.Range(0, 5)));
                Assert.That(layout.VisualClusters.Select(cluster => cluster.VisualIndex), Is.EqualTo(Enumerable.Range(0, 5)));
                Assert.That(layout.VisualClusters.Select(cluster => cluster.Start), Is.Not.EqualTo(layout.Clusters.Select(cluster => cluster.Start)));
                Assert.That(layout.GetClusterIndex(1), Is.EqualTo(1));
                Assert.That(layout.GetClusterIndex(2), Is.EqualTo(1));
                Assert.That(layout.Runs, Has.All.Property(nameof(TextLayoutRun.Bounds)).Not.Null);
                Assert.That(layout.Runs.SelectMany(run => run.Glyphs), Has.All.Property(nameof(TextLayoutGlyph.Bounds)).Not.Null);
            });
        }

        [Test]
        public void LayoutPreservesUnicodeAndApplicationParagraphBoundaries()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 16);
            var engine = new TextLayoutEngine();
            var unicode = engine.Layout(font, "a\r\nb\u0085c\u000Bd\u000Ce\u2028f\u2029g");
            var customOptions = new TextLayoutOptions(paragraphSeparator: "||");
            var custom = engine.Layout(font, "one||two||", customOptions);

            Assert.Multiple(() =>
            {
                Assert.That(unicode.Lines.Select(line => unicode.Text.Substring(line.Start, line.Length)),
                    Is.EqualTo(new[] { "a", "b", "c", "d", "e", "f", "g" }));
                Assert.That(custom.Lines.Select(line => custom.Text.Substring(line.Start, line.Length)),
                    Is.EqualTo(new[] { "one", "two", string.Empty }));
                Assert.That(custom.Lines.Select(line => line.Start), Is.EqualTo(new[] { 0, 5, 10 }));
                Assert.That(custom.Options.ParagraphSeparator, Is.EqualTo("||"));
                Assert.That(engine.Layout(font, custom.Text), Is.Not.SameAs(custom));
            });
        }

        [Test]
        public void DynamicLayoutPreservesWhitespaceAndAppliesRepeatingTabStops()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var sourceStops = new[] { 24f, 48f };
            var options = new TextLayoutOptions(tabStops: sourceStops);
            sourceStops[0] = 100;
            var layout = new TextLayoutEngine().Layout(new DynamicUIFont(face, 16), " A\tB\tC ", options);

            Assert.Multiple(() =>
            {
                Assert.That(options.TabStops, Is.EqualTo(new[] { 24f, 48f }));
                Assert.That(layout.Lines[0].Length, Is.EqualTo(layout.Text.Length));
                Assert.That(layout.GetCaretPosition(3).X, Is.EqualTo(24).Within(.01f));
                Assert.That(layout.GetCaretPosition(5).X, Is.EqualTo(72).Within(.01f));
                Assert.That(layout.Runs.Where(run => layout.Text.Substring(run.Start, run.Length) == "\t"),
                    Has.All.Property(nameof(TextLayoutRun.Glyphs)).Empty);
            });
        }

        [Test]
        public void LayoutAppliesTabStopsToSpriteFontsWrappingAndLabels()
        {
            var spriteLayout = new TextLayoutEngine().Layout(
                new SpriteFontAdapter(CreateTestFont()),
                "A\tB",
                new TextLayoutOptions(tabStops: new[] { 24f }));
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 16);
            var wrapped = new TextLayoutEngine().Layout(
                font,
                "A\tB",
                new TextLayoutOptions(maxWidth: 30, wrapping: TextWrapping.Character, tabStops: new[] { 24f }));
            var label = new Label
            {
                UIFont = font,
                Text = "A\tB||C",
                ParagraphSeparator = "||",
                Padding = new Thickness(0),
                Size = new Vector2(100, 60)
            };
            label.SetTabStops(new[] { 24f });

            Assert.Multiple(() =>
            {
                Assert.That(spriteLayout.GetCaretPosition(2).X, Is.EqualTo(24));
                Assert.That(spriteLayout.Size.X, Is.EqualTo(32));
                Assert.That(wrapped.Lines, Has.Count.EqualTo(2));
                Assert.That(wrapped.Lines.Select(line => line.Size.X), Is.All.LessThanOrEqualTo(30.01f));
                Assert.That(label.GetLineCount(), Is.EqualTo(2));
                Assert.That(label.GetCharacterBounds(2).X, Is.EqualTo(24));
                Assert.That(label.GetCharacterBounds(3), Is.EqualTo(Rectangle.Empty));
            });
        }

        [Test]
        public void TextLayoutNavigatesGraphemesAndUnionsBidiRangeBounds()
        {
            using var latinFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            using var arabicFace = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
            var text = "A😀e\u0301 אב\nZ";
            var layout = new TextLayoutEngine().Layout(new DynamicUIFont(latinFace, 20, UIFontHinting.Default, arabicFace), text);
            var selection = layout.GetSelectionRectangles(1, 7);
            var bounds = layout.GetRangeBounds(1, 7);

            Assert.Multiple(() =>
            {
                Assert.That(layout.GetNextGraphemeBoundary(1), Is.EqualTo(3));
                Assert.That(layout.GetNextGraphemeBoundary(2), Is.EqualTo(3));
                Assert.That(layout.GetPreviousGraphemeBoundary(3), Is.EqualTo(1));
                Assert.That(layout.GetNextGraphemeBoundary(4), Is.EqualTo(5));
                Assert.That(layout.GetPreviousGraphemeBoundary(5), Is.EqualTo(3));
                Assert.That(bounds.Left, Is.EqualTo(selection.Min(rectangle => rectangle.Left)));
                Assert.That(bounds.Right, Is.EqualTo(selection.Max(rectangle => rectangle.Right)));
                Assert.That(bounds.Height, Is.GreaterThan(0));
                Assert.That(layout.GetRangeBounds(0, 0).Width, Is.Zero);
            });
        }

        [Test]
        public void TextLayoutExposesUnicodeWordRangesWithoutSplittingUtf16Clusters()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var layout = new TextLayoutEngine().Layout(new DynamicUIFont(face, 16), "can't 12.5 😀");

            Assert.Multiple(() =>
            {
                Assert.That(layout.GetWordBoundary(2), Is.EqualTo(new TextLayoutRange(0, 5)));
                Assert.That(layout.GetWordBoundary(7), Is.EqualTo(new TextLayoutRange(6, 4)));
                Assert.That(layout.GetPreviousWordBoundary(5), Is.EqualTo(0));
                Assert.That(layout.GetNextWordBoundary(6), Is.EqualTo(10));
                Assert.That(layout.GetWordBoundary(12), Is.EqualTo(new TextLayoutRange(11, 2)));
            });
        }

        [Test]
        public void TextLayoutVisibleRangeCountsGraphemesInsteadOfUtf16CodeUnits()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var font = new DynamicUIFont(face, 16);
            var limited = new TextLayoutEngine().Layout(font, "A😀e\u0301Z", new TextLayoutOptions(maxVisibleCharacters: 2));
            var hidden = new TextLayoutEngine().Layout(font, "A😀", new TextLayoutOptions(maxVisibleCharacters: 0));

            Assert.Multiple(() =>
            {
                Assert.That(limited.VisibleRange, Is.EqualTo(new TextLayoutRange(0, 3)));
                Assert.That(limited.VisibleRanges, Is.EqualTo(new[] { new TextLayoutRange(0, 3) }));
                Assert.That(limited.VisibleGlyphs, Is.Not.Empty);
                Assert.That(limited.VisibleGlyphs.Select(glyph => glyph.Utf16Cluster), Is.All.LessThan(3));
                Assert.That(limited.VisibleGlyphs.Count, Is.LessThan(limited.Runs.Sum(run => run.Glyphs.Count)));
                Assert.That(hidden.VisibleRange, Is.EqualTo(new TextLayoutRange(0, 0)));
                Assert.That(hidden.VisibleGlyphs, Is.Empty);
            });
        }

        [Test]
        public void DynamicLabelVisibilityNeverSplitsGraphemeClusters()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var label = new Label
            {
                UIFont = new DynamicUIFont(face, 16),
                Text = "A😀e\u0301Z",
                VisibleCharacters = 2,
                VisibleCharactersBehavior = LabelVisibleCharactersBehavior.CharactersBeforeShaping,
                Padding = new Thickness(0),
                Size = new Vector2(100, 30)
            };

            Assert.That(label.GetCharacterBounds(2), Is.Not.EqualTo(Rectangle.Empty));
            Assert.That(label.GetCharacterBounds(3), Is.EqualTo(Rectangle.Empty));

            label.VisibleCharactersBehavior = LabelVisibleCharactersBehavior.CharactersAfterShaping;
            Assert.That(label.GetCharacterBounds(2), Is.Not.EqualTo(Rectangle.Empty));
            Assert.That(label.GetCharacterBounds(3), Is.EqualTo(Rectangle.Empty));
        }

        [Test]
        public void DynamicLabelUsesRetainedWordEllipsisAndHidesTrimmedCharacterBounds()
        {
            using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
            var label = new Label
            {
                UIFont = new DynamicUIFont(face, 20),
                Text = "alpha beta gamma",
                TextOverrunBehavior = LabelTextOverrunBehavior.WordEllipsis,
                EllipsisCharacter = "...",
                Padding = new Thickness(0),
                Size = new Vector2(90, 30)
            };

            Assert.Multiple(() =>
            {
                Assert.That(label.GetCharacterBounds(4), Is.Not.EqualTo(Rectangle.Empty));
                Assert.That(label.GetCharacterBounds(5), Is.EqualTo(Rectangle.Empty));
                Assert.That(label.GetCharacterBounds(label.Text.Length - 1), Is.EqualTo(Rectangle.Empty));
            });
        }

        private static SpriteFont CreateTestFont()
        {
            var characters = new List<char>();
            var bounds = new List<Rectangle>();
            var cropping = new List<Rectangle>();
            var kerning = new List<Vector3>();
            for (var character = (char)32; character <= (char)126; character++)
            {
                characters.Add(character);
                bounds.Add(new Rectangle(0, 0, 8, 16));
                cropping.Add(new Rectangle(0, 0, 8, 16));
                kerning.Add(new Vector3(0, 8, 0));
            }
            foreach (var character in new[] { '\u2026', '\uD83D', '\uDE00' })
            {
                characters.Add(character);
                bounds.Add(new Rectangle(0, 0, 8, 16));
                cropping.Add(new Rectangle(0, 0, 8, 16));
                kerning.Add(new Vector3(0, 8, 0));
            }
            return (SpriteFont)Activator.CreateInstance(
                typeof(SpriteFont),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { null, bounds, cropping, characters, 16, 0f, kerning, null },
                null);
        }
    }
}