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

    private sealed class HostClipboard : IClipboard
    {
        private string _text;
        public string GetText() => _text;
        public bool SetText(string text) { _text = text; return true; }
    }
}
