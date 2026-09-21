// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

namespace Forma;

internal interface IRuntimeCursorAdapter : IDisposable
{
    bool IsSupported { get; }
    bool OwnsPointer { get; }
    void SetCursor(Cursor cursor);
}

internal sealed class RuntimeCursorRouter : IDisposable
{
    private readonly UIContext _context;
    private readonly IRuntimeCursorAdapter _adapter;
    private Cursor? _applied;
    private bool _disposed;

    internal RuntimeCursorRouter(UIContext context, IRuntimeCursorAdapter adapter)
    {
        _context = context;
        _adapter = adapter;
    }

    internal bool IsSupported => !_disposed && _adapter.IsSupported;

    internal void Update(bool enabled = true)
    {
        if (!IsSupported) return;
        if ((!enabled || _context.IsDisposingOrDisposed) && !_applied.HasValue) return;
        if (!_adapter.OwnsPointer)
        {
            Suspend();
            return;
        }
        if (!enabled || _context.IsDisposingOrDisposed)
        {
            Restore();
            return;
        }
        var cursor = _context.EffectiveCursor;
        if (_applied == cursor) return;
        _adapter.SetCursor(cursor);
        _applied = cursor;
    }

    // A different window may have changed the process-wide SDL cursor while we were away.
    internal void Suspend() => _applied = null;

    private void Restore()
    {
        if (_applied.HasValue && _applied != Cursor.Arrow) _adapter.SetCursor(Cursor.Arrow);
        _applied = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            if (_applied.HasValue && _adapter.IsSupported && _adapter.OwnsPointer) Restore();
        }
        finally
        {
            _disposed = true;
            _applied = null;
            _adapter.Dispose();
        }
    }
}
