// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

public sealed class RichTextInteractionTest
{
    private static TextBlock Block(UIFont font) => new()
    {
        UIFont = font, Padding = Thickness.Zero, Position = new Vector2(20, 30), Size = new Vector2(240, 100),
        VisibleCharactersBehavior = LabelVisibleCharactersBehavior.CharactersAfterShaping,
    };

    private static Point Center(TextBlock block, Rectangle bounds) => new(
        (int)block.GlobalPosition.X + bounds.Center.X, (int)block.GlobalPosition.Y + bounds.Center.Y);

    [TestCase("Inter_Regular.ttf", "café", TextDirection.LeftToRight)]
    [TestCase("NotoSansArabic_Variable.ttf", "مرحبا", TextDirection.RightToLeft)]
    [TestCase("NotoSansCJK_Subset.ttf", "日本語", TextDirection.LeftToRight)]
    public void MetadataUsesRevealedShapedBoundsAndNeverActivatesHiddenOrDisabledText(string file, string source, TextDirection direction)
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/" + file);
        var block = Block(new DynamicUIFont(face, 24));
        var link = new Span { Meta = "lesson", Direction = direction };
        link.Inlines.Add(new Run(source) { Decoration = TextDecoration.Underline, Foreground = Color.Blue });
        block.Inlines.Add(link);
        var first = block.GetCharacterBounds(0);
        var last = block.GetCharacterBounds(source.Length - 1);
        var clicks = new List<object>();
        block.MetaClicked += (_, meta) => clicks.Add(meta);
        Assert.That(block.GetMetaUnderPosition(Center(block, last)), Is.EqualTo("lesson"));
        block.VisibleCharacters = 0;
        block.PointerPressed(Center(block, first));
        Assert.That(block.GetMetaUnderPosition(Center(block, last)), Is.Null);
        block.VisibleCharacters = 1;
        Assert.That(block.GetCharacterBounds(0), Is.EqualTo(first));
        Assert.That(block.GetMetaUnderPosition(Center(block, first)), Is.EqualTo("lesson"));
        Assert.That(block.GetMetaUnderPosition(Center(block, last)), Is.Null);
        block.PointerPressed(Center(block, first));
        block.Enabled = false;
        block.PointerPressed(Center(block, first));
        Assert.That(clicks, Is.EqualTo(new object[] { "lesson" }));
        block.Enabled = true;
        link.Meta = "changed";
        Assert.That(block.GetMetaUnderPosition(Center(block, first)), Is.EqualTo("changed"));
    }

    [Test]
    public void NestedMixedFontLinksRetainWrappedAlignedPositionsAndImageReveal()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        var font = new DynamicUIFont(face, 18);
        var block = Block(font);
        block.HorizontalAlignment = HorizontalAlignment.Center;
        block.VerticalAlignment = VerticalAlignment.Center;
        block.AutowrapMode = LabelAutowrapMode.Word;
        block.Size = new Vector2(130, 160);
        block.Inlines.Add(new Run("Lead "));
        var link = new Span { Font = new DynamicUIFont(face, 28), Meta = "parent" };
        link.Inlines.Add(new Run("linked words here") { Foreground = Color.Red });
        link.Inlines.Add(new InlineImage { Size = new Vector2(12, 12), Meta = "image" });
        block.Inlines.Add(link);
        var point = Center(block, block.GetCharacterBounds(5));
        var image = Center(block, block.GetCharacterBounds(((Label)block).Text.Length - 1));
        Assert.That(block.GetMetaUnderPosition(point), Is.EqualTo("parent"));
        Assert.That(block.GetMetaUnderPosition(image), Is.EqualTo("image"));
        var size = block.GetMinimumSize();
        block.VisibleCharacters = 6;
        Assert.That(block.GetMinimumSize(), Is.EqualTo(size));
        Assert.That(block.GetMetaUnderPosition(point), Is.EqualTo("parent"));
        Assert.That(block.GetMetaUnderPosition(image), Is.Null);
        block.MaxLinesVisible = 1;
        Assert.That(block.GetMetaUnderPosition(image), Is.Null);
        Assert.That(block.GetMetaUnderPosition(new Point(0, 0)), Is.Null);
    }

    [Test]
    public void EmptyAlternativeTextImageStillDisplaysWithoutBecomingACharacter()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        var block = Block(new DynamicUIFont(face, 18));
        block.Inlines.Add(new InlineImage { AlternativeText = "", Size = new Vector2(20, 20), Meta = "image" });
        var point = Center(block, new Rectangle(0, 0, 20, 20));
        Assert.That(block.GetMetaUnderPosition(point), Is.EqualTo("image"));
        block.VisibleCharacters = 0;
        Assert.That(block.GetMetaUnderPosition(point), Is.Null);
    }

    [Test]
    public void RevealRatioCountsDocumentGraphemesAcrossStyleBoundaries()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        var block = Block(new DynamicUIFont(face, 18));
        block.Inlines.Add(new Run("Ae"));
        block.Inlines.Add(new Run("\u0301BC") { Foreground = Color.Red });
        block.VisibleRatio = .5f;
        Assert.That(block.GetCharacterBounds(1), Is.Not.EqualTo(Rectangle.Empty));
        Assert.That(block.GetCharacterBounds(2), Is.Not.EqualTo(Rectangle.Empty));
        Assert.That(block.GetCharacterBounds(3), Is.EqualTo(Rectangle.Empty));
        block.VisibleRatio = float.NaN;
        Assert.That(() => block.GetMinimumSize(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void BeforeShapingRevealTruncatesAtGraphemesAndAfterShapingPreservesLayout()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        var font = new DynamicUIFont(face, 18);
        var block = Block(font);
        block.Inlines.Add(new Run("e\u0301XYZ"));
        var full = block.GetMinimumSize();
        block.VisibleCharacters = 1;
        Assert.That(block.GetMinimumSize(), Is.EqualTo(full));
        block.VisibleCharactersBehavior = LabelVisibleCharactersBehavior.CharactersBeforeShaping;
        Assert.That(block.GetMinimumSize(), Is.EqualTo(TextMetrics.Measure(font, "e\u0301")));
        var plain = new Label { UIFont = font, Text = "e\u0301XYZ", Padding = Thickness.Zero, VisibleCharacters = 1 };
        Assert.That(plain.GetMinimumSize(), Is.EqualTo(block.GetMinimumSize()));
    }
}
