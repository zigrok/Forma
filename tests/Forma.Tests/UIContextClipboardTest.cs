// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;

namespace Forma.Tests;

public sealed class UIContextClipboardTest
{
    [Test]
    public void HostClipboardIsUsedWithoutDefaultInitialization()
    {
        var clipboard = new HostClipboard();
        using var context = new UIContext(clipboard);
        Assert.That(context.Clipboard, Is.SameAs(clipboard));
        Assert.That(context.Clipboard.SetText("مرحبا 日本語"), Is.True);
        Assert.That(context.Clipboard.GetText(), Is.EqualTo("مرحبا 日本語"));
    }

    [Test]
    public void NullHostClipboardIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new UIContext(null));
    }

    [Test]
    public void ComponentBindingReplacesOnlyTheDefaultClipboard()
    {
        var clipboard = new HostClipboard();
        using var context = new UIContext();
        context.BindDefaultClipboard(() => clipboard);
        Assert.That(context.Clipboard, Is.SameAs(clipboard));
        context.BindDefaultClipboard(() => throw new InvalidOperationException("Must not replace a host clipboard."));
        Assert.That(context.Clipboard, Is.SameAs(clipboard));
    }

    [Test]
    public void ComponentBindingDoesNotProbeTheRuntimeForAnInjectedClipboard()
    {
        var clipboard = new HostClipboard();
        using var context = new UIContext(clipboard);
        context.BindDefaultClipboard(() => throw new InvalidOperationException("Must not probe the runtime."));
        Assert.That(context.Clipboard, Is.SameAs(clipboard));
    }

    private sealed class HostClipboard : IClipboard
    {
        private string _text;
        public string GetText() => _text;
        public bool SetText(string text) { _text = text; return true; }
    }
}
