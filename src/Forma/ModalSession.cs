// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Runtime.ExceptionServices;
using Forma.Xaml;
using Microsoft.Xna.Framework;

namespace Forma;

public enum ModalCancelReason { User, Token, Hidden, Detached, Disposed }

public abstract record ModalOutcome<T>
{
    private ModalOutcome() { }
    public sealed record Accepted(T Value) : ModalOutcome<T>;
    public sealed record Cancelled(ModalCancelReason Reason) : ModalOutcome<T>;
}

/// <summary>
/// Owns one detached popup and its compiled control lifetime. Construct, open, accept, cancel and dispose
/// on the context's creation thread. Token callbacks only latch; Update drains cancellation.
/// Acceptance linearizes under the same lock as cancellation, after result validation.
/// A cancellation already signalled at that point wins. Cleanup precedes completion; cleanup failure faults it.
/// Context disposal terminates even an unopened session and prevents further session reservations.
/// The caller supplies result validation and is responsible for immutable result ownership.
/// </summary>
public sealed class ModalSession<T> : IDisposable
{
    private enum State { Created, Active, Closing, Closed }
    private readonly object _gate = new();
    private readonly UIContext _context;
    private readonly Popup _popup;
    private readonly Action<T> _validate;
    private readonly IDisposable _lifetime;
    private readonly CancellationToken _token;
    private readonly TaskCompletionSource<ModalOutcome<T>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IDisposable _frame;
    private readonly CancellationTokenRegistration _registration;
    private State _state;
    private bool _cancellationRequested;

    public ModalSession(UIContext context, Popup popup, Action<T> validateResult,
        IDisposable lifetime = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(validateResult);
        _context = context;
        VerifyThread();
        ObjectDisposedException.ThrowIf(context.IsDisposingOrDisposed, context);
        if (popup.Context != null || popup.Parent != null || popup.VisualParent != null || popup.Visible || !popup.Modal)
            throw new ArgumentException("A modal session requires a detached, hidden modal popup.", nameof(popup));
        if (context.ModalSessionOwner != null)
            throw new InvalidOperationException("This context already owns a modal session.");
        _popup = popup;
        _validate = validateResult;
        _lifetime = lifetime;
        _token = cancellationToken;
        context.ModalSessionOwner = this;
        _frame = context.RegisterFrameBoundaryCallback(_ => PollCancellation());
        popup.PopupHidden += OnHidden;
        popup.Detached += OnDetached;
        _registration = cancellationToken.Register(() =>
        {
            lock (_gate)
                if (_state < State.Closing) _cancellationRequested = true;
        });
    }

    public Task<ModalOutcome<T>> Completion => _completion.Task;

    public void Open(Vector2 position, Control initialFocus = null)
    {
        VerifyThread();
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (initialFocus != null && !_popup.IsAncestorOf(initialFocus))
            throw new ArgumentException("Initial focus must belong to the popup.", nameof(initialFocus));
        lock (_gate)
        {
            if (_state != State.Created) throw new InvalidOperationException("A modal session opens only once.");
            _state = State.Active;
        }
        if (PollCancellation()) return;
        try
        {
            _context.Add(_popup);
            lock (_gate)
                if (_state != State.Active) return;
            _popup.PopupAt(position);
            _context.Layout();
            lock (_gate)
                if (_state != State.Active) return;
            if (initialFocus != null)
            {
                if (!_context.CanFocus(initialFocus))
                    throw new ArgumentException("Initial focus must be effectively eligible.", nameof(initialFocus));
                initialFocus.GrabFocus();
            }
        }
        catch (Exception exception)
        {
            Fault(exception);
            throw;
        }
    }

    /// <summary>Returns false if already closed or cancellation wins; invalid results always throw.</summary>
    public bool TryAccept(T result)
    {
        VerifyThread();
        _validate(result);
        ModalOutcome<T> outcome;
        lock (_gate)
        {
            if (_state == State.Created) throw new InvalidOperationException("Open the modal before accepting.");
            if (_state >= State.Closing) return false;
            outcome = CancellationPending()
                ? new ModalOutcome<T>.Cancelled(ModalCancelReason.Token)
                : new ModalOutcome<T>.Accepted(result);
            _state = State.Closing;
        }
        Finish(outcome);
        return outcome is ModalOutcome<T>.Accepted;
    }

    public void Cancel(ModalCancelReason reason = ModalCancelReason.User)
    {
        VerifyThread();
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        lock (_gate)
        {
            if (_state >= State.Closing) return;
            if (CancellationPending()) reason = ModalCancelReason.Token;
            _state = State.Closing;
        }
        Finish(new ModalOutcome<T>.Cancelled(reason));
    }

    public void Dispose() => Cancel(ModalCancelReason.Disposed);

    private bool CancellationPending() => _cancellationRequested || _token.IsCancellationRequested;

    private bool PollCancellation()
    {
        VerifyThread();
        lock (_gate)
            if (_state >= State.Closing || !CancellationPending()) return false;
        Cancel(ModalCancelReason.Token);
        return true;
    }

    private void OnHidden(Popup popup, PopupHideReason reason) => Cancel(
        reason == PopupHideReason.Cancelled ? ModalCancelReason.User : ModalCancelReason.Hidden);
    private void OnDetached(object sender, EventArgs args) => Cancel(ModalCancelReason.Detached);

    private void Finish(ModalOutcome<T> outcome)
    {
        var failures = Cleanup();
        lock (_gate) _state = State.Closed;
        if (failures.Count != 0)
        {
            var error = new AggregateException("Modal cleanup failed.", failures);
            _completion.TrySetException(error);
            throw error;
        }
        _completion.TrySetResult(outcome);
    }

    private void Fault(Exception cause)
    {
        lock (_gate)
        {
            if (_state >= State.Closing) return;
            _state = State.Closing;
        }
        var failures = Cleanup();
        lock (_gate) _state = State.Closed;
        failures.Insert(0, cause);
        var error = new AggregateException("Modal opening failed.", failures);
        _completion.TrySetException(error);
        ExceptionDispatchInfo.Capture(error).Throw();
    }

    private List<Exception> Cleanup()
    {
        var failures = new List<Exception>();
        void Release(Action action)
        {
            try { action(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        var logical = new List<Control>();
        var visual = new HashSet<Control>();
        void Collect(Control control, bool logicalOwner)
        {
            if (!visual.Add(control)) return;
            if (logicalOwner) logical.Add(control);
            foreach (var child in control.Children) Collect(child, logicalOwner);
            foreach (var child in control.VisualChildren) Collect(child, false);
        }
        Collect(_popup, true);
        _popup.PopupHidden -= OnHidden;
        _popup.Detached -= OnDetached;
        Release(_registration.Dispose);
        Release(_frame.Dispose);
        Release(() => _context.ResetInteractionState(_popup));
        Release(() => _popup.Hide());
        Release(() => _context.Remove(_popup));
        if (_lifetime != null) Release(_lifetime.Dispose);
        foreach (var control in visual) Release(() => XamlAttachment.DisposeScope(control));
        // Generated visual controls are owned by their template/items hosts, not by this session twice.
        for (var index = logical.Count - 1; index >= 0; index--)
            if (logical[index] is IDisposable disposable) Release(disposable.Dispose);
        if (ReferenceEquals(_context.ModalSessionOwner, this)) _context.ModalSessionOwner = null;
        return failures;
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
            throw new InvalidOperationException("Modal operations must run on the UIContext creation thread.");
    }
}
