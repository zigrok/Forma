// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

namespace Forma.Xaml
{
    /// <summary>One style rule considered for a control: whether it matched, and if not, why.</summary>
    public sealed class StyleRuleInspection
    {
        internal StyleRuleInspection(string selector, int specificity, int order, bool matched, string reason, IReadOnlyList<string> properties, string origin = null)
        {
            Origin = origin;
            Selector = selector;
            Specificity = specificity;
            Order = order;
            Matched = matched;
            Reason = reason;
            Properties = properties;
        }

        public string Selector { get; }
        /// <summary>The source location of the rule (for example theme.fcss:12) when the style carries one.</summary>
        public string Origin { get; }
        public int Specificity { get; }
        /// <summary>Cascade position: higher specificity wins, then later declaration order.</summary>
        public int Order { get; }
        public bool Matched { get; }
        /// <summary>For an unmatched rule, the first reason it does not apply. Null when matched.</summary>
        public string Reason { get; }
        public IReadOnlyList<string> Properties { get; }
    }

    /// <summary>The winning style value for one property and the rule it came from.</summary>
    public sealed class StylePropertyInspection
    {
        internal StylePropertyInspection(string property, string value, string source)
        {
            Property = property;
            Value = value;
            Source = source;
        }

        public string Property { get; }
        public string Value { get; }
        public string Source { get; }
    }

    public sealed class StyleInspection
    {
        internal StyleInspection(Control control, IReadOnlyList<StyleRuleInspection> rules, IReadOnlyList<StylePropertyInspection> winners, string template)
        {
            ControlType = control.GetType().Name;
            Name = control.Name;
            Classes = control.Classes.ToArray();
            PseudoStates = new[] { "hover", "focus", "focus-within", "disabled", "pressed", "checked", "selected", "current" }
                .Where(control.IsPseudoStateActive).ToArray();
            Template = template;
            Rules = rules;
            Winners = winners;
        }

        public string ControlType { get; }
        public string Name { get; }
        public IReadOnlyList<string> Classes { get; }
        public IReadOnlyList<string> PseudoStates { get; }
        public string Template { get; }
        /// <summary>Every rule attached to the control's style scope, in cascade order (lowest priority first).</summary>
        public IReadOnlyList<StyleRuleInspection> Rules { get; }
        public IReadOnlyList<StylePropertyInspection> Winners { get; }
    }

    public static class StyleInspector
    {
        /// <summary>
        /// Explains the styling of <paramref name="control"/>: matched rules in cascade order, the winning value and source
        /// per property, active pseudo-states, classes, template, and for each unmatched rule why it did not match.
        /// </summary>
        public static StyleInspection Inspect(Control control)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            var rules = new List<(int Priority, StyleRuleInspection Rule, Style Style)>();
            var scopeFound = false;
            for (var owner = control; owner != null; owner = owner.VisualParent)
            {
                if (!StyleAttachment.Registry.TryGetValue(owner, out var attachments)) continue;
                foreach (var attachment in attachments.ToArray())
                {
                    if (attachment.Disposed || !attachment.Contains(control)) continue;
                    scopeFound = true;
                    for (var index = 0; index < attachment.Styles.Length; index++)
                    {
                        var style = attachment.Styles[index];
                        var matched = attachment.Matches(control, index, out var specificity);
                        var props = style.Setters.OfType<IStyleSetterInfo>().Select(setter => setter.PropertyName).ToArray();
                        var reason = matched ? null : Explain(style, control);
                        var priority = matched ? specificity * (attachment.Styles.Length + 1) + index : -1;
                        rules.Add((priority, new StyleRuleInspection(style.Selector.ToString(), matched ? specificity : style.Selector.Specificity, index, matched, reason, props, style.Origin), style));
                    }
                }
            }

            if (!scopeFound)
                rules.Add((-1, new StyleRuleInspection("(none)", 0, 0, false,
                    "No style set is attached to this control's style scope. A data template, item row or compiled view is its own style boundary; call StyleEngine.AttachFrom(view, source) from the view.", Array.Empty<string>()), null));

            var ordered = rules.OrderBy(rule => rule.Priority).ThenBy(rule => rule.Rule.Order).ToArray();
            var winners = new Dictionary<string, StylePropertyInspection>(StringComparer.Ordinal);
            foreach (var entry in ordered.Where(rule => rule.Rule.Matched))
                foreach (var setter in entry.Style.Setters.OfType<IStyleSetterInfo>())
                {
                    string value;
                    try { var raw = setter.ValueFor(control); value = raw is SolidColorBrush brush ? "SolidColorBrush " + brush.Color : raw?.ToString(); }
                    catch (Exception exception) { value = $"<{exception.GetType().Name}>"; }
                    winners[setter.PropertyName] = new StylePropertyInspection(setter.PropertyName, value, entry.Rule.Origin == null ? entry.Rule.Selector : entry.Rule.Selector + " @ " + entry.Rule.Origin);
                }

            var template = control is TemplatedControl templated ? templated.Template?.GetType().Name : null;
            return new StyleInspection(control, ordered.Select(rule => rule.Rule).ToArray(), winners.Values.OrderBy(w => w.Property, StringComparer.Ordinal).ToArray(), template);
        }

        private static string Explain(Style style, Control control)
        {
            if (style.Condition != null && control.Context != null && !style.Condition.Matches(control.Context))
                return "The rule's adaptive condition is not met for the current viewport, scale, theme or input.";
            string firstReason = null;
            foreach (var arm in style.Selector.Arms)
            {
                var subject = arm.Subject;
                var reason = Why(subject, control);
                if (reason != null) { firstReason ??= reason; continue; }
                if (arm.Compounds.Count > 1)
                    return "The target part matches, but an ancestor part of the selector does not match, or the ancestor lies across a style boundary.";
                return "The selector matches but the style did not apply (scope or boundary mismatch).";
            }
            return firstReason ?? "Unknown.";
        }

        private static string Why(StyleSelectorCompound compound, Control control)
        {
            if (compound.TypeName != null && !IsType(control.GetType(), compound.TypeName))
                return $"Type '{compound.TypeName}' does not match {control.GetType().Name}.";
            if (compound.Name != null && control.Name != compound.Name)
                return $"Name '{compound.Name}' does not match '{control.Name}'.";
            foreach (var className in compound.Classes)
                if (!control.Classes.Contains(className)) return $"Class '{className}' is missing.";
            foreach (var pseudo in compound.PseudoStates)
                if (!control.IsPseudoStateActive(pseudo)) return $"Pseudo-state ':{pseudo}' is not active.";
            foreach (var predicate in compound.Predicates)
                if (!predicate.Matches(control))
                    return predicate.Kind == "attribute" ? $"Attribute [{predicate.Name}{(predicate.Value == null ? string.Empty : "=" + predicate.Value)}] does not match (value is '{control.GetData(predicate.Name) ?? "unset"}')." : $"Structural condition :{predicate.Kind} does not match this control's position.";
            foreach (var negation in compound.Negations)
                if (Why(negation, control) == null) return "A :not() exclusion matches this control.";
            return null;
        }

        private static bool IsType(Type type, string typeName)
        {
            for (var current = type; current != null && typeof(Control).IsAssignableFrom(current); current = current.BaseType)
                if (current.Name == typeName || current.FullName == typeName) return true;
            return false;
        }
    }
}
