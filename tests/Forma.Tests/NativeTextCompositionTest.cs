// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

public sealed class NativeTextCompositionTest
{
    private sealed class PositionMappedLineEdit : LineEdit
    {
        protected override int GetCaretColumnAtPosition(Point position) => Math.Clamp(position.X, 0, Text.Length);
    }

    private sealed class DragButton : BaseButton
    {
        internal int Releases { get; private set; }
        public override object GetDragData(Point position) => "payload";
        internal override void PointerReleased(Point position, bool isInside)
        {
            Releases++;
            base.PointerReleased(position, isInside);
        }
    }

    private sealed class DropControl : Control
    {
        internal int Drops { get; private set; }
        public override bool CanDropData(Point position, object data) => Equals(data, "payload");
        public override void DropData(Point position, object data) => Drops++;
    }

    private static void PointerTick(UIContext context, int x, int y, ButtonState left = ButtonState.Released) =>
        context.Update(new GameTime(), new MouseState(x, y, 0, left, ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released), default);

    public sealed class NativeWindowStub
    {
        public bool SupportsTextComposition { get; set; } = true;
        public event Action<string> TextCommitted;
        public event Action<string, int, int> TextEditing;
        public readonly List<bool> Activations = new();
        public readonly List<Rectangle> Rectangles = new();
        public bool SetTextInputActive(bool active)
        {
            Activations.Add(active);
            if (!active) Edit(string.Empty);
            return true;
        }
        public bool SetTextInputRectangle(Rectangle rectangle) { Rectangles.Add(rectangle); return true; }
        public void Commit(string text) => TextCommitted?.Invoke(text);
        public void Edit(string text, int start = 0, int length = 0) => TextEditing?.Invoke(text, start, length);
    }

    private static UIContext CreateContext() => new() { ViewportSize = new Vector2(640, 480) };
    private static LineEdit AddEditor(UIContext context, string text = "before")
    {
        var editor = new LineEdit { Text = text, Size = new Vector2(240, 40) };
        context.Add(editor);
        editor.GrabFocus();
        editor.ClearUndoHistory();
        return editor;
    }

    [Test]
    public void OptionalBridgeRejectsMissingAndUnsupportedRuntimeApis()
    {
        Assert.That(RuntimeTextCompositionBridge.TryCreate(new object(), typeof(object)), Is.Null);
        var window = new NativeWindowStub { SupportsTextComposition = false };
        Assert.That(RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub)), Is.Null);
        Assert.That(window.Activations, Is.Empty);
    }

    [Test]
    public void BridgeRoutesPreeditAndAtomicUnicodeCommitThroughFocusedEditor()
    {
        using var context = CreateContext();
        var editor = AddEditor(context);
        editor.Select(0, editor.Text.Length);
        var changes = new List<string>();
        editor.TextChanged += (_, text) => changes.Add(text);
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("日😀e\u0301", 1, 2);
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("before"));
            Assert.That(editor.ImeCompositionSelection, Is.EqualTo(new Point(1, 2)));
            Assert.That(changes, Is.Empty);
            Assert.That(editor.HasUndo, Is.False);
        });
        // Cocoa sends empty marked text immediately before its complete commit.
        window.Edit(string.Empty);
        window.Commit("日本😀e\u0301");
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(new[] { "日本😀e\u0301" }));
            Assert.That(editor.CaretColumn, Is.EqualTo("日本😀e\u0301".Length));
            Assert.That(editor.HasImeComposition, Is.False);
        });
        editor.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("before"));
            Assert.That(editor.HasUndo, Is.False);
        });
    }

    [Test]
    public void NativeNavigationDoesNotPrematurelyCommitOrSubmitPreedit()
    {
        using var context = CreateContext();
        var editor = AddEditor(context);
        var submissions = 0;
        editor.TextSubmitted += (_, _) => submissions++;
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("draft");
        foreach (var key in new[] { Keys.Enter, Keys.Left, Keys.Back, Keys.Tab, Keys.Escape })
        {
            context.Update(new GameTime(), default, new KeyboardState(key));
            context.Update(new GameTime(), default, default);
        }
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("before"));
            Assert.That(editor.ImeCompositionText, Is.EqualTo("draft"));
            Assert.That(submissions, Is.Zero);
            Assert.That(context.FocusedControl, Is.SameAs(editor));
        });
        window.Edit(string.Empty);
        Assert.That(editor.HasImeComposition, Is.False);
    }

    [Test]
    public void FocusTransferCancelsNativeAndLocalCompositionWithoutEditingEitherControl()
    {
        using var context = CreateContext();
        var first = AddEditor(context);
        var second = AddEditor(context, "second");
        first.GrabFocus();
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("draft");
        second.GrabFocus();
        Assert.Multiple(() =>
        {
            Assert.That(first.HasImeComposition, Is.False);
            Assert.That(first.NativeImeActive, Is.False);
            Assert.That(second.NativeImeActive, Is.True);
            Assert.That(first.Text, Is.EqualTo("before"));
            Assert.That(second.Text, Is.EqualTo("second"));
            Assert.That(window.Activations, Is.EqualTo(new[] { true, false, true }));
        });
        window.Commit("😀");
        Assert.That(second.Text, Is.EqualTo("second😀"));
    }

    [Test]
    public void WindowDeactivationRejectsLateTextAndResetsModifiers()
    {
        using var context = CreateContext();
        var editor = AddEditor(context);
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("draft");
        context.Update(new GameTime(), default, new KeyboardState(Keys.LeftShift, Keys.LeftControl));
        session.Update(false);
        context.ResetPlatformInput();
        window.Commit("late");
        window.Edit("late");
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("before"));
            Assert.That(editor.HasImeComposition, Is.False);
            Assert.That(context.CurrentKeyboardState.GetPressedKeys(), Is.Empty);
        });
        session.Update(true);
        window.Commit("x");
        Assert.That(editor.Text, Is.EqualTo("beforex"));
    }

    [TestCase("")]
    [TestCase("日本語")]
    public void DeactivationCancelsJapanesePreeditButPreservesCommittedText(string committed)
    {
        using var context = CreateContext();
        var editor = AddEditor(context, "");
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        if (committed.Length != 0) window.Commit(committed);
        window.Edit("その目誰の目");
        session.Update(false);
        context.ResetPlatformInput();
        session.Update(true);
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo(committed));
            Assert.That(editor.ImeCompositionText, Is.Empty);
            Assert.That(editor.HasUndo, Is.EqualTo(committed.Length != 0));
        });
        window.Edit("ありがとう");
        window.Commit("ありがとう");
        session.Update(false);
        context.ResetPlatformInput();
        session.Update(true);
        Assert.That(editor.Text, Is.EqualTo(committed + "ありがとう"));
        editor.Undo();
        Assert.That(editor.Text, Is.EqualTo(committed));
    }

    [Test]
    public void HeldShiftStillExtendsSelectionAfterNativeCommit()
    {
        using var context = CreateContext();
        var editor = AddEditor(context);
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("draft");
        context.Update(new GameTime(), default, new KeyboardState(Keys.LeftShift));
        window.Commit("日");
        context.Update(new GameTime(), default, new KeyboardState(Keys.LeftShift, Keys.Left));
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("before日"));
            Assert.That(editor.SelectionFrom, Is.EqualTo(6));
            Assert.That(editor.SelectionTo, Is.EqualTo(7));
            Assert.That(context.CurrentKeyboardState.IsKeyDown(Keys.LeftShift), Is.True);
        });
    }

    [Test]
    public void WindowDeactivationCancelsPointerSelectionButPreservesFocusAndSelection()
    {
        using var context = CreateContext();
        var editor = new PositionMappedLineEdit { Text = "abcdef", Size = new Vector2(100, 40) };
        context.Add(editor);
        PointerTick(context, 1, 10, ButtonState.Pressed);
        PointerTick(context, 3, 10, ButtonState.Pressed);
        Assert.That(new Point(editor.SelectionFrom, editor.SelectionTo), Is.EqualTo(new Point(1, 3)));
        context.ResetPlatformInput();
        Assert.Multiple(() =>
        {
            Assert.That(context.FocusedControl, Is.SameAs(editor));
            Assert.That(context.HasPinnedInteraction(editor), Is.False);
        });
        // The physical release happened outside the window; only unpressed motion returns.
        PointerTick(context, 5, 10);
        PointerTick(context, 4, 10);
        Assert.That(new Point(editor.SelectionFrom, editor.SelectionTo), Is.EqualTo(new Point(1, 3)));
        PointerTick(context, 0, 10, ButtonState.Pressed);
        PointerTick(context, 2, 10, ButtonState.Pressed);
        PointerTick(context, 2, 10);
        Assert.That(new Point(editor.SelectionFrom, editor.SelectionTo), Is.EqualTo(new Point(0, 2)));
        context.TextInput("X");
        Assert.That(editor.Text, Is.EqualTo("Xcdef"));
    }

    [Test]
    public void WindowDeactivationCancelsButtonCaptureWithoutClickAndAllowsNextClick()
    {
        using var context = CreateContext();
        var button = new Button { Size = new Vector2(100, 40) };
        var clicks = 0;
        var ups = 0;
        button.Pressed += (_, _) => clicks++;
        button.ButtonUp += (_, _) => ups++;
        context.Add(button);
        PointerTick(context, 10, 10, ButtonState.Pressed);
        context.ResetPlatformInput();
        PointerTick(context, 20, 10);
        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero);
            Assert.That(ups, Is.EqualTo(1));
            Assert.That(context.FocusedControl, Is.SameAs(button));
            Assert.That(context.HasPinnedInteraction(button), Is.False);
        });
        PointerTick(context, 10, 10, ButtonState.Pressed);
        PointerTick(context, 10, 10);
        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void WindowDeactivationCancelsDragWithoutReleaseActionOrDropAndAllowsNextDrag()
    {
        using var context = CreateContext();
        var source = new DragButton { Size = new Vector2(70, 40) };
        var target = new DropControl { Position = new Vector2(100, 0), Size = new Vector2(70, 40) };
        var endings = new List<bool>();
        var clicks = 0;
        source.DragEnded += (_, success) =>
        {
            if (!success) Assert.That(context.IsDragging, Is.False);
            endings.Add(success);
        };
        source.Pressed += (_, _) => clicks++;
        context.Add(source);
        context.Add(target);
        PointerTick(context, 10, 10, ButtonState.Pressed);
        PointerTick(context, 110, 10, ButtonState.Pressed);
        Assert.That(context.IsDragging, Is.True);
        context.ResetPlatformInput();
        PointerTick(context, 115, 10);
        Assert.Multiple(() =>
        {
            Assert.That(context.FocusedControl, Is.SameAs(source));
            Assert.That(context.IsDragging, Is.False);
            Assert.That(context.DragData, Is.Null);
            Assert.That(context.HasPinnedInteraction(source), Is.False);
            Assert.That(endings, Is.EqualTo(new[] { false }));
            Assert.That(source.Releases, Is.Zero);
            Assert.That(target.Drops, Is.Zero);
            Assert.That(clicks, Is.Zero);
        });
        PointerTick(context, 10, 10, ButtonState.Pressed);
        PointerTick(context, 110, 10, ButtonState.Pressed);
        PointerTick(context, 110, 10);
        Assert.Multiple(() =>
        {
            Assert.That(target.Drops, Is.EqualTo(1));
            Assert.That(endings, Is.EqualTo(new[] { false, true }));
            Assert.That(clicks, Is.Zero);
        });
    }

    [TestCase("disabled")]
    [TestCase("detached")]
    [TestCase("readonly")]
    public void IneligibleEditorCannotReceiveBufferedCommit(string reason)
    {
        using var context = CreateContext();
        var editor = AddEditor(context);
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("draft");
        if (reason == "disabled") editor.Enabled = false;
        else if (reason == "detached") context.Remove(editor);
        else editor.Editable = false;
        window.Commit("late");
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("before"));
            Assert.That(editor.HasImeComposition, Is.False);
            Assert.That(window.Activations.Last(), Is.False);
        });
    }

    [Test]
    public void LocalCancellationResetsNativeCompositionAndKeepsTypingAvailable()
    {
        using var context = CreateContext();
        var editor = AddEditor(context);
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("draft");
        editor.CancelImeComposition();
        Assert.That(window.Activations, Is.EqualTo(new[] { true, false, true }));
        window.Commit("x");
        Assert.That(editor.Text, Is.EqualTo("beforex"));
    }

    [Test]
    public void ProgrammaticCaretMovementCancelsTheOldNativeReplacementRange()
    {
        using var context = CreateContext();
        var editor = AddEditor(context);
        editor.Select(0, 3);
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("draft");
        editor.Select(editor.Text.Length, editor.Text.Length);
        window.Commit("x");
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("beforex"));
            Assert.That(editor.HasImeComposition, Is.False);
            Assert.That(window.Activations, Is.EqualTo(new[] { true, false, true }));
        });
    }

    [Test]
    public void CaretAreaTracksCaretMovementResizeAndDisplayScale()
    {
        using var face = UIFontFace.FromProjectFile(TestContext.CurrentContext.TestDirectory, "Fonts/Inter_Regular.ttf");
        using var context = CreateContext();
        var editor = AddEditor(context, "abcdefghijklmno");
        editor.UIFont = new DynamicUIFont(face, 18);
        editor.Position = new Vector2(20, 30);
        editor.Size = new Vector2(100, 40);
        context.Layout();
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        editor.Select(0, 0);
        session.Update(true);
        var start = window.Rectangles.Last();
        editor.Select(editor.Text.Length, editor.Text.Length);
        session.Update(true);
        var end = window.Rectangles.Last();
        Assert.That(end.X, Is.GreaterThan(start.X));
        editor.Position += new Vector2(30, 10);
        editor.Size = new Vector2(200, 60);
        context.Layout();
        session.Update(true);
        var moved = window.Rectangles.Last();
        Assert.That(moved.X, Is.GreaterThan(end.X));
        Assert.That(moved.Y, Is.GreaterThan(end.Y));
        context.DisplayScale = 2;
        session.Update(true);
        Assert.That(window.Rectangles.Last(), Is.EqualTo(
            new Rectangle(moved.X * 2, moved.Y * 2, moved.Width * 2, moved.Height * 2)));
    }

    [TestCase(1f, 1920, 1200)]
    [TestCase(2f, 960, 600)]
    [TestCase(1.5f, 1280, 800)]
    public void ComponentViewportUsesLogicalSizeWhileCaretAreaUsesDrawablePixels(
        float scale, int logicalWidth, int logicalHeight)
    {
        using var context = CreateContext();
        context.DisplayScale = scale;
        context.SetDrawableViewportSize(1920, 1200);
        var root = new Control();
        context.Add(root);
        context.Layout();
        Assert.Multiple(() =>
        {
            Assert.That(context.ViewportSize, Is.EqualTo(new Vector2(logicalWidth, logicalHeight)));
            Assert.That(root.Size, Is.EqualTo(context.ViewportSize));
        });
    }

    [TestCase("a😀b", 2, "a")]
    [TestCase("e\u0301x", 1, "")]
    [TestCase("😀x", 2, "😀")]
    public void AtomicCommitLengthLimitDoesNotSplitUnicodeTextElements(string text, int limit, string expected)
    {
        using var context = CreateContext();
        var editor = AddEditor(context, string.Empty);
        editor.MaxLength = limit;
        context.TextInput(text);
        Assert.That(editor.Text, Is.EqualTo(expected));
    }

    [Test]
    public void TextEditRetainsLegacyTypingWithoutClaimingNativeComposition()
    {
        using var context = CreateContext();
        var editor = new TextEdit();
        context.Add(editor);
        editor.GrabFocus();
        var window = new NativeWindowStub();
        using var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        using var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("draft");
        window.Commit("ab");
        Assert.Multiple(() =>
        {
            Assert.That(editor.SupportsNativeTextComposition, Is.False);
            Assert.That(editor.NativeImeActive, Is.False);
            Assert.That(editor.HasImeComposition, Is.False);
            Assert.That(editor.Text, Is.EqualTo("ab"));
            Assert.That(window.Rectangles, Is.Empty);
        });
    }

    [Test]
    public void DisposalUnsubscribesAndCancelsNativeSession()
    {
        using var context = CreateContext();
        var editor = AddEditor(context);
        var window = new NativeWindowStub();
        var bridge = RuntimeTextCompositionBridge.TryCreate(window, typeof(NativeWindowStub));
        var session = new RuntimeTextCompositionSession(context, bridge);
        session.Update(true);
        window.Edit("draft");
        session.Dispose();
        bridge.Dispose();
        window.Commit("late");
        window.Edit("late");
        Assert.Multiple(() =>
        {
            Assert.That(editor.Text, Is.EqualTo("before"));
            Assert.That(editor.HasImeComposition, Is.False);
            Assert.That(window.Activations.Last(), Is.False);
        });
    }
}
