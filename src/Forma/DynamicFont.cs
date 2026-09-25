// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
#if !FORMA_EXTERNAL_DYNAMIC_TEXT_BACKEND
using FreeTypeSharp;
using HarfBuzzSharp;
using Microsoft.Win32.SafeHandles;
using static FreeTypeSharp.FT;
using HarfBuzzBlob = HarfBuzzSharp.Blob;
using HarfBuzzBuffer = HarfBuzzSharp.Buffer;
using HarfBuzzFace = HarfBuzzSharp.Face;
using HarfBuzzFont = HarfBuzzSharp.Font;
#endif

namespace Forma
{
    public sealed class DynamicTextNativeDiagnostics
    {
        private readonly string _freeTypeLibraryName;
        private readonly string _freeTypePackageId;
        private readonly string _harfBuzzLibraryName;
        private readonly string _harfBuzzPackageId;

        internal DynamicTextNativeDiagnostics(string freeTypeLibraryName, string freeTypePackageId, string harfBuzzLibraryName, string harfBuzzPackageId)
        {
            _freeTypeLibraryName = freeTypeLibraryName;
            _freeTypePackageId = freeTypePackageId;
            _harfBuzzLibraryName = harfBuzzLibraryName;
            _harfBuzzPackageId = harfBuzzPackageId;
        }

        public static DynamicTextNativeDiagnostics Current => DynamicTextBackendRegistry.Backend.Diagnostics;
        public string RuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;
        public string FreeTypeLibraryName => _freeTypeLibraryName;
        public string FreeTypePackageId => _freeTypePackageId;
        public string HarfBuzzLibraryName => _harfBuzzLibraryName;
        public string HarfBuzzPackageId => _harfBuzzPackageId;
        public bool UsesRuntimeGeneratedMarshalling => false;
        public bool RegistersUnmanagedCallbacks => false;
    }

    public sealed class UIFontAsset
    {
        private readonly byte[] _source;
        private readonly ReadOnlyCollection<UIFontVariationAxis> _variationAxes;

        private UIFontAsset(byte[] source, int faceIndex)
        {
            using var face = UIFontFace.FromMemory(source, faceIndex);
            _source = source;
            FaceIndex = face.FaceIndex;
            FaceCount = face.FaceCount;
            FamilyName = face.FamilyName;
            StyleName = face.StyleName;
            _variationAxes = new List<UIFontVariationAxis>(face.VariationAxes).AsReadOnly();
        }

        public int FaceIndex { get; }
        public int FaceCount { get; }
        public string FamilyName { get; }
        public string StyleName { get; }
        public IReadOnlyList<UIFontVariationAxis> VariationAxes => _variationAxes;

        public static UIFontAsset FromMemory(ReadOnlyMemory<byte> source, int faceIndex = 0)
        {
            if (source.Length > UIFontFace.MaximumSourceBytes)
                throw new FontLoadException(FontLoadErrorCode.SourceTooLarge, $"Font source exceeds the {UIFontFace.MaximumSourceBytes}-byte limit.");
            return new UIFontAsset(source.ToArray(), faceIndex);
        }

        public static UIFontAsset FromStream(Stream source, int faceIndex = 0)
        {
            using var face = UIFontFace.FromStream(source, faceIndex);
            return new UIFontAsset(face.CopySource(), faceIndex);
        }

        public static UIFontAsset FromProjectFile(string projectDirectory, string relativePath, int faceIndex = 0)
        {
            using var face = UIFontFace.FromProjectFile(projectDirectory, relativePath, faceIndex);
            return new UIFontAsset(face.CopySource(), faceIndex);
        }

        public UIFontFace CreateFace() => UIFontFace.FromMemory(_source, FaceIndex);

        public Stream OpenStream() => new MemoryStream(_source, writable: false);
    }

    public sealed class UIFontFace : IDisposable
    {
        public const int MaximumSourceBytes = 64 * 1024 * 1024;
        public const int MaximumFaces = 256;
        public const int MaximumTables = 4096;
        public const int MaximumTableBytes = 32 * 1024 * 1024;
        public const int MaximumGlyphDimension = 4096;
        public const int MaximumGlyphArea = 16 * 1024 * 1024;

        private readonly byte[] _source;
        private readonly IDynamicTextFaceBackend _backend;

        private UIFontFace(byte[] source, int faceIndex)
        {
            _source = source;
            Identity = new UIFontIdentity("font-face", $"{Convert.ToHexString(SHA256.HashData(source))}:{faceIndex}");
            _backend = DynamicTextBackendRegistry.Backend.CreateFace(source, faceIndex);
        }

        public int FaceCount => _backend.FaceCount;
        public int FaceIndex => _backend.FaceIndex;
        public UIFontIdentity Identity { get; }
        public int GlyphCount => _backend.GlyphCount;
        public string FamilyName => _backend.FamilyName;
        public string StyleName => _backend.StyleName;
        public ushort UnitsPerEm => _backend.UnitsPerEm;
        public UIFontFaceMetrics DesignMetrics => _backend.DesignMetrics;
        public IReadOnlyList<UIFontVariationAxis> VariationAxes => _backend.VariationAxes;
        internal static (int PinnedMemories, int FreeTypeLibraries, int FreeTypeFaces) NativeHandleCounts => DynamicTextBackendRegistry.Backend.NativeHandleCounts;

        internal byte[] CopySource() => (byte[])_source.Clone();

        public static UIFontFace FromMemory(ReadOnlyMemory<byte> source, int faceIndex = 0)
        {
            if (source.Length > MaximumSourceBytes)
                throw Error(FontLoadErrorCode.SourceTooLarge, $"Font source exceeds the {MaximumSourceBytes}-byte limit.");
            return new UIFontFace(source.ToArray(), faceIndex);
        }

        public static UIFontFace FromStream(Stream source, int faceIndex = 0)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!source.CanRead) throw new ArgumentException("Font stream must be readable.", nameof(source));
            if (source.CanSeek && source.Length - source.Position > MaximumSourceBytes)
                throw Error(FontLoadErrorCode.SourceTooLarge, $"Font source exceeds the {MaximumSourceBytes}-byte limit.");
            using var bytes = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = source.Read(buffer, 0, Math.Min(buffer.Length, MaximumSourceBytes + 1 - checked((int)bytes.Length)));
                if (read == 0) break;
                bytes.Write(buffer, 0, read);
                if (bytes.Length > MaximumSourceBytes)
                    throw Error(FontLoadErrorCode.SourceTooLarge, $"Font source exceeds the {MaximumSourceBytes}-byte limit.");
            }
            return new UIFontFace(bytes.ToArray(), faceIndex);
        }

        public static UIFontFace FromProjectFile(string projectDirectory, string relativePath, int faceIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory)) throw new ArgumentException("A project directory is required.", nameof(projectDirectory));
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A project-relative path is required.", nameof(relativePath));
            if (Path.IsPathRooted(relativePath)) throw new ArgumentException("The font path must be project-relative.", nameof(relativePath));
            var root = Path.GetFullPath(projectDirectory);
            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? root : root + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, StringComparison.Ordinal))
                throw new ArgumentException("The font path must remain inside the project directory.", nameof(relativePath));
            using var stream = File.OpenRead(path);
            return FromStream(stream, faceIndex);
        }

        public uint GetGlyphId(int unicodeScalar) => _backend.GetGlyphId(unicodeScalar);
        public bool SupportsCharacter(int unicodeScalar) => GetGlyphId(unicodeScalar) != 0;
        public IReadOnlyList<int> GetSupportedCodePoints() => _backend.GetSupportedCodePoints();

        public UIFontFaceMetrics GetMetrics(float logicalSize)
        {
            ValidateLogicalSize(logicalSize);
            var scale = logicalSize / UnitsPerEm;
            return new UIFontFaceMetrics(
                DesignMetrics.Ascender * scale,
                DesignMetrics.Descender * scale,
                DesignMetrics.LineGap * scale,
                DesignMetrics.LineHeight * scale,
                DesignMetrics.UnderlinePosition * scale,
                DesignMetrics.UnderlineThickness * scale);
        }

        public UIFontGlyphMetrics GetGlyphMetrics(uint glyphId, float logicalSize, IReadOnlyList<UIFontVariationCoordinate> variations = null)
            => _backend.GetGlyphMetrics(glyphId, logicalSize, variations);

        public UIFontGlyphBitmap RasterizeCharacter(int unicodeScalar, float logicalSize, float displayScale = 1, UIFontHinting hinting = UIFontHinting.Default, IReadOnlyList<UIFontVariationCoordinate> variations = null)
            => RasterizeGlyph(GetGlyphId(unicodeScalar), logicalSize, displayScale, hinting, variations);

        public UIFontGlyphBitmap RasterizeGlyph(uint glyphId, float logicalSize, float displayScale = 1, UIFontHinting hinting = UIFontHinting.Default, IReadOnlyList<UIFontVariationCoordinate> variations = null)
            => _backend.RasterizeGlyph(glyphId, logicalSize, displayScale, hinting, variations);

        public UIFontGlyphBitmap RasterizeGlyph(uint glyphId, float logicalSize, float displayScale, UIFontHinting hinting, IReadOnlyList<UIFontVariationCoordinate> variations, UIFontSynthesis synthesis)
        {
            if ((synthesis & ~(UIFontSynthesis.Bold | UIFontSynthesis.Oblique)) != 0) throw new ArgumentOutOfRangeException(nameof(synthesis));
            return _backend.RasterizeGlyph(glyphId, logicalSize, displayScale, hinting, variations, synthesis);
        }

        public UIFontShapedRun Shape(string text, float logicalSize, TextDirection direction = TextDirection.Auto, string locale = null, string script = null, IReadOnlyList<UIFontOpenTypeFeature> features = null, IReadOnlyList<UIFontVariationCoordinate> variations = null)
            => _backend.Shape(text, logicalSize, direction, locale, script, features, variations);

        public void Dispose() => _backend.Dispose();

        internal static bool IsNativeDependencyFailure(Exception exception)
        {
            while (exception is TypeInitializationException && exception.InnerException != null)
                exception = exception.InnerException;
            return exception is DllNotFoundException or FileLoadException or BadImageFormatException or EntryPointNotFoundException;
        }

        private static void ValidateLogicalSize(float logicalSize)
        {
            if (!float.IsFinite(logicalSize) || logicalSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));
        }

        private static FontLoadException Error(FontLoadErrorCode errorCode, string message) => new FontLoadException(errorCode, message);
    }

#if !FORMA_EXTERNAL_DYNAMIC_TEXT_BACKEND
    internal sealed class FreeTypeHarfBuzzDynamicTextBackend : IDynamicTextBackend
    {
        internal static FreeTypeHarfBuzzDynamicTextBackend Instance { get; } = new FreeTypeHarfBuzzDynamicTextBackend();
        private static readonly DynamicTextNativeDiagnostics NativeDiagnostics = new DynamicTextNativeDiagnostics(
            FT.LibName,
            "FreeTypeSharp",
            "libHarfBuzzSharp",
            "HarfBuzzSharp.NativeAssets");

        private FreeTypeHarfBuzzDynamicTextBackend() { }

        public string Name => "FreeTypeSharp/HarfBuzzSharp";
        public DynamicTextNativeDiagnostics Diagnostics => NativeDiagnostics;
        public (int PinnedMemories, int FreeTypeLibraries, int FreeTypeFaces) NativeHandleCounts => FreeTypeHarfBuzzFaceBackend.NativeHandleCounts;
        public IDynamicTextFaceBackend CreateFace(byte[] source, int faceIndex) => new FreeTypeHarfBuzzFaceBackend(source, faceIndex);
    }

    internal sealed unsafe partial class FreeTypeHarfBuzzFaceBackend : IDynamicTextFaceBackend
    {
        private static int _activePinnedMemories;
        private static int _activeFreeTypeLibraries;
        private static int _activeFreeTypeFaces;
        private const int MaximumSourceBytes = UIFontFace.MaximumSourceBytes;
        private const int MaximumFaces = UIFontFace.MaximumFaces;
        private const int MaximumTables = UIFontFace.MaximumTables;
        private const int MaximumTableBytes = UIFontFace.MaximumTableBytes;
        private const int MaximumGlyphDimension = UIFontFace.MaximumGlyphDimension;
        private const int MaximumGlyphArea = UIFontFace.MaximumGlyphArea;

        private const long MaximumRasterTicks = TimeSpan.TicksPerMillisecond * 100;
        private readonly object _sync = new object();
        private readonly PinnedFontMemoryHandle _memory;
        private readonly FreeTypeLibraryHandle _library;
        private readonly FreeTypeFaceHandle _face;
        private readonly byte[] _source;
        private readonly HarfBuzzBlob _harfBuzzBlob;
        private readonly HarfBuzzFace _harfBuzzFace;
        private readonly ReadOnlyCollection<UIFontVariationAxis> _variationAxes;
        private ReadOnlyCollection<int> _supportedCodePoints;
        private bool _disposed;

        internal FreeTypeHarfBuzzFaceBackend(byte[] source, int faceIndex)
        {
            _source = source;
            Identity = new UIFontIdentity("font-face", $"{Convert.ToHexString(SHA256.HashData(source))}:{faceIndex}");
            var fontData = SfntFontData.Parse(source, faceIndex);
            _variationAxes = fontData.VariationAxes.AsReadOnly();
            _memory = new PinnedFontMemoryHandle(source);
            try
            {
                _library = FreeTypeLibraryHandle.Create();
                _face = FreeTypeFaceHandle.Create(_library, _memory, source.Length, faceIndex);
                var face = Pointer;
                FaceCount = checked((int)face->num_faces);
                FaceIndex = checked((int)face->face_index);
                GlyphCount = checked((int)face->num_glyphs);
                FamilyName = ReadUtf8(face->family_name);
                StyleName = ReadUtf8(face->style_name);
                UnitsPerEm = face->units_per_EM;
                if (UnitsPerEm == 0)
                    throw Error(FontLoadErrorCode.UnsupportedFormat, "Font face does not provide scalable design units.");
                var charmapError = FT_Select_Charmap(face, FT_Encoding_.FT_ENCODING_UNICODE);
                if (charmapError != FT_Error.FT_Err_Ok)
                    throw Error(FontLoadErrorCode.UnsupportedFormat, "Font face does not provide a Unicode character map.");
                DesignMetrics = CreateFaceMetrics(face, UnitsPerEm);
                _harfBuzzBlob = new HarfBuzzBlob(_memory.Address, source.Length, MemoryMode.ReadOnly);
                _harfBuzzFace = new HarfBuzzFace(_harfBuzzBlob, faceIndex);
            }
            catch (Exception exception)
            {
                _harfBuzzFace?.Dispose();
                _harfBuzzBlob?.Dispose();
                _face?.Dispose();
                _library?.Dispose();
                _memory.Dispose();
                if (IsNativeDependencyFailure(exception))
                    throw new FontLoadException(
                        FontLoadErrorCode.NativeFailure,
                        "A native font dependency is unavailable or incompatible.",
                        exception);
                throw;
            }
        }

        public int FaceCount { get; }
        public int FaceIndex { get; }
        public UIFontIdentity Identity { get; }
        public int GlyphCount { get; }
        public string FamilyName { get; }
        public string StyleName { get; }
        public ushort UnitsPerEm { get; }
        public UIFontFaceMetrics DesignMetrics { get; }
        public IReadOnlyList<UIFontVariationAxis> VariationAxes => _variationAxes;
        internal static (int PinnedMemories, int FreeTypeLibraries, int FreeTypeFaces) NativeHandleCounts =>
            (Volatile.Read(ref _activePinnedMemories), Volatile.Read(ref _activeFreeTypeLibraries), Volatile.Read(ref _activeFreeTypeFaces));

        internal byte[] CopySource()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                return (byte[])_source.Clone();
            }
        }

        public uint GetGlyphId(int unicodeScalar)
        {
            if (!Rune.IsValid(unicodeScalar)) throw new ArgumentOutOfRangeException(nameof(unicodeScalar));
            lock (_sync)
            {
                ThrowIfDisposed();
                return FT_Get_Char_Index(Pointer, checked((uint)unicodeScalar));
            }
        }

        public bool SupportsCharacter(int unicodeScalar) => GetGlyphId(unicodeScalar) != 0;

        public IReadOnlyList<int> GetSupportedCodePoints()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_supportedCodePoints != null) return _supportedCodePoints;
                var values = new List<int>();
                uint glyphId;
                var codePoint = FT_Get_First_Char(Pointer, &glyphId);
                while (glyphId != 0)
                {
                    if (codePoint <= 0x10FFFF) values.Add(checked((int)codePoint));
                    codePoint = FT_Get_Next_Char(Pointer, codePoint, &glyphId);
                }
                _supportedCodePoints = values.AsReadOnly();
                return _supportedCodePoints;
            }
        }

        public UIFontFaceMetrics GetMetrics(float logicalSize)
        {
            ValidateLogicalSize(logicalSize);
            var scale = logicalSize / UnitsPerEm;
            return new UIFontFaceMetrics(
                DesignMetrics.Ascender * scale,
                DesignMetrics.Descender * scale,
                DesignMetrics.LineGap * scale,
                DesignMetrics.LineHeight * scale,
                DesignMetrics.UnderlinePosition * scale,
                DesignMetrics.UnderlineThickness * scale);
        }

        public UIFontGlyphMetrics GetGlyphMetrics(uint glyphId, float logicalSize, IReadOnlyList<UIFontVariationCoordinate> variations = null)
        {
            ValidateLogicalSize(logicalSize);
            lock (_sync)
            {
                ThrowIfDisposed();
                ValidateGlyphId(glyphId);
                ApplyVariations(variations);
                LoadGlyphOrNotdef(glyphId, FT_LOAD.FT_LOAD_NO_SCALE | FT_LOAD.FT_LOAD_NO_HINTING | FT_LOAD.FT_LOAD_NO_BITMAP);
                var metrics = Pointer->glyph->metrics;
                var scale = logicalSize / UnitsPerEm;
                return new UIFontGlyphMetrics(
                    checked((long)metrics.width) * scale,
                    checked((long)metrics.height) * scale,
                    checked((long)metrics.horiBearingX) * scale,
                    checked((long)metrics.horiBearingY) * scale,
                    checked((long)metrics.horiAdvance) * scale,
                    checked((long)metrics.vertAdvance) * scale);
            }
        }

        public UIFontGlyphBitmap RasterizeCharacter(int unicodeScalar, float logicalSize, float displayScale = 1, UIFontHinting hinting = UIFontHinting.Default, IReadOnlyList<UIFontVariationCoordinate> variations = null)
            => RasterizeGlyph(GetGlyphId(unicodeScalar), logicalSize, displayScale, hinting, variations);

        public UIFontGlyphBitmap RasterizeGlyph(uint glyphId, float logicalSize, float displayScale = 1, UIFontHinting hinting = UIFontHinting.Default, IReadOnlyList<UIFontVariationCoordinate> variations = null, UIFontSynthesis synthesis = UIFontSynthesis.None)
        {
            ValidateLogicalSize(logicalSize);
            if (!float.IsFinite(displayScale) || displayScale <= 0) throw new ArgumentOutOfRangeException(nameof(displayScale));
            if (!Enum.IsDefined(typeof(UIFontHinting), hinting)) throw new ArgumentOutOfRangeException(nameof(hinting));
            var physicalSize = logicalSize * displayScale;
            if (!float.IsFinite(physicalSize) || physicalSize > MaximumGlyphDimension)
                throw Error(FontLoadErrorCode.RasterLimitExceeded, $"Physical font size exceeds the {MaximumGlyphDimension}-pixel limit.");

            lock (_sync)
            {
                ThrowIfDisposed();
                ValidateGlyphId(glyphId);
                ApplyVariations(variations);
                var started = Stopwatch.GetTimestamp();
                var charHeight = checked((nint)MathF.Round(physicalSize * 64));
                ThrowNative(FT_Set_Char_Size(Pointer, 0, charHeight, 72, 72), "set font size");
                var flags = hinting switch
                {
                    UIFontHinting.None => FT_LOAD.FT_LOAD_DEFAULT | FT_LOAD.FT_LOAD_NO_HINTING,
                    UIFontHinting.Auto => FT_LOAD.FT_LOAD_DEFAULT | FT_LOAD.FT_LOAD_FORCE_AUTOHINT,
                    UIFontHinting.Light => FT_LOAD.FT_LOAD_DEFAULT | (FT_LOAD)(1 << 16),
                    _ => FT_LOAD.FT_LOAD_DEFAULT
                };
                var actualGlyphId = LoadGlyphOrNotdef(glyphId, flags);
                if ((synthesis & UIFontSynthesis.Bold) != 0) FreeTypeVariations.Embolden(Pointer->glyph);
                if ((synthesis & UIFontSynthesis.Oblique) != 0) FreeTypeVariations.Oblique(Pointer->glyph);
                ThrowNative(FT_Render_Glyph(Pointer->glyph, FT_Render_Mode_.FT_RENDER_MODE_NORMAL), $"render glyph {actualGlyphId}");
                var bitmap = Pointer->glyph->bitmap;
                var width = checked((int)bitmap.width);
                var height = checked((int)bitmap.rows);
                ValidateBitmap(bitmap, width, height);
                var pixels = new byte[checked(width * height)];
                if (pixels.Length != 0)
                {
                    var pitch = Math.Abs(bitmap.pitch);
                    for (var row = 0; row < height; row++)
                    {
                        var sourceRow = bitmap.pitch >= 0 ? row : height - row - 1;
                        new ReadOnlySpan<byte>(bitmap.buffer + checked(sourceRow * pitch), width).CopyTo(pixels.AsSpan(row * width, width));
                    }
                }
                var elapsed = Stopwatch.GetElapsedTime(started);
                if (elapsed.Ticks > MaximumRasterTicks)
                    throw Error(FontLoadErrorCode.RasterTimeout, "Glyph rasterization exceeded the 100 ms budget.");
                return new UIFontGlyphBitmap(
                    actualGlyphId,
                    width,
                    height,
                    Pointer->glyph->bitmap_left,
                    Pointer->glyph->bitmap_top,
                    checked((long)Pointer->glyph->advance.x) / 64f / displayScale,
                    pixels);
            }
        }

        public UIFontShapedRun Shape(
            string text,
            float logicalSize,
            TextDirection direction = TextDirection.Auto,
            string locale = null,
            string script = null,
            IReadOnlyList<UIFontOpenTypeFeature> features = null,
            IReadOnlyList<UIFontVariationCoordinate> variations = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            ValidateLogicalSize(logicalSize);
            if (!Enum.IsDefined(typeof(TextDirection), direction)) throw new ArgumentOutOfRangeException(nameof(direction));
            if (text.Length > 1_000_000) throw new ArgumentOutOfRangeException(nameof(text), "Text exceeds the 1,000,000 UTF-16 code-unit limit.");
            text = ReplaceMalformedUtf16(text);
            lock (_sync)
            {
                ThrowIfDisposed();
                var started = Stopwatch.GetTimestamp();
                using var font = new HarfBuzzFont(_harfBuzzFace);
                font.SetFunctionsOpenType();
                var harfBuzzVariations = new Variation[variations?.Count ?? 0];
                for (var index = 0; index < harfBuzzVariations.Length; index++)
                {
                    var variation = variations[index];
                    harfBuzzVariations[index] = new Variation
                    {
                        Tag = new Tag(variation.Tag[0], variation.Tag[1], variation.Tag[2], variation.Tag[3]),
                        Value = variation.Value
                    };
                }
                font.SetVariations(harfBuzzVariations);
                var scale = checked((int)MathF.Round(logicalSize * 64));
                font.SetScale(scale, scale);
                using var buffer = new HarfBuzzBuffer { ClusterLevel = ClusterLevel.MonotoneGraphemes };
                buffer.AddUtf16(text);
                if (direction == TextDirection.LeftToRight) buffer.Direction = HarfBuzzSharp.Direction.LeftToRight;
                else if (direction == TextDirection.RightToLeft) buffer.Direction = HarfBuzzSharp.Direction.RightToLeft;
                if (!string.IsNullOrWhiteSpace(locale)) buffer.Language = new Language(locale);
                if (!string.IsNullOrWhiteSpace(script)) buffer.Script = Script.Parse(script);
                buffer.GuessSegmentProperties();
                var harfBuzzFeatures = new Feature[features?.Count ?? 0];
                for (var index = 0; index < harfBuzzFeatures.Length; index++)
                {
                    var feature = features[index];
                    harfBuzzFeatures[index] = new Feature(new Tag(feature.Tag[0], feature.Tag[1], feature.Tag[2], feature.Tag[3]), feature.Value);
                }
                font.Shape(buffer, harfBuzzFeatures);
                var infos = buffer.GlyphInfos;
                var positions = buffer.GlyphPositions;
                if (infos.Length > 1_000_000) throw new ArgumentOutOfRangeException(nameof(text), "Shaping exceeds the 1,000,000-glyph limit.");
                var glyphs = new List<UIFontShapedGlyph>(infos.Length);
                for (var index = 0; index < infos.Length; index++)
                {
                    glyphs.Add(new UIFontShapedGlyph(
                        infos[index].Codepoint,
                        checked((int)infos[index].Cluster),
                        positions[index].XAdvance / 64f,
                        positions[index].YAdvance / 64f,
                        positions[index].XOffset / 64f,
                        positions[index].YOffset / 64f));
                }
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > 500)
                    throw Error(FontLoadErrorCode.ShapingTimeout, "Text shaping exceeded the 500 ms budget.");
                var resolvedDirection = buffer.Direction == HarfBuzzSharp.Direction.RightToLeft ? TextDirection.RightToLeft : TextDirection.LeftToRight;
                return new UIFontShapedRun(text, resolvedDirection, glyphs);
            }
        }

        private void ApplyVariations(IReadOnlyList<UIFontVariationCoordinate> variations)
        {
            if (_variationAxes.Count == 0) return;
            var coordinates = stackalloc nint[_variationAxes.Count];
            for (var axisIndex = 0; axisIndex < _variationAxes.Count; axisIndex++)
            {
                var axis = _variationAxes[axisIndex];
                var value = axis.Default;
                if (variations != null)
                    for (var variationIndex = 0; variationIndex < variations.Count; variationIndex++)
                    {
                        var variation = variations[variationIndex];
                        if (!string.Equals(variation.Tag, axis.Tag, StringComparison.Ordinal)) continue;
                        if (variation.Value < axis.Minimum || variation.Value > axis.Maximum)
                            throw new ArgumentOutOfRangeException(nameof(variations), $"Variation '{axis.Tag}' must be between {axis.Minimum} and {axis.Maximum}.");
                        value = variation.Value;
                        break;
                    }
                coordinates[axisIndex] = checked((nint)MathF.Round(value * 65536));
            }
            ThrowNative(FreeTypeVariations.SetDesignCoordinates(Pointer, checked((uint)_variationAxes.Count), coordinates), "set variation coordinates");
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _harfBuzzFace.Dispose();
                _harfBuzzBlob.Dispose();
                _face.Dispose();
                _library.Dispose();
                _memory.Dispose();
            }
        }

        private FT_FaceRec_* Pointer => (FT_FaceRec_*)_face.DangerousGetHandle();

        private static class FreeTypeVariations
        {
            [DllImport(FT.LibName, EntryPoint = "FT_Set_Var_Design_Coordinates", CallingConvention = CallingConvention.Cdecl)]
            internal static extern FT_Error SetDesignCoordinates(FT_FaceRec_* face, uint coordinateCount, nint* coordinates);
            [DllImport(FT.LibName, EntryPoint = "FT_GlyphSlot_Embolden", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void Embolden(FT_GlyphSlotRec_* slot);
            [DllImport(FT.LibName, EntryPoint = "FT_GlyphSlot_Oblique", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void Oblique(FT_GlyphSlotRec_* slot);
        }

        private uint LoadGlyphOrNotdef(uint glyphId, FT_LOAD flags)
        {
            var error = FT_Load_Glyph(Pointer, glyphId, flags);
            if (error == FT_Error.FT_Err_Ok) return glyphId;
            if (glyphId == 0) ThrowNative(error, "load the .notdef glyph");
            ThrowNative(FT_Load_Glyph(Pointer, 0, flags), "load the .notdef glyph");
            return 0;
        }

        private void ValidateGlyphId(uint glyphId)
        {
            if (glyphId >= GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyphId));
        }

        private static void ValidateLogicalSize(float logicalSize)
        {
            if (!float.IsFinite(logicalSize) || logicalSize <= 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));
        }

        private static string ReplaceMalformedUtf16(string text)
        {
            char[] replacement = null;
            for (var index = 0; index < text.Length; index++)
            {
                var value = text[index];
                if (char.IsHighSurrogate(value) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    index++;
                    continue;
                }
                if (!char.IsSurrogate(value)) continue;
                replacement ??= text.ToCharArray();
                replacement[index] = '\uFFFD';
            }
            return replacement == null ? text : new string(replacement);
        }

        private static void ValidateBitmap(FT_Bitmap_ bitmap, int width, int height)
        {
            if (width > MaximumGlyphDimension || height > MaximumGlyphDimension || checked((long)width * height) > MaximumGlyphArea)
                throw Error(FontLoadErrorCode.RasterLimitExceeded, "Glyph bitmap exceeds configured dimensions.");
            if (width == 0 || height == 0) return;
            if (bitmap.buffer == null || bitmap.pitch == int.MinValue || Math.Abs(bitmap.pitch) < width)
                throw Error(FontLoadErrorCode.InvalidData, "Glyph bitmap has an invalid row pitch.");
            if (bitmap.pixel_mode != FT_Pixel_Mode_.FT_PIXEL_MODE_GRAY || bitmap.num_grays != 256)
                throw Error(FontLoadErrorCode.UnsupportedFormat, $"Unsupported glyph bitmap format {bitmap.pixel_mode}.");
        }

        private static UIFontFaceMetrics CreateFaceMetrics(FT_FaceRec_* face, ushort unitsPerEm)
        {
            var lineGap = face->height - (face->ascender - face->descender);
            return new UIFontFaceMetrics(face->ascender, face->descender, lineGap, face->height, face->underline_position, face->underline_thickness);
        }

        private static string ReadUtf8(byte* value) => value == null ? string.Empty : Marshal.PtrToStringUTF8((IntPtr)value) ?? string.Empty;

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIFontFace));
        }

        private static void ThrowNative(FT_Error error, string operation)
        {
            if (error != FT_Error.FT_Err_Ok)
                throw Error(FontLoadErrorCode.NativeFailure, $"Failed to {operation}: {error}.");
        }

        internal static bool IsNativeDependencyFailure(Exception exception)
        {
            while (exception is TypeInitializationException && exception.InnerException != null)
                exception = exception.InnerException;
            return exception is DllNotFoundException or FileLoadException or BadImageFormatException or EntryPointNotFoundException;
        }

        private static FontLoadException Error(FontLoadErrorCode errorCode, string message) => new FontLoadException(errorCode, message);

    }
#endif

        internal sealed class SfntFontData
        {
            private const int MaximumSourceBytes = UIFontFace.MaximumSourceBytes;
            private const int MaximumFaces = UIFontFace.MaximumFaces;
            private const int MaximumTables = UIFontFace.MaximumTables;
            private const int MaximumTableBytes = UIFontFace.MaximumTableBytes;
            private static FontLoadException Error(FontLoadErrorCode code, string message) => new FontLoadException(code, message);
            private const uint TrueTypeCollectionTag = 0x74746366;
            private readonly byte[] _source;
            private readonly int _faceOffset;
            private readonly Dictionary<uint, TableRange> _tables;

            private SfntFontData(byte[] source, int faceOffset)
            {
                _source = source;
                _faceOffset = faceOffset;
                _tables = ReadTables();
                VariationAxes = ReadVariationAxes();
            }

            public List<UIFontVariationAxis> VariationAxes { get; }

            public static SfntFontData Parse(byte[] source, int faceIndex)
            {
                if (source == null || source.Length < 12) throw Error(FontLoadErrorCode.InvalidData, "Font source is truncated.");
                if (source.Length > MaximumSourceBytes) throw Error(FontLoadErrorCode.SourceTooLarge, "Font source exceeds the configured limit.");
                if (faceIndex < 0) throw Error(FontLoadErrorCode.FaceIndexOutOfRange, "Font face index cannot be negative.");
                var span = source.AsSpan();
                var signature = ReadUInt32(span, 0);
                int faceOffset;
                if (signature == TrueTypeCollectionTag)
                {
                    var faceCount = checked((int)ReadUInt32(span, 8));
                    if (faceCount <= 0 || faceCount > MaximumFaces) throw Error(FontLoadErrorCode.InvalidData, "Font collection has an invalid face count.");
                    EnsureRange(span, 12, checked(faceCount * 4));
                    if (faceIndex >= faceCount) throw Error(FontLoadErrorCode.FaceIndexOutOfRange, "Font face index is outside the collection.");
                    faceOffset = checked((int)ReadUInt32(span, 12 + faceIndex * 4));
                }
                else
                {
                    if (faceIndex != 0) throw Error(FontLoadErrorCode.FaceIndexOutOfRange, "A single-face font only supports face index zero.");
                    faceOffset = 0;
                }
                return new SfntFontData(source, faceOffset);
            }

            private Dictionary<uint, TableRange> ReadTables()
            {
                var span = _source.AsSpan();
                EnsureRange(span, _faceOffset, 12);
                var signature = ReadUInt32(span, _faceOffset);
                if (signature != 0x00010000 && signature != 0x4F54544F && signature != 0x74727565 && signature != 0x74797031)
                    throw Error(FontLoadErrorCode.UnsupportedFormat, "Font face is not a supported SFNT font.");
                var count = ReadUInt16(span, _faceOffset + 4);
                if (count == 0 || count > MaximumTables) throw Error(FontLoadErrorCode.InvalidData, "Font face has an invalid table count.");
                EnsureRange(span, _faceOffset + 12, checked(count * 16));
                var tables = new Dictionary<uint, TableRange>(count);
                var ranges = new List<TableRange>(count);
                long aggregate = 0;
                for (var index = 0; index < count; index++)
                {
                    var record = _faceOffset + 12 + index * 16;
                    var tag = ReadUInt32(span, record);
                    var offset = checked((int)ReadUInt32(span, record + 8));
                    var length = checked((int)ReadUInt32(span, record + 12));
                    if (length > MaximumTableBytes) throw Error(FontLoadErrorCode.InvalidData, "Font table exceeds the configured limit.");
                    EnsureRange(span, offset, length);
                    aggregate = checked(aggregate + length);
                    if (aggregate > MaximumSourceBytes) throw Error(FontLoadErrorCode.InvalidData, "Font tables exceed the aggregate byte limit.");
                    var range = new TableRange(offset, length);
                    foreach (var existing in ranges)
                        if (range.Overlaps(existing)) throw Error(FontLoadErrorCode.InvalidData, "Font tables contain overlapping ranges.");
                    if (!tables.TryAdd(tag, range)) throw Error(FontLoadErrorCode.InvalidData, "Font face contains duplicate table tags.");
                    if (length != 0) ranges.Add(range);
                }
                return tables;
            }

            private List<UIFontVariationAxis> ReadVariationAxes()
            {
                const uint fvarTag = 0x66766172;
                var axes = new List<UIFontVariationAxis>();
                if (!_tables.TryGetValue(fvarTag, out var table)) return axes;
                var span = _source.AsSpan();
                EnsureRange(span, table.Offset, Math.Min(table.Length, 16));
                if (table.Length < 16) throw Error(FontLoadErrorCode.InvalidData, "The fvar table is truncated.");
                var axesOffset = ReadUInt16(span, table.Offset + 4);
                var axisCount = ReadUInt16(span, table.Offset + 8);
                var axisSize = ReadUInt16(span, table.Offset + 10);
                if (axisCount > 64 || axisSize < 20) throw Error(FontLoadErrorCode.InvalidData, "The fvar axis records are invalid.");
                var recordsOffset = checked(table.Offset + axesOffset);
                var recordsLength = checked(axisCount * axisSize);
                if (recordsOffset < table.Offset || recordsOffset + recordsLength > table.Offset + table.Length)
                    throw Error(FontLoadErrorCode.InvalidData, "The fvar axis records exceed the table bounds.");
                for (var index = 0; index < axisCount; index++)
                {
                    var record = recordsOffset + index * axisSize;
                    var tagValue = ReadUInt32(span, record);
                    var tag = string.Create(4, tagValue, static (chars, value) =>
                    {
                        chars[0] = (char)((value >> 24) & 0xFF);
                        chars[1] = (char)((value >> 16) & 0xFF);
                        chars[2] = (char)((value >> 8) & 0xFF);
                        chars[3] = (char)(value & 0xFF);
                    });
                    var minimum = ReadInt32(span, record + 4) / 65536f;
                    var defaultValue = ReadInt32(span, record + 8) / 65536f;
                    var maximum = ReadInt32(span, record + 12) / 65536f;
                    if (minimum > defaultValue || defaultValue > maximum) throw Error(FontLoadErrorCode.InvalidData, "The fvar axis range is invalid.");
                    axes.Add(new UIFontVariationAxis(tag, minimum, defaultValue, maximum, ReadUInt16(span, record + 18)));
                }
                return axes;
            }

            private static ushort ReadUInt16(ReadOnlySpan<byte> source, int offset)
            {
                EnsureRange(source, offset, 2);
                return BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset, 2));
            }

            private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset)
            {
                EnsureRange(source, offset, 4);
                return BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset, 4));
            }

            private static int ReadInt32(ReadOnlySpan<byte> source, int offset)
            {
                EnsureRange(source, offset, 4);
                return BinaryPrimitives.ReadInt32BigEndian(source.Slice(offset, 4));
            }

            private static void EnsureRange(ReadOnlySpan<byte> source, int offset, int length)
            {
                if (offset < 0 || length < 0 || offset > source.Length - length)
                    throw Error(FontLoadErrorCode.InvalidData, "Font source contains an out-of-range offset.");
            }

            private readonly struct TableRange
            {
                public TableRange(int offset, int length) { Offset = offset; Length = length; }
                public int Offset { get; }
                public int Length { get; }
                public bool Overlaps(TableRange other) => Length != 0 && other.Length != 0 && Offset < other.Offset + other.Length && other.Offset < Offset + Length;
            }
        }

#if !FORMA_EXTERNAL_DYNAMIC_TEXT_BACKEND
    internal sealed unsafe partial class FreeTypeHarfBuzzFaceBackend
    {
        private sealed class PinnedFontMemoryHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public PinnedFontMemoryHandle(byte[] source) : base(true)
            {
                SetHandle(GCHandle.ToIntPtr(GCHandle.Alloc(source, GCHandleType.Pinned)));
                Interlocked.Increment(ref _activePinnedMemories);
            }

            public IntPtr Address => GCHandle.FromIntPtr(handle).AddrOfPinnedObject();

            protected override bool ReleaseHandle()
            {
                GCHandle.FromIntPtr(handle).Free();
                Interlocked.Decrement(ref _activePinnedMemories);
                return true;
            }
        }

        private sealed class FreeTypeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private FreeTypeLibraryHandle() : base(true) { }

            public static FreeTypeLibraryHandle Create()
            {
                FT_LibraryRec_* library;
                ThrowNative(FT_Init_FreeType(&library), "initialize FreeType");
                var result = new FreeTypeLibraryHandle();
                result.SetHandle((IntPtr)library);
                Interlocked.Increment(ref _activeFreeTypeLibraries);
                return result;
            }

            protected override bool ReleaseHandle()
            {
                var released = FT_Done_FreeType((FT_LibraryRec_*)handle) == FT_Error.FT_Err_Ok;
                Interlocked.Decrement(ref _activeFreeTypeLibraries);
                return released;
            }
        }

        private sealed class FreeTypeFaceHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private readonly FreeTypeLibraryHandle _library;
            private readonly PinnedFontMemoryHandle _memory;

            private FreeTypeFaceHandle(FreeTypeLibraryHandle library, PinnedFontMemoryHandle memory) : base(true)
            {
                _library = library;
                _memory = memory;
            }

            public static FreeTypeFaceHandle Create(FreeTypeLibraryHandle library, PinnedFontMemoryHandle memory, int length, int faceIndex)
            {
                FT_FaceRec_* face;
                ThrowNative(FT_New_Memory_Face((FT_LibraryRec_*)library.DangerousGetHandle(), (byte*)memory.Address, length, faceIndex, &face), $"load font face {faceIndex}");
                var result = new FreeTypeFaceHandle(library, memory);
                result.SetHandle((IntPtr)face);
                Interlocked.Increment(ref _activeFreeTypeFaces);
                return result;
            }

            protected override bool ReleaseHandle()
            {
                var released = FT_Done_Face((FT_FaceRec_*)handle) == FT_Error.FT_Err_Ok;
                Interlocked.Decrement(ref _activeFreeTypeFaces);
                return released;
            }
        }
    }
#endif
}