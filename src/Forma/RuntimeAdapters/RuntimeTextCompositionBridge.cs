// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Forma;

/// <summary>Optional APIs are bound as typed delegates so packaged runtimes remain valid dependencies.</summary>
internal sealed class RuntimeTextCompositionBridge : IRuntimeTextCompositionSource, IDisposable
{
    private readonly Action<Action<string>> _removeCommitted;
    private readonly Action<Action<string, int, int>> _removeEditing;
    private readonly Func<bool, bool> _setActive;
    private readonly Func<Rectangle, bool> _setRectangle;

    public event Action<string> Committed;
    public event Action<string, int, int> Editing;

    private RuntimeTextCompositionBridge(object window, EventInfo committed, EventInfo editing,
        MethodInfo setActive, MethodInfo setRectangle)
    {
        _setActive = setActive.CreateDelegate<Func<bool, bool>>(window);
        _setRectangle = setRectangle.CreateDelegate<Func<Rectangle, bool>>(window);
        _removeCommitted = committed.RemoveMethod.CreateDelegate<Action<Action<string>>>(window);
        _removeEditing = editing.RemoveMethod.CreateDelegate<Action<Action<string, int, int>>>(window);
        committed.AddMethod.CreateDelegate<Action<Action<string>>>(window)(OnCommitted);
        editing.AddMethod.CreateDelegate<Action<Action<string, int, int>>>(window)(OnEditing);
    }

    internal static RuntimeTextCompositionBridge TryCreate(object window,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicEvents)] Type apiType)
    {
        var supported = apiType.GetProperty("SupportsTextComposition")?.GetMethod;
        var committed = apiType.GetEvent("TextCommitted");
        var editing = apiType.GetEvent("TextEditing");
        var active = apiType.GetMethod("SetTextInputActive", new[] { typeof(bool) });
        var rectangle = apiType.GetMethod("SetTextInputRectangle", new[] { typeof(Rectangle) });
        if (supported?.ReturnType != typeof(bool) ||
            committed?.EventHandlerType != typeof(Action<string>) ||
            editing?.EventHandlerType != typeof(Action<string, int, int>) ||
            active?.ReturnType != typeof(bool) || rectangle?.ReturnType != typeof(bool) ||
            !supported.CreateDelegate<Func<bool>>(window)())
            return null;
        return new RuntimeTextCompositionBridge(window, committed, editing, active, rectangle);
    }

    private void OnCommitted(string text) => Committed?.Invoke(text);
    private void OnEditing(string text, int start, int length) => Editing?.Invoke(text, start, length);
    public bool SetActive(bool active) => _setActive(active);
    public bool SetRectangle(Rectangle rectangle) => _setRectangle(rectangle);
    public void Dispose()
    {
        _removeCommitted(OnCommitted);
        _removeEditing(OnEditing);
    }
}
