// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

public sealed class TextBlockMetaFocusTest
{
    [Test]
    public void RevealedMetaRangesAreTabStopsActivatedFromTheKeyboard()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        using var context = new UIContext();
        var host = new Control { Size = new Vector2(600, 200) };
        var block = new TextBlock
        {
            UIFont = new DynamicUIFont(face, 24), Padding = Thickness.Zero, Size = new Vector2(500, 60),
            VisibleCharactersBehavior = LabelVisibleCharactersBehavior.CharactersAfterShaping,
            MetaFocusable = true, FocusMode = FocusMode.All,
        };
        block.Inlines.Add(new Run("See "));
        block.Inlines.Add(new Run("one") { Meta = "a" });
        block.Inlines.Add(new Run(" and "));
        var second = new Span { Meta = "b" };
        second.Inlines.Add(new Run("t"));
        second.Inlines.Add(new Run("wo") { Foreground = Color.Red });
        block.Inlines.Add(second);
        block.Inlines.Add(new Run(" end"));
        var button = new Button { Text = "Continue", Position = new Vector2(0, 100), Size = new Vector2(120, 40), FocusMode = FocusMode.All };
        host.AddChild(block);
        host.AddChild(button);
        context.Add(host);
        context.Layout();
        var activated = new List<object>();
        block.MetaClicked += (_, meta) => activated.Add(meta);

        block.VisibleCharacters = 4;
        Assert.That(block.GetMetaStops(), Is.Empty);
        Assert.That(block.AcceptsFocus, Is.False, "unrevealed links are not stops");
        Press(context, Keys.Tab);
        Assert.That(context.FocusedControl, Is.SameAs(button));

        block.VisibleCharacters = -1;
        var stops = block.GetMetaStops();
        Assert.That(stops.Select(stop => stop.Meta), Is.EqualTo(new object[] { "a", "b" }));
        Assert.That(stops[1].Bounds, Has.Count.EqualTo(2), "one stop spans every run of a link");
        Press(context, Keys.Tab);
        Assert.That(context.FocusedControl, Is.SameAs(block));
        Assert.That(block.FocusedMeta, Is.EqualTo("a"));
        Press(context, Keys.Tab);
        Assert.That(block.FocusedMeta, Is.EqualTo("b"));
        Press(context, Keys.Enter);
        Press(context, Keys.Space);
        Assert.That(activated, Is.EqualTo(new object[] { "b", "b" }));
        Press(context, Keys.Tab);
        Assert.That(context.FocusedControl, Is.SameAs(button));
        Assert.That(block.FocusedMeta, Is.Null);
        Press(context, Keys.Tab, Keys.LeftShift);
        Assert.That(block.FocusedMeta, Is.EqualTo("b"), "Shift+Tab enters at the last stop");
        Press(context, Keys.Tab, Keys.LeftShift);
        Assert.That(block.FocusedMeta, Is.EqualTo("a"));

        context.SetFocus(button);
        var prose = block.GetCharacterBounds(0);
        block.PointerPressed(new Point((int)block.GlobalPosition.X + prose.Center.X, (int)block.GlobalPosition.Y + prose.Center.Y));
        Assert.That(context.FocusedControl, Is.SameAs(button), "clicking prose keeps keyboard focus");
        Assert.That(activated, Has.Count.EqualTo(2));

        block.Enabled = false;
        Assert.That(block.AcceptsFocus, Is.False);
    }

    private static void Press(UIContext context, Keys key, Keys? modifier = null)
    {
        context.Update(new GameTime(), default, modifier is { } held ? new KeyboardState(held, key) : new KeyboardState(key));
        context.Update(new GameTime(), default, modifier is { } still ? new KeyboardState(still) : default);
    }
}
