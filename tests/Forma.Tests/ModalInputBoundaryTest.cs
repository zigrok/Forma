// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

public sealed class ModalInputBoundaryTest
{
    [Test]
    public void OpeningModalCancelsPreexistingPointerCapture()
    {
        using var context = CreateContext();
        var background = new Button { Size = new Vector2(100, 40) };
        var calls = 0;
        background.Pressed += (_, _) => calls++;
        context.Add(background);
        context.InjectPointerPress(new Point(10, 10));
        var popup = new Popup { Visible = false, Size = new Vector2(120, 80) };
        context.Add(popup);
        popup.PopupAt(new Vector2(150, 50));
        context.InjectPointerRelease(new Point(10, 10));
        Assert.That(calls, Is.Zero);
    }

    [TestCase("collapse")]
    [TestCase("disable")]
    [TestCase("detach")]
    [TestCase("hidden")]
    public void IneligibleAncestorCancelsPreeditAndBarsTextAndFocus(string change)
    {
        using var context = CreateContext();
        var parent = new Control { Size = new Vector2(200, 100) };
        var editor = new LineEdit { Text = "old", Size = new Vector2(100, 30) };
        parent.AddChild(editor);
        context.Add(parent);
        editor.GrabFocus();
        context.TextComposition("draft", 1, 2);
        if (change == "collapse") parent.Visible = false;
        else if (change == "disable") parent.Enabled = false;
        else if (change == "hidden") parent.Visibility = Visibility.Hidden;
        else context.Remove(parent);
        context.TextInput('x');
        Tick(context, Keys.Back);
        Tick(context);
        context.TextComposition("late");
        editor.GrabFocus();
        Assert.Multiple(() =>
        {
            Assert.That(context.FocusedControl, Is.Null);
            Assert.That(editor.Text, Is.EqualTo("old"));
            Assert.That(editor.HasImeComposition, Is.False);
        });
    }

    [Test]
    public void HiddenOpenerIsNotRestored()
    {
        using var context = CreateContext();
        var group = new Control();
        var opener = new Button();
        group.AddChild(opener);
        context.Add(group);
        opener.GrabFocus();
        var popup = new Popup { Visible = false };
        context.Add(popup);
        popup.PopupAt(Vector2.Zero);
        group.Visible = false;
        popup.Hide();
        Assert.That(context.FocusedControl, Is.Null);
    }

    [Test]
    public void ClosingEarlierModalCannotRestoreFocusThroughCurrentModal()
    {
        using var context = CreateContext();
        var opener = new Button();
        context.Add(opener);
        opener.GrabFocus();
        var first = new Popup { Visible = false };
        using var session = new ModalSession<int>(context, first, _ => { });
        session.Open(Vector2.Zero);
        var current = new Popup { Visible = false };
        context.Add(current);
        current.PopupAt(new Vector2(150, 50));
        session.Cancel();
        Assert.That(context.FocusedControl, Is.SameAs(current));
        opener.GrabFocus();
        Assert.That(context.FocusedControl, Is.SameAs(current));
    }

    [Test]
    public void CancellingHeldKeyboardPressDoesNotActivateOrLeaveButtonStuck()
    {
        using var context = CreateContext();
        var button = new Button();
        var calls = 0;
        button.Pressed += (_, _) => calls++;
        context.Add(button);
        button.GrabFocus();
        Tick(context, Keys.Space);
        button.Visible = false;
        Tick(context);
        Assert.That(calls, Is.Zero);
        button.Visible = true;
        button.GrabFocus();
        Tick(context, Keys.Space);
        Tick(context);
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void DetachPropagatesContextDespiteInputCancellationFailure(bool buttonUp, bool directlyRoot)
    {
        using var context = CreateContext();
        var parent = new Control();
        var button = new Button();
        var sibling = new Control();
        var root = directlyRoot ? button : parent;
        if (!directlyRoot) parent.AddChild(button);
        root.AddChild(sibling);
        context.Add(root);
        button.GrabFocus();
        Tick(context, Keys.Space);
        var error = new InvalidOperationException("input cleanup");
        if (buttonUp) button.ButtonUp += (_, _) => throw error;
        else button.FocusExited += (_, _) => throw error;
        Assert.That(Assert.Throws<InvalidOperationException>(() => context.Remove(root)), Is.SameAs(error));
        Assert.Multiple(() =>
        {
            Assert.That(context.Roots, Is.Empty);
            Assert.That(root.Context, Is.Null);
            Assert.That(button.Context, Is.Null);
            Assert.That(sibling.Context, Is.Null);
            Assert.That(context.FocusedControl, Is.Null);
        });
    }

    [TestCase("")]
    [TestCase("Draft")]
    public void OrdinaryTypingIntoEmptyOrSelectedTextKeepsCaretWithinText(string initial)
    {
        using var context = CreateContext();
        var editor = new LineEdit { Text = initial };
        context.Add(editor);
        editor.GrabFocus();
        if (initial.Length != 0) editor.SelectAll();
        Assert.DoesNotThrow(() =>
        {
            context.TextInput('E');
            context.TextInput('d');
        });
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("Ed"));
            Assert.That(editor.CaretColumn, Is.EqualTo(2));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void StringInsertionUsesOriginalOffset(int offset)
    {
        var editor = new LineEdit { Text = "abc" };
        Assert.That(editor.CaretColumn, Is.EqualTo(3));
        editor.Select(offset, offset);
        editor.Deselect();
        editor.InsertText("XY");
        Assert.That(editor.Text, Is.EqualTo("abc".Insert(offset, "XY")));
        Assert.That(editor.CaretColumn, Is.EqualTo(offset + 2));
        editor.InsertText("Z");
        Assert.That(editor.Text, Is.EqualTo("abc".Insert(offset, "XYZ")));
        Assert.That(editor.CaretColumn, Is.EqualTo(offset + 3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProgrammaticInitialPopulationRetainsConfiguredPolicy(bool moveToEnd)
    {
        LineEdit editor = moveToEnd ? new LineEdit() : new TextEdit();
        editor.Text = "abc";
        Assert.That(editor.CaretColumn, Is.EqualTo(moveToEnd ? 3 : 0));
        editor.Text = "abcdef";
        Assert.That(editor.CaretColumn, Is.EqualTo(moveToEnd ? 3 : 0));
    }

    [Test]
    public void SuccessiveCodeUnitsAndSyntheticStringCompositionKeepExistingOffsets()
    {
        using var context = CreateContext();
        var editor = new LineEdit();
        context.Add(editor);
        editor.GrabFocus();
        const string units = "\uD83D\uDE42e\u0301";
        for (var index = 0; index < units.Length; index++)
        {
            context.TextInput(units[index]);
            Assert.That(editor.Text, Is.EqualTo(units[..(index + 1)]));
            Assert.That(editor.CaretColumn, Is.EqualTo(index + 1));
        }
        context.TextComposition("\u65E5\u672C");
        Assert.That(editor.Text, Is.EqualTo(units));
        editor.CommitImeComposition();
        context.TextInput('!');
        Assert.That(editor.Text, Is.EqualTo(units + "\u65E5\u672C!"));
        Assert.That(editor.CaretColumn, Is.EqualTo(7));
        editor.Select(0, 2);
        editor.InsertText("\u03B1\u03B2");
        Assert.That(editor.Text, Is.EqualTo("\u03B1\u03B2e\u0301\u65E5\u672C!"));
        Assert.That(editor.CaretColumn, Is.EqualTo(2));
    }

    [Test]
    public void ResultingCaretIsBoundedAfterLimitAndTextNotification()
    {
        var editor = new LineEdit { MaxLength = 2 };
        editor.InsertText("abcd");
        Assert.That(editor.Text, Is.EqualTo("ab"));
        Assert.That(editor.CaretColumn, Is.EqualTo(2));
        editor.SelectAll();
        editor.TextChanged += (_, text) => { if (text.Length != 0) editor.Text = ""; };
        editor.InsertText("x");
        Assert.That(editor.Text, Is.Empty);
        Assert.That(editor.CaretColumn, Is.Zero);
    }

    internal static UIContext CreateContext() => new(new Clipboard()) { ViewportSize = new Vector2(400, 300) };
    internal static void Tick(UIContext context, params Keys[] keys) =>
        context.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)), default, new KeyboardState(keys));

    private sealed class Clipboard : IClipboard
    {
        public string GetText() => string.Empty;
        public bool SetText(string text) => true;
    }
}
