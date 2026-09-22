// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>Why a located element could not be acted on.</summary>
    public enum ActionabilityFailure
    {
        None,

        /// <summary>No element matched, or the element left the tree.</summary>
        NotPresent,

        /// <summary>Reported <see cref="AccessibilityStates.Disabled"/>.</summary>
        Disabled,

        /// <summary>Scrolled out of view. Real, and distinct from hidden — scroll it in first.</summary>
        Offscreen,

        /// <summary>Zero-area, so there is nowhere to click.</summary>
        ZeroSized,

        /// <summary>Something else is on top at this point, typically a modal or an overlay.</summary>
        Obscured,
    }

    /// <summary>Thrown when a locator cannot resolve, or resolves to something unusable.</summary>
    public sealed class LocatorException : Exception
    {
        internal LocatorException(string message) : base(message) { }
    }

    /// <summary>
    /// A lazily-resolved query for one element, in the style of Playwright's locators.
    /// <para>
    /// Lazy is the whole point: a locator describes <em>how to find</em> an element, not an element
    /// it already found. It re-resolves at the moment it is used, so a locator built before the UI
    /// changed still works afterwards, and a test never holds a stale reference to something that
    /// has since been re-laid-out, recycled or replaced.
    /// </para>
    /// <para>
    /// Open for extension by design (see <see cref="Where"/>): an app builds its own vocabulary as
    /// extension methods over this type rather than forking it, which is how a canvas or any other
    /// app-specific surface gets first-class locators without Forma knowing what they mean.
    /// </para>
    /// <para>
    /// Every resolution waits for the UI to settle first, so a test never advances a fixed number of
    /// frames and hopes. Too few and the assertion races layout; too many and every test pays for
    /// the slowest case.
    /// </para>
    /// </summary>
    public sealed class Locator
    {
        private readonly UIContext _context;
        private readonly Locator _scope;
        private readonly IReadOnlyList<Filter> _filters;
        private readonly int? _index;

        internal Locator(UIContext context, Locator scope, IReadOnlyList<Filter> filters, int? index)
        {
            _context = context;
            _scope = scope;
            _filters = filters;
            _index = index;
        }

        internal readonly struct Filter
        {
            public Filter(string description, Func<AccessibilityNode, bool> predicate)
            {
                Description = description;
                Predicate = predicate;
            }

            public string Description { get; }

            public Func<AccessibilityNode, bool> Predicate { get; }
        }

        /// <summary>A human-readable form of this query, used in failure messages.</summary>
        public string Description
        {
            get
            {
                var builder = new StringBuilder();
                if (_scope != null)
                {
                    builder.Append(_scope.Description);
                    builder.Append(" >> ");
                }

                builder.Append(_filters.Count == 0 ? "*" : string.Join(" & ", _filters.Select(filter => filter.Description)));
                if (_index.HasValue) builder.Append($" [nth={_index.Value}]");
                return builder.ToString();
            }
        }

        /// <summary>
        /// Narrows by an arbitrary predicate. This is the extension seam: an app adds its own
        /// vocabulary as extension methods that call this, so domain locators compose with the
        /// built-in ones instead of living in a parallel API.
        /// </summary>
        public Locator Where(string description, Func<AccessibilityNode, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            var filters = new List<Filter>(_filters) { new Filter(description ?? "custom", predicate) };
            return new Locator(_context, _scope, filters, _index);
        }

        public Locator ByRole(AccessibilityRole role) =>
            Where($"role={role}", node => node.Role == role);

        public Locator ByName(string name, bool exact = true) =>
            Where($"name={Quote(name)}", node => Matches(node.Name, name, exact));

        public Locator ByAutomationId(string automationId) =>
            Where($"id={Quote(automationId)}", node => string.Equals(node.AutomationId, automationId, StringComparison.Ordinal));

        /// <summary>Matches the accessible name or the value, which is where visible text lives.</summary>
        public Locator ByText(string text, bool exact = false) =>
            Where($"text={Quote(text)}", node => Matches(node.Name, text, exact) || Matches(node.Value, text, exact));

        /// <summary>Selects one of several matches. Negative indexes count from the end, so -1 is
        /// the last match.</summary>
        public Locator Nth(int index) => new Locator(_context, _scope, _filters, index);

        public Locator First => Nth(0);

        public Locator Last => Nth(-1);

        /// <summary>Restricts the search to descendants of this locator's match.</summary>
        public Locator Within() => new Locator(_context, this, Array.Empty<Filter>(), null);

        /// <summary>Every match, in tree order. Empty rather than throwing.</summary>
        public IReadOnlyList<AccessibilityNode> ResolveAll()
        {
            var snapshot = AccessibilityTree.Capture(_context);
            return ResolveAll(snapshot);
        }

        /// <summary>True when exactly the expected element is present.</summary>
        public bool Exists => ResolveAll().Count > 0;

        public int Count => ResolveAll().Count;

        /// <summary>
        /// The single matching element, or a failure that says what went wrong and what was nearby.
        /// "not found" on its own is nearly useless in a test log; these messages carry the query,
        /// the candidates that were considered, and the surrounding tree.
        /// </summary>
        public AccessibilityNode Resolve()
        {
            var snapshot = AccessibilityTree.Capture(_context);
            var matches = ResolveAll(snapshot);

            if (matches.Count == 1) return matches[0];
            if (matches.Count == 0) throw new LocatorException(Describe("resolved to nothing", snapshot, matches));

            throw new LocatorException(Describe($"is ambiguous: {matches.Count} elements match", snapshot, matches)
                + "\nNarrow it, or select one with .Nth(i)/.First/.Last.");
        }

        /// <summary>
        /// Resolves and checks the element can actually be acted on, the way Playwright gates every
        /// action. The states needed for this were already carried by the tree; nothing checked them
        /// before, so a test could "click" something disabled or covered and quietly pass.
        /// </summary>
        public AccessibilityNode ResolveActionable()
        {
            var node = Resolve();
            var failure = CheckActionability(node, out var detail);
            if (failure == ActionabilityFailure.None) return node;

            throw new LocatorException($"Locator {Quote(Description)} matched an element that cannot be acted on: {detail}");
        }

        /// <summary>Why this element cannot be acted on, or <see cref="ActionabilityFailure.None"/>.</summary>
        public ActionabilityFailure GetActionability()
        {
            var matches = ResolveAll();
            if (matches.Count != 1) return ActionabilityFailure.NotPresent;
            return CheckActionability(matches[0], out _);
        }

        // --- actions ----------------------------------------------------------------------------

        /// <summary>
        /// Activates the element, after checking it can actually be acted on. This is the semantic
        /// equivalent of a click: it runs the control's own activation path, so the observable
        /// result is the same as a real press rather than a parallel implementation of one.
        /// </summary>
        public void Click() => Perform(AccessibilityActions.Press, null, "click");

        /// <summary>Gives the element keyboard focus.</summary>
        public void Focus() => Perform(AccessibilityActions.Focus, null, "focus");

        /// <summary>Selects the element, for list, tree and tab items.</summary>
        public void Select() => Perform(AccessibilityActions.Select, null, "select");

        /// <summary>Replaces the element's text. The editable-field counterpart of <see cref="Click"/>.</summary>
        public void Fill(string text) => Perform(AccessibilityActions.SetValue, text ?? string.Empty, "fill");

        /// <summary>Sets a numeric value, for sliders, scrollbars and spin buttons.</summary>
        public void SetValue(object value) => Perform(AccessibilityActions.SetValue, value, "set value");

        public void Increment() => Perform(AccessibilityActions.Increment, null, "increment");

        public void Decrement() => Perform(AccessibilityActions.Decrement, null, "decrement");

        private void Perform(AccessibilityActions action, object argument, string verb)
        {
            var node = ResolveActionable();

            // The node is a value snapshot, so the live control has to be found again to act on it.
            var control = FindControl(node.Id)
                ?? throw new LocatorException($"Cannot {verb} {Quote(Description)}: it left the tree between resolving and acting.");

            if ((node.Actions & action) == 0)
            {
                throw new LocatorException(
                    $"Cannot {verb} {Quote(Description)}: {node.Role} \"{node.Name}\" does not support {action}. "
                    + $"It advertises {node.Actions}.");
            }

            if (!control.AccessibilityPeer.Invoke(action, argument))
                throw new LocatorException($"Cannot {verb} {Quote(Description)}: {action} was refused by {node.Role} \"{node.Name}\".");
        }

        private Control FindControl(int id)
        {
            foreach (var control in AccessibilityTree.EnumerateControls(_context))
                if (control.AccessibilityId == id) return control;
            return null;
        }

        // --- assertions -------------------------------------------------------------------------

        public bool IsVisible => ResolveAll().Count > 0;

        public bool IsEnabled
        {
            get
            {
                var matches = ResolveAll();
                return matches.Count == 1 && (matches[0].States & AccessibilityStates.Disabled) == 0;
            }
        }

        public bool IsFocused
        {
            get
            {
                var snapshot = AccessibilityTree.Capture(_context);
                var matches = ResolveAll(snapshot);
                return matches.Count == 1 && snapshot.FocusedId == matches[0].Id;
            }
        }

        public bool HasText(string text, bool exact = false)
        {
            var matches = ResolveAll();
            return matches.Count == 1
                && (Matches(matches[0].Name, text, exact) || Matches(matches[0].Value, text, exact));
        }

        public bool HasValue(string value, bool exact = true)
        {
            var matches = ResolveAll();
            return matches.Count == 1 && Matches(matches[0].Value, value, exact);
        }

        public override string ToString() => $"Locator({Description})";

        // --- internals --------------------------------------------------------------------------

        private IReadOnlyList<AccessibilityNode> ResolveAll(AccessibilityTreeSnapshot snapshot)
        {
            IEnumerable<AccessibilityNode> candidates = snapshot.Nodes;

            if (_scope != null)
            {
                var scopeMatches = _scope.ResolveAll(snapshot);
                if (scopeMatches.Count != 1)
                {
                    // An ambiguous or absent scope cannot narrow anything meaningfully, and silently
                    // searching the whole tree instead would hide the mistake.
                    return Array.Empty<AccessibilityNode>();
                }

                var descendants = Descendants(snapshot, scopeMatches[0].Id);
                candidates = descendants;
            }

            foreach (var filter in _filters) candidates = candidates.Where(filter.Predicate);

            var results = candidates.ToList();
            if (!_index.HasValue) return results;

            // Negative counts from the end, so Nth(-1) is the last match.
            var index = _index.Value < 0 ? results.Count + _index.Value : _index.Value;
            return index >= 0 && index < results.Count
                ? new[] { results[index] }
                : Array.Empty<AccessibilityNode>();
        }

        private static HashSet<int> DescendantIds(AccessibilityTreeSnapshot snapshot, int rootId)
        {
            var ids = new HashSet<int> { rootId };
            // One forward pass is enough because nodes are in depth-first order, so a parent always
            // precedes its children.
            foreach (var node in snapshot.Nodes)
                if (ids.Contains(node.ParentId)) ids.Add(node.Id);

            ids.Remove(rootId);
            return ids;
        }

        private static List<AccessibilityNode> Descendants(AccessibilityTreeSnapshot snapshot, int rootId)
        {
            var ids = DescendantIds(snapshot, rootId);
            return snapshot.Nodes.Where(node => ids.Contains(node.Id)).ToList();
        }

        private ActionabilityFailure CheckActionability(AccessibilityNode node, out string detail)
        {
            if ((node.States & AccessibilityStates.Disabled) != 0)
            {
                detail = "it is disabled.";
                return ActionabilityFailure.Disabled;
            }

            if ((node.States & AccessibilityStates.Offscreen) != 0)
            {
                detail = "it is scrolled out of view. Bring it into view first.";
                return ActionabilityFailure.Offscreen;
            }

            if (node.Bounds.Width <= 0 || node.Bounds.Height <= 0)
            {
                detail = $"it has no area ({node.Bounds.Width}x{node.Bounds.Height}), so there is nowhere to act.";
                return ActionabilityFailure.ZeroSized;
            }

            // The decisive check: whatever is actually on top at this point must be the element
            // itself or something inside it. This is what catches an element covered by an overlay
            // or behind a modal, which no state flag reports.
            var point = node.Bounds.Center;
            var hit = _context.HitTest(point);
            if (hit == null)
            {
                detail = $"nothing is hit-testable at its centre {point}.";
                return ActionabilityFailure.Obscured;
            }

            if (!IsSelfOrDescendant(hit, node.Id))
            {
                detail = $"another element ({hit.AccessibilityRole} \"{hit.AccessibilityName}\") is on top at {point}.";
                return ActionabilityFailure.Obscured;
            }

            detail = null;
            return ActionabilityFailure.None;
        }

        private static bool IsSelfOrDescendant(Control hit, int id)
        {
            for (var current = hit; current != null; current = current.VisualParent)
                if (current.AccessibilityId == id) return true;
            return false;
        }

        private string Describe(string problem, AccessibilityTreeSnapshot snapshot, IReadOnlyList<AccessibilityNode> matches)
        {
            var builder = new StringBuilder();
            builder.Append("Locator ").Append(Quote(Description)).Append(' ').Append(problem).Append('.');

            if (matches.Count > 1)
            {
                builder.Append("\nMatches:");
                foreach (var match in matches.Take(10))
                    builder.Append($"\n  - {match.Role} \"{match.Name}\"{(match.AutomationId.Length > 0 ? " #" + match.AutomationId : string.Empty)} at {match.Bounds}");
                if (matches.Count > 10) builder.Append($"\n  ... and {matches.Count - 10} more");
            }

            builder.Append("\nTree at the time of the failure:\n");
            builder.Append(snapshot.ToText());
            return builder.ToString();
        }

        private static bool Matches(string actual, string expected, bool exact) =>
            exact
                ? string.Equals(actual, expected, StringComparison.Ordinal)
                : actual != null && expected != null && actual.Contains(expected, StringComparison.OrdinalIgnoreCase);

        private static string Quote(string value) => value == null ? "null" : $"\"{value}\"";
    }

    /// <summary>Entry point for locating elements in a <see cref="UIContext"/>.</summary>
    public static class Ui
    {
        /// <summary>Starts a query matching everything, to be narrowed.</summary>
        public static Locator Query(this UIContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return new Locator(context, null, Array.Empty<Locator.Filter>(), null);
        }

        public static Locator ByRole(this UIContext context, AccessibilityRole role) => context.Query().ByRole(role);

        public static Locator ByName(this UIContext context, string name, bool exact = true) => context.Query().ByName(name, exact);

        public static Locator ByAutomationId(this UIContext context, string automationId) => context.Query().ByAutomationId(automationId);

        public static Locator ByText(this UIContext context, string text, bool exact = false) => context.Query().ByText(text, exact);
    }
}
