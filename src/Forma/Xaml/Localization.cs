// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;

namespace Forma.Xaml
{
    /// <summary>
    /// The data-i18n convention (as i18next's DOM bindings use): controls authored with <c>data-i18n="menu.play"</c> carry the key as the
    /// data attribute <c>i18n</c>; <see cref="Apply"/> sets their text from a translation function, and is called again when the
    /// language changes. <c>data-i18n-placeholder</c>, <c>data-i18n-label</c> and <c>data-i18n-title</c> localize the placeholder, the
    /// accessibility label and the title the same way.
    /// </summary>
    public static class Localization
    {
        public static void Apply(Control root, Func<string, string> translate)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (translate == null) throw new ArgumentNullException(nameof(translate));
            Walk(root, translate);
        }

        private static void Walk(Control control, Func<string, string> translate)
        {
            if (control.GetData("i18n") is { } key)
            {
                var text = translate(key);
                if (control is Label label) label.Text = text;
                else if (control is BaseButton button) button.Text = text;
                else if (control is GroupBox group) group.Title = text;
                else if (control is FoldableContainer fold) fold.Title = text;
            }

            if (control.GetData("i18n-label") is { } labelKey) control.AccessibilityLabel = translate(labelKey);
            if (control.GetData("i18n-title") is { } titleKey) control.TooltipText = translate(titleKey);
            if (control.GetData("i18n-placeholder") is { } placeholderKey && control is LineEdit edit) edit.PlaceholderText = translate(placeholderKey);
            foreach (var child in control.Children) Walk(child, translate);
        }
    }
}
