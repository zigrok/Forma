// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;

namespace Forma;

internal interface IRuntimeTextCompositionSource
{
    event Action<string> Committed;
    event Action<string, int, int> Editing;
    bool SetActive(bool active);
    bool SetRectangle(Rectangle rectangle);
}

/// <summary>Owns one native text-input session; platform callbacks never edit an unfocused control.</summary>
internal sealed class RuntimeTextCompositionSession : IDisposable
{
    private readonly UIContext _context;
    private readonly IRuntimeTextCompositionSource _source;
    private Control _target;
    private bool _windowActive;
    private bool _changing;
    private bool _dispatching;
    private bool _disposed;

    internal RuntimeTextCompositionSession(UIContext context, IRuntimeTextCompositionSource source)
    {
        _context = context;
        _source = source;
        _source.Committed += OnCommitted;
        _source.Editing += OnEditing;
        _context.InputFocusChanged += Synchronize;
    }

    internal void Update(bool windowActive)
    {
        _windowActive = windowActive;
        Synchronize();
        UpdateRectangle();
    }

    private void Synchronize()
    {
        if (_changing || _disposed) return;
        var focus = _context.FocusedControl;
        var target = _windowActive && _context.CanFocus(focus) &&
            focus is not LineEdit { Editable: false } ? focus : null;
        if (ReferenceEquals(target, _target)) return;
        _changing = true;
        try
        {
            var previous = _target;
            _target = null;
            if (previous != null)
            {
                if (previous is LineEdit previousEditor)
                {
                    previousEditor.ImeCompositionCancelled -= OnCancelled;
                    previousEditor.NativeImeActive = false;
                    previousEditor.CancelImeComposition();
                }
                Require(_source.SetActive(false));
            }
            _target = target;
            if (target != null)
            {
                if (target is LineEdit editor)
                {
                    editor.NativeImeActive = editor.SupportsNativeTextComposition;
                    editor.ImeCompositionCancelled += OnCancelled;
                }
                UpdateRectangle();
                Require(_source.SetActive(true));
            }
        }
        finally { _changing = false; }
    }

    private void OnCancelled()
    {
        if (_dispatching || _changing || _target == null) return;
        var previous = _target;
        Synchronize();
        if (!ReferenceEquals(previous, _target)) return;
        _changing = true;
        try
        {
            Require(_source.SetActive(false));
            Require(_source.SetActive(true));
            UpdateRectangle();
        }
        finally { _changing = false; }
    }

    private void OnEditing(string text, int start, int length)
    {
        if (_changing || _disposed) return;
        Synchronize();
        if (_target is not LineEdit { SupportsNativeTextComposition: true }) return;
        _dispatching = true;
        try { _context.TextComposition(text, start, length); }
        finally { _dispatching = false; }
        UpdateRectangle();
    }

    private void OnCommitted(string text)
    {
        if (_changing || _disposed) return;
        Synchronize();
        if (_target == null) return;
        _dispatching = true;
        try { _context.TextInput(text); }
        finally { _dispatching = false; }
        UpdateRectangle();
    }

    private void UpdateRectangle()
    {
        if (_target is LineEdit { SupportsNativeTextComposition: true } editor)
            Require(_source.SetRectangle(editor.GetTextInputRectangle()));
    }

    private static void Require(bool succeeded)
    {
        if (!succeeded) throw new InvalidOperationException("The native text-input session could not be configured.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        Update(false);
        _disposed = true;
        _context.InputFocusChanged -= Synchronize;
        _source.Committed -= OnCommitted;
        _source.Editing -= OnEditing;
    }
}
