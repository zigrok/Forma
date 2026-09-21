using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

internal sealed class TextNavigationTest
{
    private sealed class ProfileLineEdit(bool mac) : LineEdit
    {
        internal override bool UsesMacTextNavigation => mac;
    }

    private sealed class ProfileTextEdit(bool mac) : TextEdit
    {
        internal override bool UsesMacTextNavigation => mac;
    }

    private static UIContext Context(LineEdit edit)
    {
        var context = new UIContext { ViewportSize = new Vector2(640, 480) };
        edit.Size = new Vector2(500, 200);
        context.Add(edit);
        edit.GrabFocus();
        return context;
    }

    private static void Press(UIContext context, Keys key, params Keys[] modifiers)
    {
        context.Update(new GameTime(), default, new KeyboardState(modifiers));
        context.Update(new GameTime(), default, new KeyboardState(modifiers.Append(key).ToArray()));
        context.Update(new GameTime(), default, default);
    }

    [TestCase(true, Keys.LeftAlt, Keys.Left, TextNavigationAction.PreviousWord)]
    [TestCase(true, Keys.RightAlt, Keys.Right, TextNavigationAction.NextWord)]
    [TestCase(true, Keys.LeftWindows, Keys.Left, TextNavigationAction.LineStart)]
    [TestCase(true, Keys.RightWindows, Keys.Right, TextNavigationAction.LineEnd)]
    [TestCase(true, Keys.LeftWindows, Keys.Up, TextNavigationAction.DocumentStart)]
    [TestCase(true, Keys.RightWindows, Keys.Down, TextNavigationAction.DocumentEnd)]
    [TestCase(true, Keys.LeftAlt, Keys.Up, TextNavigationAction.PreviousParagraph)]
    [TestCase(true, Keys.RightAlt, Keys.Down, TextNavigationAction.NextParagraph)]
    [TestCase(false, Keys.LeftControl, Keys.Left, TextNavigationAction.PreviousWord)]
    [TestCase(false, Keys.RightControl, Keys.Right, TextNavigationAction.NextWord)]
    [TestCase(false, Keys.RightAlt, Keys.Right, TextNavigationAction.NextCharacter)]
    [TestCase(false, Keys.LeftControl, Keys.Home, TextNavigationAction.DocumentStart)]
    [TestCase(false, Keys.LeftControl, Keys.End, TextNavigationAction.DocumentEnd)]
    [TestCase(true, Keys.LeftControl, Keys.Left, TextNavigationAction.None)]
    public void PlatformMappingSeparatesWordsLinesAndDocuments(bool mac, Keys modifier, Keys key, TextNavigationAction expected) =>
        Assert.That(TextNavigation.Resolve(key, new KeyboardState(modifier), mac, true), Is.EqualTo(expected));

    [TestCase(Keys.Left, Keys.LeftAlt, 6)]
    [TestCase(Keys.Right, Keys.RightAlt, 10)]
    [TestCase(Keys.Left, Keys.LeftWindows, 0)]
    [TestCase(Keys.Right, Keys.RightWindows, 16)]
    [TestCase(Keys.Up, Keys.LeftWindows, 0)]
    [TestCase(Keys.Down, Keys.LeftWindows, 16)]
    public void MacLineEditUsesNativeNavigation(Keys key, Keys modifier, int expected)
    {
        var edit = new ProfileLineEdit(true) { Text = "alpha beta gamma" };
        using var context = Context(edit);
        edit.Select(8, 8);
        Press(context, key, modifier);
        Assert.That(edit.CaretColumn, Is.EqualTo(expected));
        Assert.That(edit.HasSelection, Is.False);
        Assert.That(edit.Text, Is.EqualTo("alpha beta gamma"));
    }

    [TestCase(true, Keys.LeftAlt)]
    [TestCase(false, Keys.LeftControl)]
    public void WordSelectionExtendsAndReversesWithoutChangingAnchor(bool mac, Keys modifier)
    {
        var edit = new ProfileLineEdit(mac) { Text = "alpha beta gamma" };
        using var context = Context(edit);
        edit.Select(11, 11);
        Press(context, Keys.Left, modifier, Keys.LeftShift);
        Assert.That(edit.SelectedText, Is.EqualTo("beta "));
        Press(context, Keys.Left, modifier, Keys.LeftShift);
        Assert.That(edit.SelectedText, Is.EqualTo("alpha beta "));
        Press(context, Keys.Right, modifier, Keys.LeftShift);
        Assert.That(edit.SelectionTo, Is.EqualTo(11));
        Assert.That(edit.CaretColumn, Is.EqualTo(5));
    }

    [TestCase(Keys.Left, Keys.LeftWindows, 1, 0)]
    [TestCase(Keys.Right, Keys.RightWindows, 1, 11)]
    [TestCase(Keys.Up, Keys.LeftWindows, 0, 0)]
    [TestCase(Keys.Down, Keys.RightWindows, 2, 5)]
    [TestCase(Keys.Left, Keys.LeftAlt, 1, 0)]
    [TestCase(Keys.Right, Keys.RightAlt, 1, 5)]
    [TestCase(Keys.Up, Keys.LeftAlt, 1, 0)]
    [TestCase(Keys.Down, Keys.RightAlt, 1, 11)]
    public void MacMultilineDistinguishesLineDocumentAndWordBoundaries(Keys key, Keys modifier, int line, int column)
    {
        var edit = new ProfileTextEdit(true) { Text = "alpha beta\ngamma delta\nomega" };
        using var context = Context(edit);
        edit.SetCaret(1, 3);
        Press(context, key, modifier, Keys.LeftShift);
        Assert.That(edit.CaretLine, Is.EqualTo(line));
        Assert.That(edit.CaretColumnInLine, Is.EqualTo(column));
        Assert.That(edit.GetSelectionOriginLine(), Is.EqualTo(1));
        Assert.That(edit.GetSelectionOriginColumn(), Is.EqualTo(3));
    }

    [Test]
    public void CommandAndOptionUseVisualLineAndHardParagraphRespectively()
    {
        var edit = new ProfileTextEdit(true)
        {
            Text = "alpha beta\ngamma", WrapAtColumn = 6,
            LineWrappingMode = TextEditLineWrappingMode.Boundary
        };
        using var context = Context(edit);
        edit.SetCaret(0, 8);
        Press(context, Keys.Left, Keys.LeftWindows);
        Assert.That(edit.CaretColumnInLine, Is.EqualTo(6));
        Press(context, Keys.Up, Keys.LeftAlt);
        Assert.That(edit.CaretColumnInLine, Is.Zero);
    }

    [Test]
    public void MulticaretsKeepModifierSemanticsAndBackwardSelectionDirection()
    {
        var edit = new ProfileTextEdit(true) { Text = "alpha beta\ngamma delta", MultipleCaretsEnabled = true };
        using var context = Context(edit);
        edit.SetCaret(0, 8);
        edit.AddCaret(1, 8);
        Press(context, Keys.Left, Keys.LeftWindows, Keys.LeftShift);
        Assert.That(edit.GetCaretColumn(0), Is.Zero);
        Assert.That(edit.GetCaretColumn(1), Is.Zero);
        Assert.That(edit.GetSelectionOriginColumn(0), Is.EqualTo(8));
        Assert.That(edit.GetSelectionOriginColumn(1), Is.EqualTo(8));
        Press(context, Keys.Down, Keys.LeftWindows);
        Assert.That(edit.CaretCount, Is.EqualTo(1));
        Assert.That(edit.CaretColumn, Is.EqualTo(edit.Text.Length));
    }

    [Test]
    public void MergingSelectionsAtDocumentStartRetainsTheActiveCaretAtTheStart()
    {
        var edit = new ProfileTextEdit(true) { Text = "alpha beta\ngamma delta", MultipleCaretsEnabled = true };
        using var context = Context(edit);
        edit.SetCaret(0, 8);
        edit.AddCaret(1, 8);
        Press(context, Keys.Up, Keys.LeftWindows, Keys.LeftShift);
        Assert.That(edit.CaretCount, Is.EqualTo(1));
        Assert.That(edit.CaretColumn, Is.Zero);
        Assert.That(edit.SelectionFrom, Is.Zero);
        Assert.That(edit.SelectionTo, Is.EqualTo(19));
    }

    [Test]
    public void MergingBackwardSelectionsDoesNotReverseAnUnrelatedForwardSelection()
    {
        var edit = new ProfileTextEdit(true) { Text = "abcdef ghijkl mnopqr stuvwx", MultipleCaretsEnabled = true };
        using var context = Context(edit);
        edit.Select(0, 2, 0, 3);
        edit.AddCaret(0, 5);
        edit.Select(0, 4, 0, 5, 1);
        edit.AddCaret(0, 22);
        edit.Select(0, 14, 0, 22, 2);
        Press(context, Keys.Left, Keys.LeftAlt, Keys.LeftShift);
        Assert.That(edit.CaretCount, Is.EqualTo(2));
        Assert.That(edit.CaretColumn, Is.Zero);
        Assert.That(edit.GetSelectionOriginColumn(1), Is.EqualTo(14));
        Assert.That(edit.GetCaretColumn(1), Is.EqualTo(21));
        Press(context, Keys.Right, Keys.LeftAlt, Keys.LeftShift);
        Assert.That(edit.GetSelectionOriginColumn(1), Is.EqualTo(14));
        Assert.That(edit.GetCaretColumn(1), Is.EqualTo(edit.Text.Length));
    }

    [TestCase(true, Keys.LeftWindows, Keys.Back, 8, "ta")]
    [TestCase(true, Keys.LeftWindows, Keys.Delete, 8, "al")]
    [TestCase(true, Keys.LeftAlt, Keys.Back, 4, "a beta")]
    [TestCase(false, Keys.LeftControl, Keys.Back, 4, "a beta")]
    public void OverlappingWordAndLineDeletionsMergeIntoOneUndoableEdit(bool mac, Keys modifier, Keys key, int second, string expected)
    {
        var edit = new ProfileTextEdit(mac) { Text = "alpha beta", MultipleCaretsEnabled = true };
        using var context = Context(edit);
        edit.ClearUndoHistory();
        edit.SetCaret(0, 2);
        edit.AddCaret(0, second);
        Press(context, key, modifier);
        Assert.That(edit.Text, Is.EqualTo(expected));
        Assert.That(edit.CaretCount, Is.EqualTo(1));
        edit.Undo();
        Assert.That(edit.Text, Is.EqualTo("alpha beta"));
        Assert.That(edit.HasUndo, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CharacterNavigationDoesNotSplitEmojiOrCombiningSequences(bool multiline)
    {
        LineEdit edit = multiline ? new ProfileTextEdit(true) : new ProfileLineEdit(true);
        edit.Text = "A\U0001F600e\u0301Z";
        using var context = Context(edit);
        edit.Select(5, 5);
        Press(context, Keys.Left);
        Assert.That(edit.CaretColumn, Is.EqualTo(3));
        Press(context, Keys.Left);
        Assert.That(edit.CaretColumn, Is.EqualTo(1));
        Press(context, Keys.Right);
        Assert.That(edit.CaretColumn, Is.EqualTo(3));
    }

    [Test]
    public void MacDeletionUsesWordOrCurrentLineRatherThanWholeDocument()
    {
        var edit = new ProfileTextEdit(true) { Text = "alpha beta\ngamma delta\nomega" };
        using var context = Context(edit);
        edit.SetCaret(1, 6);
        Press(context, Keys.Back, Keys.LeftAlt);
        Assert.That(edit.Text, Is.EqualTo("alpha beta\ndelta\nomega"));
        edit.SetCaret(1, 3);
        Press(context, Keys.Back, Keys.LeftWindows);
        Assert.That(edit.Text, Is.EqualTo("alpha beta\nta\nomega"));
    }
}
