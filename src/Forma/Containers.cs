// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// Layout algorithms are adapted from Godot Engine's container.cpp, box_container.cpp,
// center_container.cpp, grid_container.cpp, and margin_container.cpp;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>Base for controls that measure and arrange their visible children.</summary>
    public class Container : Control
    {
        /// <summary>Sizes and positions a child within a rectangle honoring its size flags, matching Godot's Container::fit_child_in_rect. A non-Fill axis sizes the child to its own minimum and aligns it within the span; horizontal (X) alignment flips under RTL, vertical (Y) never does, matching Godot exactly. This port has no desired-size/maximum-size layer, so a non-Fill axis always resolves to the child's plain minimum size.</summary>
        protected internal static void FitChildInRect(Control child, Vector2 rectPosition, Vector2 rectSize, bool rtl)
        {
            var min = child.GetMinimumSize();
            var position = rectPosition;
            var size = rectSize;
            if ((child.HorizontalSizeFlags & SizeFlags.Fill) == 0)
            {
                size.X = min.X;
                if ((child.HorizontalSizeFlags & SizeFlags.ShrinkEnd) != 0) position.X += rtl ? 0 : rectSize.X - min.X;
                else if ((child.HorizontalSizeFlags & SizeFlags.ShrinkCenter) != 0) position.X += MathF.Floor((rectSize.X - min.X) / 2);
                else position.X += rtl ? rectSize.X - min.X : 0;
            }
            if ((child.VerticalSizeFlags & SizeFlags.Fill) == 0)
            {
                size.Y = min.Y;
                if ((child.VerticalSizeFlags & SizeFlags.ShrinkEnd) != 0) position.Y += rectSize.Y - min.Y;
                else if ((child.VerticalSizeFlags & SizeFlags.ShrinkCenter) != 0) position.Y += MathF.Floor((rectSize.Y - min.Y) / 2);
            }
            child.Position = position;
            child.Size = Vector2.Max(Vector2.Zero, size);
        }
    }

    /// <summary>Main-axis placement used by a BoxContainer when no child consumes its spare space.</summary>
    public enum BoxAlignment { Begin, Center, End }

    /// <summary>Arranges visible children sequentially along one axis and distributes spare space to expanding children.</summary>
    public class BoxContainer : Container
    {
        private BoxAlignment _alignment;
        private bool _reverseSort;
        public BoxContainer() : this(Orientation.Vertical) { }
        public BoxContainer(Orientation orientation) { Orientation = orientation; }
        public Orientation Orientation { get; }
        public float Separation { get; set; } = float.NaN;
        public BoxAlignment Alignment { get => _alignment; set { _alignment = value; QueueLayout(); } }
        /// <summary>Arranges visible children in reverse order while preserving their ownership order.</summary>
        public bool ReverseSort { get => _reverseSort; set { _reverseSort = value; QueueLayout(); } }
        /// <summary>Appends a mouse-pass-through, expand-fill child that consumes leftover space along the box's axis, matching Godot's BoxContainer::add_spacer.</summary>
        public Control AddSpacer(bool begin = false)
        {
            var spacer = new Control { MouseFilter = MouseFilter.Pass };
            if (Orientation == Orientation.Vertical) spacer.VerticalSizeFlags = SizeFlags.Expand | SizeFlags.Fill;
            else spacer.HorizontalSizeFlags = SizeFlags.Expand | SizeFlags.Fill;
            AddChild(spacer);
            if (begin) MoveChild(spacer, 0);
            return spacer;
        }
        public override Vector2 GetMinimumSize()
        {
            var size = Vector2.Zero;
            var count = 0;
            foreach (var child in VisualChildren)
            {
                if (!child.Visible) continue;
                var childSize = child.GetMinimumSize() + new Vector2(child.Margins.Horizontal, child.Margins.Vertical);
                if (Orientation == Orientation.Horizontal) { size.X += childSize.X; size.Y = Math.Max(size.Y, childSize.Y); }
                else { size.X = Math.Max(size.X, childSize.X); size.Y += childSize.Y; }
                count++;
            }
            if (count > 1)
            {
                var gap = float.IsNaN(Separation) ? Context?.Theme.Separation ?? 4 : Separation;
                if (Orientation == Orientation.Horizontal) size.X += gap * (count - 1); else size.Y += gap * (count - 1);
            }
            return Vector2.Max(CustomMinimumSize, size);
        }
        protected override void ArrangeChildren()
        {
            var children = new List<Control>();
            foreach (var child in VisualChildren) if (child.Visible) children.Add(child);
            var reverseOrder = ReverseSort ^ (Orientation == Orientation.Horizontal && IsLayoutRtl());
            if (reverseOrder) children.Reverse();
            var count = children.Count;
            if (count == 0) return;
            var gap = float.IsNaN(Separation) ? Context?.Theme.Separation ?? 4 : Separation;

            // Per-child main-axis minimum and Expand eligibility, mirroring Godot's box_container.cpp
            // MinSizeCache pass.
            var minSizes = new float[count];
            var willStretch = new bool[count];
            var finalSizes = new float[count];
            var combinedMin = 0f;
            var stretchSpace = 0f;
            var stretchRatioTotal = 0f;
            for (var i = 0; i < count; i++)
            {
                var child = children[i];
                var min = child.GetMinimumSize();
                var mainMin = Orientation == Orientation.Horizontal ? min.X + child.Margins.Horizontal : min.Y + child.Margins.Vertical;
                minSizes[i] = mainMin;
                finalSizes[i] = mainMin;
                combinedMin += mainMin;
                var flags = Orientation == Orientation.Horizontal ? child.HorizontalSizeFlags : child.VerticalSizeFlags;
                willStretch[i] = (flags & SizeFlags.Expand) != 0;
                if (willStretch[i])
                {
                    stretchSpace += mainMin;
                    // Godot's Control::set_stretch_ratio performs no clamping, so a true zero ratio is
                    // valid and legitimately starves that child of any bonus space (control.cpp:2343-2355).
                    stretchRatioTotal += child.SizeFlagsStretchRatio;
                }
            }

            var maxSpace = (Orientation == Orientation.Horizontal ? Size.X : Size.Y) - gap * (count - 1);
            stretchSpace += Math.Max(0, maxSpace - combinedMin);

            // Iterative "starvation" redistribution: allocate stretch_space by ratio; any child whose
            // share would fall below its own minimum is pinned to its minimum and dropped from the
            // ratio pool, then the remaining pool is redivided. Repeats until a pass needs no pinning.
            // Matches Godot's BoxContainer::_resort while (stretch_ratio_total > 0) loop exactly, minus
            // the desired-size/max-size layer this port doesn't model.
            while (stretchRatioTotal > 0)
            {
                var refitSuccessful = true;
                var error = 0f;
                for (var i = 0; i < count; i++)
                {
                    if (!willStretch[i]) continue;
                    var ratio = children[i].SizeFlagsStretchRatio;
                    var finalPixelSize = stretchSpace * ratio / stretchRatioTotal;
                    error += finalPixelSize - MathF.Floor(finalPixelSize);
                    if (error >= 1) { finalPixelSize += 1; error -= 1; }
                    if (finalPixelSize < minSizes[i])
                    {
                        willStretch[i] = false;
                        stretchRatioTotal -= ratio;
                        stretchSpace -= minSizes[i];
                        refitSuccessful = false;
                        break;
                    }
                    finalSizes[i] = finalPixelSize;
                }
                if (refitSuccessful) break;
            }

            var finalStretchDiff = maxSpace - combinedMin;
            for (var i = 0; i < count; i++) finalStretchDiff -= finalSizes[i] - minSizes[i];
            if (finalStretchDiff < 0) finalStretchDiff = 0;

            var rtl = IsLayoutRtl();
            var cursor = 0f;
            if (Alignment == BoxAlignment.Center) cursor = finalStretchDiff / 2;
            else if (Alignment == BoxAlignment.End ^ (Orientation == Orientation.Horizontal && rtl)) cursor = finalStretchDiff;

            for (var i = 0; i < count; i++)
            {
                var child = children[i];
                var min = child.GetMinimumSize();
                var main = finalSizes[i];
                // Godot's fit_child_in_rect also honors the perpendicular-axis size flag: a non-Fill
                // cross-axis child is sized to its own minimum and aligned within the cross-axis span
                // instead of being stretched to fill it. Horizontal (X) alignment flips under RTL;
                // vertical (Y) alignment never does, matching Container::fit_child_in_rect exactly.
                var crossFlags = Orientation == Orientation.Horizontal ? child.VerticalSizeFlags : child.HorizontalSizeFlags;
                var crossAvailable = Math.Max(0, Orientation == Orientation.Horizontal ? Size.Y - child.Margins.Vertical : Size.X - child.Margins.Horizontal);
                var crossMin = Orientation == Orientation.Horizontal ? min.Y : min.X;
                var cross = (crossFlags & SizeFlags.Fill) != 0 ? crossAvailable : Math.Min(crossAvailable, crossMin);
                var crossRtlSensitive = Orientation == Orientation.Vertical;
                var crossOffset = (crossFlags & SizeFlags.ShrinkEnd) != 0 ? (crossRtlSensitive && rtl ? 0 : crossAvailable - cross)
                    : (crossFlags & SizeFlags.ShrinkCenter) != 0 ? MathF.Floor((crossAvailable - cross) / 2)
                    : (crossRtlSensitive && rtl ? crossAvailable - cross : 0);
                if (Orientation == Orientation.Horizontal)
                {
                    child.Position = new Vector2(cursor + child.Margins.Left, child.Margins.Top + crossOffset);
                    child.Size = new Vector2(Math.Max(0, main - child.Margins.Horizontal), cross);
                }
                else
                {
                    child.Position = new Vector2(child.Margins.Left + crossOffset, cursor + child.Margins.Top);
                    child.Size = new Vector2(cross, Math.Max(0, main - child.Margins.Vertical));
                }
                cursor += main + gap;
            }
        }
    }
    /// <summary>Arranges visible children from left to right, or right to left under RTL layout.</summary>
    public sealed class HBoxContainer : BoxContainer { public HBoxContainer() : base(Orientation.Horizontal) { } }
    /// <summary>Arranges visible children from top to bottom.</summary>
    public sealed class VBoxContainer : BoxContainer { public VBoxContainer() : base(Orientation.Vertical) { } }

    /// <summary>Insets each visible child by configurable theme margins.</summary>
    public sealed class MarginContainer : Container
    {
        public Thickness ThemeOverrides { get; set; } = new Thickness(4);
        public override Vector2 GetMinimumSize()
        {
            var size = CustomMinimumSize;
            foreach (var child in VisualChildren) if (child.Visible) size = Vector2.Max(size, child.GetMinimumSize() + new Vector2(ThemeOverrides.Horizontal, ThemeOverrides.Vertical));
            return size;
        }
        protected override void ArrangeChildren()
        {
            var rtl = IsLayoutRtl();
            var rectPosition = new Vector2(ThemeOverrides.Left, ThemeOverrides.Top);
            var rectSize = Vector2.Max(Vector2.Zero, Size - new Vector2(ThemeOverrides.Horizontal, ThemeOverrides.Vertical));
            foreach (var child in VisualChildren)
                FitChildInRect(child, rectPosition, rectSize, rtl);
        }
    }

    /// <summary>Sizes visible children to their minimum size and centers them within the available area.</summary>
    public sealed class CenterContainer : Container
    {
        private bool _useTopLeft;
        /// <summary>Uses Godot's origin-centered placement (-child size / 2) and suppresses child minimum contribution.</summary>
        public bool UseTopLeft { get => _useTopLeft; set { _useTopLeft = value; QueueLayout(); } }
        public override Vector2 GetMinimumSize()
        {
            if (UseTopLeft) return CustomMinimumSize;
            var minimum = CustomMinimumSize;
            foreach (var child in Children) if (child.Visible) minimum = Vector2.Max(minimum, child.GetMinimumSize());
            return minimum;
        }
        protected override void ArrangeChildren()
        {
            foreach (var child in Children)
            {
                if (!child.Visible) continue;
                var childSize = child.GetMinimumSize();
                child.Size = childSize;
                // Godot's CenterContainer::_notification explicitly floors this offset (Vector2::floor,
                // which rounds toward negative infinity like MathF.Floor); a bare float division left an
                // unfloored fractional pixel position whenever the size difference was odd.
                var offset = UseTopLeft ? -childSize / 2 : (Size - childSize) / 2;
                child.Position = new Vector2(MathF.Floor(offset.X), MathF.Floor(offset.Y));
            }
        }
    }

    /// <summary>Arranges visible children into a fixed-column grid with expandable rows and columns.</summary>
    public sealed class GridContainer : Container
    {
        private int _columns = 1;
        public int Columns { get => _columns; set { _columns = Math.Max(1, value); QueueLayout(); } }
        public float HorizontalSeparation { get; set; } = float.NaN;
        public float VerticalSeparation { get; set; } = float.NaN;
        public override Vector2 GetMinimumSize()
        {
            GetGridMetrics(out var children, out var columnWidths, out var rowHeights, out var hGap, out var vGap, out _, out _);
            if (children.Count == 0) return CustomMinimumSize;
            var width = hGap * Math.Max(0, columnWidths.Length - 1);
            var height = vGap * Math.Max(0, rowHeights.Length - 1);
            foreach (var value in columnWidths) width += value;
            foreach (var value in rowHeights) height += value;
            return Vector2.Max(CustomMinimumSize, new Vector2(width, height));
        }
        protected override void ArrangeChildren()
        {
            GetGridMetrics(out var children, out var columnWidths, out var rowHeights, out var hGap, out var vGap, out var expandColumns, out var expandRows);
            if (children.Count == 0) return;
            DistributeExpand(columnWidths, expandColumns, Size.X, hGap);
            DistributeExpand(rowHeights, expandRows, Size.Y, vGap);
            var columnOffsets = GetOffsets(columnWidths, hGap);
            var rowOffsets = GetOffsets(rowHeights, vGap);
            var rtl = IsLayoutRtl();
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i]; var row = i / Columns; var column = i % Columns;
                // Godot lays RTL columns out from the right edge inward; mirroring each column's cell
                // within the container width reproduces that without reversing column order.
                var cellX = rtl ? Size.X - columnOffsets[column] - columnWidths[column] : columnOffsets[column];
                var cell = new Rectangle((int)MathF.Round(cellX), (int)MathF.Round(rowOffsets[row]), (int)MathF.Round(columnWidths[column]), (int)MathF.Round(rowHeights[row]));
                var minimum = child.GetMinimumSize();
                var width = (child.HorizontalSizeFlags & SizeFlags.Fill) != 0 ? cell.Width : Math.Min(cell.Width, (int)MathF.Round(minimum.X));
                var height = (child.VerticalSizeFlags & SizeFlags.Fill) != 0 ? cell.Height : Math.Min(cell.Height, (int)MathF.Round(minimum.Y));
                var x = cell.X + Align(cell.Width, width, child.HorizontalSizeFlags);
                var y = cell.Y + Align(cell.Height, height, child.VerticalSizeFlags);
                child.Position = new Vector2(x + child.Margins.Left, y + child.Margins.Top);
                child.Size = new Vector2(Math.Max(0, width - child.Margins.Horizontal), Math.Max(0, height - child.Margins.Vertical));
            }
        }
        private void GetGridMetrics(out List<Control> children, out float[] columnWidths, out float[] rowHeights, out float hGap, out float vGap, out bool[] expandColumns, out bool[] expandRows)
        {
            children = new List<Control>(); foreach (var child in Children) if (child.Visible) children.Add(child);
            var columnCount = Math.Min(Columns, Math.Max(1, children.Count));
            var rowCount = Math.Max(1, (int)Math.Ceiling(children.Count / (float)Columns));
            columnWidths = new float[columnCount]; rowHeights = new float[rowCount]; expandColumns = new bool[columnCount]; expandRows = new bool[rowCount];
            hGap = float.IsNaN(HorizontalSeparation) ? Context?.Theme.Separation ?? 4 : HorizontalSeparation;
            vGap = float.IsNaN(VerticalSeparation) ? Context?.Theme.Separation ?? 4 : VerticalSeparation;
            for (var i = 0; i < children.Count; i++)
            {
                var row = i / Columns; var column = i % Columns; var child = children[i]; var minimum = child.GetMinimumSize() + new Vector2(child.Margins.Horizontal, child.Margins.Vertical);
                columnWidths[column] = Math.Max(columnWidths[column], minimum.X); rowHeights[row] = Math.Max(rowHeights[row], minimum.Y);
                expandColumns[column] |= (child.HorizontalSizeFlags & SizeFlags.Expand) != 0;
                expandRows[row] |= (child.VerticalSizeFlags & SizeFlags.Expand) != 0;
            }
        }
        // Matches Godot's GridContainer::_resort: every expanded column/row receives the SAME final
        // size (remaining_space / expanded_count), not its own minimum plus an equal share of the
        // extra. Any expanded entry whose minimum exceeds that equal share is pinned to its minimum
        // and dropped from the pool (the entry with the single largest minimum each pass, since if
        // the share can't fit it, it can't fit any smaller minimum either), then the remaining space
        // is redivided among what's left - repeat until a share fits everyone still in the pool.
        private static void DistributeExpand(float[] sizes, bool[] expanded, float totalSize, float gap)
        {
            if (sizes.Length == 0) return;
            var pool = new List<int>();
            var nonExpandedSum = 0f;
            for (var i = 0; i < sizes.Length; i++)
            {
                if (expanded[i]) pool.Add(i);
                else nonExpandedSum += sizes[i];
            }
            if (pool.Count == 0) { for (var i = 0; i < expanded.Length; i++) { expanded[i] = true; pool.Add(i); } nonExpandedSum = 0; }
            var remaining = totalSize - nonExpandedSum - gap * Math.Max(0, sizes.Length - 1);
            while (pool.Count > 0)
            {
                var share = remaining / pool.Count;
                var maxIndex = pool[0];
                foreach (var i in pool) if (sizes[i] > sizes[maxIndex]) maxIndex = i;
                if (share >= sizes[maxIndex]) break;
                pool.Remove(maxIndex);
                remaining -= sizes[maxIndex];
            }
            if (pool.Count > 0)
            {
                var finalShare = remaining / pool.Count;
                foreach (var i in pool) sizes[i] = finalShare;
            }
        }
        private static float[] GetOffsets(float[] sizes, float gap) { var offsets = new float[sizes.Length]; for (var i = 1; i < sizes.Length; i++) offsets[i] = offsets[i - 1] + sizes[i - 1] + gap; return offsets; }
        private static int Align(int available, int size, SizeFlags flags) => (flags & SizeFlags.ShrinkEnd) != 0 ? available - size : (flags & SizeFlags.ShrinkCenter) != 0 ? (available - size) / 2 : 0;
    }
}
