// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Text;

namespace Forma;

internal sealed class ExternalDynamicTextBackend : IDynamicTextBackend
{
    private static readonly DynamicTextNativeDiagnostics BackendDiagnostics = new DynamicTextNativeDiagnostics(
        "platform-font",
        "authorized-host",
        "platform-shaper",
        "authorized-host");

    internal static ExternalDynamicTextBackend Instance { get; } = new ExternalDynamicTextBackend();

    private ExternalDynamicTextBackend() { }

    public string Name => "PlatformFont";
    public DynamicTextNativeDiagnostics Diagnostics => BackendDiagnostics;
    public (int PinnedMemories, int FreeTypeLibraries, int FreeTypeFaces) NativeHandleCounts => (0, 0, 0);

    public IDynamicTextFaceBackend CreateFace(byte[] source, int faceIndex)
    {
        if (source.Length == 0) throw new FontLoadException(FontLoadErrorCode.InvalidData, "Platform font source is empty.");
        if (faceIndex != 0) throw new FontLoadException(FontLoadErrorCode.FaceIndexOutOfRange, "Platform font supports one face.");
        return new PlatformDynamicTextFaceBackend();
    }
}

internal sealed class PlatformDynamicTextFaceBackend : IDynamicTextFaceBackend
{
    private bool _disposed;

    public int FaceCount => 1;
    public int FaceIndex => 0;
    public int GlyphCount => 0x110000;
    public string FamilyName => "Platform Sans";
    public string StyleName => "Regular";
    public ushort UnitsPerEm => 1000;
    public UIFontFaceMetrics DesignMetrics => new UIFontFaceMetrics(800, -200, 200, 1200, -100, 50);
    public IReadOnlyList<UIFontVariationAxis> VariationAxes => Array.Empty<UIFontVariationAxis>();

    public uint GetGlyphId(int unicodeScalar)
    {
        ThrowIfDisposed();
        if (!Rune.IsValid(unicodeScalar)) throw new ArgumentOutOfRangeException(nameof(unicodeScalar));
        return checked((uint)unicodeScalar + 1);
    }

    public IReadOnlyList<int> GetSupportedCodePoints()
    {
        ThrowIfDisposed();
        return new[] { (int)'A', 0x0645, 0x0915 };
    }

    public UIFontGlyphMetrics GetGlyphMetrics(uint glyphId, float logicalSize, IReadOnlyList<UIFontVariationCoordinate> variations)
    {
        ThrowIfDisposed();
        ValidateSize(logicalSize);
        return new UIFontGlyphMetrics(logicalSize / 2, logicalSize, 0, logicalSize, logicalSize * 0.6f, logicalSize);
    }

    public UIFontGlyphBitmap RasterizeGlyph(uint glyphId, float logicalSize, float displayScale, UIFontHinting hinting, IReadOnlyList<UIFontVariationCoordinate> variations, UIFontSynthesis synthesis = UIFontSynthesis.None)
    {
        if (synthesis != UIFontSynthesis.None) throw new NotSupportedException("The platform spike does not synthesize glyph outlines.");
        ThrowIfDisposed();
        ValidateSize(logicalSize);
        if (!float.IsFinite(displayScale) || displayScale <= 0) throw new ArgumentOutOfRangeException(nameof(displayScale));
        return new UIFontGlyphBitmap(glyphId, 2, 2, 0, 2, logicalSize * 0.6f, new byte[] { 0, 128, 192, 255 });
    }

    public UIFontShapedRun Shape(string text, float logicalSize, TextDirection direction, string locale, string script, IReadOnlyList<UIFontOpenTypeFeature> features, IReadOnlyList<UIFontVariationCoordinate> variations)
    {
        ThrowIfDisposed();
        if (text == null) throw new ArgumentNullException(nameof(text));
        ValidateSize(logicalSize);
        var glyphs = new List<UIFontShapedGlyph>();
        var remaining = text.AsSpan();
        var cluster = 0;
        while (!remaining.IsEmpty)
        {
            Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            glyphs.Add(new UIFontShapedGlyph(GetGlyphId(rune.Value), cluster, logicalSize * 0.6f, 0, 0, 0));
            remaining = remaining.Slice(consumed);
            cluster += consumed;
        }
        var resolvedDirection = direction == TextDirection.RightToLeft ? TextDirection.RightToLeft : TextDirection.LeftToRight;
        return new UIFontShapedRun(text, resolvedDirection, glyphs);
    }

    public void Dispose() => _disposed = true;

    private static void ValidateSize(float logicalSize)
    {
        if (!float.IsFinite(logicalSize) || logicalSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UIFontFace));
    }
}