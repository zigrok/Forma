using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

public sealed class SystemCursorTest
{
    private sealed class Clipboard : IClipboard
    {
        public string GetText() => null;
        public bool SetText(string text) => false;
    }

    private sealed class CursorAdapter : IRuntimeCursorAdapter
    {
        public bool IsSupported { get; set; } = true;
        public bool OwnsPointer { get; set; } = true;
        public List<Cursor> Applied { get; } = new();
        public int DisposalCount { get; private set; }
        public void SetCursor(Cursor cursor) => Applied.Add(cursor);
        public void Dispose() => DisposalCount++;
    }

    private static UIContext CreateContext(Control root)
    {
        var context = new UIContext(new Clipboard()) { ViewportSize = new Vector2(400, 300) };
        context.Add(root);
        context.Layout();
        return context;
    }

    [Test]
    public void EditorsDefaultToIBeamAndAllowExplicitOrInheritedOverrides()
    {
        var parent = new Control { Cursor = Cursor.Hand };
        var line = new LineEdit();
        var text = new TextEdit();
        parent.AddChild(line);
        parent.AddChild(text);
        Assert.That(line.EffectiveCursor, Is.EqualTo(Cursor.IBeam));
        Assert.That(text.EffectiveCursor, Is.EqualTo(Cursor.IBeam));
        line.Cursor = Cursor.Inherited;
        text.Cursor = Cursor.Arrow;
        Assert.That(line.EffectiveCursor, Is.EqualTo(Cursor.Hand));
        Assert.That(text.EffectiveCursor, Is.EqualTo(Cursor.Arrow));
    }

    [Test]
    public void CursorFollowsHitTargetNotKeyboardFocusAndRefreshesWithoutPointerMotion()
    {
        var editor = new LineEdit { Size = new Vector2(100, 30) };
        using var context = CreateContext(editor);
        editor.GrabFocus();
        context.InjectPointerMove(new Point(10, 10));
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.IBeam));
        editor.Cursor = Cursor.Crosshair;
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Crosshair));
        editor.Position = new Vector2(150, 100);
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Arrow));
        Assert.That(context.FocusedControl, Is.SameAs(editor));
    }

    [Test]
    public void CaptureKeepsIBeamOverOtherControlsAndReleaseRestoresTheirCursor()
    {
        var root = new Control { Size = new Vector2(400, 300), Cursor = Cursor.Hand };
        var editor = new TextEdit { Size = new Vector2(100, 40) };
        root.AddChild(editor);
        using var context = CreateContext(root);
        context.InjectPointerPress(new Point(10, 10));
        context.InjectPointerMove(new Point(200, 100));
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.IBeam));
        context.InjectPointerRelease(new Point(200, 100));
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Hand));
        context.InjectPointerMove(new Point(450, 350));
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Arrow));
    }

    [TestCase("disabled")]
    [TestCase("hidden")]
    [TestCase("detached")]
    public void IneligibleCapturedSubtreeCannotRetainItsCursor(string change)
    {
        var root = new Control { Size = new Vector2(400, 300) };
        var parent = new Control { Size = new Vector2(200, 100) };
        var editor = new LineEdit { Size = new Vector2(100, 30) };
        parent.AddChild(editor);
        root.AddChild(parent);
        using var context = CreateContext(root);
        context.InjectPointerPress(new Point(10, 10));
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.IBeam));
        if (change == "disabled") parent.Enabled = false;
        else if (change == "hidden") parent.Visibility = Visibility.Hidden;
        else root.RemoveChild(parent);
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Arrow));
    }

    [Test]
    public void ModalGatesBackgroundHoverAndCaptureAndClosingRestoresHover()
    {
        var editor = new LineEdit { Size = new Vector2(100, 30) };
        using var context = CreateContext(editor);
        context.InjectPointerPress(new Point(10, 10));
        var popup = new Popup { Visible = false, Size = new Vector2(100, 80), Cursor = Cursor.Hand };
        context.Add(popup);
        popup.PopupAt(new Vector2(150, 100));
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Arrow));
        context.InjectPointerMove(new Point(160, 110));
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Hand));
        context.InjectPointerMove(new Point(10, 10));
        popup.Hide();
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.IBeam));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PointerResolutionAppliesDisplayScaleExactlyOnce(bool injected)
    {
        var editor = new LineEdit { Position = new Vector2(50, 50), Size = new Vector2(50, 30) };
        using var context = CreateContext(editor);
        context.DisplayScale = 2;
        if (injected) context.InjectPointerMove(new Point(120, 120));
        else context.Update(new GameTime(), new MouseState(120, 120, 0, ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released), default);
        Assert.That(context.PointerPosition, Is.EqualTo(new Point(60, 60)));
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.IBeam));
    }

    [Test]
    public void IgnoredOrNonHitTestableTargetsUseUnderlyingCursor()
    {
        var root = new Control { Cursor = Cursor.Hand, Size = new Vector2(200, 100) };
        var target = new Control { Cursor = Cursor.IBeam, Size = new Vector2(100, 30) };
        root.AddChild(target);
        using var context = CreateContext(root);
        context.InjectPointerMove(new Point(10, 10));
        target.IsHitTestVisible = false;
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Hand));
        target.IsHitTestVisible = true;
        target.MouseFilter = MouseFilter.Ignore;
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Hand));
    }

    [Test]
    public void RouterAppliesChangesOnceAndRestoresArrowOverEmptySpace()
    {
        using var context = CreateContext(new LineEdit { Size = new Vector2(100, 30) });
        var adapter = new CursorAdapter();
        using var router = new RuntimeCursorRouter(context, adapter);
        context.InjectPointerMove(new Point(10, 10));
        router.Update();
        router.Update();
        context.InjectPointerMove(new Point(200, 100));
        router.Update();
        router.Update();
        Assert.That(adapter.Applied, Is.EqualTo(new[] { Cursor.IBeam, Cursor.Arrow }));
    }

    [Test]
    public void LeavingOwnershipDoesNotSetGlobalArrowAndReentryReappliesUnchangedCursor()
    {
        using var context = CreateContext(new LineEdit { Size = new Vector2(100, 30) });
        var adapter = new CursorAdapter();
        using var router = new RuntimeCursorRouter(context, adapter);
        context.InjectPointerMove(new Point(10, 10));
        router.Update();
        adapter.OwnsPointer = false;
        router.Update();
        Assert.That(adapter.Applied, Is.EqualTo(new[] { Cursor.IBeam }));
        adapter.OwnsPointer = true;
        router.Update();
        Assert.That(adapter.Applied, Is.EqualTo(new[] { Cursor.IBeam, Cursor.IBeam }));
    }

    [Test]
    public void DeactivationDropsCaptureAndForgetsCursorWithoutChangingFocusOrGlobalCursor()
    {
        var editor = new LineEdit { Size = new Vector2(100, 30) };
        using var context = CreateContext(editor);
        var adapter = new CursorAdapter();
        using var router = new RuntimeCursorRouter(context, adapter);
        context.InjectPointerPress(new Point(10, 10));
        router.Update();
        router.Suspend();
        context.ResetPlatformInput();
        Assert.That(context.FocusedControl, Is.SameAs(editor));
        Assert.That(adapter.Applied, Is.EqualTo(new[] { Cursor.IBeam }));
        context.InjectPointerMove(new Point(200, 100));
        router.Update();
        Assert.That(adapter.Applied, Is.EqualTo(new[] { Cursor.IBeam, Cursor.Arrow }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisposalRestoresOnlyWhileOwningPointerAndIsIdempotent(bool ownsPointer)
    {
        using var context = CreateContext(new LineEdit { Size = new Vector2(100, 30) });
        var adapter = new CursorAdapter();
        var router = new RuntimeCursorRouter(context, adapter);
        context.InjectPointerMove(new Point(10, 10));
        router.Update();
        adapter.OwnsPointer = ownsPointer;
        router.Dispose();
        router.Dispose();
        router.Update();
        Assert.That(adapter.Applied, Is.EqualTo(ownsPointer
            ? new[] { Cursor.IBeam, Cursor.Arrow } : new[] { Cursor.IBeam }));
        Assert.That(adapter.DisposalCount, Is.EqualTo(1));
        Assert.That(router.IsSupported, Is.False);
    }

    [Test]
    public void DisabledComponentAndDisposedContextReleaseTheirCursor()
    {
        using var context = CreateContext(new LineEdit { Size = new Vector2(100, 30) });
        var adapter = new CursorAdapter();
        using var router = new RuntimeCursorRouter(context, adapter);
        context.InjectPointerMove(new Point(10, 10));
        router.Update();
        router.Update(false);
        router.Update(false);
        router.Update();
        context.Dispose();
        router.Update();
        Assert.That(context.EffectiveCursor, Is.EqualTo(Cursor.Arrow));
        Assert.That(adapter.Applied, Is.EqualTo(new[] { Cursor.IBeam, Cursor.Arrow, Cursor.IBeam, Cursor.Arrow }));
    }

    [Test]
    public void UnsupportedRuntimeAndNeverOwnedRouterDoNotChangeTheCursorOnDisposal()
    {
        using var context = CreateContext(new LineEdit { Size = new Vector2(100, 30) });
        var unsupported = new CursorAdapter { IsSupported = false };
        using (var router = new RuntimeCursorRouter(context, unsupported))
        {
            router.Update();
            Assert.That(router.IsSupported, Is.False);
        }
        var untouched = new CursorAdapter();
        new RuntimeCursorRouter(context, untouched).Dispose();
        Assert.That(unsupported.Applied, Is.Empty);
        Assert.That(untouched.Applied, Is.Empty);
    }
}
