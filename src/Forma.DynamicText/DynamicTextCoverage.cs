// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Forma
{
    /// <summary>A character that no face of a font stack covers, with the first string that contains it.</summary>
    public sealed class MissingGlyph
    {
        /// <summary>Creates a missing-glyph finding.</summary>
        public MissingGlyph(int codePoint, string text)
        {
            CodePoint = codePoint;
            Text = text;
        }

        /// <summary>The Unicode code point no face covers.</summary>
        public int CodePoint { get; }
        /// <summary>The first string that contains the character.</summary>
        public string Text { get; }
        /// <summary>The character as a string.</summary>
        public string Character => char.ConvertFromUtf32(CodePoint);
        /// <summary>The code point, the character and its string.</summary>
        public override string ToString() => $"U+{CodePoint:X4} '{Character}' in \"{Text}\"";
    }

    /// <summary>Finds the characters of a set of strings that a font stack cannot draw, so a missing glyph is caught before a release.</summary>
    public static class DynamicTextCoverage
    {
        /// <summary>The characters of <paramref name="strings"/> that neither <paramref name="font"/>'s face nor any of its fallback faces cover.</summary>
        public static IReadOnlyList<MissingGlyph> FindMissingGlyphs(DynamicUIFont font, IEnumerable<string> strings)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            var faces = new List<UIFontFace> { font.Face };
            faces.AddRange(font.FallbackFaces);
            return FindMissingGlyphs(faces, strings);
        }

        /// <summary>The characters of <paramref name="strings"/> that none of <paramref name="faces"/> covers. Control and formatting characters are ignored.</summary>
        public static IReadOnlyList<MissingGlyph> FindMissingGlyphs(IEnumerable<UIFontFace> faces, IEnumerable<string> strings)
        {
            if (faces == null) throw new ArgumentNullException(nameof(faces));
            if (strings == null) throw new ArgumentNullException(nameof(strings));
            var stack = new List<UIFontFace>(faces);
            var missing = new List<MissingGlyph>();
            var seen = new HashSet<int>();
            foreach (var text in strings)
            {
                if (string.IsNullOrEmpty(text)) continue;
                foreach (var rune in text.EnumerateRunes())
                {
                    var category = Rune.GetUnicodeCategory(rune);
                    if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned) continue;
                    if (!seen.Add(rune.Value)) continue;
                    var covered = false;
                    foreach (var face in stack)
                        if (face.SupportsCharacter(rune.Value)) { covered = true; break; }
                    if (!covered) missing.Add(new MissingGlyph(rune.Value, text));
                }
            }

            return missing;
        }
    }
}
