using Forma.Xaml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

public sealed class ModalSessionTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void CancellationClearsButtonWithoutActivationAndFreshInputWorks(bool keyboard)
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        var button = new Button { ToggleMode = true, Size = new Vector2(100, 40) };
        var pressed = 0;
        var up = 0;
        var toggles = 0;
        button.Pressed += (_, _) => pressed++;
        button.ButtonUp += (_, _) => up++;
        button.Toggled += (_, _) => toggles++;
        context.Add(button);
        button.GrabFocus();
        if (keyboard) ModalInputBoundaryTest.Tick(context, Keys.Space);
        else context.InjectPointerPress(new Point(10, 10));
        context.ResetInteractionState(button);
        context.ResetInteractionState(button);
        Assert.Multiple(() =>
        {
            Assert.That(pressed, Is.Zero);
            Assert.That(toggles, Is.Zero);
            Assert.That(up, Is.EqualTo(1));
            Assert.That(button.IsVisuallyPressed, Is.False);
        });
        ModalInputBoundaryTest.Tick(context);
        button.GrabFocus();
        if (keyboard)
        {
            ModalInputBoundaryTest.Tick(context, Keys.Space);
            ModalInputBoundaryTest.Tick(context);
        }
        else
        {
            context.InjectPointerPress(new Point(10, 10));
            context.InjectPointerRelease(new Point(10, 10));
        }
        Assert.Multiple(() =>
        {
            Assert.That(pressed, Is.EqualTo(1));
            Assert.That(toggles, Is.EqualTo(1));
            Assert.That(up, Is.EqualTo(2));
        });
    }

    [Test]
    public void CleanupPrecedesResultAndReentrantSignalsCannotChangeIt()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        var popup = new Popup { Visible = false };
        ModalSession<int> session = null;
        var disposed = 0;
        var lease = new Lease(() => { disposed++; Assert.That(session.Completion.IsCompleted, Is.False); session.Cancel(); });
        using (session = new ModalSession<int>(context, popup, Validate, lease))
        {
            session.Open(Vector2.Zero);
            popup.PopupHidden += (_, _) => session.Cancel();
            Assert.That(session.TryAccept(7), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(disposed, Is.EqualTo(1));
                Assert.That(context.Roots, Is.Empty);
                Assert.That(session.Completion.Result, Is.EqualTo(new ModalOutcome<int>.Accepted(7)));
                Assert.That(session.TryAccept(8), Is.False);
            });
        }
    }

    [TestCase("token")]
    [TestCase("hide")]
    [TestCase("detach")]
    [TestCase("dispose")]
    public void EveryExitCompletesOnceAndReleasesContext(string exit)
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        using var token = new CancellationTokenSource();
        var popup = new Popup { Visible = false };
        using var session = new ModalSession<int>(context, popup, Validate, cancellationToken: token.Token);
        session.Open(Vector2.Zero);
        var reason = exit switch
        {
            "token" => ModalCancelReason.Token, "hide" => ModalCancelReason.Hidden,
            "detach" => ModalCancelReason.Detached, _ => ModalCancelReason.Disposed
        };
        if (exit == "token") { Task.Run(token.Cancel).GetAwaiter().GetResult(); ModalInputBoundaryTest.Tick(context); }
        else if (exit == "hide") popup.Hide();
        else if (exit == "detach") context.Remove(popup);
        else session.Dispose();
        Assert.That(session.Completion.Result, Is.EqualTo(new ModalOutcome<int>.Cancelled(reason)));
        Assert.That(context.Roots, Is.Empty);
        using var next = new ModalSession<string>(context, new Popup { Visible = false }, value => ArgumentNullException.ThrowIfNull(value));
    }

    [Test]
    public void PendingCancellationWinsAcceptanceEvenBeforeFrameDrain()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        using var token = new CancellationTokenSource();
        using var session = new ModalSession<int>(context, new Popup { Visible = false }, Validate, cancellationToken: token.Token);
        session.Open(Vector2.Zero);
        Task.Run(token.Cancel).GetAwaiter().GetResult();
        Assert.That(session.Completion.IsCompleted, Is.False);
        Assert.That(session.TryAccept(3), Is.False);
        Assert.That(session.Completion.Result, Is.EqualTo(new ModalOutcome<int>.Cancelled(ModalCancelReason.Token)));
    }

    [Test]
    public void CleanupFailureFaultsRatherThanAcceptingAndReleasesOtherLeases()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        var popup = new Popup { Visible = false };
        var released = 0;
        XamlAttachment.RegisterDisposable(popup, new Lease(() => released++));
        using var session = new ModalSession<int>(context, popup, Validate, new Lease(() => throw new InvalidOperationException("cleanup")));
        session.Open(Vector2.Zero);
        Assert.Throws<AggregateException>(() => session.TryAccept(1));
        Assert.Multiple(() =>
        {
            Assert.That(session.Completion.IsFaulted, Is.True);
            Assert.That(session.Completion.Exception, Is.Not.Null);
            Assert.That(released, Is.EqualTo(1));
            Assert.That(context.Roots, Is.Empty);
            Assert.That(context.ModalSessionOwner, Is.Null);
        });
    }

    [Test]
    public void InvalidResultAndWrongThreadAreExplicitAndNonterminal()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        using var session = new ModalSession<int>(context, new Popup { Visible = false }, Validate);
        session.Open(Vector2.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.TryAccept(-1));
        var error = Task.Run(() => Assert.Throws<InvalidOperationException>(() => session.Cancel())).Result;
        Assert.That(error, Is.Not.Null);
        Assert.That(session.Completion.IsCompleted, Is.False);
        Assert.That(session.TryAccept(2), Is.True);
    }

    [Test]
    public void PrecancelledOpenDoesNotPublishAndTwoSessionsAreRefused()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        using var session = new ModalSession<int>(context, new Popup { Visible = false }, Validate,
            cancellationToken: new CancellationToken(true));
        Assert.Throws<InvalidOperationException>(() => new ModalSession<int>(context, new Popup { Visible = false }, Validate));
        session.Open(Vector2.Zero);
        Assert.That(context.Roots, Is.Empty);
        Assert.That(session.Completion.Result, Is.EqualTo(new ModalOutcome<int>.Cancelled(ModalCancelReason.Token)));
    }

    [Test]
    public void ContextDisposalCompletesWithoutAnotherUpdate()
    {
        var context = ModalInputBoundaryTest.CreateContext();
        using var session = new ModalSession<int>(context, new Popup { Visible = false }, Validate);
        session.Open(Vector2.Zero);
        context.Dispose();
        Assert.That(session.Completion.Result, Is.TypeOf<ModalOutcome<int>.Cancelled>());
    }

    [Test]
    public void ContextDisposalCompletesUnopenedSessionAndReleasesItsLeases()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        var popup = new Popup { Visible = false };
        var releases = 0;
        XamlAttachment.RegisterDisposable(popup, new Lease(() => releases++));
        using var session = new ModalSession<int>(context, popup, Validate, new Lease(() => releases++));
        context.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(session.Completion.IsCompletedSuccessfully, Is.True);
            Assert.That(releases, Is.EqualTo(2));
            Assert.That(context.ModalSessionOwner, Is.Null);
        });
    }

    [Test]
    public void ContextDisposalRejectsNewModalOwnershipDuringAndAfterCleanup()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        var attempts = 0;
        void RejectNewSession()
        {
            attempts++;
            Assert.Throws<ObjectDisposedException>(() =>
                new ModalSession<int>(context, new Popup { Visible = false }, Validate));
        }
        using var session = new ModalSession<int>(context, new Popup { Visible = false },
            Validate, new Lease(RejectNewSession));
        context.Dispose();
        RejectNewSession();
        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(context.ModalSessionOwner, Is.Null);
        Assert.That(session.Completion.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void UnopenedCleanupFailureDoesNotPreventContextRootCleanup()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        var root = new Control();
        var detached = 0;
        root.Detached += (_, _) => detached++;
        context.Add(root);
        using var session = new ModalSession<int>(context, new Popup { Visible = false },
            Validate, new Lease(() => throw new InvalidOperationException("unopened cleanup")));
        Assert.Throws<AggregateException>(context.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(session.Completion.IsFaulted, Is.True);
            Assert.That(session.Completion.Exception, Is.Not.Null);
            Assert.That(context.ModalSessionOwner, Is.Null);
            Assert.That(context.Roots, Is.Empty);
            Assert.That(root.Context, Is.Null);
            Assert.That(detached, Is.EqualTo(1));
        });
    }

    private static void Validate(int value) { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }

    [TestCase(false)]
    [TestCase(true)]
    public void CancellationDuringAttachmentCannotLeaveChildrenInContext(bool fromChild)
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        var popup = new Popup { Visible = false };
        var first = new Control();
        var second = new LineEdit();
        popup.AddChild(first);
        popup.AddChild(second);
        using var session = new ModalSession<int>(context, popup, Validate);
        (fromChild ? first : popup).Attached += (_, _) => session.Cancel();
        session.Open(Vector2.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(session.Completion.Result, Is.TypeOf<ModalOutcome<int>.Cancelled>());
            Assert.That(context.Roots, Is.Empty);
            Assert.That(popup.Context, Is.Null);
            Assert.That(first.Context, Is.Null);
            Assert.That(second.Context, Is.Null);
            Assert.That(context.ModalSessionOwner, Is.Null);
        });
    }

    [Test]
    public void CancellationInValidatorWinsButCancellationDuringAcceptedCleanupDoesNot()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        using var before = new CancellationTokenSource();
        using (var session = new ModalSession<int>(context, new Popup { Visible = false },
            _ => before.Cancel(), cancellationToken: before.Token))
        {
            session.Open(Vector2.Zero);
            Assert.That(session.TryAccept(1), Is.False);
            Assert.That(session.Completion.Result, Is.EqualTo(new ModalOutcome<int>.Cancelled(ModalCancelReason.Token)));
        }
        using var after = new CancellationTokenSource();
        using var accepted = new ModalSession<int>(context, new Popup { Visible = false },
            Validate, new Lease(after.Cancel), after.Token);
        accepted.Open(Vector2.Zero);
        Assert.That(accepted.TryAccept(2), Is.True);
        ModalInputBoundaryTest.Tick(context);
        Assert.That(accepted.Completion.Result, Is.EqualTo(new ModalOutcome<int>.Accepted(2)));
    }

    [Test]
    public void OpenValidationAndFailureReleaseOwnershipExplicitly()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        var popup = new Popup { Visible = false };
        var hidden = new Button { Visible = false };
        popup.AddChild(hidden);
        var releases = 0;
        using var session = new ModalSession<int>(context, popup, Validate, new Lease(() => releases++));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Open(new Vector2(float.NaN, 0)));
        Assert.Throws<ArgumentException>(() => session.Open(Vector2.Zero, new Button()));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Cancel((ModalCancelReason)100));
        Assert.That(session.Completion.IsCompleted, Is.False);
        Assert.Throws<AggregateException>(() => session.Open(Vector2.Zero, hidden));
        Assert.Multiple(() =>
        {
            Assert.That(session.Completion.IsFaulted, Is.True);
            Assert.That(session.Completion.Exception, Is.Not.Null);
            Assert.That(context.Roots, Is.Empty);
            Assert.That(context.ModalSessionOwner, Is.Null);
            Assert.That(releases, Is.EqualTo(1));
        });
    }

    [Test]
    public void ReentrantShownCancellationNeverPublishesInitialFocus()
    {
        using var context = ModalInputBoundaryTest.CreateContext();
        var popup = new Popup { Visible = false };
        var editor = new LineEdit();
        popup.AddChild(editor);
        using var session = new ModalSession<int>(context, popup, Validate);
        popup.PopupShown += (_, _) => session.Cancel();
        session.Open(Vector2.Zero, editor);
        Assert.That(session.Completion.Result, Is.TypeOf<ModalOutcome<int>.Cancelled>());
        Assert.That(context.FocusedControl, Is.Null);
        Assert.That(editor.Context, Is.Null);
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
