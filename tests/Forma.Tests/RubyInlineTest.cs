// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

public sealed class RubyInlineTest
{
    [Test]
    public void AnnotationSitsAboveCenteredBaseTextThatAloneFormsTheLabelText()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        var font = new DynamicUIFont(face, 24);
        var plain = Block(font);
        plain.Inlines.Add(new Run("AB"));
        var block = Block(font);
        block.AlignInlineBaselines = true;
        block.Inlines.Add(new Run("A"));
        block.Inlines.Add(new Ruby("ii", "annotation") { Meta = "gloss" });
        block.Inlines.Add(new Run("B"));
        Assert.That(((Label)block).Text, Is.EqualTo("AiiB"));
        var lead = block.GetCharacterBounds(0);
        var baseStart = block.GetCharacterBounds(1);
        var trail = block.GetCharacterBounds(3);
        var engine = new TextLayoutEngine();
        var annotation = engine.Layout(font.WithSize(12), "annotation").Size;
        var baseWidth = engine.Layout(font, "ii").Size.X;
        Assert.That(annotation.X, Is.GreaterThan(baseWidth * 2));
        Assert.That(trail.Left - lead.Right, Is.EqualTo(annotation.X).Within(2), "the ruby box is as wide as its annotation");
        Assert.That(baseStart.Left - lead.Right, Is.EqualTo((annotation.X - baseWidth) / 2).Within(2), "base text is centered");
        Assert.That(baseStart.Bottom, Is.EqualTo(lead.Bottom).Within(1), "base text shares the line baseline");
        Assert.That(lead.Top - plain.GetCharacterBounds(0).Top, Is.EqualTo(annotation.Y).Within(1), "the line grows by the annotation");
        Assert.That(block.GetMetaUnderPosition(Center(block, baseStart)), Is.EqualTo("gloss"));
        Assert.That(block.GetMetaUnderPosition(Center(block, lead)), Is.Null);
        block.VisibleCharacters = 1;
        Assert.That(block.GetCharacterBounds(1), Is.EqualTo(Rectangle.Empty));
        Assert.That(block.GetMetaUnderPosition(Center(block, baseStart)), Is.Null);
        block.VisibleCharacters = 2;
        Assert.That(block.GetCharacterBounds(1), Is.EqualTo(baseStart));
        Assert.That(block.GetCharacterBounds(2), Is.EqualTo(Rectangle.Empty));
    }

    [Test]
    public void AnnotationFontAndEmptyAnnotationAreHonoured()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        var font = new DynamicUIFont(face, 24);
        var bare = Block(font);
        bare.Inlines.Add(new Ruby("ii", ""));
        var plain = Block(font);
        plain.Inlines.Add(new Run("ii"));
        Assert.That(bare.GetCharacterBounds(0), Is.EqualTo(plain.GetCharacterBounds(0)));
        var large = Block(font);
        large.Inlines.Add(new Ruby("ii", "x") { AnnotationFont = font.WithSize(20) });
        var small = Block(font);
        small.Inlines.Add(new Ruby("ii", "x"));
        Assert.That(large.GetCharacterBounds(0).Top, Is.GreaterThan(small.GetCharacterBounds(0).Top + 4));
    }

    private static TextBlock Block(UIFont font) => new()
    {
        UIFont = font, Padding = Thickness.Zero, Position = new Vector2(20, 30), Size = new Vector2(400, 100),
        VisibleCharactersBehavior = LabelVisibleCharactersBehavior.CharactersAfterShaping,
    };

    private static Point Center(TextBlock block, Rectangle bounds) => new(
        (int)block.GlobalPosition.X + bounds.Center.X, (int)block.GlobalPosition.Y + bounds.Center.Y);
}
