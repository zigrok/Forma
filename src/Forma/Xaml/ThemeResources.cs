// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

namespace Forma.Xaml
{
    /// <summary>
    /// Applies a named theme to a view's resources. The HTML and CSS dialect emits a token's overrides as <c>name@theme</c> resources
    /// (from <c>:root[data-theme="dark"]</c> or <c>@media (prefers-color-scheme: dark)</c>) and its original value as <c>name@default</c>;
    /// <see cref="Apply"/> copies the chosen set over the base keys, so every live <c>var(--name)</c> follows, and records the theme as the
    /// root's <c>data-theme</c> attribute so <c>[data-theme="dark"]</c> selectors match.
    /// </summary>
    public static class ThemeResources
    {
        /// <summary>Applies <paramref name="theme"/> (null or "default" restores the original values).</summary>
        public static void Apply(Control root, string theme)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var name = string.IsNullOrEmpty(theme) ? "default" : theme;
            var dictionaries = new List<ResourceDictionary>();
            Collect(root.Resources, dictionaries, new HashSet<ResourceDictionary>());
            var suffix = "@" + name;
            foreach (var dictionary in dictionaries)
                foreach (var key in dictionary.Keys.Where(key => key.EndsWith(suffix, StringComparison.Ordinal)).ToArray())
                {
                    var baseKey = key.Substring(0, key.Length - suffix.Length);
                    var owner = dictionaries.FirstOrDefault(candidate => candidate.ContainsKey(baseKey)) ?? dictionary;
                    owner[baseKey] = dictionary[key];
                }

            root.SetData("theme", name == "default" ? null : name);
        }

        private static void Collect(ResourceDictionary dictionary, List<ResourceDictionary> into, HashSet<ResourceDictionary> seen)
        {
            if (!seen.Add(dictionary)) return;
            into.Add(dictionary);
            foreach (var merged in dictionary.MergedDictionaries) Collect(merged, into, seen);
        }
    }
}
