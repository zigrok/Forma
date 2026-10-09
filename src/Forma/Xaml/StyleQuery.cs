// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Forma.Xaml
{
    /// <summary>
    /// The equivalent of <c>document.querySelectorAll</c>: finds the controls under a root that match a style selector, using the same
    /// matching rules (type, name, classes, pseudo-states, data attributes, structural conditions, descendant and child combinators)
    /// as the style engine.
    /// </summary>
    public static class StyleQuery
    {
        /// <summary>Returns the matching controls in document order, including the root itself.</summary>
        public static IReadOnlyList<Control> Select(Control root, string selector)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrWhiteSpace(selector)) throw new ArgumentException("A selector is required.", nameof(selector));
            var parsed = StyleSelector.Parse(selector);
            var matches = new List<Control>();
            var state = new StyleControlState();
            Walk(root);
            return matches;

            void Walk(Control control)
            {
                if (parsed.TryMatch(control, root, _ => state, out _)) matches.Add(control);
                foreach (var child in control.VisualChildren) Walk(child);
            }
        }
    }
}
