// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Forma.Xaml
{
    /// <summary>Options for <see cref="PseudoLocalizer"/>.</summary>
    public sealed class PseudoLocaleOptions
    {
        /// <summary>Creates the default options: 40% longer, accented, in markers, left to right.</summary>
        public PseudoLocaleOptions() { }

        /// <summary>How much longer each string becomes, as a fraction of its length (0.4 is 40% longer). Default 0.4.</summary>
        public double ExpansionFactor { get; set; } = 0.4;
        /// <summary>Replaces plain letters with accented ones, so text that assumes ASCII widths or a font without accents shows up.</summary>
        public bool Accents { get; set; } = true;
        /// <summary>Wraps each string in <c>[</c> and <c>]</c> so a clipped end is visible and untranslated strings stand out.</summary>
        public bool Markers { get; set; } = true;
        /// <summary>Reverses the word order and wraps the text in right-to-left embedding marks, to exercise mirrored layouts.</summary>
        public bool RightToLeft { get; set; }
    }

    /// <summary>
    /// A pseudo-locale for layout stress tests: every string is accented, wrapped in markers and made longer, so a layout that only
    /// fits the source language fails in a test before a real translation exists. Output is deterministic, and placeholders such as
    /// <c>{0}</c> or <c>{name}</c> are copied verbatim. It is a testing and tooling feature; it is never a shipped language.
    /// </summary>
    public static class PseudoLocalizer
    {
        private const string FillerCycle = "WMÄÖÜWMÂÊÎ";
        private static readonly Dictionary<char, char> Accented = new Dictionary<char, char>
        {
            ['a'] = 'á', ['e'] = 'é', ['i'] = 'í', ['o'] = 'ó', ['u'] = 'ú', ['c'] = 'ç', ['n'] = 'ñ', ['y'] = 'ý',
            ['A'] = 'Å', ['E'] = 'É', ['I'] = 'Î', ['O'] = 'Ö', ['U'] = 'Ü', ['C'] = 'Ç', ['N'] = 'Ñ', ['Y'] = 'Ý',
        };

        /// <summary>Transforms one string.</summary>
        public static string Transform(string text, PseudoLocaleOptions options = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            options ??= new PseudoLocaleOptions();
            if (text.Length == 0) return text;
            var body = new StringBuilder(text.Length);
            var depth = 0;
            foreach (var ch in text)
            {
                if (ch == '{') depth++;
                if (depth == 0 && options.Accents && Accented.TryGetValue(ch, out var accent)) body.Append(accent);
                else body.Append(ch);
                if (ch == '}' && depth > 0) depth--;
            }

            var result = body.ToString();
            if (options.RightToLeft) result = "\u202B" + string.Join(" ", Reverse(result.Split(' '))) + "\u202C";
            var extra = (int)Math.Ceiling(text.Length * Math.Max(0, options.ExpansionFactor));
            var filler = new StringBuilder(extra);
            for (var index = 0; index < extra; index++) filler.Append(FillerCycle[index % FillerCycle.Length]);
            result += filler.ToString();
            return options.Markers ? "[" + result + "]" : result;
        }

        /// <summary>Wraps a translation function so every string it returns is pseudo-localized, for <see cref="Localization.Apply"/>.</summary>
        public static Func<string, string> Wrap(Func<string, string> translate, PseudoLocaleOptions options = null)
        {
            if (translate == null) throw new ArgumentNullException(nameof(translate));
            return key => Transform(translate(key), options);
        }

        private static IEnumerable<string> Reverse(string[] words)
        {
            for (var index = words.Length - 1; index >= 0; index--) yield return words[index];
        }
    }
}
