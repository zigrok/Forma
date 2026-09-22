// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Text;

namespace Forma
{
    /// <summary>
    /// Assertions that fail with enough context to diagnose without re-running.
    /// <para>
    /// A UI test that fails with "expected true but was false" tells you nothing: you cannot see
    /// what was on screen, so the only way forward is to reproduce it locally. Every failure here
    /// carries the tree as it stood, which is usually enough to find the cause from the log alone.
    /// </para>
    /// <para>
    /// Framework-agnostic on purpose — it throws rather than depending on NUnit, xUnit or MSTest, so
    /// the same package works whichever a consuming app uses.
    /// </para>
    /// </summary>
    public static class UiAssert
    {
        public static void IsVisible(Locator locator, string because = null) =>
            Check(locator, locator.IsVisible, "to be visible", because);

        public static void IsNotVisible(Locator locator, string because = null) =>
            Check(locator, !locator.IsVisible, "not to be visible", because);

        public static void IsEnabled(Locator locator, string because = null) =>
            Check(locator, locator.IsEnabled, "to be enabled", because);

        public static void IsFocused(Locator locator, string because = null) =>
            Check(locator, locator.IsFocused, "to be focused", because);

        public static void HasText(Locator locator, string text, bool exact = false, string because = null) =>
            Check(locator, locator.HasText(text, exact), $"to have text \"{text}\"", because);

        public static void HasValue(Locator locator, string value, bool exact = true, string because = null) =>
            Check(locator, locator.HasValue(value, exact), $"to have value \"{value}\"", because);

        public static void HasCount(Locator locator, int expected, string because = null) =>
            Check(locator, locator.Count == expected, $"to match {expected} element(s), found {locator.Count}", because);

        /// <summary>Asserts the element can actually be acted on, naming the reason when it cannot.</summary>
        public static void IsActionable(Locator locator, string because = null)
        {
            var failure = locator.GetActionability();
            Check(locator, failure == ActionabilityFailure.None, $"to be actionable, but it is {failure}", because);
        }

        private static void Check(Locator locator, bool condition, string expectation, string because)
        {
            if (condition) return;

            var builder = new StringBuilder();
            builder.Append("Expected ").Append(locator).Append(' ').Append(expectation).Append('.');
            if (!string.IsNullOrEmpty(because)) builder.Append(' ').Append(because);

            builder.Append("\n\nTree at the time of the failure:\n");
            builder.Append(locator.CaptureTreeText());

            throw new UiAssertionException(builder.ToString());
        }
    }

    /// <summary>Thrown by <see cref="UiAssert"/>. Carries the tree in its message.</summary>
    public sealed class UiAssertionException : Exception
    {
        internal UiAssertionException(string message) : base(message) { }
    }
}
