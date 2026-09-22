// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>
    /// One node of an <see cref="AccessibilityTreeSnapshot"/>: everything a consumer needs about a
    /// single element, flattened so the tree can be transported, diffed or serialized without
    /// walking live controls.
    /// </summary>
    public readonly struct AccessibilityNode
    {
        internal AccessibilityNode(
            int id,
            int parentId,
            AccessibilityRole role,
            string name,
            string automationId,
            string value,
            AccessibilityActions actions,
            AccessibilityStates states,
            Rectangle bounds)
        {
            Id = id;
            ParentId = parentId;
            Role = role;
            Name = name ?? string.Empty;
            AutomationId = automationId ?? string.Empty;
            Value = value ?? string.Empty;
            Actions = actions;
            States = states;
            Bounds = bounds;
        }

        /// <summary>Identity of this node, unique within the process and stable while it lives.</summary>
        public int Id { get; }

        /// <summary><see cref="Id"/> of the containing node, or 0 for a root.</summary>
        public int ParentId { get; }

        public AccessibilityRole Role { get; }

        /// <summary>What assistive technology announces. Changes when a control is relabelled.</summary>
        public string Name { get; }

        /// <summary>Author-assigned identifier, empty when unset. Stable across relabelling.</summary>
        public string AutomationId { get; }

        public string Value { get; }

        public AccessibilityActions Actions { get; }

        public AccessibilityStates States { get; }

        /// <summary>Bounds in global coordinates, matching what pointer input uses.</summary>
        public Rectangle Bounds { get; }

        public bool IsRoot => ParentId == 0;
    }

    /// <summary>
    /// A flat, ordered, serializable projection of the accessibility tree at one instant.
    /// <para>
    /// Flat rather than nested because every consumer wants it that way: a diff needs to find a node
    /// by id without a search, a transport wants no recursion, and a locator wants to filter. Order
    /// is depth-first in draw order, so reading the list top to bottom matches what a person sees.
    /// </para>
    /// </summary>
    public sealed class AccessibilityTreeSnapshot
    {
        private readonly Dictionary<int, int> _indexById;

        internal AccessibilityTreeSnapshot(IReadOnlyList<AccessibilityNode> nodes, int focusedId, bool modalActive)
        {
            Nodes = nodes;
            FocusedId = focusedId;
            IsModalActive = modalActive;

            _indexById = new Dictionary<int, int>(nodes.Count);
            for (var index = 0; index < nodes.Count; index++) _indexById[nodes[index].Id] = index;
        }

        /// <summary>Every visible node, depth-first in draw order.</summary>
        public IReadOnlyList<AccessibilityNode> Nodes { get; }

        /// <summary><see cref="AccessibilityNode.Id"/> of the focused node, or 0 when nothing has focus.</summary>
        public int FocusedId { get; }

        /// <summary>
        /// Whether a modal popup was up. When it is, the snapshot contains only that popup's subtree,
        /// matching what input does — anything behind a modal is unreachable, so reporting it would
        /// invite a consumer to act on something a person could not.
        /// </summary>
        public bool IsModalActive { get; }

        public bool TryGetNode(int id, out AccessibilityNode node)
        {
            if (_indexById.TryGetValue(id, out var index))
            {
                node = Nodes[index];
                return true;
            }

            node = default;
            return false;
        }

        /// <summary>
        /// Indented <c>role "name"</c> lines, one per node. Stable enough to commit as a golden file:
        /// bounds and ids are deliberately omitted, because they churn with window size and
        /// allocation order and would make every unrelated change look like a regression.
        /// </summary>
        public string ToText()
        {
            var depthById = new Dictionary<int, int>(Nodes.Count);
            var builder = new StringBuilder();

            foreach (var node in Nodes)
            {
                var depth = node.IsRoot || !depthById.TryGetValue(node.ParentId, out var parentDepth)
                    ? 0
                    : parentDepth + 1;
                depthById[node.Id] = depth;

                builder.Append(' ', depth * 2);
                builder.Append(node.Role.ToString());

                if (node.Name.Length > 0)
                {
                    builder.Append(" \"");
                    builder.Append(node.Name);
                    builder.Append('"');
                }

                if (node.AutomationId.Length > 0)
                {
                    builder.Append(" #");
                    builder.Append(node.AutomationId);
                }

                if (node.Value.Length > 0)
                {
                    builder.Append(" =");
                    builder.Append(node.Value);
                }

                if (node.States != AccessibilityStates.None)
                {
                    builder.Append(" [");
                    builder.Append(node.States.ToString());
                    builder.Append(']');
                }

                if (node.Id == FocusedId) builder.Append(" <focus>");

                builder.Append('\n');
            }

            return builder.ToString();
        }

        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"AccessibilityTreeSnapshot({Nodes.Count} nodes, focus={FocusedId})");
    }

    /// <summary>
    /// Captures the accessibility tree of a <see cref="UIContext"/>.
    /// <para>
    /// The walk deliberately mirrors the rules input already follows — draw order, modal gating,
    /// rendered-only — rather than inventing its own. A tree that disagrees with input about what is
    /// reachable is worse than no tree: it tells a consumer to act on something that cannot be acted
    /// on.
    /// </para>
    /// </summary>
    public static class AccessibilityTree
    {
        /// <summary>Captures the currently visible tree. Layout is settled first, so bounds are real.</summary>
        public static AccessibilityTreeSnapshot Capture(UIContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Settle rather than a single pass: a snapshot taken mid-layout reports bounds that are
            // about to change, and every consumer downstream would inherit that race.
            context.WaitForSettled();

            var nodes = new List<AccessibilityNode>();
            var modal = context.GetActiveModalPopup();

            // With a modal up, only its subtree is reachable, so only its subtree is reported.
            if (modal != null)
            {
                Visit(modal, 0, nodes);
            }
            else
            {
                foreach (var root in context.GetRootsInDrawOrder()) Visit(root, 0, nodes);
            }

            var focused = context.FocusedControl;
            var focusedId = focused != null ? focused.AccessibilityId : 0;

            return new AccessibilityTreeSnapshot(nodes, focusedId, modal != null);
        }

        /// <summary>
        /// Compares two snapshots. A node counts as updated only when something the snapshot
        /// actually carries differs, so a repaint or a change to a property outside this projection
        /// produces an empty delta rather than a spurious push.
        /// </summary>
        public static AccessibilityTreeDelta Diff(AccessibilityTreeSnapshot previous, AccessibilityTreeSnapshot current)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));

            var added = new List<AccessibilityNode>();
            var updated = new List<AccessibilityNode>();
            var removed = new List<int>();

            if (previous == null)
            {
                // No prior state: everything is new, and focus counts as changed so a consumer
                // starting mid-session is told where focus is rather than assuming nothing.
                added.AddRange(current.Nodes);
                return new AccessibilityTreeDelta(added, updated, removed, current.FocusedId, current.FocusedId != 0);
            }

            foreach (var node in current.Nodes)
            {
                if (!previous.TryGetNode(node.Id, out var before)) added.Add(node);
                else if (!AreEquivalent(before, node)) updated.Add(node);
            }

            foreach (var node in previous.Nodes)
            {
                if (!current.TryGetNode(node.Id, out _)) removed.Add(node.Id);
            }

            return new AccessibilityTreeDelta(added, updated, removed, current.FocusedId, previous.FocusedId != current.FocusedId);
        }

        /// <summary>Walks the same controls <see cref="Capture"/> would, without building a snapshot.</summary>
        internal static IEnumerable<Control> EnumerateControls(UIContext context)
        {
            var modal = context.GetActiveModalPopup();
            if (modal != null)
            {
                foreach (var control in Descend(modal)) yield return control;
                yield break;
            }

            foreach (var root in context.GetRootsInDrawOrder())
                foreach (var control in Descend(root))
                    yield return control;
        }

        private static IEnumerable<Control> Descend(Control control)
        {
            if (control == null || !control.IsRendered) yield break;

            yield return control;
            foreach (var child in control.GetChildrenInDrawOrder())
                foreach (var descendant in Descend(child))
                    yield return descendant;
        }

        private static bool AreEquivalent(AccessibilityNode left, AccessibilityNode right) =>
            left.ParentId == right.ParentId
            && left.Role == right.Role
            && left.Actions == right.Actions
            && left.States == right.States
            && left.Bounds == right.Bounds
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.AutomationId, right.AutomationId, StringComparison.Ordinal)
            && string.Equals(left.Value, right.Value, StringComparison.Ordinal);

        private static void Visit(Control control, int parentId, List<AccessibilityNode> nodes)
        {
            // Not rendered means not perceivable, and its whole subtree goes with it: a hidden
            // panel's children are no more reachable than the panel.
            if (control == null || !control.IsRendered) return;

            var peer = control.AccessibilityPeer;
            var states = peer.States;

            // Offscreen is reported rather than dropped. A consumer that scrolls before acting needs
            // to know the node exists; dropping it would make a scrolled-away item indistinguishable
            // from one that was never there.
            nodes.Add(new AccessibilityNode(
                peer.Id,
                parentId,
                peer.Role,
                peer.Name,
                peer.AutomationId,
                peer.Value,
                peer.Actions,
                states,
                peer.Bounds));

            var id = peer.Id;

            // A virtualized list's rows are not visual children, so they exist only as peers and
            // would otherwise be missed. Everything else GetAccessibilityChildren returns is the
            // peer of a visual child, which the recursion below reaches on its own — adding those
            // here as well would list every control twice.
            foreach (var itemPeer in control.GetAccessibilityChildren())
            {
                if (itemPeer is not ItemAccessibilityPeer) continue;

                nodes.Add(new AccessibilityNode(
                    itemPeer.Id,
                    id,
                    itemPeer.Role,
                    itemPeer.Name,
                    itemPeer.AutomationId,
                    itemPeer.Value,
                    itemPeer.Actions,
                    itemPeer.States,
                    itemPeer.Bounds));
            }

            foreach (var child in control.GetChildrenInDrawOrder()) Visit(child, id, nodes);
        }
    }

    /// <summary>
    /// What changed between two snapshots. This, not a whole tree, is what an OS bridge or a remote
    /// client wants: pushing 500 unchanged nodes because one label moved is the difference between a
    /// usable bridge and an unusable one.
    /// </summary>
    public readonly struct AccessibilityTreeDelta
    {
        internal AccessibilityTreeDelta(
            IReadOnlyList<AccessibilityNode> added,
            IReadOnlyList<AccessibilityNode> updated,
            IReadOnlyList<int> removed,
            int focusedId,
            bool focusChanged)
        {
            Added = added;
            Updated = updated;
            Removed = removed;
            FocusedId = focusedId;
            FocusChanged = focusChanged;
        }

        public IReadOnlyList<AccessibilityNode> Added { get; }

        public IReadOnlyList<AccessibilityNode> Updated { get; }

        /// <summary>Ids of nodes that are gone, so a consumer can drop them without a search.</summary>
        public IReadOnlyList<int> Removed { get; }

        public int FocusedId { get; }

        public bool FocusChanged { get; }

        /// <summary>True when nothing moved, so a caller can skip the push entirely.</summary>
        public bool IsEmpty => Added.Count == 0 && Updated.Count == 0 && Removed.Count == 0 && !FocusChanged;
    }

    /// <summary>
    /// Tracks whether the accessibility tree could have changed, so consumers re-capture on a signal
    /// instead of walking the whole tree every frame.
    /// <para>
    /// Deliberately conservative: it reports <em>possible</em> change, never certainty. A control
    /// raising <see cref="Control.AccessibilityChanged"/> may have changed something the snapshot
    /// does not carry, in which case the diff simply comes back empty — cheap, and far safer than
    /// missing a real change.
    /// </para>
    /// </summary>
    public sealed class AccessibilityTreeWatcher : IDisposable
    {
        private readonly UIContext _context;
        private readonly List<Control> _subscribed = new List<Control>();
        private bool _disposed;

        public AccessibilityTreeWatcher(UIContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _context.InputFocusChanged += OnFocusChanged;
        }

        /// <summary>Whether something may have changed since the last <see cref="Update"/>.</summary>
        public bool IsDirty { get; private set; } = true;

        /// <summary>The most recent snapshot, or null before the first <see cref="Update"/>.</summary>
        public AccessibilityTreeSnapshot Current { get; private set; }

        /// <summary>
        /// Re-captures when dirty and returns what changed. When nothing is dirty this does no work
        /// and returns an empty delta, which is the whole point of the type.
        /// </summary>
        public AccessibilityTreeDelta Update()
        {
            if (!IsDirty && Current != null)
                return new AccessibilityTreeDelta(Array.Empty<AccessibilityNode>(), Array.Empty<AccessibilityNode>(), Array.Empty<int>(), Current.FocusedId, false);

            var previous = Current;
            var next = AccessibilityTree.Capture(_context);
            Resubscribe(next);
            Current = next;
            IsDirty = false;

            return AccessibilityTree.Diff(previous, next);
        }

        /// <summary>Forces the next <see cref="Update"/> to re-capture.</summary>
        public void Invalidate() => IsDirty = true;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _context.InputFocusChanged -= OnFocusChanged;
            Unsubscribe();
        }

        private void OnFocusChanged() => IsDirty = true;

        private void OnAccessibilityChanged(object sender, AccessibilityChangedEventArgs args) => IsDirty = true;

        private void Resubscribe(AccessibilityTreeSnapshot snapshot)
        {
            Unsubscribe();

            // Subscriptions follow the snapshot, so a control that leaves the tree stops being
            // listened to and cannot keep it dirty forever.
            foreach (var control in AccessibilityTree.EnumerateControls(_context))
            {
                control.AccessibilityChanged += OnAccessibilityChanged;
                _subscribed.Add(control);
            }
        }

        private void Unsubscribe()
        {
            foreach (var control in _subscribed) control.AccessibilityChanged -= OnAccessibilityChanged;
            _subscribed.Clear();
        }
    }
}
