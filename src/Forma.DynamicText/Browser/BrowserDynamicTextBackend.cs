// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Forma;

internal sealed class ExternalDynamicTextBackend : IDynamicTextBackend
{
    internal static ExternalDynamicTextBackend Instance { get; } = new();
    public string Name => "FreeType/HarfBuzz (Forma native ABI v1)";
    public DynamicTextNativeDiagnostics Diagnostics { get; } =
        new("forma_dynamictext", "host-linked FreeType", "forma_dynamictext", "host-linked HarfBuzz");
    public (int PinnedMemories, int FreeTypeLibraries, int FreeTypeFaces) NativeHandleCounts =>
        (0, BrowserTextFaceHandle.Count, BrowserTextFaceHandle.Count);

    public IDynamicTextFaceBackend CreateFace(byte[] source, int faceIndex) => new BrowserTextFace(source, faceIndex);
}

internal sealed class BrowserTextFaceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private static int _count;
    internal static int Count => Volatile.Read(ref _count);
    internal BrowserTextFaceHandle(nint handle) : base(true)
    {
        SetHandle(handle);
        Interlocked.Increment(ref _count);
    }
    protected override bool ReleaseHandle()
    {
        BrowserTextNative.fdt_face_destroy(handle);
        Interlocked.Decrement(ref _count);
        return true;
    }
}

internal sealed unsafe class BrowserTextFace : IDynamicTextFaceBackend
{
    private readonly BrowserTextFaceHandle _handle;
    private readonly object _sync = new();
    private IReadOnlyList<int> _codePoints;
    public int FaceCount { get; }
    public int FaceIndex { get; }
    public int GlyphCount { get; }
    public string FamilyName { get; }
    public string StyleName { get; }
    public ushort UnitsPerEm { get; }
    public UIFontFaceMetrics DesignMetrics { get; }
    public IReadOnlyList<UIFontVariationAxis> VariationAxes { get; }

    internal BrowserTextFace(byte[] source, int faceIndex)
    {
        VariationAxes = SfntFontData.Parse(source, faceIndex).VariationAxes.AsReadOnly();
        try
        {
            if (BrowserTextNative.fdt_abi_version() != 1)
                throw new FontLoadException(FontLoadErrorCode.NativeFailure, "The host's Forma dynamic-text ABI is incompatible; rebuild the platform.");
            fixed (byte* bytes = source)
            {
                Check(BrowserTextNative.fdt_face_create(bytes, source.Length, faceIndex, out var handle, out var info), "create face");
                _handle = new BrowserTextFaceHandle(handle);
                FaceCount = info.FaceCount;
                FaceIndex = info.FaceIndex;
                GlyphCount = info.GlyphCount;
                UnitsPerEm = checked((ushort)info.UnitsPerEm);
                DesignMetrics = new(info.Ascender, info.Descender, info.LineGap, info.LineHeight, info.UnderlinePosition, info.UnderlineThickness);
                FamilyName = Marshal.PtrToStringUTF8(BrowserTextNative.fdt_family_name(_handle)) ?? string.Empty;
                StyleName = Marshal.PtrToStringUTF8(BrowserTextNative.fdt_style_name(_handle)) ?? string.Empty;
            }
        }
        catch (Exception exception) when (UIFontFace.IsNativeDependencyFailure(exception))
        {
            throw new FontLoadException(FontLoadErrorCode.NativeFailure, "The browser host must link Forma dynamictext, FreeType, and HarfBuzz.", exception);
        }
    }

    public uint GetGlyphId(int unicodeScalar)
    {
        if (!Rune.IsValid(unicodeScalar)) throw new ArgumentOutOfRangeException(nameof(unicodeScalar));
        lock (_sync) return BrowserTextNative.fdt_glyph_id(Handle, (uint)unicodeScalar);
    }

    public IReadOnlyList<int> GetSupportedCodePoints()
    {
        lock (_sync)
        {
            var handle = Handle;
            if (_codePoints != null) return _codePoints;
            var values = new List<int>();
            var scalar = BrowserTextNative.fdt_first_char(handle, out var glyph);
            while (glyph != 0)
            {
                if (scalar > 0x10FFFF || values.Count > 0 && scalar <= values[^1])
                    throw new FontLoadException(FontLoadErrorCode.InvalidData, "The font has an invalid Unicode character map.");
                if (Rune.IsValid((int)scalar)) values.Add((int)scalar);
                var next = BrowserTextNative.fdt_next_char(handle, scalar, out glyph);
                if (glyph != 0 && next <= scalar)
                    throw new FontLoadException(FontLoadErrorCode.InvalidData, "The font character map did not advance.");
                scalar = next;
            }
            return _codePoints = values.AsReadOnly();
        }
    }

    public UIFontGlyphMetrics GetGlyphMetrics(uint glyphId, float logicalSize, IReadOnlyList<UIFontVariationCoordinate> variations)
    {
        ValidateSize(logicalSize);
        ValidateGlyph(glyphId);
        var coordinates = Coordinates(variations);
        lock (_sync)
        fixed (BrowserTextNative.Variation* values = coordinates)
        {
            Check(BrowserTextNative.fdt_glyph_metrics(Handle, glyphId, logicalSize, values, coordinates.Length, out var metrics), "measure glyph");
            return new(metrics.Width, metrics.Height, metrics.BearingX, metrics.BearingY, metrics.AdvanceX, metrics.AdvanceY);
        }
    }

    public UIFontGlyphBitmap RasterizeGlyph(uint glyphId, float logicalSize, float displayScale, UIFontHinting hinting, IReadOnlyList<UIFontVariationCoordinate> variations, UIFontSynthesis synthesis = UIFontSynthesis.None)
    {
        ValidateSize(logicalSize);
        ValidateGlyph(glyphId);
        if (!float.IsFinite(displayScale) || displayScale <= 0) throw new ArgumentOutOfRangeException(nameof(displayScale));
        if (!Enum.IsDefined(hinting)) throw new ArgumentOutOfRangeException(nameof(hinting));
        var coordinates = Coordinates(variations);
        lock (_sync)
        fixed (BrowserTextNative.Variation* values = coordinates)
        {
            var started = Stopwatch.GetTimestamp();
            Check(BrowserTextNative.fdt_rasterize(Handle, glyphId, logicalSize, displayScale, (int)hinting | (int)synthesis << 8, values, coordinates.Length, out var bitmap, out var source), "rasterize glyph");
            var pixels = new byte[checked(bitmap.Width * bitmap.Height)];
            for (var row = 0; row < bitmap.Height && bitmap.Width > 0; row++)
            {
                var sourceRow = bitmap.Pitch >= 0 ? row : bitmap.Height - row - 1;
                new ReadOnlySpan<byte>(source + checked(sourceRow * Math.Abs(bitmap.Pitch)), bitmap.Width)
                    .CopyTo(pixels.AsSpan(row * bitmap.Width, bitmap.Width));
            }
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(100))
                throw new FontLoadException(FontLoadErrorCode.RasterTimeout, "Glyph rasterization exceeded the 100 ms budget.");
            return new(bitmap.GlyphId, bitmap.Width, bitmap.Height, bitmap.BearingX, bitmap.BearingY, bitmap.AdvanceX, pixels);
        }
    }

    public UIFontShapedRun Shape(string text, float logicalSize, TextDirection direction, string locale, string script,
        IReadOnlyList<UIFontOpenTypeFeature> features, IReadOnlyList<UIFontVariationCoordinate> variations)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateSize(logicalSize);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (text.Length > 1_000_000) throw new ArgumentOutOfRangeException(nameof(text));
        if ((features?.Count ?? 0) > 65536) throw new ArgumentOutOfRangeException(nameof(features));
        text = ReplaceMalformedUtf16(text);
        var coordinates = Coordinates(variations);
        var nativeFeatures = new BrowserTextNative.Feature[features?.Count ?? 0];
        for (var i = 0; i < nativeFeatures.Length; i++) nativeFeatures[i] = new() { Tag = Tag(features[i].Tag), Value = features[i].Value };
        var localeBytes = Encoding.UTF8.GetBytes((locale ?? string.Empty) + '\0');
        var scriptBytes = Encoding.UTF8.GetBytes((script ?? string.Empty) + '\0');
        lock (_sync)
        fixed (char* chars = text)
        fixed (byte* language = localeBytes, scriptName = scriptBytes)
        fixed (BrowserTextNative.Variation* values = coordinates)
        fixed (BrowserTextNative.Feature* feats = nativeFeatures)
        {
            var started = Stopwatch.GetTimestamp();
            var nativeDirection = direction == TextDirection.RightToLeft ? 2 : direction == TextDirection.LeftToRight ? 1 : 0;
            var request = new BrowserTextNative.ShapeRequest
            {
                Text = chars, Length = text.Length, Size = logicalSize, Direction = nativeDirection,
                Locale = language, Script = scriptName, Features = feats, FeatureCount = nativeFeatures.Length,
                Variations = values, VariationCount = coordinates.Length,
            };
            Check(BrowserTextNative.fdt_shape(Handle, &request, out var shaped), "shape text");
            var glyphs = shaped.Glyphs;
            try
            {
                var result = new List<UIFontShapedGlyph>(shaped.Count);
                for (var i = 0; i < shaped.Count; i++)
                {
                    var glyph = glyphs[i];
                    result.Add(new(glyph.GlyphId, checked((int)glyph.Cluster), glyph.AdvanceX / 64f,
                        glyph.AdvanceY / 64f, glyph.OffsetX / 64f, glyph.OffsetY / 64f));
                }
                if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromMilliseconds(500))
                    throw new FontLoadException(FontLoadErrorCode.ShapingTimeout, "Text shaping exceeded the 500 ms budget.");
                return new(text, shaped.Direction == 2 ? TextDirection.RightToLeft : TextDirection.LeftToRight, result);
            }
            finally { BrowserTextNative.fdt_shape_free(glyphs); }
        }
    }

    private BrowserTextNative.Variation[] Coordinates(IReadOnlyList<UIFontVariationCoordinate> variations)
    {
        if ((variations?.Count ?? 0) > 64) throw new ArgumentOutOfRangeException(nameof(variations));
        var result = new BrowserTextNative.Variation[variations?.Count ?? 0];
        for (var i = 0; i < result.Length; i++)
        {
            var value = variations[i];
            foreach (var axis in VariationAxes)
                if (axis.Tag == value.Tag && (value.Value < axis.Minimum || value.Value > axis.Maximum))
                    throw new ArgumentOutOfRangeException(nameof(variations), $"Variation '{axis.Tag}' is outside its font axis range.");
            result[i] = new() { Tag = Tag(value.Tag), Value = value.Value };
        }
        return result;
    }

    private static uint Tag(string value) => (uint)value[0] << 24 | (uint)value[1] << 16 | (uint)value[2] << 8 | value[3];
    private BrowserTextFaceHandle Handle => !_handle.IsClosed ? _handle : throw new ObjectDisposedException(nameof(UIFontFace));
    public void Dispose() { lock (_sync) _handle.Dispose(); }
    private void ValidateGlyph(uint glyph) { if (glyph >= GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyph)); }
    private static void ValidateSize(float size)
    {
        if (!float.IsFinite(size) || size <= 0 || size * 64.0 >= int.MaxValue) throw new ArgumentOutOfRangeException(nameof(size));
    }
    private static void Check(int result, string operation)
    {
        if (result != 0)
            throw new FontLoadException((FontLoadErrorCode)(result - 1), $"Failed to {operation} using the Forma native font ABI (error {result}).");
    }
    private static string ReplaceMalformedUtf16(string text)
    {
        char[] replacement = null;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
            if (!char.IsSurrogate(text[i])) continue;
            replacement ??= text.ToCharArray();
            replacement[i] = '\uFFFD';
        }
        return replacement == null ? text : new string(replacement);
    }
}

internal static unsafe class BrowserTextNative
{
#if FORMA_BROWSER_DYNAMIC_TEXT
    private const string Library = "libforma_dynamictext";
#else
    private const string Library = "forma_dynamictext";
#endif
    [StructLayout(LayoutKind.Sequential)]
    internal struct FaceInfo
    {
        internal int FaceCount, FaceIndex, GlyphCount, UnitsPerEm;
        internal float Ascender, Descender, LineGap, LineHeight, UnderlinePosition, UnderlineThickness;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Variation { internal uint Tag; internal float Value; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Feature { internal uint Tag, Value; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Metrics { internal float Width, Height, BearingX, BearingY, AdvanceX, AdvanceY; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Bitmap { internal uint GlyphId; internal int Width, Height, Pitch, BearingX, BearingY; internal float AdvanceX; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Glyph { internal uint GlyphId, Cluster; internal int AdvanceX, AdvanceY, OffsetX, OffsetY; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShapeRequest
    {
        internal char* Text;
        internal int Length;
        internal float Size;
        internal int Direction;
        internal byte* Locale;
        internal byte* Script;
        internal Feature* Features;
        internal int FeatureCount;
        internal Variation* Variations;
        internal int VariationCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShapeResult { internal Glyph* Glyphs; internal int Count, Direction; }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint fdt_abi_version();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int fdt_face_create(byte* bytes, int length, int index, out nint face, out FaceInfo info);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void fdt_face_destroy(nint face);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint fdt_family_name(BrowserTextFaceHandle face);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint fdt_style_name(BrowserTextFaceHandle face);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint fdt_glyph_id(BrowserTextFaceHandle face, uint scalar);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint fdt_first_char(BrowserTextFaceHandle face, out uint glyph);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint fdt_next_char(BrowserTextFaceHandle face, uint scalar, out uint glyph);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int fdt_glyph_metrics(BrowserTextFaceHandle face, uint glyph, float size, Variation* variations, int count, out Metrics metrics);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int fdt_rasterize(BrowserTextFaceHandle face, uint glyph, float size, float scale, int options, Variation* variations, int count, out Bitmap bitmap, out byte* pixels);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int fdt_shape(BrowserTextFaceHandle face, ShapeRequest* request, out ShapeResult result);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void fdt_shape_free(Glyph* glyphs);
}
