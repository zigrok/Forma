// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Forma
{
    internal interface IDynamicTextFaceBackend : IDisposable
    {
        int FaceCount { get; }
        int FaceIndex { get; }
        int GlyphCount { get; }
        string FamilyName { get; }
        string StyleName { get; }
        ushort UnitsPerEm { get; }
        UIFontFaceMetrics DesignMetrics { get; }
        IReadOnlyList<UIFontVariationAxis> VariationAxes { get; }
        uint GetGlyphId(int unicodeScalar);
        IReadOnlyList<int> GetSupportedCodePoints();
        UIFontGlyphMetrics GetGlyphMetrics(uint glyphId, float logicalSize, IReadOnlyList<UIFontVariationCoordinate> variations);
        UIFontGlyphBitmap RasterizeGlyph(uint glyphId, float logicalSize, float displayScale, UIFontHinting hinting, IReadOnlyList<UIFontVariationCoordinate> variations, UIFontSynthesis synthesis = UIFontSynthesis.None);
        UIFontShapedRun Shape(string text, float logicalSize, TextDirection direction, string locale, string script, IReadOnlyList<UIFontOpenTypeFeature> features, IReadOnlyList<UIFontVariationCoordinate> variations);
    }

    internal interface IDynamicTextBackend
    {
        string Name { get; }
        DynamicTextNativeDiagnostics Diagnostics { get; }
        (int PinnedMemories, int FreeTypeLibraries, int FreeTypeFaces) NativeHandleCounts { get; }
        IDynamicTextFaceBackend CreateFace(byte[] source, int faceIndex);
    }

    internal static class DynamicTextBackendRegistry
    {
#if FORMA_EXTERNAL_DYNAMIC_TEXT_BACKEND
    private static IDynamicTextBackend _backend = ExternalDynamicTextBackend.Instance;
#else
        private static IDynamicTextBackend _backend = FreeTypeHarfBuzzDynamicTextBackend.Instance;
#endif
        private static int _started;

        internal static IDynamicTextBackend Backend
        {
            get
            {
                System.Threading.Volatile.Write(ref _started, 1);
                return System.Threading.Volatile.Read(ref _backend)
                    ?? throw new InvalidOperationException("No dynamic-text backend was registered before first use.");
            }
        }

        internal static void Register(IDynamicTextBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            if (System.Threading.Volatile.Read(ref _started) != 0)
                throw new InvalidOperationException("The dynamic-text backend cannot change after the first face is created.");
            System.Threading.Volatile.Write(ref _backend, backend);
        }
    }
}