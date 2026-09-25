// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// GraphEditMinimap transforms and input behavior are adapted from Godot Engine's
// scene/gui/graph_edit.cpp; see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Forma
{
    /// <summary>Minimap overlay for a <see cref="GraphEdit"/>; its bounds can be driven by graph-node positions.</summary>
    public sealed class GraphEditMinimap : Control
    {
        private readonly struct MinimapTransform
        {
            public MinimapTransform(Vector2 graphOffset, Vector2 graphPadding, Vector2 graphProportions, Vector2 renderSize, Vector2 minimapOffset)
            {
                GraphOffset = graphOffset; GraphPadding = graphPadding; GraphProportions = graphProportions; RenderSize = renderSize; MinimapOffset = minimapOffset;
            }
            public Vector2 GraphOffset { get; }
            public Vector2 GraphPadding { get; }
            public Vector2 GraphProportions { get; }
            public Vector2 RenderSize { get; }
            public Vector2 MinimapOffset { get; }
        }
        private const int MinimapPadding = 5;
        private const int ResizeHandleSize = 12;
        private bool _panning;
        private bool _resizing;
        private Point _lastPointerPosition;
        public GraphEditMinimap() => Opacity = .65f;
        public GraphEdit Graph { get; set; }
        public Color NodeColor { get; set; } = new Color(112, 178, 255);
        public Color ViewportColor { get; set; } = new Color(255, 255, 255, 120);
        /// <summary>Screen-space bounds of Godot's top-left minimap resize handle.</summary>
        public Rectangle GetResizeHandleBounds() => new Rectangle(Bounds.X, Bounds.Y, ResizeHandleSize, ResizeHandleSize);
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, new Vector2(120, 80));
        public Rectangle GetNodeBounds(GraphNode node)
        {
            if (node == null || Graph == null || node.Parent != Graph) return Rectangle.Empty;
            var transform = GetMinimapTransform();
            var position = ConvertFromGraphPosition(node.Position * Graph.Zoom - transform.GraphOffset, transform) + transform.MinimapOffset + new Vector2(Bounds.X, Bounds.Y);
            var size = ConvertFromGraphPosition(node.Size * Graph.Zoom, transform);
            return new Rectangle((int)position.X, (int)position.Y, Math.Max(2, (int)size.X), Math.Max(2, (int)size.Y));
        }
        public Vector2 MapGraphPoint(Vector2 graphPoint)
        {
            if (Graph == null) return new Vector2(Bounds.X, Bounds.Y);
            var transform = GetMinimapTransform();
            return ConvertFromGraphPosition(graphPoint * Graph.Zoom - transform.GraphOffset, transform) + transform.MinimapOffset + new Vector2(Bounds.X, Bounds.Y);
        }
        /// <summary>Screen-space camera rectangle, equivalent to Godot's <c>GraphEditMinimap::get_camera_rect()</c>.</summary>
        public Rectangle GetCameraBounds()
        {
            if (Graph == null) return Rectangle.Empty;
            var transform = GetMinimapTransform();
            var cameraPosition = Graph.ScrollOffset - transform.GraphOffset;
            var cameraCenter = ConvertFromGraphPosition(cameraPosition + Graph.Size * .5f, transform) + transform.MinimapOffset + new Vector2(Bounds.X, Bounds.Y);
            var cameraSize = ConvertFromGraphPosition(Graph.Size, transform);
            var position = cameraCenter - cameraSize * .5f;
            return new Rectangle((int)position.X, (int)position.Y, Math.Max(1, (int)cameraSize.X), Math.Max(1, (int)cameraSize.Y));
        }
        public IReadOnlyList<Vector2> GetConnectionLinePoints(GraphConnection connection)
        {
            var points = new List<Vector2>();
            if (!TryGetConnectionEndpoints(connection, out var from, out var to)) return points;
            foreach (var point in Graph.GetConnectionLinePoints(from, to)) points.Add(MapGraphPoint(point));
            return points;
        }
        public (Color From, Color To) GetConnectionLineColors(GraphConnection connection, Theme theme) => Graph == null ? (Color.Transparent, Color.Transparent) : Graph.GetConnectionLineColors(connection, theme);
        protected internal override void PointerPressed(Point position)
        {
            base.PointerPressed(position);
            _lastPointerPosition = position;
            _resizing = GetResizeHandleBounds().Contains(position);
            _panning = !_resizing;
            if (_panning) PanGraphTo(position);
        }
        protected internal override void PointerMoved(Point position)
        {
            if (_resizing && Graph != null)
            {
                var relative = new Vector2(position.X - _lastPointerPosition.X, position.Y - _lastPointerPosition.Y);
                var maximum = Vector2.Max(Vector2.Zero, Graph.Size - new Vector2(MinimapPadding * 2));
                Graph.MinimapSize = Vector2.Min(Size - relative, maximum);
                _lastPointerPosition = position;
            }
            else if (_panning) PanGraphTo(position);
        }
        protected internal override void PointerReleased(Point position, bool isInside) { _panning = false; _resizing = false; }
        internal override void CancelInput() { _panning = false; _resizing = false; base.CancelInput(); }
        internal override void Draw(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor);
            context.Border(Bounds, context.Theme.PanelBorderColor);
            if (Graph != null)
            {
                foreach (var connection in Graph.Connections)
                {
                    var points = GetConnectionLinePoints(connection);
                    if (points.Count < 2) continue;
                    var colors = GetConnectionLineColors(connection, context.Theme);
                    DrawConnection(context, points, colors.From, colors.To);
                }
                foreach (var child in Graph.Children)
                    if (child is GraphNode node && node.Visible) context.Fill(GetNodeBounds(node), NodeColor);
                context.Border(GetCameraBounds(), ViewportColor);
            }
            var resizeHandle = GetResizeHandleBounds();
            var resizeColor = context.Theme.PanelBorderColor;
            for (var offset = 3; offset <= 9; offset += 3)
                context.Fill(new Rectangle(resizeHandle.X + offset - 1, resizeHandle.Y, 1, 12 - offset), resizeColor);
            base.Draw(context);
        }
        private void PanGraphTo(Point position)
        {
            if (Graph == null) return;
            var transform = GetMinimapTransform();
            var local = new Vector2(position.X - Bounds.X - MinimapPadding, position.Y - Bounds.Y - MinimapPadding);
            var graphPoint = ConvertToGraphPosition(local, transform) - transform.GraphPadding;
            Graph.ScrollOffset = graphPoint + transform.GraphOffset - Graph.Size * .5f;
        }
        private MinimapTransform GetMinimapTransform()
        {
            var first = true;
            var bounds = default(RectangleF);
            foreach (var child in Graph.Children)
            {
                if (child is not GraphElement element) continue;
                var rect = new RectangleF(element.Position.X * Graph.Zoom, element.Position.Y * Graph.Zoom, element.Size.X * Graph.Zoom, element.Size.Y * Graph.Zoom);
                bounds = first ? rect : RectangleF.Union(bounds, rect);
                first = false;
            }
            if (first) bounds = new RectangleF(0, 0, 0, 0);
            var graphOffset = new Vector2(bounds.X, bounds.Y) - Graph.Size;
            var graphSize = new Vector2(Math.Max(1, bounds.Width + Graph.Size.X * 2), Math.Max(1, bounds.Height + Graph.Size.Y * 2));
            var renderSize = Vector2.Max(Vector2.One, Size - new Vector2(MinimapPadding * 2));
            var targetRatio = renderSize.X / renderSize.Y;
            var graphRatio = graphSize.X / graphSize.Y;
            var graphProportions = graphSize;
            var graphPadding = Vector2.Zero;
            if (graphRatio > targetRatio)
            {
                graphProportions.Y = graphSize.X / targetRatio;
                graphPadding.Y = Math.Abs(graphSize.Y - graphProportions.Y) * .5f;
            }
            else
            {
                graphProportions.X = graphSize.Y * targetRatio;
                graphPadding.X = Math.Abs(graphSize.X - graphProportions.X) * .5f;
            }
            var transform = new MinimapTransform(graphOffset, graphPadding, graphProportions, renderSize, Vector2.Zero);
            var minimapOffset = new Vector2(MinimapPadding) + ConvertFromGraphPosition(graphPadding, transform);
            return new MinimapTransform(graphOffset, graphPadding, graphProportions, renderSize, minimapOffset);
        }
        private static Vector2 ConvertFromGraphPosition(Vector2 position, MinimapTransform transform) => new Vector2(position.X * transform.RenderSize.X / transform.GraphProportions.X, position.Y * transform.RenderSize.Y / transform.GraphProportions.Y);
        private static Vector2 ConvertToGraphPosition(Vector2 position, MinimapTransform transform) => new Vector2(position.X * transform.GraphProportions.X / transform.RenderSize.X, position.Y * transform.GraphProportions.Y / transform.RenderSize.Y);
        private bool TryGetConnectionEndpoints(GraphConnection connection, out Vector2 from, out Vector2 to)
        {
            from = Vector2.Zero; to = Vector2.Zero;
            if (Graph == null) return false;
            GraphNode fromNode = null, toNode = null;
            foreach (var child in Graph.Children)
            {
                if (child is not GraphNode node) continue;
                if (node.Name == connection.FromNode) fromNode = node;
                if (node.Name == connection.ToNode) toNode = node;
            }
            if (fromNode == null || toNode == null || connection.FromPort < 0 || connection.FromPort >= fromNode.OutputPortCount || connection.ToPort < 0 || connection.ToPort >= toNode.InputPortCount) return false;
            from = fromNode.Position + fromNode.GetOutputPortPosition(connection.FromPort);
            to = toNode.Position + toNode.GetInputPortPosition(connection.ToPort);
            return true;
        }
        private static void DrawConnection(UIRenderContext context, IReadOnlyList<Vector2> points, Color fromColor, Color toColor)
        {
            for (var segment = 1; segment < points.Count; segment++)
            {
                var previous = points[segment - 1]; var current = points[segment];
                var delta = current - previous; var length = Math.Max(1, (int)MathF.Round(delta.Length()));
                var angle = MathF.Atan2(delta.Y, delta.X);
                var color = Color.Lerp(fromColor, toColor, (segment - .5f) / (points.Count - 1));
                context.SpriteBatch.Draw(context.Pixel, new Rectangle((int)MathF.Round(previous.X), (int)MathF.Round(previous.Y), length, 1), null, color, angle, Vector2.Zero, SpriteEffects.None, 0);
            }
        }
    }

    /// <summary>Input filter overlay used by graph editors to consume canvas gestures without affecting nodes underneath.</summary>
    public sealed class GraphEditFilter : Control
    {
        public GraphEditFilter()
        {
            MouseFilter = MouseFilter.Stop;
            FocusMode = FocusMode.All;
        }
        public event Action<GraphEditFilter, Point> CanvasPressed;
        public event Action<GraphEditFilter, Point> CanvasReleased;
        protected internal override void PointerPressed(Point position) { base.PointerPressed(position); CanvasPressed?.Invoke(this, position); }
        protected internal override void PointerReleased(Point position, bool isInside) { CanvasReleased?.Invoke(this, position); }
    }

    /// <summary>Visual drag handle that forwards pointer movement to a split container.</summary>
    public sealed class SplitContainerDragger : Control
    {
        private bool _dragging;
        public SplitContainer Target { get; set; }
        /// <summary>The whole handle is a divider, so it advertises its target's resize axis unless a host
        /// sets <see cref="Control.Cursor"/> explicitly.</summary>
        public override Cursor GetCursorAt(Point position) =>
            Cursor != Cursor.Inherited || Target == null ? base.GetCursorAt(position)
                : Target.Orientation == Orientation.Horizontal ? Cursor.SizeHorizontal : Cursor.SizeVertical;
        protected internal override void PointerPressed(Point position) { _dragging = Target != null; GrabFocus(); }
        protected internal override void PointerMoved(Point position)
        {
            if (!_dragging || Target == null) return;
            Target.SplitOffset = Target.Orientation == Orientation.Horizontal ? position.X - Target.Bounds.X : position.Y - Target.Bounds.Y;
        }
        protected internal override void PointerReleased(Point position, bool isInside) { _dragging = false; }
        internal override void CancelInput() { _dragging = false; base.CancelInput(); }
    }

    /// <summary>Dragger capable of selecting among multiple split handles.</summary>
    public sealed class SplitContainerMultiDragger : Control
    {
        private readonly System.Collections.Generic.List<SplitContainer> _targets = new System.Collections.Generic.List<SplitContainer>();
        public System.Collections.Generic.IList<SplitContainer> Targets => _targets;
        public int ActiveIndex { get; set; }
        private bool _dragging;
        /// <summary>Advertises the resize axis of the handle currently selected by <see cref="ActiveIndex"/>.</summary>
        public override Cursor GetCursorAt(Point position) =>
            Cursor != Cursor.Inherited || ActiveIndex < 0 || ActiveIndex >= _targets.Count ? base.GetCursorAt(position)
                : _targets[ActiveIndex].Orientation == Orientation.Horizontal ? Cursor.SizeHorizontal : Cursor.SizeVertical;
        protected internal override void PointerPressed(Point position) { _dragging = ActiveIndex >= 0 && ActiveIndex < _targets.Count; GrabFocus(); }
        protected internal override void PointerMoved(Point position)
        {
            if (!_dragging) return;
            var target = _targets[ActiveIndex];
            target.SplitOffset = target.Orientation == Orientation.Horizontal ? position.X - target.Bounds.X : position.Y - target.Bounds.Y;
        }
        protected internal override void PointerReleased(Point position, bool isInside) { _dragging = false; }
        internal override void CancelInput() { _dragging = false; base.CancelInput(); }
    }

    /// <summary>Lightweight floating-point rectangle used by retained UI geometry queries.</summary>
    public readonly struct RectangleF : IEquatable<RectangleF>
    {
        public RectangleF(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public float Left => X;
        public float Top => Y;
        public float Right => X + Width;
        public float Bottom => Y + Height;
        public bool Contains(Vector2 point) => point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
        public bool Intersects(RectangleF other) => other.Left <= Right && other.Right >= Left && other.Top <= Bottom && other.Bottom >= Top;
        public static RectangleF Union(RectangleF a, RectangleF b)
        {
            var left = Math.Min(a.X, b.X); var top = Math.Min(a.Y, b.Y);
            var right = Math.Max(a.X + a.Width, b.X + b.Width); var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new RectangleF(left, top, right - left, bottom - top);
        }
        public bool Equals(RectangleF other) => X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);
        public override bool Equals(object obj) => obj is RectangleF other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    }
}
