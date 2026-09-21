// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

namespace Forma
{
    /// <summary>An editor clipboard operation that the platform could not complete.</summary>
    public enum ClipboardOperation { Copy, Cut, Paste }

    /// <summary>Platform clipboard access used by retained text controls.</summary>
    public interface IClipboard
    {
        /// <summary>Returns the current plain-text clipboard content, or <see langword="null"/> when unavailable.</summary>
        string GetText();
        /// <summary>Writes plain text to the clipboard and reports whether the platform accepted it.</summary>
        bool SetText(string text);
    }
}