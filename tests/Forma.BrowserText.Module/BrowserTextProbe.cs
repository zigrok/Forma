// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

namespace Forma.BrowserText.Module;

/// <summary>Runs in a separately loaded module; fonts are fetched content, never compiled resources.</summary>
public static class BrowserTextProbe
{
    public static string Run(IReadOnlyDictionary<string, byte[]> fonts)
    {
        var samples = new Dictionary<string, string>
        {
            ["en"] = "Hello, Luna! café",
            ["ar"] = "مَرْحَبًا يا لونا!",
            ["es-419"] = "¡Hola, Luna! ¿Cómo estás?",
            ["ja"] = "こんにちは、ルナ！友達",
            ["ko"] = "안녕, 루나! 친구",
            ["pt-BR"] = "Olá, Luna! coração",
            ["zh-Hans"] = "你好，露娜！朋友",
        };
        var reports = new List<string>();
        foreach (var (locale, text) in samples)
        {
            if (!fonts.TryGetValue(locale, out var bytes))
                throw new ArgumentException($"Missing production font bytes for {locale}.", nameof(fonts));
            using var face = UIFontFace.FromMemory(bytes);
            var font = new DynamicUIFont(face, 24, UIFontHinting.None);
            var engine = new TextLayoutEngine();
            var full = engine.Layout(font, text, new TextLayoutOptions(locale: locale));
            var glyphs = full.Runs.SelectMany(r => r.Glyphs).ToArray();
            if (glyphs.Length == 0 || glyphs.Any(g => g.GlyphId == 0))
                throw new InvalidOperationException($"{locale}: font coverage is incomplete after shaping.");
            if (locale == "ar" && !full.Runs.Any(r => r.Direction == TextDirection.RightToLeft))
                throw new InvalidOperationException("Arabic did not retain the bidi/shaping pipeline.");
            var coverage = 0L;
            foreach (var glyph in glyphs)
                foreach (var value in face.RasterizeGlyph(glyph.GlyphId, 24).Pixels.Span)
                    coverage += value;
            if (coverage == 0)
                throw new InvalidOperationException($"{locale}: real glyph rasterization produced no coverage.");
            var partial = engine.Layout(font, text, new TextLayoutOptions(locale: locale, maxVisibleCharacters: 1));
            if (!partial.Runs.SelectMany(r => r.Glyphs).Select(g => (g.GlyphId, g.Position))
                .SequenceEqual(glyphs.Select(g => (g.GlyphId, g.Position))))
                throw new InvalidOperationException($"{locale}: typewriter reveal changed shaping.");
            reports.Add($"{locale}:{glyphs.Length}:{coverage}");
        }
        return string.Join(";", reports);
    }
}
