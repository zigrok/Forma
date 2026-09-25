// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

public sealed class InlineBidiTest
{
    private const string First = "\u0633\u0644\u0627\u0645";
    private const string Second = "\u0639\u0644\u064A\u0643\u0645";

    [Test]
    public void RightToLeftRunsSpanningStyledInlinesRenderInVisualOrder()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
        var block = Block(face);
        block.Inlines.Add(new Run("A "));
        block.Inlines.Add(new Run(First + " "));
        block.Inlines.Add(new Run(Second) { Foreground = Color.Red });
        block.Inlines.Add(new Run(" B"));
        var text = ((Label)block).Text;
        var a = block.GetCharacterBounds(0);
        var firstStart = block.GetCharacterBounds(text.IndexOf(First[0]));
        var firstEnd = block.GetCharacterBounds(text.IndexOf(First[^1]));
        var secondStart = block.GetCharacterBounds(text.IndexOf(Second[0]));
        var b = block.GetCharacterBounds(text.Length - 1);
        Assert.That(a.Left, Is.LessThan(secondStart.Left), "the LTR paragraph starts on the left");
        Assert.That(secondStart.Right, Is.LessThanOrEqualTo(firstEnd.Left + 1), "the later Arabic word, in another span, is drawn to the left");
        Assert.That(firstStart.Left, Is.GreaterThan(firstEnd.Left), "each Arabic word is shaped right to left");
        Assert.That(b.Left, Is.GreaterThan(firstStart.Right - 1), "the trailing LTR text follows the whole RTL run");
    }

    [Test]
    public void RightToLeftParagraphsKeepEmbeddedLatinReadingForward()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
        var block = Block(face);
        block.TextDirection = TextDirection.RightToLeft;
        block.Inlines.Add(new Run(First + " "));
        block.Inlines.Add(new Run("ab") { Foreground = Color.Red });
        block.Inlines.Add(new Run(" cd"));
        var text = ((Label)block).Text;
        var arabic = block.GetCharacterBounds(0);
        var a = block.GetCharacterBounds(text.IndexOf('a'));
        var c = block.GetCharacterBounds(text.IndexOf('c'));
        Assert.That(arabic.Left, Is.GreaterThan(c.Right - 1), "the RTL paragraph starts on the right");
        Assert.That(a.Left, Is.LessThan(c.Left), "embedded Latin across spans still reads left to right");
    }

    [Test]
    public void LineEditRightAlignsRightToLeftParagraphsLikeDirAuto()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
        float Offset(string text, TextDirection direction = TextDirection.Auto)
        {
            var editor = new LineEdit { UIFont = new DynamicUIFont(face, 20), Size = new Vector2(400, 40), Text = text };
            editor.SetTextDirection(direction);
            return editor.GetParagraphAlignmentOffset(editor.GetEditingLayout());
        }
        Assert.Multiple(() =>
        {
            Assert.That(Offset(First), Is.GreaterThan(200), "an Arabic-first draft hugs the right edge");
            Assert.That(Offset(First + " ab"), Is.GreaterThan(0), "the first strong character decides");
            Assert.That(Offset("ab " + First), Is.Zero, "a Latin-first draft stays on the left");
            Assert.That(Offset(First, TextDirection.LeftToRight), Is.Zero, "an explicit LTR field is never flipped");
            Assert.That(Offset(""), Is.Zero);
        });
    }

    [Test]
    public void LeftToRightTextKeepsItsLayout()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/NotoSansArabic_Variable.ttf");
        var styled = Block(face);
        styled.Inlines.Add(new Run("ab "));
        styled.Inlines.Add(new Run("cd") { Foreground = Color.Red });
        var plain = Block(face);
        plain.Inlines.Add(new Run("ab cd"));
        for (var index = 0; index < 5; index++)
            Assert.That(styled.GetCharacterBounds(index), Is.EqualTo(plain.GetCharacterBounds(index)));
    }

    private static TextBlock Block(UIFontFace face) => new()
    {
        UIFont = new DynamicUIFont(face, 24), Padding = Thickness.Zero, Position = new Vector2(10, 10), Size = new Vector2(600, 80),
        AlignInlineBaselines = true,
    };
}
