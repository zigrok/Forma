// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>Where a dropped section lands relative to the pane under the pointer.</summary>
    public enum DockZone
    {
        /// <summary>Add as another tab in the target pane.</summary>
        Center,
        Left,
        Right,
        Top,
        Bottom,
    }

    /// <summary>A dockable panel: a stable id, a tab title, and the content shown when it is active.</summary>
    public sealed class DockSection
    {
        public DockSection(string id, string title, Control content)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A dock section needs a stable id.", nameof(id));
            Id = id;
            Title = title ?? id;
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public string Id { get; }
        public string Title { get; }
        public Control Content { get; }
    }

    /// <summary>Payload carried by Forma's retained drag system while a dock tab is being moved.</summary>
    internal sealed class DockDragPayload
    {
        public DockDragPayload(DockSection section, DockPane source)
        {
            Section = section;
            Source = source;
        }

        public DockSection Section { get; }
        public DockPane Source { get; }
    }

    /// <summary>
    /// One tab in a <see cref="DockPane"/>'s header. Dragging it hands a <see cref="DockDragPayload"/>
    /// to <see cref="UIContext"/>'s drag system, which resolves the drop target by walking ancestors
    /// and asking each for <see cref="Control.CanDropData"/>.
    /// </summary>
    public sealed class DockTab : BaseButton
    {
        internal DockTab(DockSection section, DockPane pane)
        {
            Section = section;
            Pane = pane;
            Text = section.Title;
            // The "checked" pseudo-state maps to ButtonPressed, which gives the active tab its
            // selected styling from the theme rather than hand-drawn chrome.
            ToggleMode = true;
        }

        internal DockSection Section { get; }
        internal DockPane Pane { get; }

        public override object GetDragData(Point position) => new DockDragPayload(Section, Pane);
    }

    /// <summary>
    /// A tabbed group of dock sections. Accepts dropped sections and reports which
    /// <see cref="DockZone"/> the pointer is over so the workspace can either merge the section in
    /// as a tab or split this pane.
    /// </summary>
    public sealed class DockPane : Container
    {
        /// <summary>Fraction of the pane's width/height treated as a split edge rather than the centre.</summary>
        private const float EdgeFraction = 0.25f;
        private const float HeaderHeight = 28f;

        private readonly List<DockSection> _sections = new List<DockSection>();
        private readonly StackPanel _header = new StackPanel { Orientation = Orientation.Horizontal, Gap = 2 };
        private Control _body;
        private int _activeIndex = -1;
        private bool _pointerInside;

        public DockPane()
        {
            AddChild(_header);
            MouseEntered += (_, _) => { _pointerInside = true; };
            MouseExited += (_, _) => { _pointerInside = false; };
        }

        internal DockWorkspace Workspace { get; set; }

        public IReadOnlyList<DockSection> Sections => _sections;

        internal int IndexOf(DockSection section) => _sections.IndexOf(section);

        public DockSection ActiveSection => _activeIndex >= 0 && _activeIndex < _sections.Count ? _sections[_activeIndex] : null;

        public void Add(DockSection section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            _sections.Add(section);
            RebuildHeader();
            Activate(_sections.Count - 1);
        }

        public bool Remove(DockSection section)
        {
            var index = _sections.IndexOf(section);
            if (index < 0) return false;
            _sections.RemoveAt(index);
            RebuildHeader();
            Activate(Math.Min(index, _sections.Count - 1));
            return true;
        }

        public void Activate(int index)
        {
            _activeIndex = _sections.Count == 0 ? -1 : MathHelper.Clamp(index, 0, _sections.Count - 1);

            // The active section's content is a direct child so this pane can arrange it; an
            // intermediate plain Container would never lay it out.
            var active = ActiveSection;
            var next = active?.Content;
            if (!ReferenceEquals(_body, next))
            {
                if (_body != null && _body.Parent == this) RemoveChild(_body);
                _body = next;
                if (_body != null) AddChild(_body);
            }

            RebuildHeader();
            QueueLayout();
        }

        private void RebuildHeader()
        {
            for (var i = _header.Children.Count - 1; i >= 0; i--) _header.RemoveChild(_header.Children[i]);
            for (var i = 0; i < _sections.Count; i++)
            {
                var index = i;
                var tab = new DockTab(_sections[i], this) { CustomMinimumSize = new Vector2(0, HeaderHeight - 6) };
                tab.ButtonPressed = index == _activeIndex;
                tab.Pressed += (_, _) => Activate(index);
                _header.AddChild(tab);
            }
        }

        /// <summary>Which zone a pointer position falls into, in this pane's local coordinates.</summary>
        internal DockZone ResolveZone(Point globalPosition)
        {
            var localX = globalPosition.X - GlobalPosition.X;
            var localY = globalPosition.Y - GlobalPosition.Y;
            var edgeX = Size.X * EdgeFraction;
            var edgeY = Size.Y * EdgeFraction;

            // Compare against the nearest edge so corners resolve to the closer axis rather than
            // always favouring one of them.
            var distances = new[]
            {
                (Zone: DockZone.Left, Distance: localX),
                (Zone: DockZone.Right, Distance: Size.X - localX),
                (Zone: DockZone.Top, Distance: localY),
                (Zone: DockZone.Bottom, Distance: Size.Y - localY),
            };

            var best = distances[0];
            foreach (var candidate in distances)
            {
                if (candidate.Distance < best.Distance) best = candidate;
            }

            var withinEdge = best.Zone == DockZone.Left || best.Zone == DockZone.Right
                ? best.Distance < edgeX
                : best.Distance < edgeY;

            return withinEdge ? best.Zone : DockZone.Center;
        }

        public override bool CanDropData(Point position, object data) =>
            Workspace != null && data is DockDragPayload payload && payload.Section != null;

        public override void DropData(Point position, object data)
        {
            if (Workspace == null || data is not DockDragPayload payload) return;
            Workspace.MoveSection(payload, this, ResolveZone(position));
        }

        public override Vector2 GetMinimumSize()
        {
            var header = _header.GetMinimumSize();
            var body = _body?.GetMinimumSize() ?? Vector2.Zero;
            return Vector2.Max(
                CustomMinimumSize,
                new Vector2(MathF.Max(header.X, body.X), HeaderHeight + body.Y));
        }

        protected override void ArrangeChildren()
        {
            var rtl = IsLayoutRtl();
            FitChildInRect(_header, Vector2.Zero, new Vector2(Size.X, HeaderHeight), rtl);
            if (_body != null)
                FitChildInRect(_body, new Vector2(0, HeaderHeight), new Vector2(Size.X, MathF.Max(0, Size.Y - HeaderHeight)), rtl);
        }

        internal override void Draw(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.PanelColor);
            context.Border(Bounds, context.Theme.PanelBorderColor, 1);
            base.Draw(context);

            // Preview the drop while a dock drag is in flight and the pointer is over this pane.
            if (!_pointerInside || Context?.DragData is not DockDragPayload) return;
            var preview = GetZonePreview(ResolveZone(Context.PointerPosition));
            context.Fill(preview, new Color(90, 140, 220, 110));
            context.Border(preview, new Color(140, 190, 255, 220), 2);
        }

        private Rectangle GetZonePreview(DockZone zone)
        {
            var bounds = Bounds;
            var halfWidth = bounds.Width / 2;
            var halfHeight = bounds.Height / 2;
            return zone switch
            {
                DockZone.Left => new Rectangle(bounds.X, bounds.Y, halfWidth, bounds.Height),
                DockZone.Right => new Rectangle(bounds.X + halfWidth, bounds.Y, bounds.Width - halfWidth, bounds.Height),
                DockZone.Top => new Rectangle(bounds.X, bounds.Y, bounds.Width, halfHeight),
                DockZone.Bottom => new Rectangle(bounds.X, bounds.Y + halfHeight, bounds.Width, bounds.Height - halfHeight),
                _ => bounds,
            };
        }
    }

    /// <summary>
    /// Root of a docking workspace. Holds a logical tree of panes and splits, and rebuilds the
    /// control tree from it whenever the layout changes.
    /// </summary>
    /// <remarks>
    /// The layout is modelled separately from the controls on purpose. <see cref="SplitContainer"/>
    /// is a <see cref="TemplatedControl"/>, and detaching one from its parent and re-adding it
    /// leaves it unusable — it either stops laying out or throws
    /// <see cref="ObjectDisposedException"/> from <c>ApplyTemplate</c> on the next frame. Earlier
    /// versions of this type mutated the live control tree in place and hit exactly that. Splits are
    /// therefore never reparented or reordered: each rebuild creates fresh ones and only the
    /// <see cref="DockPane"/>s (plain containers, safe to reparent) are carried over. Split offsets
    /// live on the logical nodes so a rebuild does not throw away the user's sizing.
    /// </remarks>
    public sealed class DockWorkspace : Container
    {
        private DockNode _tree;

        public DockPane CreatePane()
        {
            var pane = new DockPane
            {
                Workspace = this,
                HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand,
                VerticalSizeFlags = SizeFlags.Fill | SizeFlags.Expand,
            };

            if (_tree == null)
            {
                _tree = new DockLeaf(pane);
                RebuildControls();
            }

            return pane;
        }

        /// <summary>
        /// Docks <paramref name="pane"/> against <paramref name="target"/>, splitting it in the
        /// given direction. This is the programmatic form of the drag-and-drop path and shares its
        /// implementation, so a layout built in code and one built by dragging agree.
        /// </summary>
        /// <returns>The split that now holds both panes, or null for a centre dock, which merges
        /// tabs instead of splitting.</returns>
        public SplitContainer Dock(DockPane pane, DockPane target, DockZone zone)
        {
            if (pane == null) throw new ArgumentNullException(nameof(pane));
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (zone == DockZone.Center)
            {
                foreach (var section in new List<DockSection>(pane.Sections)) target.Add(section);
                return null;
            }

            var split = SplitAt(target, pane, zone);
            RebuildControls();
            return split?.Container;
        }

        internal void MoveSection(DockDragPayload payload, DockPane target, DockZone zone)
        {
            if (payload?.Section == null || target == null) return;

            var source = payload.Source;
            var section = payload.Section;

            // A centre drop onto the pane it already lives in is just a re-activation.
            if (zone == DockZone.Center && source == target)
            {
                target.Activate(target.IndexOf(section));
                return;
            }

            source?.Remove(section);

            if (zone == DockZone.Center)
            {
                target.Add(section);
            }
            else
            {
                var moved = new DockPane
                {
                    Workspace = this,
                    HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand,
                    VerticalSizeFlags = SizeFlags.Fill | SizeFlags.Expand,
                };
                moved.Add(section);
                SplitAt(target, moved, zone);
            }

            PruneEmptyPanes();
            RebuildControls();
        }

        /// <summary>Replaces the target's leaf with a split holding the target and the new pane.</summary>
        private DockSplit SplitAt(DockPane target, DockPane inserted, DockZone zone)
        {
            var leaf = FindLeaf(_tree, target);
            if (leaf == null) return null;

            var horizontal = zone == DockZone.Left || zone == DockZone.Right;
            var insertedFirst = zone == DockZone.Left || zone == DockZone.Top;
            var insertedLeaf = new DockLeaf(inserted);
            var split = new DockSplit(
                horizontal ? Orientation.Horizontal : Orientation.Vertical,
                insertedFirst ? insertedLeaf : leaf,
                insertedFirst ? leaf : insertedLeaf);

            Replace(leaf, split);
            return split;
        }

        /// <summary>Drops emptied panes and collapses the splits that held them, so a drag never
        /// leaves a blank region behind.</summary>
        private void PruneEmptyPanes()
        {
            while (true)
            {
                var empty = FindEmptyLeaf(_tree);
                if (empty == null) return;

                if (ReferenceEquals(empty, _tree))
                {
                    _tree = null;
                    return;
                }

                var parent = FindParent(_tree, empty);
                if (parent == null) return;
                var survivor = ReferenceEquals(parent.First, empty) ? parent.Second : parent.First;
                Replace(parent, survivor);
            }
        }

        private void Replace(DockNode existing, DockNode replacement)
        {
            if (ReferenceEquals(existing, _tree))
            {
                _tree = replacement;
                return;
            }

            var parent = FindParent(_tree, existing);
            if (parent == null) return;
            if (ReferenceEquals(parent.First, existing)) parent.First = replacement;
            else parent.Second = replacement;
        }

        private static DockLeaf FindLeaf(DockNode node, DockPane pane)
        {
            switch (node)
            {
                case DockLeaf leaf:
                    return ReferenceEquals(leaf.Pane, pane) ? leaf : null;
                case DockSplit split:
                    return FindLeaf(split.First, pane) ?? FindLeaf(split.Second, pane);
                default:
                    return null;
            }
        }

        private static DockLeaf FindEmptyLeaf(DockNode node)
        {
            switch (node)
            {
                case DockLeaf leaf:
                    return leaf.Pane.Sections.Count == 0 ? leaf : null;
                case DockSplit split:
                    return FindEmptyLeaf(split.First) ?? FindEmptyLeaf(split.Second);
                default:
                    return null;
            }
        }

        private static DockSplit FindParent(DockNode node, DockNode child)
        {
            if (node is not DockSplit split) return null;
            if (ReferenceEquals(split.First, child) || ReferenceEquals(split.Second, child)) return split;
            return FindParent(split.First, child) ?? FindParent(split.Second, child);
        }

        /// <summary>
        /// Rebuilds the control tree from the logical one. Panes are detached and reattached (they
        /// are plain containers); splits are always created fresh and the previous ones disposed.
        /// </summary>
        private void RebuildControls()
        {
            CaptureSplitOffsets(_tree);

            var previousSplits = new List<SplitContainer>();
            CollectSplitContainers(_tree, previousSplits);

            DetachPanes(_tree);
            for (var index = Children.Count - 1; index >= 0; index--) RemoveChild(Children[index]);

            var root = BuildControls(_tree);
            if (root != null) AddChild(root);

            foreach (var split in previousSplits)
            {
                split.ChildAddedHandlerSafeDispose();
            }

            QueueLayout();
        }

        private static void CaptureSplitOffsets(DockNode node)
        {
            if (node is not DockSplit split) return;
            if (split.Container != null) split.Offset = split.Container.SplitOffset;
            CaptureSplitOffsets(split.First);
            CaptureSplitOffsets(split.Second);
        }

        private static void CollectSplitContainers(DockNode node, List<SplitContainer> result)
        {
            if (node is not DockSplit split) return;
            if (split.Container != null) result.Add(split.Container);
            CollectSplitContainers(split.First, result);
            CollectSplitContainers(split.Second, result);
        }

        private static void DetachPanes(DockNode node)
        {
            switch (node)
            {
                case DockLeaf leaf:
                    leaf.Pane.Parent?.RemoveChild(leaf.Pane);
                    break;
                case DockSplit split:
                    DetachPanes(split.First);
                    DetachPanes(split.Second);
                    break;
            }
        }

        private Control BuildControls(DockNode node)
        {
            switch (node)
            {
                case DockLeaf leaf:
                    return leaf.Pane;
                case DockSplit split:
                {
                    var container = new SplitContainer(split.Orientation)
                    {
                        HorizontalSizeFlags = SizeFlags.Fill | SizeFlags.Expand,
                        VerticalSizeFlags = SizeFlags.Fill | SizeFlags.Expand,
                    };

                    var first = BuildControls(split.First);
                    var second = BuildControls(split.Second);
                    if (first != null) container.AddChild(first);
                    if (second != null) container.AddChild(second);
                    container.SetSplitOffset(split.Offset);
                    split.Container = container;
                    return container;
                }

                default:
                    return null;
            }
        }

        /// <summary>Renders the current layout tree as text. Intended for diagnostics and tests.</summary>
        public string DescribeTree() => Describe(_tree);

        private static string Describe(DockNode node)
        {
            switch (node)
            {
                case DockLeaf leaf:
                {
                    var titles = new List<string>(leaf.Pane.Sections.Count);
                    foreach (var section in leaf.Pane.Sections) titles.Add(section.Id);
                    return "Pane[" + string.Join(",", titles) + "]";
                }

                case DockSplit split:
                    return (split.Orientation == Orientation.Horizontal ? "H(" : "V(")
                        + Describe(split.First) + " | " + Describe(split.Second) + ")";
                default:
                    return "(empty)";
            }
        }

        public override Vector2 GetMinimumSize() =>
            Children.Count == 0 ? CustomMinimumSize : Vector2.Max(CustomMinimumSize, Children[0].GetMinimumSize());

        protected override void ArrangeChildren()
        {
            if (Children.Count == 0) return;
            FitChildInRect(Children[0], Vector2.Zero, Size, IsLayoutRtl());
        }

        private abstract class DockNode { }

        private sealed class DockLeaf : DockNode
        {
            public DockLeaf(DockPane pane) => Pane = pane;

            public DockPane Pane { get; }
        }

        private sealed class DockSplit : DockNode
        {
            public DockSplit(Orientation orientation, DockNode first, DockNode second)
            {
                Orientation = orientation;
                First = first;
                Second = second;
            }

            public Orientation Orientation { get; }
            public DockNode First { get; set; }
            public DockNode Second { get; set; }
            public float Offset { get; set; }

            /// <summary>The control realising this node, replaced on every rebuild.</summary>
            public SplitContainer Container { get; set; }
        }
    }

    internal static class DockDisposalExtensions
    {
        /// <summary>Disposes a discarded split, tolerating a type that does not own disposable
        /// state so the rebuild never fails on cleanup.</summary>
        public static void ChildAddedHandlerSafeDispose(this SplitContainer container)
        {
            if (container is not IDisposable disposable) return;
            try { disposable.Dispose(); }
            catch (ObjectDisposedException) { }
        }
    }
}
