// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

public sealed class ClipboardShortcutTest
{
    private const string Sample = "caf\u00e9 \u65e5\u672c\u8a9e \U0001f600";

    private sealed class MemoryClipboard : IClipboard
    {
        internal string Text;
        internal bool AcceptWrites = true;
        internal bool ThrowOnWrite;
        internal int Reads;
        internal int Writes;
        public string GetText() { Reads++; return Text; }
        public bool SetText(string text)
        {
            Writes++;
            if (ThrowOnWrite) throw new InvalidOperationException("Clipboard transport failed.");
            if (!AcceptWrites) return false;
            Text = text;
            return true;
        }
    }

    private static UIContext Attach(LineEdit editor, IClipboard clipboard)
    {
        var context = new UIContext(clipboard) { ViewportSize = new Vector2(640, 480) };
        editor.Size = new Vector2(500, 200);
        context.Add(editor);
        editor.GrabFocus();
        editor.ClearUndoHistory();
        if (editor is TextEdit multiline) multiline.ClearUndoHistory();
        return context;
    }

    private static void Press(UIContext context, Keys key, params Keys[] modifiers)
    {
        context.Update(new GameTime(), default, new KeyboardState(modifiers));
        context.Update(new GameTime(), default, new KeyboardState(modifiers.Append(key).ToArray()));
        context.Update(new GameTime(), default, default);
    }

    [Test, Combinatorial]
    public void StandardPlatformShortcutsRoundTripUnicode(
        [Values("macOS", "Windows", "Linux")] string platform,
        [Values(false, true)] bool multiline, [Values(false, true)] bool rightModifier)
    {
        var modifier = platform == "macOS"
            ? rightModifier ? Keys.RightWindows : Keys.LeftWindows
            : rightModifier ? Keys.RightControl : Keys.LeftControl;
        LineEdit editor = multiline ? new TextEdit() : new LineEdit();
        editor.Text = Sample;
        var clipboard = new MemoryClipboard();
        using var context = Attach(editor, clipboard);
        Press(context, Keys.A, modifier);
        Press(context, Keys.C, modifier);
        Assert.That(clipboard.Text, Is.EqualTo(Sample));
        Assert.That(editor.Text, Is.EqualTo(Sample));
        Assert.That(editor.HasUndo, Is.False);
        Press(context, Keys.X, modifier);
        Assert.That(editor.Text, Is.Empty);
        Assert.That(clipboard.Text, Is.EqualTo(Sample));
        Press(context, Keys.V, modifier);
        Assert.That(editor.Text, Is.EqualTo(Sample));
        Assert.That(clipboard.Reads, Is.EqualTo(1));
        Assert.That(clipboard.Writes, Is.EqualTo(2));
        Press(context, Keys.Z, modifier);
        Assert.That(editor.Text, Is.Empty);
        Press(context, Keys.Z, modifier, Keys.LeftShift);
        Assert.That(editor.Text, Is.EqualTo(Sample));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void FailedCutPreservesTextSelectionAndUndo(bool multiline, bool throws)
    {
        LineEdit editor = multiline ? new TextEdit() : new LineEdit();
        editor.Text = Sample;
        var clipboard = new MemoryClipboard { Text = "prior", AcceptWrites = false, ThrowOnWrite = throws };
        using var context = Attach(editor, clipboard);
        editor.SelectAll();
        var failures = new List<ClipboardOperation>();
        editor.ClipboardOperationFailed += (_, operation) => failures.Add(operation);
        if (throws) Assert.Throws<InvalidOperationException>(() => Press(context, Keys.X, Keys.LeftWindows));
        else Press(context, Keys.X, Keys.LeftWindows);
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo(Sample));
            Assert.That(editor.SelectedText, Is.EqualTo(Sample));
            Assert.That(editor.HasUndo, Is.False);
            Assert.That(clipboard.Text, Is.EqualTo("prior"));
            Assert.That(failures, Is.EqualTo(throws ? Array.Empty<ClipboardOperation>() : new[] { ClipboardOperation.Cut }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedMulticaretAndWholeLineCutsAreAtomic(bool selected)
    {
        var editor = new TextEdit { Text = "alpha\nbravo", MultipleCaretsEnabled = true };
        var clipboard = new MemoryClipboard { AcceptWrites = false };
        using var context = Attach(editor, clipboard);
        editor.SetCaret(0, 0);
        editor.AddCaret(1, 0);
        if (selected) { editor.Select(0, 0, 0, 1); editor.Select(1, 0, 1, 1, 1); }
        var carets = editor.GetCarets().ToArray();
        editor.Cut();
        Assert.That(editor.Text, Is.EqualTo("alpha\nbravo"));
        Assert.That(editor.GetCarets(), Is.EqualTo(carets));
        Assert.That(editor.HasUndo, Is.False);
        Assert.That(clipboard.Writes, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnavailableCopyAndPasteReportFailureWithoutEdits(bool multiline)
    {
        LineEdit editor = multiline ? new TextEdit() : new LineEdit();
        editor.Text = Sample;
        var clipboard = new MemoryClipboard { AcceptWrites = false };
        using var context = Attach(editor, clipboard);
        editor.SelectAll();
        var failures = new List<ClipboardOperation>();
        editor.ClipboardOperationFailed += (_, operation) => failures.Add(operation);
        Press(context, Keys.C, Keys.LeftControl);
        Press(context, Keys.V, Keys.LeftControl);
        Assert.That(failures, Is.EqualTo(new[] { ClipboardOperation.Copy, ClipboardOperation.Paste }));
        Assert.That(editor.Text, Is.EqualTo(Sample));
        Assert.That(editor.SelectedText, Is.EqualTo(Sample));
        Assert.That(editor.HasUndo, Is.False);
        clipboard.Text = string.Empty;
        Press(context, Keys.V, Keys.LeftControl);
        Assert.That(failures.Count, Is.EqualTo(2), "An empty available clipboard is not an error.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReadonlyCutCopiesAndPasteDoesNotReadClipboard(bool multiline)
    {
        LineEdit editor = multiline ? new TextEdit() : new LineEdit();
        editor.Text = Sample;
        editor.Editable = false;
        var clipboard = new MemoryClipboard();
        using var context = Attach(editor, clipboard);
        editor.SelectAll();
        Press(context, Keys.X, Keys.LeftControl);
        Press(context, Keys.V, Keys.LeftControl);
        Assert.That(clipboard.Text, Is.EqualTo(Sample));
        Assert.That(clipboard.Reads, Is.Zero);
        Assert.That(editor.Text, Is.EqualTo(Sample));
        Assert.That(editor.HasUndo, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AltGrAndDisabledShortcutsDoNotAccessClipboard(bool multiline)
    {
        LineEdit editor = multiline ? new TextEdit() : new LineEdit();
        editor.Text = Sample;
        var clipboard = new MemoryClipboard();
        using var context = Attach(editor, clipboard);
        editor.SelectAll();
        foreach (var key in new[] { Keys.C, Keys.X, Keys.V })
            Press(context, key, Keys.LeftControl, Keys.RightAlt);
        Assert.That(editor.Text, Is.EqualTo(Sample));
        editor.ShortcutKeysEnabled = false;
        foreach (var key in new[] { Keys.C, Keys.X, Keys.V })
            Press(context, key, Keys.LeftWindows);
        Assert.That(clipboard.Reads, Is.Zero);
        Assert.That(clipboard.Writes, Is.Zero);
        Assert.That(editor.Text, Is.EqualTo(Sample));
        context.TextInput('\u00a9');
        Assert.That(editor.Text, Is.EqualTo("\u00a9"), "Printable AltGr text still reaches the editor.");
    }

    [Test]
    public void PasswordClipboardCommandsNeverExportTheSecret()
    {
        var editor = new LineEdit { Text = Sample, SecretCharacter = "*" };
        var clipboard = new MemoryClipboard();
        using var context = Attach(editor, clipboard);
        editor.SelectAll();
        Press(context, Keys.C, Keys.LeftWindows);
        Press(context, Keys.X, Keys.LeftWindows);
        Assert.That(clipboard.Writes, Is.Zero);
        Assert.That(editor.Text, Is.EqualTo(Sample));
    }

    [Test]
    public void MultilinePasteNormalizesWindowsLineEndingsAndDistributesToCarets()
    {
        var editor = new TextEdit { Text = "a0\nb1", MultipleCaretsEnabled = true };
        var clipboard = new MemoryClipboard { Text = "X\r\nY" };
        using var context = Attach(editor, clipboard);
        editor.SetCaret(0, 1);
        editor.AddCaret(1, 1);
        Press(context, Keys.V, Keys.RightControl);
        Assert.That(editor.Text, Is.EqualTo("aX0\nbY1"));
        editor.Undo();
        Assert.That(editor.Text, Is.EqualTo("a0\nb1"));
    }
}
