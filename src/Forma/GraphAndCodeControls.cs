// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// GraphElement, GraphNode, GraphFrame, GraphEdit, GraphEditArranger, and CodeEdit APIs
// and behavior are adapted from their Godot Engine implementations under scene/gui;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Base movable graph element, corresponding to Godot's GraphElement.</summary>
    public class GraphElement : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Group;
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions |
            (Selectable ? AccessibilityActions.Select : AccessibilityActions.None);
        public override AccessibilityStates AccessibilityStates => base.AccessibilityStates |
            (Selected ? AccessibilityStates.Selected : AccessibilityStates.None);
        private bool _dragging;
        private Vector2 _dragOffset;
        private Vector2 _positionOffset;
        private Vector2 _graphSize;
        private bool _resizing;
        private Vector2 _resizeFrom;
        private Vector2 _resizeFromSize;
        private bool _hasGraphPosition;
        private bool _hasGraphSize;
        private bool _selected;
        private bool _selectable = true;
        private Vector2 _dragFrom;
        public GraphElement() { FocusMode = FocusMode.All; }
        public bool Draggable { get; set; } = true;
        /// <summary>Whether this element may be selected, equivalent to Godot's selectable property.</summary>
        public bool Selectable { get => _selectable; set => SetSelectable(value); }
        /// <summary>Retained state for Godot's scaling_menus flag.</summary>
        public bool ScalingMenus { get; set; }
        /// <summary>Whether the element exposes Godot's bottom-right resize handle.</summary>
        public bool Resizable { get; set; }
        /// <summary>Whether a retained resize gesture is currently active.</summary>
        public bool IsResizing => _resizing;
        public bool Selected { get => _selected; internal set => SetSelected(value); }
        /// <summary>Position in graph coordinates, equivalent to Godot's position_offset.</summary>
        public Vector2 PositionOffset { get => Position; set => Position = value; }
        /// <summary>Logical graph position. The base control position is transformed only for viewport rendering.</summary>
        public new Vector2 Position
        {
            get => _hasGraphPosition ? _positionOffset : base.Position;
            set
            {
                if (_hasGraphPosition && _positionOffset == value) return;
                _positionOffset = value; _hasGraphPosition = true; base.Position = value; PositionChanged?.Invoke(this);
            }
        }
        /// <summary>Logical graph size. The rendered base-control size is scaled by the parent GraphEdit zoom.</summary>
        public new Vector2 Size
        {
            get => _hasGraphSize ? _graphSize : base.Size;
            set { _graphSize = Vector2.Max(Vector2.Zero, value); _hasGraphSize = true; base.Size = _graphSize; }
        }
        public event Action<GraphElement> PositionChanged;
        public event Action<GraphElement, Vector2> ResizeRequest;
        public event Action<GraphElement, Vector2> ResizeEnd;
        public event Action<GraphElement> NodeSelected;
        public event Action<GraphElement> NodeDeselected;
        public event Action<GraphElement> RaiseRequest;
        public event Action<GraphElement, Vector2, Vector2> Dragged;
        public void SetDraggable(bool draggable) => Draggable = draggable;
        public bool IsDraggable() => Draggable;
        public void SetResizable(bool resizable) => Resizable = resizable;
        public bool IsResizable() => Resizable;
        public void SetSelectable(bool selectable)
        {
            if (!selectable) SetSelected(false);
            _selectable = selectable;
        }
        public bool IsSelectable() => Selectable;
        public void SetSelected(bool selected)
        {
            if (!Selectable || _selected == selected) return;
            _selected = selected;
            if (selected) NodeSelected?.Invoke(this);
            else NodeDeselected?.Invoke(this);
        }
        public bool IsSelected() => Selected;
        public void SetScalingMenus(bool scalingMenus) => ScalingMenus = scalingMenus;
        public bool IsScalingMenus() => ScalingMenus;
        public void SetDrag(bool drag)
        {
            if (drag) _dragFrom = PositionOffset;
            else Dragged?.Invoke(this, _dragFrom, PositionOffset);
        }
        public Vector2 GetDragFrom() => _dragFrom;
        public Rectangle GetResizeHandleBounds()
        {
            if (!Resizable) return Rectangle.Empty;
            var size = Math.Max(8, (int)MathF.Round(12 * (Parent is GraphEdit graph ? graph.Zoom : 1f)));
            return new Rectangle(Bounds.Right - size, Bounds.Bottom - size, size, size);
        }
        protected internal override void PointerPressed(Point position)
        {
            base.PointerPressed(position);
            if (Resizable && GetResizeHandleBounds().Contains(position))
            {
                _resizing = true;
                _dragging = false;
                _resizeFrom = GetPointerGraphPosition(position);
                _resizeFromSize = Size;
                return;
            }
            RaiseRequest?.Invoke(this);
            if (Parent is GraphEdit parentGraph) parentGraph.RaiseElement(this);
            if (!Draggable) return;
            SetDrag(true);
            _dragging = true;
            _dragOffset = Parent is GraphEdit graph ? graph.ScreenToGraph(new Vector2(position.X, position.Y) - graph.GlobalPosition) - Position : new Vector2(position.X, position.Y) - GlobalPosition;
            if (Parent is GraphEdit dragGraph) dragGraph.BeginGraphElementDrag(this);
        }
        protected internal override void PointerMoved(Point position)
        {
            if (_resizing)
            {
                var requestedSize = _resizeFromSize + GetPointerGraphPosition(position) - _resizeFrom;
                ResizeRequest?.Invoke(this, requestedSize);
                if (Parent is GraphEdit parentGraph) parentGraph.ApplyGraphElementResizeRequest(this, requestedSize);
                else Size = Vector2.Max(GetMinimumSize(), requestedSize);
                return;
            }
            if (!_dragging || !Draggable) return;
            if (Parent is GraphEdit graph)
            {
                var graphPosition = graph.ScreenToGraph(new Vector2(position.X, position.Y) - graph.GlobalPosition) - _dragOffset;
                if (!graph.UpdateGraphElementDrag(this, graphPosition)) Position = graph.SnappingEnabled ? graph.SnapPosition(graphPosition) : graphPosition;
            }
            else Position = new Vector2(position.X, position.Y) - _dragOffset - (Parent?.GlobalPosition ?? Vector2.Zero);
        }
        protected internal override void PointerReleased(Point position, bool isInside)
        {
            if (_resizing)
            {
                _resizing = false;
                ResizeEnd?.Invoke(this, Size);
            }
            var moved = _dragging && _dragFrom != PositionOffset;
            if (_dragging) SetDrag(false);
            _dragging = false;
            if (Parent is GraphEdit graph) graph.EndGraphElementDrag(this, moved, position);
        }
        internal override void CancelInput()
        {
            _dragging = false;
            _resizing = false;
            if (Parent is GraphEdit graph)
            {
                graph.CancelGraphElementDrag(this);
                if (this is GraphNode node && graph.IsConnectionDragSource(node)) graph.ForceConnectionDragEnd();
            }
            base.CancelInput();
        }
        internal void ApplyViewportTransform(float zoom, Vector2 scrollOffset)
        {
            if (!_hasGraphPosition) { _positionOffset = base.Position; _hasGraphPosition = true; }
            if (!_hasGraphSize) { _graphSize = base.Size; _hasGraphSize = true; }
            base.Position = _positionOffset * zoom - scrollOffset;
            base.Size = _graphSize * zoom;
        }
        protected void DrawResizeHandle(UIRenderContext context)
        {
            var bounds = GetResizeHandleBounds();
            if (bounds == Rectangle.Empty) return;
            var color = Selected || IsResizing ? context.Theme.FocusColor : context.Theme.PanelBorderColor;
            for (var offset = 3; offset <= bounds.Width - 3; offset += 4)
            {
                context.Fill(new Rectangle(bounds.Right - offset, bounds.Bottom - 2, Math.Min(offset, 2), 2), color);
                context.Fill(new Rectangle(bounds.Right - 2, bounds.Bottom - offset, 2, Math.Min(offset, 2)), color);
            }
        }
        internal virtual void DrawGraphElementChrome(UIRenderContext context) => DrawResizeHandle(context);
        protected static void FitChildInRect(Control child, Vector2 position, Vector2 size, bool rtl) => Container.FitChildInRect(child, position, size, rtl);
        private Vector2 GetPointerGraphPosition(Point position)
        {
            if (Parent is GraphEdit graph) return graph.ScreenToGraph(new Vector2(position.X, position.Y) - graph.GlobalPosition);
            return new Vector2(position.X, position.Y) - (Parent?.GlobalPosition ?? Vector2.Zero);
        }
    }

    /// <summary>Graph node with named input and output port metadata.</summary>
    public class GraphNode : GraphElement
    {
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private sealed class SlotLayout
        {
            public Control Child;
            public int Slot;
            public float Minimum;
            public float Maximum;
            public float Final;
            public float Ratio;
            public bool Stretching;
            public Thickness StyleMargin;
        }
        private sealed class SlotState
        {
            public bool EnableLeft;
            public bool EnableRight;
            public int TypeLeft;
            public int TypeRight;
            public Color ColorLeft = Color.White;
            public Color ColorRight = Color.White;
            public Texture2D CustomIconLeft;
            public Texture2D CustomIconRight;
            public object MetadataLeft;
            public object MetadataRight;
            public bool DrawStyleBox = true;
        }
        private sealed class Port
        {
            public int Slot;
            public string Name;
            public int Type;
            public Color Color;
            public Texture2D Icon;
        }
        private readonly Dictionary<int, SlotState> _slots = new Dictionary<int, SlotState>();
        private readonly List<Port> _inputPorts = new List<Port>();
        private readonly List<Port> _outputPorts = new List<Port>();
        private readonly Dictionary<int, float> _slotCenterCache = new Dictionary<int, float>();
        private readonly Dictionary<int, Rectangle> _slotBoundsCache = new Dictionary<int, Rectangle>();
        private string _title = string.Empty;
        public GraphNode() { CustomMinimumSize = new Vector2(140, 80); }
        public string Title { get => _title; set { value ??= string.Empty; if (_title == value) return; _title = value; QueueLayout(); } }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        /// <summary>Allows this node to accept an interactive connection whose type is not otherwise compatible.</summary>
        public bool IgnoreInvalidConnectionType { get; set; }
        public IReadOnlyList<string> InputPorts => GetPortNames(_inputPorts);
        public IReadOnlyList<string> OutputPorts => GetPortNames(_outputPorts);
        public int InputPortCount => _inputPorts.Count;
        public int OutputPortCount => _outputPorts.Count;
        public void AddInputPort(string name, int type = 0, Color? color = null)
        {
            var slot = NextSlot();
            SetSlot(slot, true, type, color ?? Color.White, false, 0, Color.White);
            GetPort(_inputPorts, InputPortCount - 1).Name = name ?? string.Empty;
        }
        public void AddOutputPort(string name, int type = 0, Color? color = null)
        {
            var slot = NextSlot();
            SetSlot(slot, false, 0, Color.White, true, type, color ?? Color.White);
            GetPort(_outputPorts, OutputPortCount - 1).Name = name ?? string.Empty;
        }
        /// <summary>Configures Godot's paired left/input and right/output port slot.</summary>
        public void SetSlot(int slot, bool enableLeft, int typeLeft, Color colorLeft, bool enableRight, int typeRight, Color colorRight) => SetSlot(slot, enableLeft, typeLeft, colorLeft, enableRight, typeRight, colorRight, null, null, true);
        /// <summary>Configures Godot's paired left/input and right/output port slot, including retained custom icons and slot stylebox policy.</summary>
        public void SetSlot(int slot, bool enableLeft, int typeLeft, Color colorLeft, bool enableRight, int typeRight, Color colorRight, Texture2D customIconLeft, Texture2D customIconRight, bool drawStyleBox = true)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            if (!enableLeft && !enableRight) { ClearSlot(slot); return; }
            var state = GetOrCreateSlot(slot);
            state.EnableLeft = enableLeft; state.TypeLeft = typeLeft; state.ColorLeft = colorLeft; state.CustomIconLeft = customIconLeft;
            state.EnableRight = enableRight; state.TypeRight = typeRight; state.ColorRight = colorRight; state.CustomIconRight = customIconRight;
            state.DrawStyleBox = drawStyleBox;
            RebuildPortCaches();
            QueueLayout();
            SlotUpdated?.Invoke(this, slot);
        }
        public void ClearSlot(int slot)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            if (!_slots.Remove(slot)) return;
            RebuildPortCaches();
            QueueLayout();
            SlotUpdated?.Invoke(this, slot);
        }
        public void ClearAllSlots()
        {
            if (_slots.Count == 0 && _inputPorts.Count == 0 && _outputPorts.Count == 0) return;
            _slots.Clear(); _inputPorts.Clear(); _outputPorts.Clear();
            QueueLayout();
        }
        public bool IsSlotEnabledLeft(int slot) => TryGetSlot(slot, out var state) && state.EnableLeft;
        public bool IsSlotEnabledRight(int slot) => TryGetSlot(slot, out var state) && state.EnableRight;
        public void SetSlotEnabledLeft(int slot, bool enable)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            var state = GetOrCreateSlot(slot);
            if (state.EnableLeft == enable) return;
            state.EnableLeft = enable;
            RebuildPortCaches(); QueueLayout(); SlotUpdated?.Invoke(this, slot);
        }
        public void SetSlotEnabledRight(int slot, bool enable)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            var state = GetOrCreateSlot(slot);
            if (state.EnableRight == enable) return;
            state.EnableRight = enable;
            RebuildPortCaches(); QueueLayout(); SlotUpdated?.Invoke(this, slot);
        }
        public void SetSlotTypeLeft(int slot, int type) { var state = RequireSlot(slot); if (state.TypeLeft == type) return; state.TypeLeft = type; RebuildPortCaches(); SlotUpdated?.Invoke(this, slot); }
        public int GetSlotTypeLeft(int slot) => TryGetSlot(slot, out var state) ? state.TypeLeft : 0;
        public void SetSlotTypeRight(int slot, int type) { var state = RequireSlot(slot); if (state.TypeRight == type) return; state.TypeRight = type; RebuildPortCaches(); SlotUpdated?.Invoke(this, slot); }
        public int GetSlotTypeRight(int slot) => TryGetSlot(slot, out var state) ? state.TypeRight : 0;
        public void SetSlotColorLeft(int slot, Color color) { var state = RequireSlot(slot); if (state.ColorLeft == color) return; state.ColorLeft = color; RebuildPortCaches(); SlotUpdated?.Invoke(this, slot); }
        public Color GetSlotColorLeft(int slot) => TryGetSlot(slot, out var state) ? state.ColorLeft : Color.White;
        public void SetSlotColorRight(int slot, Color color) { var state = RequireSlot(slot); if (state.ColorRight == color) return; state.ColorRight = color; RebuildPortCaches(); SlotUpdated?.Invoke(this, slot); }
        public Color GetSlotColorRight(int slot) => TryGetSlot(slot, out var state) ? state.ColorRight : Color.White;
        public void SetSlotCustomIconLeft(int slot, Texture2D icon) { var state = RequireSlot(slot); if (state.CustomIconLeft == icon) return; state.CustomIconLeft = icon; RebuildPortCaches(); SlotUpdated?.Invoke(this, slot); }
        public Texture2D GetSlotCustomIconLeft(int slot) => TryGetSlot(slot, out var state) ? state.CustomIconLeft : null;
        public void SetSlotCustomIconRight(int slot, Texture2D icon) { var state = RequireSlot(slot); if (state.CustomIconRight == icon) return; state.CustomIconRight = icon; RebuildPortCaches(); SlotUpdated?.Invoke(this, slot); }
        public Texture2D GetSlotCustomIconRight(int slot) => TryGetSlot(slot, out var state) ? state.CustomIconRight : null;
        public void SetSlotMetadataLeft(int slot, object value) { var state = RequireSlot(slot); state.MetadataLeft = value; }
        public object GetSlotMetadataLeft(int slot) => TryGetSlot(slot, out var state) ? state.MetadataLeft : null;
        public void SetSlotMetadataRight(int slot, object value) { var state = RequireSlot(slot); state.MetadataRight = value; }
        public object GetSlotMetadataRight(int slot) => TryGetSlot(slot, out var state) ? state.MetadataRight : null;
        public bool IsSlotDrawStyleBox(int slot) => !TryGetSlot(slot, out var state) || state.DrawStyleBox;
        public void SetSlotDrawStyleBox(int slot, bool enable)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            var state = GetOrCreateSlot(slot);
            if (state.DrawStyleBox == enable) return;
            state.DrawStyleBox = enable;
            QueueLayout(); SlotUpdated?.Invoke(this, slot);
        }
        public int GetInputPortType(int port) => GetPort(_inputPorts, port).Type;
        public int GetOutputPortType(int port) => GetPort(_outputPorts, port).Type;
        public Color GetInputPortColor(int port) => GetPort(_inputPorts, port).Color;
        public Color GetOutputPortColor(int port) => GetPort(_outputPorts, port).Color;
        public int GetInputPortSlot(int port) => GetPort(_inputPorts, port).Slot;
        public int GetOutputPortSlot(int port) => GetPort(_outputPorts, port).Slot;
        public Texture2D GetInputPortIcon(int port) => GetPort(_inputPorts, port).Icon;
        public Texture2D GetOutputPortIcon(int port) => GetPort(_outputPorts, port).Icon;
        /// <summary>Returns the titlebar, slot, panel, and separation minimum required by Godot's GraphNode layout.</summary>
        public override Vector2 GetMinimumSize() => GetNodeSize(false);
        /// <summary>Returns the desired-size variant of the GraphNode layout requirement.</summary>
        public override Vector2 GetDesiredSize() => GetNodeSize(true);
        /// <summary>Port position in node-local coordinates.</summary>
        public Vector2 GetInputPortPosition(int port) => GetPortPosition(GetPort(_inputPorts, port), false);
        /// <summary>Port position in node-local coordinates.</summary>
        public Vector2 GetOutputPortPosition(int port) => GetPortPosition(GetPort(_outputPorts, port), true);
        /// <summary>Rendered bounds for a retained slot stylebox in screen coordinates, or an empty rectangle when disabled or absent.</summary>
        public Rectangle GetSlotStyleBoxBounds(int slot)
        {
            if (!TryGetSlot(slot, out var state) || !state.DrawStyleBox) return Rectangle.Empty;
            if (_slotBoundsCache.TryGetValue(slot, out var localBounds))
                return new Rectangle(Bounds.X + localBounds.X, Bounds.Y + localBounds.Y, localBounds.Width, localBounds.Height);
            var centerY = Bounds.Y + (int)MathF.Round(Math.Min(Size.Y - 6, 34 + slot * 20));
            return new Rectangle(Bounds.X + 8, centerY - 10, Math.Max(0, Bounds.Width - 16), 20);
        }
        /// <summary>Rendered input-port icon/dot bounds in screen coordinates.</summary>
        public Rectangle GetInputPortDrawBounds(int port) => GetPortDrawBounds(GetPort(_inputPorts, port), false);
        /// <summary>Rendered output-port icon/dot bounds in screen coordinates.</summary>
        public Rectangle GetOutputPortDrawBounds(int port) => GetPortDrawBounds(GetPort(_outputPorts, port), true);
        public event Action<GraphNode, int> SlotUpdated;
        /// <summary>Raised after child-backed slot rows are rearranged, matching Godot's slot_sizes_changed signal.</summary>
        public event Action<GraphNode> SlotSizesChanged;
        protected internal override void PointerPressed(Point position)
        {
            if (Parent is GraphEdit graph && graph.TryBeginConnectionDrag(this, position)) return;
            if (Parent is GraphEdit selectionGraph && !Selected) selectionGraph.SelectNode(this);
            base.PointerPressed(position);
        }
        protected internal override void PointerMoved(Point position)
        {
            if (Parent is GraphEdit graph && graph.IsConnectionDragging) { graph.UpdateConnectionDrag(position); return; }
            base.PointerMoved(position);
        }
        protected internal override void PointerReleased(Point position, bool isInside)
        {
            if (Parent is GraphEdit graph && graph.IsConnectionDragging) { graph.EndConnectionDrag(position); return; }
            base.PointerReleased(position, isInside);
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            _slotCenterCache.Clear();
            _slotBoundsCache.Clear();
            var layouts = new List<SlotLayout>();
            var slotStyle = GetThemeStyleBox("slot");
            var slot = 0;
            foreach (var child in Children)
            {
                if (!child.Visible || IsInternalTitlebar(child)) continue;
                var drawStyleBox = !TryGetSlot(slot, out var state) || state.DrawStyleBox;
                var styleMargin = drawStyleBox && slotStyle != null ? slotStyle.ContentMargin : new Thickness();
                var minimum = slotStyle == null ? Math.Max(20, child.GetMinimumSize().Y) : child.GetMinimumSize().Y + styleMargin.Vertical;
                var maximum = child.GetCombinedMaximumSize().Y;
                if (maximum >= 0) maximum = Math.Max(minimum, maximum + styleMargin.Vertical);
                var ratio = (child.VerticalSizeFlags & SizeFlags.Expand) != 0 ? child.SizeFlagsStretchRatio : 0;
                layouts.Add(new SlotLayout
                {
                    Child = child,
                    Slot = slot++,
                    Minimum = minimum,
                    Maximum = maximum,
                    Final = minimum,
                    Ratio = ratio,
                    Stretching = ratio > 0,
                    StyleMargin = styleMargin
                });
            }
            if (layouts.Count == 0) return;

            var separation = Context?.Theme.Separation ?? 4;
            var panelMargin = GetThemeStyleBox("panel")?.ContentMargin ?? new Thickness(8, 0, 8, 0);
            var titlebarSize = GetNodeTitlebarSize();
            var available = Math.Max(0, Size.Y - panelMargin.Vertical - titlebarSize.Y - separation * Math.Max(0, layouts.Count - 1));
            var stretchSpace = available;
            var ratioTotal = 0f;
            foreach (var layout in layouts)
            {
                if (layout.Stretching) ratioTotal += layout.Ratio;
                else stretchSpace -= layout.Minimum;
            }
            stretchSpace = Math.Max(0, stretchSpace);
            while (ratioTotal > 0)
            {
                var refitSuccessful = true;
                foreach (var layout in layouts)
                {
                    if (!layout.Stretching) continue;
                    var share = stretchSpace * layout.Ratio / ratioTotal;
                    if (share < layout.Minimum || layout.Maximum >= 0 && share > layout.Maximum)
                    {
                        layout.Final = share < layout.Minimum ? layout.Minimum : layout.Maximum;
                        layout.Stretching = false;
                        ratioTotal -= layout.Ratio;
                        stretchSpace = Math.Max(0, stretchSpace - layout.Final);
                        refitSuccessful = false;
                        break;
                    }
                    layout.Final = share;
                }
                if (refitSuccessful) break;
            }

            var y = panelMargin.Top + titlebarSize.Y;
            var rtl = IsLayoutRtl();
            foreach (var layout in layouts)
            {
                var row = new Rectangle((int)panelMargin.Left, (int)MathF.Round(y), Math.Max(0, (int)MathF.Round(Size.X - panelMargin.Horizontal)), Math.Max(0, (int)MathF.Round(layout.Final)));
                _slotBoundsCache[layout.Slot] = row;
                var contentPosition = new Vector2(row.X + layout.StyleMargin.Left, row.Y + layout.StyleMargin.Top);
                var contentSize = new Vector2(Math.Max(0, row.Width - layout.StyleMargin.Horizontal), Math.Max(0, row.Height - layout.StyleMargin.Vertical));
                FitChildInRect(layout.Child, contentPosition, contentSize, rtl);
                _slotCenterCache[layout.Slot] = layout.Child.Position.Y + layout.Child.Size.Y * .5f;
                y += layout.Final + separation;
            }
            SlotSizesChanged?.Invoke(this);
        }
        internal override void DrawGraphElementChrome(UIRenderContext context)
        {
            context.Fill(Bounds, Selected ? context.Theme.HoverColor : context.Theme.PanelColor);
            context.Border(Bounds, Selected ? context.Theme.FocusColor : context.Theme.PanelBorderColor, Selected ? 2 : 1);
            var titleRect = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, Math.Min(24, Bounds.Height));
            context.Fill(titleRect, context.Theme.AccentColor);
            if (EffectiveUIFont != null && !string.IsNullOrEmpty(Title)) context.Text(EffectiveUIFont, Title, new Vector2(titleRect.X + 6, titleRect.Y + Math.Max(2, (titleRect.Height - TextMetrics.LineHeight(EffectiveUIFont)) / 2)), context.Theme.TextColor);
            DrawSlotStyleBoxes(context);
            DrawPorts(context, _inputPorts, false); DrawPorts(context, _outputPorts, true);
            DrawResizeHandle(context);
        }
        internal Vector2 GetInputPortScreenPosition(int port) => new Vector2(Bounds.X, Bounds.Y) + GetInputPortPosition(port);
        internal Vector2 GetOutputPortScreenPosition(int port) => new Vector2(Bounds.X, Bounds.Y) + GetOutputPortPosition(port);
        private int NextSlot()
        {
            var maximum = -1; foreach (var slot in _slots.Keys) maximum = Math.Max(maximum, slot); return maximum + 1;
        }
        private static IReadOnlyList<string> GetPortNames(List<Port> ports) { var names = new List<string>(ports.Count); foreach (var port in ports) names.Add(port.Name); return names; }
        private static Port GetPort(List<Port> ports, int index) { if (index < 0 || index >= ports.Count) throw new ArgumentOutOfRangeException(nameof(index)); return ports[index]; }
        private bool TryGetSlot(int slot, out SlotState state)
        {
            if (slot < 0) { state = null; return false; }
            return _slots.TryGetValue(slot, out state);
        }
        private SlotState GetOrCreateSlot(int slot)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            if (!_slots.TryGetValue(slot, out var state)) { state = new SlotState(); _slots.Add(slot, state); }
            return state;
        }
        private SlotState RequireSlot(int slot)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            if (!_slots.TryGetValue(slot, out var state)) throw new InvalidOperationException($"Slot {slot} is not enabled.");
            return state;
        }
        private void RebuildPortCaches()
        {
            var inputNames = CaptureNames(_inputPorts);
            var outputNames = CaptureNames(_outputPorts);
            _inputPorts.Clear(); _outputPorts.Clear();
            foreach (var pair in SortedSlots())
            {
                var slot = pair.Key; var state = pair.Value;
                if (state.EnableLeft) _inputPorts.Add(new Port { Slot = slot, Name = inputNames.TryGetValue(slot, out var inputName) ? inputName : string.Empty, Type = state.TypeLeft, Color = state.ColorLeft, Icon = state.CustomIconLeft });
                if (state.EnableRight) _outputPorts.Add(new Port { Slot = slot, Name = outputNames.TryGetValue(slot, out var outputName) ? outputName : string.Empty, Type = state.TypeRight, Color = state.ColorRight, Icon = state.CustomIconRight });
            }
        }
        private IEnumerable<KeyValuePair<int, SlotState>> SortedSlots()
        {
            var keys = new List<int>(_slots.Keys); keys.Sort();
            foreach (var key in keys) yield return new KeyValuePair<int, SlotState>(key, _slots[key]);
        }
        private static Dictionary<int, string> CaptureNames(List<Port> ports)
        {
            var names = new Dictionary<int, string>();
            foreach (var port in ports) names[port.Slot] = port.Name;
            return names;
        }
        private Vector2 GetNodeTitlebarSize(bool desired = false)
        {
            var margin = GetThemeStyleBox("titlebar")?.ContentMargin ?? new Thickness();
            var content = EffectiveUIFont != null && !string.IsNullOrEmpty(Title) ? TextMetrics.Measure(EffectiveUIFont, Title) : Vector2.Zero;
            return new Vector2(content.X + margin.Horizontal, Math.Max(24, content.Y + margin.Vertical));
        }
        private Vector2 GetNodeSize(bool desired)
        {
            var panelMargin = GetThemeStyleBox("panel")?.ContentMargin ?? new Thickness(8, 0, 8, 0);
            var slotStyle = GetThemeStyleBox("slot");
            var slotMargin = slotStyle?.ContentMargin ?? new Thickness();
            var minimum = GetNodeTitlebarSize(desired);
            var separation = Context?.Theme.Separation ?? 4;
            var childCount = 0;
            var slot = 0;
            foreach (var child in Children)
            {
                if (!child.Visible || IsInternalTitlebar(child)) continue;
                var childSize = desired ? child.GetBoundDesiredSize() : child.GetMinimumSize();
                childSize.X += panelMargin.Horizontal;
                if (!TryGetSlot(slot++, out var state) || state.DrawStyleBox) childSize += new Vector2(slotMargin.Horizontal, slotMargin.Vertical);
                minimum.X = Math.Max(minimum.X, childSize.X);
                minimum.Y += slotStyle == null ? Math.Max(20, childSize.Y) : childSize.Y;
                if (childCount++ > 0) minimum.Y += separation;
            }
            minimum.Y += panelMargin.Vertical;
            return Vector2.Max(CustomMinimumSize, minimum);
        }
        private Vector2 GetPortPosition(Port port, bool output)
        {
            var y = _slotCenterCache.TryGetValue(port.Slot, out var cachedCenter) ? cachedCenter : Math.Min(Size.Y - 6, 34 + port.Slot * 20);
            return new Vector2(output ? Size.X : 0, y);
        }
        private bool IsInternalTitlebar(Control child) => this is GraphFrame frame && ReferenceEquals(child, frame.GetTitlebarHBox());
        private Rectangle GetPortDrawBounds(Port port, bool output)
        {
            var position = GetPortPosition(port, output);
            var size = port.Icon != null ? new Point(12, 12) : GetThemeIcon("port")?.LogicalSize ?? new Point(8, 8);
            return new Rectangle(Bounds.X + (int)MathF.Round(position.X) - (output ? size.X : 0), Bounds.Y + (int)MathF.Round(position.Y) - size.Y / 2, size.X, size.Y);
        }
        private void DrawSlotStyleBoxes(UIRenderContext context)
        {
            foreach (var pair in SortedSlots())
            {
                var bounds = GetSlotStyleBoxBounds(pair.Key);
                if (bounds == Rectangle.Empty) continue;
                var style = GetThemeStyleBox("slot");
                if (style != null) style.Draw(context, bounds);
                else
                {
                    context.Fill(bounds, context.Theme.HoverColor.WithAlpha(72));
                    context.Border(bounds, context.Theme.PanelBorderColor.WithAlpha(120));
                }
            }
        }
        private void DrawPorts(UIRenderContext context, List<Port> ports, bool output)
        {
            foreach (var port in ports)
            {
                var position = GetPortPosition(port, output); var bounds = GetPortDrawBounds(port, output);
                if (port.Icon != null) context.SpriteBatch.Draw(port.Icon, bounds, port.Color);
                else
                {
                    var icon = GetThemeIcon("port");
                    if (icon.HasValue) context.Icon(icon.Value, bounds, port.Color);
                    else { context.Fill(bounds, port.Color); context.Border(bounds, context.Theme.PanelBorderColor); }
                }
                if (EffectiveUIFont != null && !string.IsNullOrEmpty(port.Name))
                {
                    var textX = output ? Bounds.Right - 8 - TextMetrics.Measure(EffectiveUIFont, port.Name).X : Bounds.X + 10;
                    context.Text(EffectiveUIFont, port.Name, new Vector2(textX, Bounds.Y + position.Y - TextMetrics.LineHeight(EffectiveUIFont) / 2), context.Theme.TextColor);
                }
            }
        }
    }

    /// <summary>Graph node used as a visual frame to organize other graph elements.</summary>
    public sealed class GraphFrame : GraphNode
    {
        private readonly HBoxContainer _titlebarHBox = new HBoxContainer { Name = "_titlebar_hbox" };
        private bool _autoshrinkEnabled = true;
        private int _autoshrinkMargin = 40;
        private int _dragMargin = 16;
        private bool _tintColorEnabled;
        private Color _tintColor = new Color(77, 77, 77, 191);
        public GraphFrame()
        {
            ZIndex = -1;
            CustomMinimumSize = Vector2.Zero;
            _titlebarHBox.MouseFilter = MouseFilter.Ignore;
            AddChild(_titlebarHBox);
        }
        public bool AutoshrinkEnabled { get => _autoshrinkEnabled; set => SetAutoshrinkEnabled(value); }
        /// <summary>Uniform graph-coordinate padding around attached elements, equivalent to Godot's autoshrink_margin.</summary>
        public int AutoshrinkMargin { get => _autoshrinkMargin; set => SetAutoshrinkMargin(value); }
        public int DragMargin { get => _dragMargin; set => SetDragMargin(value); }
        public bool TintColorEnabled { get => _tintColorEnabled; set => SetTintColorEnabled(value); }
        public Color TintColor { get => _tintColor; set => SetTintColor(value); }
        public event Action<GraphFrame, Vector2> AutoshrinkChanged;
        public void SetTitle(string title) => Title = title ?? string.Empty;
        public string GetTitle() => Title;
        public void SetAutoshrinkEnabled(bool enable)
        {
            if (_autoshrinkEnabled == enable) return;
            _autoshrinkEnabled = enable;
            NotifyAutoshrinkChanged();
            QueueLayout();
        }
        public bool IsAutoshrinkEnabled() => AutoshrinkEnabled;
        public void SetAutoshrinkMargin(int margin)
        {
            if (_autoshrinkMargin == margin) return;
            _autoshrinkMargin = margin;
            NotifyAutoshrinkChanged();
        }
        public int GetAutoshrinkMargin() => AutoshrinkMargin;
        public HBoxContainer GetTitlebarHBox() => _titlebarHBox;
        public Vector2 GetTitlebarSize()
        {
            var margin = GetThemeStyleBox("titlebar")?.ContentMargin ?? new Thickness();
            var minimum = _titlebarHBox.GetMinimumSize();
            if (EffectiveUIFont != null && !string.IsNullOrEmpty(Title)) minimum = Vector2.Max(minimum, TextMetrics.Measure(EffectiveUIFont, Title));
            return new Vector2(minimum.X + margin.Horizontal, Math.Max(24, minimum.Y + margin.Vertical));
        }
        /// <summary>Returns the titlebar, body-child, and frame style minimum required by Godot's GraphFrame layout.</summary>
        public override Vector2 GetMinimumSize() => GetFrameSize(false);
        /// <summary>Returns the desired-size variant of the GraphFrame layout requirement.</summary>
        public override Vector2 GetDesiredSize() => GetFrameSize(true);
        public void SetDragMargin(int margin) => _dragMargin = margin;
        public int GetDragMargin() => DragMargin;
        public override bool ContainsPoint(Point point)
        {
            if (!base.ContainsPoint(point)) return false;
            var zoom = Parent is GraphEdit graph ? graph.Zoom : 1f;
            var local = new Vector2(point.X - Bounds.X, point.Y - Bounds.Y) / zoom;
            const float resizerSize = 12f;
            if (local.X >= Size.X - resizerSize && local.Y >= Size.Y - resizerSize) return true;
            if (local.Y < GetTitlebarSize().Y) return true;
            var margin = Math.Max(0, DragMargin);
            return margin > 0 && (local.X < margin || local.Y < margin || local.X >= Size.X - margin || local.Y >= Size.Y - margin);
        }
        protected internal override void PointerMoved(Point position)
        {
            if (AutoshrinkEnabled && IsResizing) return;
            base.PointerMoved(position);
        }
        public void SetTintColorEnabled(bool enable) { if (_tintColorEnabled == enable) return; _tintColorEnabled = enable; QueueLayout(); }
        public bool IsTintColorEnabled() => TintColorEnabled;
        public void SetTintColor(Color color) { if (_tintColor == color) return; _tintColor = color; QueueLayout(); }
        public Color GetTintColor() => TintColor;
        internal override void DrawGraphElementChrome(UIRenderContext context)
        {
            context.Fill(Bounds, TintColorEnabled ? TintColor : context.Theme.PanelColor.WithAlpha(128));
            context.Border(Bounds, context.Theme.PanelBorderColor);
            var titleHeight = Math.Min(24, Bounds.Height);
            context.Fill(new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, titleHeight), context.Theme.AccentColor.WithAlpha(160));
            if (EffectiveUIFont != null && !string.IsNullOrEmpty(Title)) context.Text(EffectiveUIFont, Title, new Vector2(Bounds.X + 6, Bounds.Y + Math.Max(2, (titleHeight - TextMetrics.LineHeight(EffectiveUIFont)) / 2)), context.Theme.TextColor);
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            var titlebarMargin = GetThemeStyleBox("titlebar")?.ContentMargin ?? new Thickness();
            var panelMargin = GetThemeStyleBox("panel")?.ContentMargin ?? new Thickness();
            var titlebarSize = GetTitlebarSize();
            FitChildInRect(
                _titlebarHBox,
                new Vector2(titlebarMargin.Left, titlebarMargin.Top),
                new Vector2(Math.Max(0, Size.X - titlebarMargin.Horizontal), Math.Max(0, titlebarSize.Y - titlebarMargin.Vertical)),
                IsLayoutRtl());
            var bodyPosition = new Vector2(panelMargin.Left, panelMargin.Top + titlebarSize.Y);
            var bodySize = new Vector2(
                Math.Max(0, Size.X - panelMargin.Horizontal),
                Math.Max(0, Size.Y - panelMargin.Vertical - titlebarSize.Y));
            foreach (var child in Children)
                if (child.Visible && !ReferenceEquals(child, _titlebarHBox)) FitChildInRect(child, bodyPosition, bodySize, IsLayoutRtl());
        }
        private Vector2 GetFrameSize(bool desired)
        {
            var titlebarMargin = GetThemeStyleBox("titlebar")?.ContentMargin ?? new Thickness();
            var panelMargin = GetThemeStyleBox("panel")?.ContentMargin ?? new Thickness();
            var titlebarSize = desired ? _titlebarHBox.GetBoundDesiredSize() : _titlebarHBox.GetMinimumSize();
            if (EffectiveUIFont != null && !string.IsNullOrEmpty(Title)) titlebarSize = Vector2.Max(titlebarSize, TextMetrics.Measure(EffectiveUIFont, Title));
            var minimum = titlebarSize + new Vector2(titlebarMargin.Horizontal, titlebarMargin.Vertical);
            minimum.Y = Math.Max(24, minimum.Y);
            foreach (var child in Children)
            {
                if (!child.Visible || ReferenceEquals(child, _titlebarHBox)) continue;
                var childSize = desired ? child.GetBoundDesiredSize() : child.GetMinimumSize();
                childSize.X += panelMargin.Horizontal;
                minimum.X = Math.Max(minimum.X, childSize.X);
                minimum.Y += Math.Max(minimum.Y, childSize.Y);
            }
            minimum.Y += panelMargin.Vertical;
            return Vector2.Max(CustomMinimumSize, minimum);
        }
        private void NotifyAutoshrinkChanged()
        {
            AutoshrinkChanged?.Invoke(this, Size);
            if (Parent is GraphEdit graph) graph.UpdateGraphFrame(this);
        }
    }

    /// <summary>An edge between named graph nodes and their port indices.</summary>
    public readonly struct GraphConnection : IEquatable<GraphConnection>
    {
        public GraphConnection(string fromNode, int fromPort, string toNode, int toPort)
        {
            FromNode = fromNode; FromPort = fromPort; ToNode = toNode; ToPort = toPort;
        }
        public string FromNode { get; }
        public int FromPort { get; }
        public string ToNode { get; }
        public int ToPort { get; }
        public bool Equals(GraphConnection other) => FromNode == other.FromNode && FromPort == other.FromPort && ToNode == other.ToNode && ToPort == other.ToPort;
        public override bool Equals(object obj) => obj is GraphConnection other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(FromNode, FromPort, ToNode, ToPort);
    }

    /// <summary>Godot GraphEdit background grid presentation.</summary>
    public enum GraphEditGridPattern { Lines, Dots }
    public enum GraphEditPanningScheme { ScrollZooms, ScrollPans }

    /// <summary>Godot-inspired navigation helper for zoomable editor canvases.</summary>
    public sealed class ViewPanner
    {
        private Point _lastPointer;
        public bool IsPanning { get; private set; }
        public int ScrollSpeed { get; set; } = 32;
        public float ScrollZoomFactor { get; set; } = 1.1f;
        public bool ScrollPans { get; set; }
        public event Action<Vector2> PanRequested;
        public event Action<float, Vector2> ZoomRequested;
        public void BeginPan(Point position) { _lastPointer = position; IsPanning = true; }
        public void UpdatePan(Point position)
        {
            if (!IsPanning) return;
            var delta = new Vector2(position.X - _lastPointer.X, position.Y - _lastPointer.Y); _lastPointer = position;
            if (delta != Vector2.Zero) PanRequested?.Invoke(delta);
        }
        public void EndPan() => IsPanning = false;
        public void ApplyWheel(int delta, bool control, bool shift, Vector2 position)
        {
            if (delta == 0) return;
            if (ScrollPans && !control || !ScrollPans && control)
            {
                var amount = Math.Sign(delta) * ScrollSpeed;
                PanRequested?.Invoke(shift ? new Vector2(amount, 0) : new Vector2(0, amount));
                return;
            }
            if (!shift) ZoomRequested?.Invoke(delta > 0 ? ScrollZoomFactor : 1f / ScrollZoomFactor, position);
        }
    }

    /// <summary>Deterministic layered graph arranger modeled after Godot's GraphEditArranger.</summary>
    public sealed class GraphEditArranger
    {
        public GraphEditArranger(GraphEdit graph) { Graph = graph ?? throw new ArgumentNullException(nameof(graph)); }
        public GraphEdit Graph { get; }
        public float HorizontalGap { get; set; } = 100;
        public float VerticalGap { get; set; } = 100;
        public void ArrangeNodes()
        {
            var nodes = new List<GraphNode>(); var selectedOnly = false;
            foreach (var child in Graph.Children) if (child is GraphNode node && node is not GraphFrame && node.Selected) { selectedOnly = true; break; }
            foreach (var child in Graph.Children) if (child is GraphNode node && node is not GraphFrame && (!selectedOnly || node.Selected)) nodes.Add(node);
            if (nodes.Count == 0) return;
            var included = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
            foreach (var node in nodes) if (!string.IsNullOrEmpty(node.Name)) included[node.Name] = node;
            var layers = new Dictionary<GraphNode, int>(); var incoming = new Dictionary<GraphNode, List<GraphNode>>();
            foreach (var node in nodes) { layers[node] = 0; incoming[node] = new List<GraphNode>(); }
            foreach (var connection in Graph.Connections)
                if (included.TryGetValue(connection.FromNode, out var from) && included.TryGetValue(connection.ToNode, out var to) && from != to && !incoming[to].Contains(from)) incoming[to].Add(from);
            var unresolved = new List<GraphNode>(nodes);
            while (unresolved.Count > 0)
            {
                var progressed = false;
                for (var index = unresolved.Count - 1; index >= 0; index--)
                {
                    var node = unresolved[index]; var ready = true; var layer = 0;
                    foreach (var parent in incoming[node])
                    {
                        if (unresolved.Contains(parent)) { ready = false; break; }
                        layer = Math.Max(layer, layers[parent] + 1);
                    }
                    if (!ready) continue;
                    layers[node] = layer; unresolved.RemoveAt(index); progressed = true;
                }
                if (!progressed) { var node = unresolved[0]; unresolved.RemoveAt(0); layers[node] = 0; }
            }
            var origin = nodes[0].Position;
            foreach (var node in nodes) origin = Vector2.Min(origin, node.Position);
            var maxLayer = 0; foreach (var pair in layers) maxLayer = Math.Max(maxLayer, pair.Value);
            var x = origin.X;
            for (var layer = 0; layer <= maxLayer; layer++)
            {
                var y = origin.Y; var width = 0f;
                foreach (var node in nodes) if (layers[node] == layer) { node.Position = new Vector2(x, y); y += node.Size.Y + VerticalGap; width = Math.Max(width, node.Size.X); }
                x += width + HorizontalGap;
            }
            Graph.NotifyNodesArranged();
        }
    }

    /// <summary>Editor graph canvas with selection, node movement, zoom state and explicit connection management.</summary>
    [TemplatePart(GraphCanvasPartName, typeof(Container))]
    public sealed class GraphEdit : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Canvas;
        public const string GraphCanvasPartName = "PART_GraphCanvas";
        private readonly List<GraphConnection> _connections = new List<GraphConnection>();
        private readonly Dictionary<GraphConnection, float> _connectionActivity = new Dictionary<GraphConnection, float>();
        private readonly HashSet<(int FromType, int ToType)> _validConnectionTypes = new HashSet<(int FromType, int ToType)>();
        private readonly HashSet<int> _validRightDisconnectTypes = new HashSet<int>();
        private readonly HashSet<int> _validLeftDisconnectTypes = new HashSet<int>();
        private readonly Dictionary<int, string> _typeNames = new Dictionary<int, string>();
        private readonly Dictionary<GraphElement, GraphFrame> _elementFrames = new Dictionary<GraphElement, GraphFrame>();
        private readonly Dictionary<GraphFrame, List<GraphElement>> _frameElements = new Dictionary<GraphFrame, List<GraphElement>>();
        private readonly Dictionary<GraphElement, Vector2> _lastElementPositions = new Dictionary<GraphElement, Vector2>();
        private readonly Dictionary<GraphElement, Vector2> _draggedElementOrigins = new Dictionary<GraphElement, Vector2>();
        private GraphElement _draggedElementSource;
        private bool _updatingFrame;
        private float _zoom = 1f;
        private Vector2 _scrollOffset;
        private bool _backgroundPanning;
        private bool _boxSelecting;
        private Point _boxSelectionStart;
        private Point _boxSelectionEnd;
        private bool _showGrid = true;
        private bool _snappingEnabled = true;
        private bool _showMenu = true;
        private bool _showZoomLabel;
        private bool _showZoomButtons = true;
        private bool _showGridButtons = true;
        private bool _showMinimapButton = true;
        private bool _showArrangeButton = true;
        private int _snappingDistance = 20;
        private float _snappingDistanceScale = 1f;
        private GraphNode _connectionSource;
        private int _connectionSourcePort = -1;
        private bool _connectionFromOutput;
        private bool _connectionJustDisconnected;
        private bool _keyboardConnecting;
        private GraphConnection? _connectionDragTarget;
        private Point _connectionDragPoint;
        public GraphEdit()
        {
            ClipContents = true;
            FocusMode = FocusMode.All;
            CustomMinimumSize = new Vector2(240, 160);
            Panner = new ViewPanner();
            Panner.PanRequested += delta => SetScrollOffset(ScrollOffset - delta);
            Panner.ZoomRequested += (factor, position) => SetZoomCustom(Zoom * factor, position);
            Arranger = new GraphEditArranger(this);
            Minimap = new GraphEditMinimap { Graph = this, Name = "_minimap", CustomMinimumSize = new Vector2(50, 50), Size = new Vector2(240, 160), Opacity = .65f, ZIndex = 1000 };
            AddChild(Minimap);
            Toolbar = new PanelContainer { Name = "_menu", ZIndex = 999, Padding = new Thickness(2) };
            ToolbarButtons = new HBoxContainer { Separation = 2 };
            ZoomLabel = new Label { Name = "_zoom_label", Text = "100%", CustomMinimumSize = new Vector2(48, 24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visible = false };
            ZoomOutButton = CreateToolbarButton("-", 24); ZoomResetButton = CreateToolbarButton("1:1", 32); ZoomInButton = CreateToolbarButton("+", 24);
            GridToggleButton = CreateToolbarButton("Grid", 38, true); SnappingToggleButton = CreateToolbarButton("Snap", 40, true); MinimapToggleButton = CreateToolbarButton("Map", 34, true); ArrangeButton = CreateToolbarButton("Arrange", 54);
            ConfigureToolbarIcon(ZoomOutButton, "zoom_out"); ConfigureToolbarIcon(ZoomResetButton, "zoom_reset"); ConfigureToolbarIcon(ZoomInButton, "zoom_in");
            ConfigureToolbarIcon(GridToggleButton, "grid_toggle"); ConfigureToolbarIcon(SnappingToggleButton, "snapping_toggle"); ConfigureToolbarIcon(MinimapToggleButton, "minimap_toggle"); ConfigureToolbarIcon(ArrangeButton, "layout");
            SnappingDistanceSpinBox = new SpinBox { Name = "_snapping_distance_spinbox", MinValue = 2, MaxValue = 100, Step = 1, Value = _snappingDistance, CustomMinimumSize = new Vector2(52, 24) };
            GridToggleButton.SetPressedNoSignal(true); SnappingToggleButton.SetPressedNoSignal(true); MinimapToggleButton.SetPressedNoSignal(true);
            ZoomOutButton.Pressed += (_, _) => ZoomOut(); ZoomResetButton.Pressed += (_, _) => ZoomReset(); ZoomInButton.Pressed += (_, _) => ZoomIn();
            GridToggleButton.Toggled += (_, pressed) => ShowGrid = pressed; SnappingToggleButton.Toggled += (_, pressed) => SnappingEnabled = pressed; MinimapToggleButton.Toggled += (_, pressed) => MinimapEnabled = pressed;
            SnappingDistanceSpinBox.ValueChanged += (_, value) => SnappingDistance = (int)MathF.Round(value);
            ArrangeButton.Pressed += (_, _) => ArrangeNodes();
            ToolbarButtons.AddChild(ZoomLabel); ToolbarButtons.AddChild(ZoomOutButton); ToolbarButtons.AddChild(ZoomResetButton); ToolbarButtons.AddChild(ZoomInButton); ToolbarButtons.AddChild(GridToggleButton); ToolbarButtons.AddChild(SnappingToggleButton); ToolbarButtons.AddChild(SnappingDistanceSpinBox); ToolbarButtons.AddChild(MinimapToggleButton); ToolbarButtons.AddChild(ArrangeButton);
            Toolbar.AddChild(ToolbarButtons); AddChild(Toolbar);
        }
        public float Zoom { get => _zoom; set => SetZoom(value); }
        public float ZoomStep { get; set; } = 1.2f;
        public float ZoomMin { get; set; } = 0.232568f;
        public float ZoomMax { get; set; } = 2.0736f;
        public Vector2 ScrollOffset { get => _scrollOffset; set => SetScrollOffset(value); }
        public ViewPanner Panner { get; }
        public GraphEditArranger Arranger { get; }
        /// <summary>Built-in minimap overlay, equivalent to Godot's <c>get_minimap()</c>.</summary>
        public GraphEditMinimap Minimap { get; }
        /// <summary>In-canvas GraphEdit toolbar, corresponding to Godot's toolbar menu panel.</summary>
        public PanelContainer Toolbar { get; }
        public HBoxContainer ToolbarButtons { get; }
        public Label ZoomLabel { get; }
        public Button ZoomOutButton { get; }
        public Button ZoomResetButton { get; }
        public Button ZoomInButton { get; }
        public Button GridToggleButton { get; }
        public Button SnappingToggleButton { get; }
        /// <summary>Built-in snapping distance editor shown with Godot's grid controls.</summary>
        public SpinBox SnappingDistanceSpinBox { get; }
        public Button MinimapToggleButton { get; }
        public Button ArrangeButton { get; }
        public bool MinimapEnabled { get => Minimap.Visible; set { Minimap.Visible = value; MinimapToggleButton.SetPressedNoSignal(value); } }
        public Vector2 MinimapSize
        {
            get => Minimap.Size;
            set { Minimap.Size = Vector2.Max(new Vector2(50, 50), value); QueueLayout(); }
        }
        public float MinimapOpacity
        {
            get => Minimap.Opacity;
            set => Minimap.Opacity = MathHelper.Clamp(value, 0, 1);
        }
        public bool ShowMenu { get => _showMenu; set { _showMenu = value; Toolbar.Visible = value; } }
        public bool ShowZoomLabel { get => _showZoomLabel; set { _showZoomLabel = value; ZoomLabel.Visible = value; QueueLayout(); } }
        public bool ShowZoomButtons { get => _showZoomButtons; set { _showZoomButtons = value; ZoomOutButton.Visible = value; ZoomResetButton.Visible = value; ZoomInButton.Visible = value; QueueLayout(); } }
        public bool ShowGridButtons { get => _showGridButtons; set { _showGridButtons = value; GridToggleButton.Visible = value; SnappingToggleButton.Visible = value; SnappingDistanceSpinBox.Visible = value; QueueLayout(); } }
        public bool ShowMinimapButton { get => _showMinimapButton; set { _showMinimapButton = value; MinimapToggleButton.Visible = value; QueueLayout(); } }
        public bool ShowArrangeButton { get => _showArrangeButton; set { _showArrangeButton = value; ArrangeButton.Visible = value; QueueLayout(); } }
        public bool ShowGrid { get => _showGrid; set { _showGrid = value; GridToggleButton.SetPressedNoSignal(value); } }
        public GraphEditGridPattern GridPattern { get; set; } = GraphEditGridPattern.Lines;
        public bool SnappingEnabled { get => _snappingEnabled; set { _snappingEnabled = value; SnappingToggleButton.SetPressedNoSignal(value); } }
        public GraphEditPanningScheme PanningScheme { get; set; }
        public bool RightDisconnectsEnabled { get; set; } = true;
        public float ConnectionLinesCurvature { get; set; } = .5f;
        public float ConnectionLinesThickness { get; set; } = 2f;
        public bool ConnectionLinesAntialiased { get; set; } = true;
        public int SnappingDistance
        {
            get => _snappingDistance;
            set
            {
                var minimum = (int)MathF.Ceiling(2 * _snappingDistanceScale); var maximum = (int)MathF.Floor(100 * _snappingDistanceScale);
                if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException(nameof(value), $"SnappingDistance must be between {minimum} and {maximum} for the active scale.");
                _snappingDistance = value; SnappingDistanceSpinBox.Value = value;
            }
        }
        public float SnappingDistanceScale { get => _snappingDistanceScale; set => _snappingDistanceScale = value; }
        public IReadOnlyList<GraphConnection> Connections => _connections;
        public IReadOnlyDictionary<int, string> TypeNames => _typeNames;
        /// <summary>Enables Godot-style background drag selection over graph elements.</summary>
        public bool BoxSelectionEnabled { get; set; } = true;
        /// <summary>Whether a background drag selection rectangle is currently active.</summary>
        public bool IsBoxSelecting => _boxSelecting;
        /// <summary>The current retained box-selection rectangle in screen coordinates.</summary>
        public Rectangle BoxSelectionRect => GetBoxSelectionRectangle();
        /// <summary>Whether a graph-port connection is presently being dragged.</summary>
        public bool IsConnectionDragging => _connectionSource != null;
        public bool IsKeyboardConnecting => _keyboardConnecting;
        /// <summary>The compatible target currently under the pointer during a connection drag, if any.</summary>
        public GraphConnection? ConnectionDragTarget => _connectionDragTarget;
        public event Action<GraphEdit, GraphConnection> ConnectionRequest;
        public event Action<GraphEdit, GraphConnection> DisconnectionRequest;
        public event Action<GraphEdit, string, int, Vector2> ConnectionToEmpty;
        public event Action<GraphEdit, string, int, Vector2> ConnectionFromEmpty;
        public event Action<GraphEdit, string, int, bool> ConnectionDragStarted;
        public event Action<GraphEdit> ConnectionDragEnded;
        public event Action<GraphEdit, GraphNode> NodeSelected;
        public event Action<GraphEdit> DuplicateNodesRequest;
        public event Action<GraphEdit> CopyNodesRequest;
        public event Action<GraphEdit> CutNodesRequest;
        public event Action<GraphEdit> PasteNodesRequest;
        public event Action<GraphEdit, IReadOnlyList<string>> DeleteNodesRequest;
        /// <summary>Requests application-owned attachment of dropped graph elements to a frame.</summary>
        public event Action<GraphEdit, IReadOnlyList<string>, string> GraphElementsLinkedToFrameRequest;
        public event Action<GraphEdit, float> ZoomChanged;
        public event Action<GraphEdit, Vector2> ScrollOffsetChanged;
        public event Action<GraphEdit> NodesArranged;
        private void ConfigureToolbarIcon(Button button, string iconName)
        {
            button.DecorativeIconProvider = () => GetThemeIcon(iconName);
            button.HideTextWhenDecorativeIconAvailable = true;
        }
        /// <summary>Sets zoom while keeping the graph coordinate under the viewport center fixed.</summary>
        public void SetZoom(float zoom) => SetZoomCustom(zoom, Size * .5f);
        /// <summary>Sets zoom while preserving the graph point under a local viewport coordinate.</summary>
        public void SetZoomCustom(float zoom, Vector2 center)
        {
            if (ZoomMin > ZoomMax) throw new InvalidOperationException("ZoomMin cannot exceed ZoomMax.");
            zoom = MathHelper.Clamp(zoom, ZoomMin, ZoomMax);
            if (Math.Abs(_zoom - zoom) < .0001f) return;
            var anchor = (_scrollOffset + center) / _zoom;
            _zoom = zoom;
            _scrollOffset = anchor * _zoom - center;
            ZoomLabel.Text = ((int)MathF.Round(_zoom * 100)).ToString() + "%";
            QueueLayout();
            ZoomChanged?.Invoke(this, _zoom);
            ScrollOffsetChanged?.Invoke(this, _scrollOffset);
        }
        public void SetScrollOffset(Vector2 offset)
        {
            if (_scrollOffset == offset) return;
            _scrollOffset = offset; QueueLayout(); ScrollOffsetChanged?.Invoke(this, _scrollOffset);
        }
        public Vector2 GetScrollOffset() => ScrollOffset;
        public void SetPanningScheme(GraphEditPanningScheme scheme) { if (!Enum.IsDefined(typeof(GraphEditPanningScheme), scheme)) throw new ArgumentOutOfRangeException(nameof(scheme)); PanningScheme = scheme; }
        public GraphEditPanningScheme GetPanningScheme() => PanningScheme;
        public float GetZoom() => Zoom;
        public void SetZoomMin(float zoomMin) { ZoomMin = zoomMin; Zoom = Zoom; }
        public float GetZoomMin() => ZoomMin;
        public void SetZoomMax(float zoomMax) { ZoomMax = zoomMax; Zoom = Zoom; }
        public float GetZoomMax() => ZoomMax;
        public void SetZoomStep(float zoomStep) => ZoomStep = zoomStep;
        public float GetZoomStep() => ZoomStep;
        public void SetShowGrid(bool enable) => ShowGrid = enable;
        public bool IsShowingGrid() => ShowGrid;
        public void SetGridPattern(GraphEditGridPattern pattern) { if (!Enum.IsDefined(typeof(GraphEditGridPattern), pattern)) throw new ArgumentOutOfRangeException(nameof(pattern)); GridPattern = pattern; QueueLayout(); }
        public GraphEditGridPattern GetGridPattern() => GridPattern;
        public void SetSnappingEnabled(bool enable) => SnappingEnabled = enable;
        public bool IsSnappingEnabled() => SnappingEnabled;
        public void SetSnappingDistance(int pixels) => SnappingDistance = pixels;
        public int GetSnappingDistance() => SnappingDistance;
        public void SetConnectionLinesCurvature(float curvature) => ConnectionLinesCurvature = curvature;
        public float GetConnectionLinesCurvature() => ConnectionLinesCurvature;
        public void SetConnectionLinesThickness(float pixels) => ConnectionLinesThickness = Math.Max(0, pixels);
        public float GetConnectionLinesThickness() => ConnectionLinesThickness;
        public void SetConnectionLinesAntialiased(bool antialiased) => ConnectionLinesAntialiased = antialiased;
        public bool IsConnectionLinesAntialiased() => ConnectionLinesAntialiased;
        /// <summary>Returns the retained screen-space connection curve points for the supplied endpoints.</summary>
        public IReadOnlyList<Vector2> GetConnectionLine(Vector2 from, Vector2 to) => GetConnectionLinePoints(from, to);
        internal IReadOnlyList<Vector2> GetConnectionLinePoints(Vector2 from, Vector2 to)
        {
            if (ConnectionLinesCurvature <= 0) return new[] { from, to };
            var xDifference = to.X - from.X;
            var controlOffset = MathF.Abs(xDifference) * ConnectionLinesCurvature;
            var firstControl = new Vector2(from.X + controlOffset, from.Y);
            var secondControl = new Vector2(to.X - controlOffset, to.Y);
            var segments = ConnectionLinesAntialiased ? 32 : 20;
            var points = new Vector2[segments + 1];
            for (var segment = 0; segment <= segments; segment++)
            {
                var t = segment / (float)segments;
                points[segment] = CubicBezier(from, firstControl, secondControl, to, t);
            }
            return points;
        }
        /// <summary>Returns the closest retained connection to a screen-space point, matching Godot's hit query shape.</summary>
        public GraphConnection? GetClosestConnectionAtPoint(Vector2 point, float maxDistance = 4f)
        {
            GraphConnection? closest = null;
            var closestDistance = Math.Max(0, maxDistance);
            foreach (var connection in _connections)
            {
                if (!TryGetConnectionLinePoints(connection, out var points)) continue;
                for (var i = 1; i < points.Count; i++)
                {
                    var distance = DistanceToSegment(point, points[i - 1], points[i]);
                    if (distance <= ConnectionLinesThickness * .5f + Math.Max(0, maxDistance) && distance < closestDistance)
                    {
                        closest = connection;
                        closestDistance = distance;
                    }
                }
            }
            return closest;
        }
        /// <summary>Returns retained connections whose screen-space line segments intersect the supplied rectangle.</summary>
        public IReadOnlyList<GraphConnection> GetConnectionsIntersectingWithRect(RectangleF rect)
        {
            var result = new List<GraphConnection>();
            foreach (var connection in _connections)
            {
                if (!TryGetConnectionLinePoints(connection, out var points)) continue;
                for (var i = 1; i < points.Count; i++)
                {
                    if (SegmentIntersectsRect(points[i - 1], points[i], rect))
                    {
                        result.Add(connection);
                        break;
                    }
                }
            }
            return result;
        }
        internal (Color From, Color To) GetConnectionLineColors(GraphConnection connection, Theme theme)
        {
            var from = FindNode(connection.FromNode); var to = FindNode(connection.ToNode);
            if (from == null || to == null || connection.FromPort < 0 || connection.FromPort >= from.OutputPortCount || connection.ToPort < 0 || connection.ToPort >= to.InputPortCount) return (Color.Transparent, Color.Transparent);
            var fromColor = from.GetOutputPortColor(connection.FromPort);
            var toColor = to.GetInputPortColor(connection.ToPort);
            var activity = MathHelper.Clamp(GetConnectionActivity(connection.FromNode, connection.FromPort, connection.ToNode, connection.ToPort), 0, 1);
            if (activity > 0)
            {
                var activityColor = theme?.ConnectionActivityColor ?? new Theme().ConnectionActivityColor;
                fromColor = Color.Lerp(fromColor, activityColor, activity);
                toColor = Color.Lerp(toColor, activityColor, activity);
            }
            return (fromColor, toColor);
        }
        public void SetMinimapSize(Vector2 size) => MinimapSize = size;
        public Vector2 GetMinimapSize() => MinimapSize;
        public void SetMinimapOpacity(float opacity) => MinimapOpacity = opacity;
        public float GetMinimapOpacity() => MinimapOpacity;
        public void SetMinimapEnabled(bool enable) => MinimapEnabled = enable;
        public bool IsMinimapEnabled() => MinimapEnabled;
        public void SetShowMenu(bool show) => ShowMenu = show;
        public bool IsShowingMenu() => ShowMenu;
        public void SetShowZoomLabel(bool show) => ShowZoomLabel = show;
        public bool IsShowingZoomLabel() => ShowZoomLabel;
        public void SetShowZoomButtons(bool show) => ShowZoomButtons = show;
        public bool IsShowingZoomButtons() => ShowZoomButtons;
        public void SetShowGridButtons(bool show) => ShowGridButtons = show;
        public bool IsShowingGridButtons() => ShowGridButtons;
        public void SetShowMinimapButton(bool show) => ShowMinimapButton = show;
        public bool IsShowingMinimapButton() => ShowMinimapButton;
        public void SetShowArrangeButton(bool show) => ShowArrangeButton = show;
        public bool IsShowingArrangeButton() => ShowArrangeButton;
        public void SetRightDisconnects(bool enable) => RightDisconnectsEnabled = enable;
        public bool IsRightDisconnectsEnabled() => RightDisconnectsEnabled;
        public void SetTypeNames(IDictionary<int, string> typeNames)
        {
            _typeNames.Clear();
            if (typeNames == null) return;
            foreach (var pair in typeNames) _typeNames[pair.Key] = pair.Value ?? string.Empty;
        }
        public IReadOnlyDictionary<int, string> GetTypeNames() => _typeNames;
        public HBoxContainer GetMenuHBox() => ToolbarButtons;
        public void ZoomIn() => SetZoom(Zoom * ZoomStep);
        public void ZoomOut() => SetZoom(Zoom / ZoomStep);
        public void ZoomReset() => SetZoom(1f);
        public void ArrangeNodes() => Arranger.ArrangeNodes();
        public Vector2 GraphToScreen(Vector2 graphPosition) => graphPosition * Zoom - ScrollOffset;
        public Vector2 ScreenToGraph(Vector2 screenPosition) => (screenPosition + ScrollOffset) / Zoom;
        public void SetBoxSelectionEnabled(bool enable) => BoxSelectionEnabled = enable;
        public bool IsBoxSelectionEnabled() => BoxSelectionEnabled;
        public IReadOnlyList<GraphNode> GetSelectedNodes()
        {
            var selected = new List<GraphNode>();
            foreach (var child in Children) if (child is GraphNode node && node.Selected) selected.Add(node);
            return selected;
        }
        public void DeselectAll()
        {
            foreach (var child in Children) if (child is GraphNode node) node.SetSelected(false);
        }
        /// <summary>Rounds a graph coordinate to the active Godot grid spacing.</summary>
        public Vector2 SnapPosition(Vector2 graphPosition)
        {
            var spacing = Math.Max(1, SnappingDistance * SnappingDistanceScale);
            return new Vector2(MathF.Round(graphPosition.X / spacing) * spacing, MathF.Round(graphPosition.Y / spacing) * spacing);
        }
        /// <summary>Registers a directional pair of port types as a valid interactive connection target.</summary>
        public void AddValidConnectionType(int fromType, int toType) => _validConnectionTypes.Add((fromType, toType));
        /// <summary>Removes a directional pair of port types from the interactive connection registry.</summary>
        public void RemoveValidConnectionType(int fromType, int toType) => _validConnectionTypes.Remove((fromType, toType));
        /// <summary>Returns whether an explicit directional type pair is registered.</summary>
        public bool IsValidConnectionType(int fromType, int toType) => _validConnectionTypes.Contains((fromType, toType));
        public void AddValidRightDisconnectType(int type) => _validRightDisconnectTypes.Add(type);
        public void RemoveValidRightDisconnectType(int type) => _validRightDisconnectTypes.Remove(type);
        public bool IsValidRightDisconnectType(int type) => _validRightDisconnectTypes.Contains(type);
        public void AddValidLeftDisconnectType(int type) => _validLeftDisconnectTypes.Add(type);
        public void RemoveValidLeftDisconnectType(int type) => _validLeftDisconnectTypes.Remove(type);
        public bool IsValidLeftDisconnectType(int type) => _validLeftDisconnectTypes.Contains(type);
        /// <summary>
        /// Returns whether an output port may be used as an interactive connection source for an input port.
        /// Like Godot, equal types always match, the destination node can opt out of checking, and registered
        /// directional type pairs also match. This does not constrain <see cref="ConnectNode"/>, which is an
        /// explicit programmatic connection API in Godot as well.
        /// </summary>
        public bool IsConnectionTargetValid(string fromNode, int fromPort, string toNode, int toPort)
        {
            var from = FindNode(fromNode); var to = FindNode(toNode);
            if (from == null || to == null || fromPort < 0 || fromPort >= from.OutputPortCount || toPort < 0 || toPort >= to.InputPortCount) return false;
            var fromType = from.GetOutputPortType(fromPort); var toType = to.GetInputPortType(toPort);
            return fromType == toType || to.IgnoreInvalidConnectionType || IsValidConnectionType(fromType, toType);
        }
        public bool ConnectNode(string fromNode, int fromPort, string toNode, int toPort)
        {
            if (!HasNode(fromNode) || !HasNode(toNode)) return false;
            var connection = new GraphConnection(fromNode, fromPort, toNode, toPort);
            if (_connections.Contains(connection)) return false;
            _connections.Add(connection);
            ConnectionRequest?.Invoke(this, connection);
            return true;
        }
        public bool DisconnectNode(string fromNode, int fromPort, string toNode, int toPort)
        {
            var connection = new GraphConnection(fromNode, fromPort, toNode, toPort);
            if (!_connections.Remove(connection)) return false;
            DisconnectionRequest?.Invoke(this, connection);
            return true;
        }
        public bool IsNodeConnected(string fromNode, int fromPort, string toNode, int toPort) => _connections.Contains(new GraphConnection(fromNode, fromPort, toNode, toPort));
        public void SetConnectionActivity(string fromNode, int fromPort, string toNode, int toPort, float amount)
        {
            var connection = new GraphConnection(fromNode, fromPort, toNode, toPort);
            if (_connections.Contains(connection)) _connectionActivity[connection] = amount;
        }
        public float GetConnectionActivity(string fromNode, int fromPort, string toNode, int toPort) => _connectionActivity.TryGetValue(new GraphConnection(fromNode, fromPort, toNode, toPort), out var amount) ? amount : 0f;
        public void SetConnections(IEnumerable<GraphConnection> connections)
        {
            _connections.Clear(); _connectionActivity.Clear();
            if (connections == null) return;
            foreach (var connection in connections)
                if (HasNode(connection.FromNode) && HasNode(connection.ToNode) && !_connections.Contains(connection)) _connections.Add(connection);
        }
        public IReadOnlyList<GraphConnection> GetConnectionList() => new List<GraphConnection>(_connections);
        public int GetConnectionCount(string fromNode, int fromPort)
        {
            var count = 0;
            foreach (var connection in _connections) if (connection.FromNode == fromNode && connection.FromPort == fromPort) count++;
            return count;
        }
        public IReadOnlyList<GraphConnection> GetConnectionListFromNode(string node)
        {
            var result = new List<GraphConnection>();
            foreach (var connection in _connections) if (connection.FromNode == node || connection.ToNode == node) result.Add(connection);
            return result;
        }
        public void ClearConnections() { _connections.Clear(); _connectionActivity.Clear(); }
        public bool IsKeyboardConnectingMode() => IsKeyboardConnecting;
        public void StartKeyboardConnecting(GraphNode node, int inputPort, int outputPort)
        {
            if (node == null || node.Parent != this || inputPort == outputPort || (inputPort != -1 && outputPort != -1)) return;
            ForceConnectionDragEnd();
            _keyboardConnecting = true;
            if (inputPort != -1)
            {
                if (inputPort < 0 || inputPort >= node.InputPortCount) { _keyboardConnecting = false; return; }
                if (TryBeginKeyboardDisconnectFromInput(node, inputPort)) return;
                _connectionSource = node; _connectionSourcePort = inputPort; _connectionFromOutput = false; _connectionJustDisconnected = false;
                _connectionDragTarget = null; _connectionDragPoint = node.GetInputPortScreenPosition(inputPort).ToPoint(); GrabFocus();
                ConnectionDragStarted?.Invoke(this, node.Name, inputPort, false);
                return;
            }
            if (outputPort != -1)
            {
                if (outputPort < 0 || outputPort >= node.OutputPortCount) { _keyboardConnecting = false; return; }
                if (TryBeginKeyboardDisconnectFromOutput(node, outputPort)) return;
                _connectionSource = node; _connectionSourcePort = outputPort; _connectionFromOutput = true; _connectionJustDisconnected = false;
                _connectionDragTarget = null; _connectionDragPoint = node.GetOutputPortScreenPosition(outputPort).ToPoint(); GrabFocus();
                ConnectionDragStarted?.Invoke(this, node.Name, outputPort, true);
            }
        }
        public void EndKeyboardConnecting(GraphNode node, int inputPort, int outputPort)
        {
            if (!_keyboardConnecting || !IsConnectionDragging || node == null || node.Parent != this) return;
            GraphConnection? target = null;
            if (inputPort != -1 && inputPort >= 0 && inputPort < node.InputPortCount && _connectionFromOutput)
                target = new GraphConnection(_connectionSource.Name, _connectionSourcePort, node.Name, inputPort);
            if (outputPort != -1 && outputPort >= 0 && outputPort < node.OutputPortCount && !_connectionFromOutput)
                target = new GraphConnection(node.Name, outputPort, _connectionSource.Name, _connectionSourcePort);
            if (target.HasValue && IsConnectionTargetValid(target.Value.FromNode, target.Value.FromPort, target.Value.ToNode, target.Value.ToPort))
                ConnectNode(target.Value.FromNode, target.Value.FromPort, target.Value.ToNode, target.Value.ToPort);
            else if (!_connectionJustDisconnected)
            {
                if (_connectionFromOutput) ConnectionToEmpty?.Invoke(this, _connectionSource.Name, _connectionSourcePort, Vector2.Zero);
                else ConnectionFromEmpty?.Invoke(this, _connectionSource.Name, _connectionSourcePort, Vector2.Zero);
            }
            _keyboardConnecting = false;
            ForceConnectionDragEnd();
        }
        /// <summary>Begins an interactive connection drag when <paramref name="position"/> is over one of <paramref name="node"/>'s ports.</summary>
        public bool TryBeginConnectionDrag(GraphNode node, Point position)
        {
            if (node == null || node.Parent != this || IsConnectionDragging) return false;
            var output = HitPort(node, position, true);
            var input = output < 0 ? HitPort(node, position, false) : -1;
            if (output < 0 && input < 0) return false;
            if (TryBeginDisconnectDrag(node, output, input, position)) return true;
            _connectionSource = node; _connectionSourcePort = output >= 0 ? output : input; _connectionFromOutput = output >= 0;
            _connectionJustDisconnected = false; _connectionDragPoint = position; _connectionDragTarget = null; GrabFocus();
            UpdateConnectionDrag(position);
            ConnectionDragStarted?.Invoke(this, _connectionSource.Name, _connectionSourcePort, _connectionFromOutput);
            return true;
        }
        internal bool IsConnectionDragSource(GraphNode node) => node != null && node == _connectionSource;
        internal void UpdateConnectionDrag(Point position)
        {
            if (!IsConnectionDragging) return;
            _connectionDragPoint = position;
            _connectionDragTarget = FindConnectionTarget(position);
        }
        internal void EndConnectionDrag(Point position)
        {
            if (!IsConnectionDragging) return;
            UpdateConnectionDrag(position);
            var target = _connectionDragTarget;
            if (target.HasValue) ConnectNode(target.Value.FromNode, target.Value.FromPort, target.Value.ToNode, target.Value.ToPort);
            else if (!_connectionJustDisconnected)
            {
                var release = new Vector2(position.X, position.Y);
                if (_connectionFromOutput) ConnectionToEmpty?.Invoke(this, _connectionSource.Name, _connectionSourcePort, release);
                else ConnectionFromEmpty?.Invoke(this, _connectionSource.Name, _connectionSourcePort, release);
            }
            ForceConnectionDragEnd();
        }
        public void ForceConnectionDragEnd()
        {
            if (!IsConnectionDragging) return;
            _connectionSource = null; _connectionSourcePort = -1; _connectionJustDisconnected = false; _connectionDragTarget = null; _keyboardConnecting = false;
            ConnectionDragEnded?.Invoke(this);
        }
        internal Vector2 ApplyGraphElementResizeRequest(GraphElement element, Vector2 requestedSize)
        {
            if (element == null || element.Parent != this) throw new InvalidOperationException("Graph element must be a direct child of this graph.");
            var newSize = requestedSize;
            if (ShouldSnapResize()) newSize = SnapPosition(newSize);
            newSize = Vector2.Max(element.GetMinimumSize(), newSize);
            if (element is GraphFrame frame && !frame.AutoshrinkEnabled && _frameElements.TryGetValue(frame, out var elements) && elements.Count > 0)
            {
                var required = Vector2.Zero;
                foreach (var attached in elements)
                    required = Vector2.Max(required, attached.Position + attached.Size - frame.Position);
                newSize = Vector2.Max(newSize, required);
            }
            element.Size = newSize;
            if (_elementFrames.TryGetValue(element, out var parentFrame)) UpdateGraphFrame(parentFrame);
            QueueLayout();
            return newSize;
        }
        private bool TryBeginDisconnectDrag(GraphNode node, int outputPort, int inputPort, Point position)
        {
            if (inputPort >= 0 && (RightDisconnectsEnabled || IsValidRightDisconnectType(node.GetInputPortType(inputPort))))
            {
                foreach (var connection in new List<GraphConnection>(_connections))
                {
                    if (connection.ToNode != node.Name || connection.ToPort != inputPort) continue;
                    var from = FindNode(connection.FromNode);
                    if (from == null || connection.FromPort < 0 || connection.FromPort >= from.OutputPortCount) return false;
                    DisconnectNode(connection.FromNode, connection.FromPort, connection.ToNode, connection.ToPort);
                    _connectionSource = from; _connectionSourcePort = connection.FromPort; _connectionFromOutput = true; _connectionJustDisconnected = true;
                    _connectionDragPoint = position; _connectionDragTarget = null; GrabFocus();
                    UpdateConnectionDrag(position);
                    ConnectionDragStarted?.Invoke(this, _connectionSource.Name, _connectionSourcePort, true);
                    return true;
                }
            }
            if (outputPort >= 0 && IsValidLeftDisconnectType(node.GetOutputPortType(outputPort)))
            {
                foreach (var connection in new List<GraphConnection>(_connections))
                {
                    if (connection.FromNode != node.Name || connection.FromPort != outputPort) continue;
                    var to = FindNode(connection.ToNode);
                    if (to == null || connection.ToPort < 0 || connection.ToPort >= to.InputPortCount) return false;
                    DisconnectNode(connection.FromNode, connection.FromPort, connection.ToNode, connection.ToPort);
                    _connectionSource = to; _connectionSourcePort = connection.ToPort; _connectionFromOutput = false; _connectionJustDisconnected = true;
                    _connectionDragPoint = position; _connectionDragTarget = null; GrabFocus();
                    UpdateConnectionDrag(position);
                    ConnectionDragStarted?.Invoke(this, _connectionSource.Name, _connectionSourcePort, false);
                    return true;
                }
            }
            return false;
        }
        private bool TryBeginKeyboardDisconnectFromInput(GraphNode node, int inputPort)
        {
            if (!(RightDisconnectsEnabled || IsValidRightDisconnectType(node.GetInputPortType(inputPort)))) return false;
            foreach (var connection in new List<GraphConnection>(_connections))
            {
                if (connection.ToNode != node.Name || connection.ToPort != inputPort) continue;
                var from = FindNode(connection.FromNode);
                if (from == null || connection.FromPort < 0 || connection.FromPort >= from.OutputPortCount) return false;
                DisconnectNode(connection.FromNode, connection.FromPort, connection.ToNode, connection.ToPort);
                _connectionSource = from; _connectionSourcePort = connection.FromPort; _connectionFromOutput = true; _connectionJustDisconnected = true;
                _connectionDragTarget = null; _connectionDragPoint = node.GetInputPortScreenPosition(inputPort).ToPoint(); GrabFocus();
                ConnectionDragStarted?.Invoke(this, _connectionSource.Name, _connectionSourcePort, true);
                return true;
            }
            return false;
        }
        private bool TryBeginKeyboardDisconnectFromOutput(GraphNode node, int outputPort)
        {
            if (!IsValidLeftDisconnectType(node.GetOutputPortType(outputPort))) return false;
            foreach (var connection in new List<GraphConnection>(_connections))
            {
                if (connection.FromNode != node.Name || connection.FromPort != outputPort) continue;
                var to = FindNode(connection.ToNode);
                if (to == null || connection.ToPort < 0 || connection.ToPort >= to.InputPortCount) return false;
                DisconnectNode(connection.FromNode, connection.FromPort, connection.ToNode, connection.ToPort);
                _connectionSource = to; _connectionSourcePort = connection.ToPort; _connectionFromOutput = false; _connectionJustDisconnected = true;
                _connectionDragTarget = null; _connectionDragPoint = node.GetOutputPortScreenPosition(outputPort).ToPoint(); GrabFocus();
                ConnectionDragStarted?.Invoke(this, _connectionSource.Name, _connectionSourcePort, false);
                return true;
            }
            return false;
        }
        public void AttachGraphElementToFrame(string graphElement, string parentFrame)
        {
            var element = FindElement(graphElement); var frame = FindElement(parentFrame) as GraphFrame;
            if (element == null) throw new ArgumentException("The graph element was not found.", nameof(graphElement));
            if (frame == null) throw new ArgumentException("The graph frame was not found.", nameof(parentFrame));
            if (element == frame) throw new InvalidOperationException("A graph frame cannot contain itself.");
            DetachGraphElementFromFrame(graphElement);
            if (!_frameElements.TryGetValue(frame, out var elements)) { elements = new List<GraphElement>(); _frameElements.Add(frame, elements); frame.PositionChanged += FrameMoved; }
            elements.Add(element); _elementFrames.Add(element, frame); _lastElementPositions[element] = element.Position;
            if (!_lastElementPositions.ContainsKey(frame)) _lastElementPositions[frame] = frame.Position;
            element.PositionChanged += ElementMoved;
            UpdateGraphFrame(frame);
        }
        public void DetachGraphElementFromFrame(string graphElement)
        {
            var element = FindElement(graphElement); if (element == null || !_elementFrames.TryGetValue(element, out var frame)) return;
            element.PositionChanged -= ElementMoved; _elementFrames.Remove(element); _lastElementPositions.Remove(element);
            if (_frameElements.TryGetValue(frame, out var elements)) { elements.Remove(element); if (elements.Count == 0) { frame.PositionChanged -= FrameMoved; _frameElements.Remove(frame); } else UpdateGraphFrame(frame); }
        }
        public GraphFrame GetElementFrame(string attachedGraphElement) => FindElement(attachedGraphElement) is GraphElement element && _elementFrames.TryGetValue(element, out var frame) ? frame : null;
        public IReadOnlyList<string> GetAttachedNodesOfFrame(string graphFrame)
        {
            var frame = FindElement(graphFrame) as GraphFrame; var result = new List<string>();
            if (frame != null && _frameElements.TryGetValue(frame, out var elements)) foreach (var element in elements) result.Add(element.Name);
            return result;
        }
        internal void BeginGraphElementDrag(GraphElement source)
        {
            _draggedElementOrigins.Clear();
            _draggedElementSource = source;
            foreach (var child in Children)
            {
                if (child is not GraphElement element || !element.Selected || !element.Draggable) continue;
                _draggedElementOrigins[element] = element.Position;
                if (!ReferenceEquals(element, source)) element.SetDrag(true);
            }
            if (!_draggedElementOrigins.ContainsKey(source)) _draggedElementOrigins[source] = source.Position;
        }
        internal bool UpdateGraphElementDrag(GraphElement source, Vector2 sourcePosition)
        {
            if (!ReferenceEquals(source, _draggedElementSource) || !_draggedElementOrigins.TryGetValue(source, out var sourceOrigin)) return false;
            var delta = sourcePosition - sourceOrigin;
            foreach (var pair in _draggedElementOrigins)
            {
                var position = pair.Value + delta;
                pair.Key.Position = SnappingEnabled ? SnapPosition(position) : position;
            }
            return true;
        }
        internal void EndGraphElementDrag(GraphElement source, bool moved, Point position)
        {
            if (!ReferenceEquals(source, _draggedElementSource)) return;
            if (moved) NotifyGraphElementDropped(source, position);
            foreach (var element in _draggedElementOrigins.Keys)
                if (!ReferenceEquals(element, source)) element.SetDrag(false);
            _draggedElementOrigins.Clear();
            _draggedElementSource = null;
        }
        internal void CancelGraphElementDrag(GraphElement source)
        {
            if (!ReferenceEquals(source, _draggedElementSource)) return;
            _draggedElementOrigins.Clear();
            _draggedElementSource = null;
        }
        private void NotifyGraphElementDropped(GraphElement element, Point position)
        {
            if (element == null || element.Parent != this) return;
            for (var index = Children.Count - 1; index >= 0; index--)
            {
                if (Children[index] is not GraphFrame frame || frame.IsResizing || !frame.Bounds.Contains(position) || _draggedElementOrigins.ContainsKey(frame)) continue;
                var names = new List<string>();
                for (var draggedIndex = Children.Count - 1; draggedIndex >= 0; draggedIndex--)
                    if (Children[draggedIndex] is GraphElement dragged && _draggedElementOrigins.ContainsKey(dragged) && !_elementFrames.ContainsKey(dragged)) names.Add(dragged.Name);
                if (names.Count > 0) GraphElementsLinkedToFrameRequest?.Invoke(this, names, frame.Name);
                return;
            }
        }
        /// <summary>Recomputes an autoshrink frame around its attached elements.</summary>
        public void UpdateGraphFrame(GraphFrame frame)
        {
            if (frame == null || !_frameElements.TryGetValue(frame, out var elements) || elements.Count == 0) return;
            var minimum = elements[0].Position; var maximum = elements[0].Position + elements[0].Size;
            foreach (var element in elements) { minimum = Vector2.Min(minimum, element.Position); maximum = Vector2.Max(maximum, element.Position + element.Size); }
            var margin = Math.Max(0, frame.AutoshrinkMargin);
            // Godot reserves at least the titlebar height above attached elements.
            minimum -= new Vector2(margin, Math.Max(margin, 24)); maximum += new Vector2(margin, margin);
            if (!frame.AutoshrinkEnabled) { minimum = Vector2.Min(minimum, frame.Position); maximum = Vector2.Max(maximum, frame.Position + frame.Size); }
            _updatingFrame = true;
            try { frame.Position = minimum; frame.Size = Vector2.Max(Vector2.Zero, maximum - minimum); }
            finally { _updatingFrame = false; }
            _lastElementPositions[frame] = frame.Position; QueueLayout();
        }
        public void SelectNode(GraphNode node)
        {
            if (node != null && node.Parent != this) throw new InvalidOperationException("Node must be a child of this graph.");
            foreach (var child in Children) if (child is GraphNode graphNode) graphNode.SetSelected(graphNode == node);
            if (node != null && node.Selected) NodeSelected?.Invoke(this, node);
        }
        public void RaiseElement(GraphElement element)
        {
            if (element == null || element.Parent != this) return;
            MoveChild(element, Children.Count - 1);
        }
        protected internal override void PointerPressed(Point position)
        {
            base.PointerPressed(position);
            for (var index = Children.Count - 1; index >= 0; index--)
                if (Children[index] is GraphNode graphNode && graphNode is not GraphFrame && TryBeginConnectionDrag(graphNode, position)) return;
            if (HitNode(position) is GraphNode node) SelectNode(node);
            else if (BoxSelectionEnabled) BeginBoxSelection(position);
            else { _backgroundPanning = true; Panner.BeginPan(position); }
        }
        protected internal override void PointerMoved(Point position)
        {
            if (IsConnectionDragging) { UpdateConnectionDrag(position); return; }
            if (_boxSelecting) { UpdateBoxSelection(position); return; }
            if (_backgroundPanning) Panner.UpdatePan(position);
        }
        protected internal override void PointerReleased(Point position, bool isInside)
        {
            if (IsConnectionDragging) { EndConnectionDrag(position); return; }
            if (_boxSelecting) { UpdateBoxSelection(position); _boxSelecting = false; return; }
            _backgroundPanning = false; Panner.EndPan();
        }
        internal override void CancelInput()
        {
            _boxSelecting = false;
            _backgroundPanning = false;
            Panner.EndPan();
            _draggedElementOrigins.Clear();
            _draggedElementSource = null;
            ForceConnectionDragEnd();
            base.CancelInput();
        }
        internal override bool ShortcutInput(Keys key, KeyboardState keyboard)
        {
            if (!OwnsKeyboardFocus()) return false;
            var command = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl) || keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);
            if (command && key == Keys.D) DuplicateNodesRequest?.Invoke(this);
            else if (command && key == Keys.C) CopyNodesRequest?.Invoke(this);
            else if (command && key == Keys.X) CutNodesRequest?.Invoke(this);
            else if (command && key == Keys.V) PasteNodesRequest?.Invoke(this);
            else if (key == Keys.Delete)
            {
                var selected = new List<string>();
                foreach (var child in Children) if (child is GraphElement element && element.Selected) selected.Add(element.Name);
                DeleteNodesRequest?.Invoke(this, selected);
            }
            else return false;
            return true;
        }
        private bool OwnsKeyboardFocus()
        {
            for (var focused = Context?.FocusedControl; focused != null; focused = focused.VisualParent)
                if (ReferenceEquals(focused, this)) return true;
            return false;
        }
        internal override bool PointerWheel(int delta) { Panner.ApplyWheel(delta, false, false, Size * .5f); return true; }
        internal void DrawGraphCanvas(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor);
            if (ShowGrid)
            {
                var grid = Math.Max(4, (int)MathF.Round(16 * Zoom));
                var startX = Bounds.X - PositiveModulo((int)MathF.Round(ScrollOffset.X), grid);
                var startY = Bounds.Y - PositiveModulo((int)MathF.Round(ScrollOffset.Y), grid);
                if (GridPattern == GraphEditGridPattern.Lines)
                {
                    for (var x = startX; x < Bounds.Right; x += grid) context.Fill(new Rectangle(x, Bounds.Y, 1, Bounds.Height), new Color(255, 255, 255, 12));
                    for (var y = startY; y < Bounds.Bottom; y += grid) context.Fill(new Rectangle(Bounds.X, y, Bounds.Width, 1), new Color(255, 255, 255, 12));
                }
                else for (var x = startX; x < Bounds.Right; x += grid) for (var y = startY; y < Bounds.Bottom; y += grid) context.Fill(new Rectangle(x, y, 2, 2), new Color(255, 255, 255, 20));
            }
            if (ClipContents) context.PushClip(Bounds);
            try
            {
                // Frames are backgrounds. Drawing them first keeps both connections and nodes readable.
                foreach (var child in GetGraphChildrenInDrawOrder()) if (child is GraphFrame && child.Visible) child.DrawTree(context);
                foreach (var connection in _connections)
                {
                    var from = FindNode(connection.FromNode); var to = FindNode(connection.ToNode);
                    if (from == null || to == null || connection.FromPort < 0 || connection.FromPort >= from.OutputPortCount || connection.ToPort < 0 || connection.ToPort >= to.InputPortCount) continue;
                    var colors = GetConnectionLineColors(connection, context.Theme);
                    DrawConnection(context, GetConnectionLinePoints(from.GetOutputPortScreenPosition(connection.FromPort), to.GetInputPortScreenPosition(connection.ToPort)), colors.From, colors.To, ConnectionLinesThickness);
                }
                if (IsConnectionDragging)
                {
                    var origin = _connectionFromOutput ? _connectionSource.GetOutputPortScreenPosition(_connectionSourcePort) : _connectionSource.GetInputPortScreenPosition(_connectionSourcePort);
                    var destination = _connectionDragTarget.HasValue ? GetConnectionTargetPoint(_connectionDragTarget.Value) : new Vector2(_connectionDragPoint.X, _connectionDragPoint.Y);
                    if (_connectionFromOutput)
                    {
                        var toColor = _connectionDragTarget.HasValue ? FindNode(_connectionDragTarget.Value.ToNode).GetInputPortColor(_connectionDragTarget.Value.ToPort) : _connectionSource.GetOutputPortColor(_connectionSourcePort);
                        DrawConnection(context, GetConnectionLinePoints(origin, destination), _connectionSource.GetOutputPortColor(_connectionSourcePort), toColor, ConnectionLinesThickness);
                    }
                    else
                    {
                        var fromColor = _connectionDragTarget.HasValue ? FindNode(_connectionDragTarget.Value.FromNode).GetOutputPortColor(_connectionDragTarget.Value.FromPort) : _connectionSource.GetInputPortColor(_connectionSourcePort);
                        DrawConnection(context, GetConnectionLinePoints(destination, origin), fromColor, _connectionSource.GetInputPortColor(_connectionSourcePort), ConnectionLinesThickness);
                    }
                }
                foreach (var child in GetGraphChildrenInDrawOrder()) if (child is GraphElement and not GraphFrame && child.Visible) child.DrawTree(context);
                if (_boxSelecting)
                {
                    var rectangle = GetBoxSelectionRectangle();
                    context.Fill(rectangle, new Color(80, 140, 255, 40));
                    context.Border(rectangle, new Color(80, 140, 255, 180));
                }
                foreach (var child in GetGraphChildrenInDrawOrder()) if (child is not GraphElement && child.Visible) child.DrawTree(context);
            }
            finally { if (ClipContents) context.PopClip(); }
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            foreach (var child in Children) if (child is GraphElement element) element.ApplyViewportTransform(Zoom, ScrollOffset);
            Minimap.Position = Vector2.Max(new Vector2(10, 10), Size - Minimap.Size - new Vector2(10, 10));
            Toolbar.Position = new Vector2(10, 10); Toolbar.Size = Toolbar.GetMinimumSize();
        }
        private List<Control> GetGraphChildrenInDrawOrder()
        {
            var ordered = new List<(Control Child, int Index)>(Children.Count);
            for (var index = 0; index < Children.Count; index++) ordered.Add((Children[index], index));
            ordered.Sort((left, right) =>
            {
                var zOrder = left.Child.ZIndex.CompareTo(right.Child.ZIndex);
                return zOrder != 0 ? zOrder : left.Index.CompareTo(right.Index);
            });
            var result = new List<Control>(ordered.Count);
            foreach (var entry in ordered) result.Add(entry.Child);
            return result;
        }
        internal void NotifyNodesArranged() { QueueLayout(); NodesArranged?.Invoke(this); }
        private void ElementMoved(GraphElement element)
        {
            _lastElementPositions[element] = element.Position;
            if (_elementFrames.TryGetValue(element, out var frame)) UpdateGraphFrame(frame);
        }
        private void FrameMoved(GraphElement graphElement)
        {
            if (_updatingFrame || !(graphElement is GraphFrame frame) || !_frameElements.TryGetValue(frame, out var elements)) return;
            var previous = _lastElementPositions.TryGetValue(frame, out var position) ? position : frame.Position;
            var delta = frame.Position - previous; _lastElementPositions[frame] = frame.Position;
            if (delta == Vector2.Zero) return;
            foreach (var element in elements) { element.Position += delta; _lastElementPositions[element] = element.Position; }
            UpdateGraphFrame(frame);
        }
        private bool HasNode(string name) => FindNode(name) != null;
        private GraphNode FindNode(string name)
        {
            foreach (var child in Children) if (child is GraphNode node && node.Name == name) return node;
            return null;
        }
        private GraphElement FindElement(string name)
        {
            foreach (var child in Children) if (child is GraphElement element && element.Name == name) return element;
            return null;
        }
        private GraphNode HitNode(Point point)
        {
            for (var i = Children.Count - 1; i >= 0; i--) if (Children[i] is GraphNode node && node.ContainsPoint(point)) return node;
            return null;
        }
        private void BeginBoxSelection(Point position)
        {
            _backgroundPanning = false; Panner.EndPan();
            _boxSelecting = true; _boxSelectionStart = position; _boxSelectionEnd = position;
            ApplyBoxSelection();
        }
        private void UpdateBoxSelection(Point position)
        {
            _boxSelectionEnd = position;
            ApplyBoxSelection();
        }
        private void ApplyBoxSelection()
        {
            var rectangle = GetBoxSelectionRectangle();
            foreach (var child in Children)
            {
                if (child is not GraphNode node) continue;
                node.SetSelected(node is GraphFrame ? ContainsRectangle(rectangle, node.Bounds) : rectangle.Intersects(node.Bounds));
            }
        }
        private Rectangle GetBoxSelectionRectangle()
        {
            var left = Math.Min(_boxSelectionStart.X, _boxSelectionEnd.X);
            var top = Math.Min(_boxSelectionStart.Y, _boxSelectionEnd.Y);
            var right = Math.Max(_boxSelectionStart.X, _boxSelectionEnd.X);
            var bottom = Math.Max(_boxSelectionStart.Y, _boxSelectionEnd.Y);
            return new Rectangle(left, top, right - left, bottom - top);
        }
        private static bool ContainsRectangle(Rectangle outer, Rectangle inner) => inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
        private bool TryGetConnectionLinePoints(GraphConnection connection, out IReadOnlyList<Vector2> points)
        {
            points = Array.Empty<Vector2>();
            var from = FindNode(connection.FromNode); var to = FindNode(connection.ToNode);
            if (from == null || to == null || connection.FromPort < 0 || connection.FromPort >= from.OutputPortCount || connection.ToPort < 0 || connection.ToPort >= to.InputPortCount) return false;
            points = GetConnectionLinePoints(from.GetOutputPortScreenPosition(connection.FromPort), to.GetInputPortScreenPosition(connection.ToPort));
            return points.Count >= 2;
        }
        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSquared = ab.LengthSquared();
            if (lengthSquared <= float.Epsilon) return Vector2.Distance(point, a);
            var t = MathHelper.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0, 1);
            return Vector2.Distance(point, a + ab * t);
        }
        private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, RectangleF rect)
        {
            if (rect.Contains(a) || rect.Contains(b)) return true;
            return SegmentsIntersect(a, b, new Vector2(rect.Left, rect.Top), new Vector2(rect.Right, rect.Top))
                || SegmentsIntersect(a, b, new Vector2(rect.Right, rect.Top), new Vector2(rect.Right, rect.Bottom))
                || SegmentsIntersect(a, b, new Vector2(rect.Right, rect.Bottom), new Vector2(rect.Left, rect.Bottom))
                || SegmentsIntersect(a, b, new Vector2(rect.Left, rect.Bottom), new Vector2(rect.Left, rect.Top));
        }
        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var ab = b - a; var cd = d - c;
            var denominator = Cross(ab, cd);
            var ca = c - a;
            if (MathF.Abs(denominator) <= .0001f)
            {
                if (MathF.Abs(Cross(ca, ab)) > .0001f) return false;
                return MathF.Min(a.X, b.X) <= MathF.Max(c.X, d.X) && MathF.Min(c.X, d.X) <= MathF.Max(a.X, b.X)
                    && MathF.Min(a.Y, b.Y) <= MathF.Max(c.Y, d.Y) && MathF.Min(c.Y, d.Y) <= MathF.Max(a.Y, b.Y);
            }
            var t = Cross(ca, cd) / denominator;
            var u = Cross(ca, ab) / denominator;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }
        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        private bool ShouldSnapResize()
        {
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
            return SnappingEnabled ^ control;
        }
        private int HitPort(GraphNode node, Point point, bool output)
        {
            var count = output ? node.OutputPortCount : node.InputPortCount;
            for (var port = 0; port < count; port++)
            {
                var position = output ? node.GetOutputPortScreenPosition(port) : node.GetInputPortScreenPosition(port);
                if (Vector2.DistanceSquared(position, new Vector2(point.X, point.Y)) <= 100) return port;
            }
            return -1;
        }
        private GraphConnection? FindConnectionTarget(Point position)
        {
            for (var childIndex = Children.Count - 1; childIndex >= 0; childIndex--)
            {
                if (Children[childIndex] is not GraphNode node || node is GraphFrame || node == _connectionSource) continue;
                var candidateOutput = !_connectionFromOutput;
                var count = candidateOutput ? node.OutputPortCount : node.InputPortCount;
                for (var port = 0; port < count; port++)
                {
                    var portPosition = candidateOutput ? node.GetOutputPortScreenPosition(port) : node.GetInputPortScreenPosition(port);
                    if (Vector2.DistanceSquared(portPosition, new Vector2(position.X, position.Y)) > 100) continue;
                    var connection = _connectionFromOutput
                        ? new GraphConnection(_connectionSource.Name, _connectionSourcePort, node.Name, port)
                        : new GraphConnection(node.Name, port, _connectionSource.Name, _connectionSourcePort);
                    if (IsConnectionTargetValid(connection.FromNode, connection.FromPort, connection.ToNode, connection.ToPort)) return connection;
                }
            }
            return null;
        }
        private Vector2 GetConnectionTargetPoint(GraphConnection connection)
        {
            var from = FindNode(connection.FromNode); var to = FindNode(connection.ToNode);
            return _connectionFromOutput ? to.GetInputPortScreenPosition(connection.ToPort) : from.GetOutputPortScreenPosition(connection.FromPort);
        }
        private static void DrawConnection(UIRenderContext context, IReadOnlyList<Vector2> points, Color fromColor, Color toColor, float thickness)
        {
            if (points == null || points.Count < 2 || thickness <= 0) return;
            var pixelThickness = Math.Max(1, (int)MathF.Round(thickness));
            for (var segment = 1; segment < points.Count; segment++)
            {
                var previous = points[segment - 1]; var current = points[segment];
                var delta = current - previous; var length = Math.Max(1, (int)MathF.Round(delta.Length()));
                var angle = MathF.Atan2(delta.Y, delta.X);
                var color = Color.Lerp(fromColor, toColor, (segment - .5f) / (points.Count - 1));
                context.SpriteBatch.Draw(context.Pixel, new Rectangle((int)MathF.Round(previous.X), (int)MathF.Round(previous.Y), length, pixelThickness), null, color, angle, new Vector2(0, pixelThickness / 2f), SpriteEffects.None, 0);
            }
        }
        private static Vector2 CubicBezier(Vector2 from, Vector2 firstControl, Vector2 secondControl, Vector2 to, float t)
        {
            var inverse = 1 - t;
            return inverse * inverse * inverse * from + 3 * inverse * inverse * t * firstControl + 3 * inverse * t * t * secondControl + t * t * t * to;
        }
        private static Button CreateToolbarButton(string text, float width, bool toggle = false) => new Button { Text = text, Flat = true, ToggleMode = toggle, CustomMinimumSize = new Vector2(width, 24) };
        private static int PositiveModulo(int value, int divisor) { var remainder = value % divisor; return remainder < 0 ? remainder + divisor : remainder; }
    }

    public enum CodeCompletionKind { Class, Function, Signal, Variable, Member, Enum, Constant, NodePath, FilePath, PlainText }

    /// <summary>One retained code-completion candidate, corresponding to Godot's submitted completion option.</summary>
    public sealed class CodeCompletionOption
    {
        public CodeCompletionOption(CodeCompletionKind kind, string displayText, string insertText, Color? textColor = null, object value = null, int location = 0, Texture2D icon = null)
        {
            Kind = kind; DisplayText = displayText ?? string.Empty; InsertText = insertText ?? string.Empty; TextColor = textColor ?? Color.White; Value = value; Location = location; Icon = icon;
        }
        public CodeCompletionKind Kind { get; }
        public string DisplayText { get; }
        public string InsertText { get; }
        public Color TextColor { get; }
        public object Value { get; }
        public int Location { get; }
        public Texture2D Icon { get; }
        public IReadOnlyList<Point> MatchSegments { get; internal set; } = Array.Empty<Point>();
    }

    /// <summary>Godot-inspired code editor with indentation, brace completion and debugger-oriented gutters.</summary>
    public sealed class CodeEdit : TextEdit
    {
        private readonly Dictionary<string, string> _bracePairs = new Dictionary<string, string>
        {
            { "(", ")" }, { "[", "]" }, { "{", "}" }, { "\"", "\"" }, { "'", "'" },
        };
        private readonly HashSet<int> _breakpointedLines = new HashSet<int>();
        private readonly HashSet<int> _bookmarkedLines = new HashSet<int>();
        private readonly HashSet<int> _executingLines = new HashSet<int>();
        private readonly HashSet<int> _foldedLines = new HashSet<int>();
        private readonly HashSet<int> _hiddenLines = new HashSet<int>();
        private readonly List<string> _autoIndentPrefixes = new List<string>();
        private readonly List<string> _lineCommentDelimiters = new List<string>();
        private readonly List<CodeDelimiter> _commentDelimiters = new List<CodeDelimiter>();
        private readonly List<CodeDelimiter> _stringDelimiters = new List<CodeDelimiter>();
        private readonly List<string> _codeCompletionPrefixes = new List<string>();
        private readonly List<CodeCompletionOption> _submittedCompletionOptions = new List<CodeCompletionOption>();
        private readonly List<CodeCompletionOption> _codeCompletionSources = new List<CodeCompletionOption>();
        private readonly List<CodeCompletionOption> _codeCompletionOptions = new List<CodeCompletionOption>();
        private readonly List<int> _lineLengthGuidelines = new List<int>();
        private int _indentSize = 4;
        private int _codeCompletionSelectedIndex = -1;
        private string _codeCompletionBase = string.Empty;
        private Rectangle _codeCompletionBounds;
        private Point _lastSymbolLookupPosition = new Point(-1, -1);
        private string _pendingSymbolLookupWord = string.Empty;
        private string _symbolLookupWord = string.Empty;
        private string _codeRegionStartTag = "region";
        private string _codeRegionEndTag = "endregion";
        private bool _draggingMinimap;
        public bool DrawLineNumbers { get; set; } = true;
        public bool DrawMinimap { get; set; }
        /// <summary>Shows Godot's fold affordance beside the code line-number gutter.</summary>
        public bool DrawFoldGutter { get; set; }
        /// <summary>Enables indentation-based source-line folding without changing the underlying document text.</summary>
        public bool LineFoldingEnabled
        {
            get => _lineFoldingEnabled;
            set
            {
                if (_lineFoldingEnabled == value) return;
                _lineFoldingEnabled = value;
                if (!value) UnfoldAllLines();
                QueueLayout();
            }
        }
        private bool _lineFoldingEnabled;
        public bool AutoBraceCompletionEnabled { get; set; } = true;
        /// <summary>Enables Godot-style retained code-completion requests, candidate selection, and confirmation.</summary>
        public bool CodeCompletionEnabled { get; set; } = true;
        public int CodeCompletionMaxLines { get; set; } = 7;
        public bool IsCodeCompletionActive => _codeCompletionSelectedIndex >= 0 && _codeCompletionOptions.Count > 0;
        public IReadOnlyList<string> CodeCompletionPrefixes => _codeCompletionPrefixes;
        public IReadOnlyList<CodeCompletionOption> CodeCompletionOptions => IsCodeCompletionActive ? _codeCompletionOptions : Array.Empty<CodeCompletionOption>();
        /// <summary>Godot-style function/signature help text anchored at the active caret.</summary>
        public string CodeHint { get; private set; } = string.Empty;
        /// <summary>Places the code hint below rather than above the caret when space permits.</summary>
        public bool CodeHintDrawBelow { get; private set; }
        public bool SymbolLookupOnClickEnabled { get; private set; }
        /// <summary>Last host-validated Command/Ctrl-click symbol, or empty while no lookup is active.</summary>
        public string SymbolLookupWord => _symbolLookupWord;
        public bool AutoIndentEnabled { get; set; }
        public bool IndentUsingSpaces { get; set; }
        public int IndentSize { get => _indentSize; set => _indentSize = Math.Max(1, value); }
        public string IndentText => IndentUsingSpaces ? new string(' ', IndentSize) : "\t";
        public IReadOnlyList<string> AutoIndentPrefixes => _autoIndentPrefixes;
        /// <summary>Configured one-line comment markers that can prefix Godot-style code-region tags.</summary>
        public IReadOnlyList<string> LineCommentDelimiters => _lineCommentDelimiters;
        public bool DrawBreakpointsGutter { get; set; }
        public bool DrawBookmarksGutter { get; set; }
        public bool DrawExecutingLinesGutter { get; set; }
        public bool LineNumbersZeroPadded { get; set; }
        public int LineNumbersMinDigits { get; set; } = 3;
        public Color BreakpointColor { get; set; } = new Color(218, 80, 80);
        public Color BookmarkColor { get; set; } = new Color(230, 180, 71);
        public Color ExecutingLineColor { get; set; } = new Color(88, 184, 116);
        public Color LineLengthGuidelineColor { get; set; } = new Color(104, 117, 140, 150);
        public IReadOnlyList<int> LineLengthGuidelines => _lineLengthGuidelines;
        public CodeEdit()
        {
            TextChanged += (_, _) =>
            {
                // Editing changes source-line ownership; retaining old line indexes could hide unrelated text.
                if (_hiddenLines.Count > 0 || _foldedLines.Count > 0) UnfoldAllLines();
            };
        }
        public event Action<CodeEdit, int> LineChanged;
        /// <summary>Raised synchronously when the host should submit candidates through <see cref="AddCodeCompletionOption"/>.</summary>
        public event Action<CodeEdit, bool> CodeCompletionRequested;
        /// <summary>Raised for a Command/Ctrl-click word when <see cref="SymbolLookupOnClickEnabled"/> is enabled.</summary>
        public event Action<CodeEdit, string, int, int> SymbolLookupRequested;
        /// <summary>Raised after an indentation-based fold or unfold changes visible code lines.</summary>
        public event Action<CodeEdit> FoldStateChanged;
        protected override float TextContentLeftInset => base.TextContentLeftInset + GetGutterWidth();
        protected override bool IsLineHiddenForDisplay(int line) => _hiddenLines.Contains(line);
        public void SetAutoIndentPrefixes(IEnumerable<string> prefixes)
        {
            _autoIndentPrefixes.Clear();
            if (prefixes == null) return;
            foreach (var prefix in prefixes) if (!string.IsNullOrEmpty(prefix)) _autoIndentPrefixes.Add(prefix);
        }
        public void SetCodeCompletionEnabled(bool enabled) { CodeCompletionEnabled = enabled; if (!enabled) CancelCodeCompletion(); }
        public bool IsCodeCompletionEnabled() => CodeCompletionEnabled;
        public void SetCodeCompletionPrefixes(IEnumerable<string> prefixes)
        {
            _codeCompletionPrefixes.Clear();
            if (prefixes == null) return;
            foreach (var prefix in prefixes) if (!string.IsNullOrEmpty(prefix) && !_codeCompletionPrefixes.Contains(prefix)) _codeCompletionPrefixes.Add(prefix);
        }
        public void SetCodeHint(string hint) { CodeHint = hint ?? string.Empty; QueueLayout(); }
        public void SetCodeHintDrawBelow(bool below) { CodeHintDrawBelow = below; QueueLayout(); }
        /// <summary>Returns the current caret-relative retained code-hint panel bounds, or an empty rectangle without a hint.</summary>
        public Rectangle GetCodeHintBounds()
        {
            if (string.IsNullOrEmpty(CodeHint)) return Rectangle.Empty;
            var lineHeight = EffectiveUIFont == null ? 16 : Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont)); var width = 0; var lines = CodeHint.Split('\n');
            foreach (var line in lines) width = Math.Max(width, (int)MathF.Ceiling(EffectiveUIFont == null ? line.Length * 8 : TextMetrics.Measure(EffectiveUIFont, line).X));
            width += 12; var height = lines.Length * lineHeight + 8; var wrapIndex = GetLineWrapIndexAtColumn(CaretLine, CaretColumnInLine); var wrapStart = GetLineWrapStartColumn(CaretLine, wrapIndex);
            var caretX = EffectiveUIFont == null ? (CaretColumnInLine - wrapStart) * 8 : TextMetrics.Layout(EffectiveUIFont, GetLine(CaretLine).Substring(wrapStart, GetLineWrapLength(CaretLine, wrapIndex))).GetCaretPosition(CaretColumnInLine - wrapStart).X;
            var x = (int)(GlobalPosition.X + Padding.Left + TextContentLeftInset + caretX);
            var caretY = (int)(GlobalPosition.Y + Padding.Top + Math.Max(0, GetVisibleRow(CaretLine, wrapIndex)) * lineHeight);
            var y = CodeHintDrawBelow ? caretY + lineHeight : caretY - height;
            if (y < Bounds.Top && caretY + lineHeight + height <= Bounds.Bottom) y = caretY + lineHeight;
            y = MathHelper.Clamp(y, Bounds.Top, Math.Max(Bounds.Top, Bounds.Bottom - height));
            x = MathHelper.Clamp(x, Bounds.Left, Math.Max(Bounds.Left, Bounds.Right - width));
            return new Rectangle(x, y, Math.Min(width, Bounds.Width), Math.Min(height, Bounds.Height));
        }
        public void SetLineLengthGuidelines(IEnumerable<int> guidelineColumns)
        {
            _lineLengthGuidelines.Clear();
            if (guidelineColumns != null) foreach (var column in guidelineColumns) if (column > 0) _lineLengthGuidelines.Add(column);
            QueueLayout();
        }
        public IReadOnlyList<int> GetLineLengthGuidelines() => _lineLengthGuidelines;
        /// <summary>Returns the screen-space x coordinate for a configured guideline column.</summary>
        public int GetLineLengthGuidelineX(int column)
        {
            var characterWidth = EffectiveUIFont == null ? 8 : TextMetrics.Measure(EffectiveUIFont, "0").X;
            var x = (int)MathF.Round(GlobalPosition.X + Padding.Left + TextContentLeftInset + characterWidth * Math.Max(0, column));
            return IsLayoutRtl() ? Bounds.Right - (x - Bounds.Left) : x;
        }
        public void SetSymbolLookupOnClickEnabled(bool enabled) { SymbolLookupOnClickEnabled = enabled; SetSymbolLookupWordAsValid(false); }
        public bool IsSymbolLookupOnClickEnabled() => SymbolLookupOnClickEnabled;
        /// <summary>Returns complete source text with U+FFFF at the last pointer position used for symbol lookup, or the caret when there is none.</summary>
        public string GetTextForSymbolLookup()
        {
            if (_lastSymbolLookupPosition.X >= 0 && _lastSymbolLookupPosition.Y >= 0 && EffectiveUIFont != null)
            {
                var row = (int)((_lastSymbolLookupPosition.Y - GlobalPosition.Y - Padding.Top) / Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont)));
                var line = GetLineAtVisibleRow(row); var wrapIndex = GetLineWrapIndexAtVisibleRow(row); var start = GetLineWrapStartColumn(line, wrapIndex); var length = GetLineWrapLength(line, wrapIndex); var localX = _lastSymbolLookupPosition.X - GlobalPosition.X - Padding.Left - TextContentLeftInset;
                var column = start + TextMetrics.Layout(EffectiveUIFont, GetLine(line).Substring(start, length)).HitTest(new Vector2(localX, 0));
                return GetTextWithCursorChar(line, column);
            }
            return GetTextWithCursorChar(CaretLine, CaretColumnInLine);
        }
        public string GetTextWithCursorChar(int line, int column)
        {
            if (line < 0 || line >= LineCount) return Text;
            var start = GetTextIndexForLine(line, GetEditableLines());
            return Text.Insert(start + MathHelper.Clamp(column, 0, GetLine(line).Length), "\uffff");
        }
        public string GetLookupWord(int line, int column)
        {
            if (line < 0 || line >= LineCount || column < 0) return string.Empty;
            var text = GetLine(line); if (string.IsNullOrEmpty(text)) return string.Empty;
            var index = Math.Min(column, text.Length - 1);
            if (!IsCompletionSymbol(text[index]) && index > 0) index--;
            if (!IsCompletionSymbol(text[index])) return string.Empty;
            var start = index; var end = index + 1;
            while (start > 0 && IsCompletionSymbol(text[start - 1])) start--;
            while (end < text.Length && IsCompletionSymbol(text[end])) end++;
            return text.Substring(start, end - start);
        }
        /// <summary>Accepts or rejects the pending Command/Ctrl-click lookup word after the host resolves it.</summary>
        public void SetSymbolLookupWordAsValid(bool valid) { _symbolLookupWord = valid ? _pendingSymbolLookupWord : string.Empty; _pendingSymbolLookupWord = string.Empty; }
        /// <summary>Returns the complete text with U+FFFF at the active caret, matching Godot's completion-provider contract.</summary>
        public string GetTextForCodeCompletion() => Text.Insert(CaretColumn, "\uffff");
        public void RequestCodeCompletion(bool force = false)
        {
            if (!CodeCompletionEnabled) return;
            CodeCompletionRequested?.Invoke(this, force);
            UpdateCodeCompletionOptions(force);
        }
        public void AddCodeCompletionOption(CodeCompletionKind type, string displayText, string insertText, Color? textColor = null, object value = null, int location = 0, Texture2D icon = null)
        {
            _submittedCompletionOptions.Add(new CodeCompletionOption(type, displayText, insertText, textColor, value, location, icon));
        }
        public void UpdateCodeCompletionOptions(bool forced = false)
        {
            if (_submittedCompletionOptions.Count > 0)
            {
                _codeCompletionSources.Clear(); _codeCompletionSources.AddRange(_submittedCompletionOptions); _submittedCompletionOptions.Clear();
            }
            _codeCompletionOptions.Clear();
            _codeCompletionBase = GetCodeCompletionBase();
            foreach (var option in _codeCompletionSources)
            {
                if (forced || string.IsNullOrEmpty(_codeCompletionBase))
                {
                    option.MatchSegments = Array.Empty<Point>();
                    _codeCompletionOptions.Add(option);
                    continue;
                }
                if (TryGetCompletionMatch(option.DisplayText, _codeCompletionBase, out var segments))
                {
                    option.MatchSegments = segments;
                    _codeCompletionOptions.Add(option);
                }
            }
            if (!forced && !string.IsNullOrEmpty(_codeCompletionBase)) _codeCompletionOptions.Sort(CompareCompletionOptions);
            _codeCompletionSelectedIndex = _codeCompletionOptions.Count == 0 ? -1 : 0;
            QueueLayout();
        }
        public CodeCompletionOption GetCodeCompletionOption(int index)
        {
            if (!IsCodeCompletionActive || index < 0 || index >= _codeCompletionOptions.Count) return null;
            return _codeCompletionOptions[index];
        }
        public int GetCodeCompletionSelectedIndex() => IsCodeCompletionActive ? _codeCompletionSelectedIndex : -1;
        public void SetCodeCompletionSelectedIndex(int index)
        {
            if (!IsCodeCompletionActive || index < 0 || index >= _codeCompletionOptions.Count) return;
            _codeCompletionSelectedIndex = index;
        }
        public void ConfirmCodeCompletion(bool replace = false)
        {
            if (!Editable || !IsCodeCompletionActive) return;
            var option = _codeCompletionOptions[_codeCompletionSelectedIndex];
            var start = Math.Max(0, CaretColumn - _codeCompletionBase.Length); var end = CaretColumn;
            if (replace) while (end < Text.Length && IsCompletionSymbol(Text[end])) end++;
            Text = Text.Remove(start, end - start).Insert(start, option.InsertText);
            CaretColumn = start + option.InsertText.Length; Deselect(); CancelCodeCompletion();
        }
        public void CancelCodeCompletion()
        {
            _codeCompletionOptions.Clear(); _codeCompletionSources.Clear(); _submittedCompletionOptions.Clear(); _codeCompletionSelectedIndex = -1; _codeCompletionBase = string.Empty; _codeCompletionBounds = Rectangle.Empty; QueueLayout();
        }
        /// <summary>Adds a comment delimiter. Line-only delimiters enable code-region tags; paired delimiters enable multiline comment folding.</summary>
        public void AddCommentDelimiter(string startKey, string endKey = "", bool lineOnly = false)
        {
            if (string.IsNullOrEmpty(startKey)) throw new ArgumentException("A comment delimiter is required.", nameof(startKey));
            if (lineOnly && string.IsNullOrEmpty(endKey))
            {
                if (!_lineCommentDelimiters.Contains(startKey)) _lineCommentDelimiters.Add(startKey);
                return;
            }
            AddDelimiter(_commentDelimiters, startKey, endKey, lineOnly);
        }
        public void RemoveCommentDelimiter(string startKey) { _lineCommentDelimiters.Remove(startKey ?? string.Empty); RemoveDelimiter(_commentDelimiters, startKey); }
        public bool HasCommentDelimiter(string startKey) => !string.IsNullOrEmpty(startKey) && (_lineCommentDelimiters.Contains(startKey) || HasDelimiter(_commentDelimiters, startKey));
        public void ClearCommentDelimiters() { _lineCommentDelimiters.Clear(); _commentDelimiters.Clear(); }
        public void AddStringDelimiter(string startKey, string endKey, bool lineOnly = false) => AddDelimiter(_stringDelimiters, startKey, endKey, lineOnly);
        public void RemoveStringDelimiter(string startKey) => RemoveDelimiter(_stringDelimiters, startKey);
        public bool HasStringDelimiter(string startKey) => HasDelimiter(_stringDelimiters, startKey);
        public void ClearStringDelimiters() => _stringDelimiters.Clear();
        public string GetCodeRegionStartTag() => _codeRegionStartTag;
        public string GetCodeRegionEndTag() => _codeRegionEndTag;
        public void SetCodeRegionTags(string start = "region", string end = "endregion")
        {
            if (string.IsNullOrEmpty(start)) throw new ArgumentException("A starting region tag is required.", nameof(start));
            if (string.IsNullOrEmpty(end)) throw new ArgumentException("An ending region tag is required.", nameof(end));
            if (string.Equals(start, end, StringComparison.Ordinal)) throw new ArgumentException("Starting and ending region tags must differ.", nameof(end));
            _codeRegionStartTag = start; _codeRegionEndTag = end;
        }
        public bool IsLineCodeRegionStart(int line) => IsCodeRegionLine(line, _codeRegionStartTag);
        public bool IsLineCodeRegionEnd(int line) => IsCodeRegionLine(line, _codeRegionEndTag);
        /// <summary>Wraps the selected line range in configured code-region tags and folds the new region.</summary>
        public void CreateCodeRegion()
        {
            if (!HasSelection || _lineCommentDelimiters.Count == 0) return;
            var first = GetLineFromTextIndex(SelectionFrom);
            var last = GetLineFromTextIndex(Math.Max(SelectionFrom, SelectionTo - 1));
            var prefix = _lineCommentDelimiters[0];
            InsertLineAt(last + 1, prefix + _codeRegionEndTag);
            InsertLineAt(first, prefix + _codeRegionStartTag + " New Code Region");
            FoldLine(first);
        }
        public void AddAutoBraceCompletionPair(string openKey, string closeKey)
        {
            if (string.IsNullOrEmpty(openKey)) throw new ArgumentException("An opening key is required.", nameof(openKey));
            _bracePairs[openKey] = closeKey ?? string.Empty;
        }
        public bool HasAutoBraceCompletionOpenKey(string openKey) => !string.IsNullOrEmpty(openKey) && _bracePairs.ContainsKey(openKey);
        public bool HasAutoBraceCompletionCloseKey(string closeKey)
        {
            if (string.IsNullOrEmpty(closeKey)) return false;
            foreach (var pair in _bracePairs) if (pair.Value == closeKey) return true;
            return false;
        }
        public string GetAutoBraceCompletionCloseKey(string openKey) => openKey != null && _bracePairs.TryGetValue(openKey, out var closeKey) ? closeKey : string.Empty;
        /// <summary>Routes normal window text input through CodeEdit's brace and indentation behavior.</summary>
        public override void InsertText(string text) => InsertCodeText(text);
        public void InsertCodeText(string text)
        {
            if (string.IsNullOrEmpty(text) || !Editable) return;
            if (text == "\n") { InsertAutoIndentedNewline(); return; }
            if (AutoBraceCompletionEnabled && !HasSelection && _bracePairs.TryGetValue(text, out var close))
            {
                base.InsertText(text + close);
                CaretColumn -= close.Length;
                return;
            }
            if (AutoBraceCompletionEnabled && !HasSelection && HasAutoBraceCompletionCloseKey(text) && CaretColumn + text.Length <= Text.Length && Text.Substring(CaretColumn, text.Length) == text)
            {
                CaretColumn += text.Length;
                return;
            }
            base.InsertText(text);
            if (IsCodeCompletionActive) UpdateCodeCompletionOptions();
            else if (IsCompletionPrefix(text)) RequestCodeCompletion();
        }
        public void SetLineAsBreakpoint(int line, bool breakpointed) => SetLineState(_breakpointedLines, line, breakpointed);
        public bool IsLineBreakpointed(int line) => _breakpointedLines.Contains(ValidateLineNumber(line));
        public void ClearBreakpointedLines() => _breakpointedLines.Clear();
        public IReadOnlyList<int> GetBreakpointedLines() => GetOrderedLines(_breakpointedLines);
        public void SetLineAsBookmarked(int line, bool bookmarked) => SetLineState(_bookmarkedLines, line, bookmarked);
        public bool IsLineBookmarked(int line) => _bookmarkedLines.Contains(ValidateLineNumber(line));
        public void ClearBookmarkedLines() => _bookmarkedLines.Clear();
        public IReadOnlyList<int> GetBookmarkedLines() => GetOrderedLines(_bookmarkedLines);
        public void SetLineAsExecuting(int line, bool executing) => SetLineState(_executingLines, line, executing);
        public bool IsLineExecuting(int line) => _executingLines.Contains(ValidateLineNumber(line));
        public void ClearExecutingLines() => _executingLines.Clear();
        public IReadOnlyList<int> GetExecutingLines() => GetOrderedLines(_executingLines);
        public void SetDrawFoldGutter(bool draw) { DrawFoldGutter = draw; QueueLayout(); }
        public bool IsDrawingFoldGutter() => DrawFoldGutter;
        public void SetLineFoldingEnabled(bool enabled) => LineFoldingEnabled = enabled;
        public bool IsLineFoldingEnabled() => LineFoldingEnabled;
        /// <summary>Returns whether this non-empty line introduces an indented child block that can be folded.</summary>
        public bool CanFoldLine(int line)
        {
            ValidateLineNumber(line);
            if (!LineFoldingEnabled || _hiddenLines.Contains(line) || _foldedLines.Contains(line) || string.IsNullOrWhiteSpace(GetLine(line))) return false;
            if (IsLineCodeRegionEnd(line)) return false;
            if (IsLineCodeRegionStart(line)) return FindCodeRegionEnd(line) >= 0;
            if (TryFindDelimitedBlockEnd(line, out _)) return true;
            var indent = GetIndentLevel(GetLine(line));
            for (var next = line + 1; next < LineCount; next++)
            {
                if (string.IsNullOrWhiteSpace(GetLine(next))) continue;
                return GetIndentLevel(GetLine(next)) > indent;
            }
            return false;
        }
        /// <summary>Hides the contiguous indented block below <paramref name="line"/>, matching Godot's source folding model.</summary>
        public void FoldLine(int line)
        {
            if (!CanFoldLine(line)) return;
            var last = line;
            if (IsLineCodeRegionStart(line)) last = FindCodeRegionEnd(line);
            else if (TryFindDelimitedBlockEnd(line, out var delimiterEnd)) last = delimiterEnd;
            else
            {
                var indent = GetIndentLevel(GetLine(line));
                for (var next = line + 1; next < LineCount; next++)
                {
                    if (string.IsNullOrWhiteSpace(GetLine(next))) { last = next; continue; }
                    if (GetIndentLevel(GetLine(next)) <= indent) break;
                    last = next;
                }
            }
            for (var hidden = line + 1; hidden <= last; hidden++) _hiddenLines.Add(hidden);
            _foldedLines.Add(line);
            if (_hiddenLines.Contains(CaretLine)) SetCaret(line, GetLine(line).Length);
            SetLineAsFirstVisible(FirstVisibleLine); FoldStateChanged?.Invoke(this); QueueLayout();
        }
        /// <summary>Reveals a folded header, or the header that contains the requested hidden line.</summary>
        public void UnfoldLine(int line)
        {
            ValidateLineNumber(line);
            var header = FindFoldHeader(line);
            if (header < 0) return;
            var first = header + 1;
            while (first < LineCount && _hiddenLines.Contains(first)) { _hiddenLines.Remove(first); _foldedLines.Remove(first); first++; }
            _foldedLines.Remove(header);
            SetLineAsFirstVisible(FirstVisibleLine); FoldStateChanged?.Invoke(this); QueueLayout();
        }
        public void FoldAllLines()
        {
            var changed = false;
            for (var line = 0; line < LineCount; line++)
            {
                if (!CanFoldLine(line)) continue;
                FoldLine(line); changed = true;
            }
            if (!changed) return;
        }
        public void UnfoldAllLines()
        {
            if (_hiddenLines.Count == 0 && _foldedLines.Count == 0) return;
            _hiddenLines.Clear(); _foldedLines.Clear(); SetLineAsFirstVisible(FirstVisibleLine); FoldStateChanged?.Invoke(this); QueueLayout();
        }
        public void ToggleFoldableLine(int line) { if (IsLineFolded(line)) UnfoldLine(line); else FoldLine(line); }
        public void ToggleFoldableLinesAtCarets() => ToggleFoldableLine(CaretLine);
        public int GetFoldedLineHeader(int line) { ValidateLineNumber(line); return FindFoldHeader(line); }
        public bool IsLineFolded(int line) { ValidateLineNumber(line); return _foldedLines.Contains(line); }
        public IReadOnlyList<int> GetFoldedLines() => GetOrderedLines(_foldedLines);
        /// <summary>Moves the selected lines, or the caret line, one logical row upward without changing their contents.</summary>
        public void MoveLinesUp()
        {
            if (CaretCount > 1)
            {
                var editableLines = GetEditableLines(); var targets = new List<CommandLineRange>();
                foreach (var range in GetCommandLineRanges())
                {
                    if (range.First <= 0) { targets.Add(range); continue; }
                    var movedBlock = editableLines.GetRange(range.First, range.Last - range.First + 1);
                    editableLines.RemoveRange(range.First, movedBlock.Count); editableLines.InsertRange(range.First - 1, movedBlock);
                    targets.Add(new CommandLineRange(range.First - 1, range.Last - 1));
                }
                SetEditableLinesAtCarets(editableLines, targets, true);
                return;
            }
            GetCommandLineRange(out var first, out var last);
            if (first <= 0) return;
            var lines = GetEditableLines(); var count = last - first + 1; var block = lines.GetRange(first, count);
            lines.RemoveRange(first, count); lines.InsertRange(first - 1, block); SetEditableLines(lines, first - 1, last - 1);
        }
        /// <summary>Moves the selected lines, or the caret line, one logical row downward without changing their contents.</summary>
        public void MoveLinesDown()
        {
            if (CaretCount > 1)
            {
                var editableLines = GetEditableLines(); var ranges = GetCommandLineRanges(); var targets = new List<CommandLineRange>();
                for (var index = ranges.Count - 1; index >= 0; index--)
                {
                    var range = ranges[index];
                    if (range.Last >= editableLines.Count - 1) { targets.Add(range); continue; }
                    var movedBlock = editableLines.GetRange(range.First, range.Last - range.First + 1);
                    editableLines.RemoveRange(range.First, movedBlock.Count); editableLines.InsertRange(range.First + 1, movedBlock);
                    targets.Add(new CommandLineRange(range.First + 1, range.Last + 1));
                }
                targets.Sort((left, right) => left.First.CompareTo(right.First));
                SetEditableLinesAtCarets(editableLines, targets, true);
                return;
            }
            GetCommandLineRange(out var first, out var last);
            if (last >= LineCount - 1) return;
            var lines = GetEditableLines(); var count = last - first + 1; var block = lines.GetRange(first, count);
            lines.RemoveRange(first, count); lines.InsertRange(first + 1, block); SetEditableLines(lines, first + 1, last + 1);
        }
        /// <summary>Deletes the selected lines, or the caret line, retaining a single empty line for an otherwise empty document.</summary>
        public void DeleteLines()
        {
            if (CaretCount > 1)
            {
                var editableLines = GetEditableLines(); var ranges = GetCommandLineRanges();
                for (var index = ranges.Count - 1; index >= 0; index--)
                {
                    var range = ranges[index];
                    editableLines.RemoveRange(range.First, range.Last - range.First + 1);
                }
                if (editableLines.Count == 0) editableLines.Add(string.Empty);
                var targets = new List<CommandLineRange>();
                foreach (var range in ranges) targets.Add(new CommandLineRange(Math.Min(range.First, editableLines.Count - 1), Math.Min(range.First, editableLines.Count - 1)));
                SetEditableLinesAtCarets(editableLines, targets, false);
                return;
            }
            GetCommandLineRange(out var first, out var last);
            var lines = GetEditableLines(); lines.RemoveRange(first, last - first + 1);
            if (lines.Count == 0) lines.Add(string.Empty);
            Text = string.Join("\n", lines); SetCaret(Math.Min(first, lines.Count - 1), 0);
        }
        /// <summary>Joins the selected line range, or the caret line and its next line, using <paramref name="lineEnding"/>.</summary>
        public void JoinLines(string lineEnding = " ")
        {
            if (CaretCount > 1)
            {
                if ((lineEnding ?? string.Empty).IndexOf('\n') >= 0) throw new ArgumentException("Cannot join lines with a newline.", nameof(lineEnding));
                var editableLines = GetEditableLines(); var targets = new List<CommandLineRange>(); var lineOffset = 0;
                foreach (var range in GetCommandLineRanges())
                {
                    var line = range.First + lineOffset;
                    for (var join = range.First; join <= range.Last && line + 1 < editableLines.Count; join++)
                    {
                        var current = editableLines[line]; var next = editableLines[line + 1]; var currentEnd = current.Length;
                        while (currentEnd > 0 && char.IsWhiteSpace(current[currentEnd - 1])) currentEnd--;
                        var nextStart = 0;
                        while (nextStart < next.Length && char.IsWhiteSpace(next[nextStart])) nextStart++;
                        editableLines[line] = current.Substring(0, currentEnd) + (nextStart < next.Length && currentEnd > 0 ? lineEnding ?? string.Empty : string.Empty) + next.Substring(nextStart);
                        editableLines.RemoveAt(line + 1); lineOffset--;
                    }
                    targets.Add(new CommandLineRange(Math.Min(line, editableLines.Count - 1), Math.Min(line, editableLines.Count - 1)));
                }
                SetEditableLinesAtCarets(editableLines, targets, false);
                return;
            }
            GetCommandLineRange(out var first, out var last);
            if (first == last) last = Math.Min(LineCount - 1, first + 1);
            if (first == last) return;
            var lines = GetEditableLines(); var joined = lines[first];
            for (var line = first + 1; line <= last; line++)
            {
                var currentEnd = joined.Length;
                while (currentEnd > 0 && char.IsWhiteSpace(joined[currentEnd - 1])) currentEnd--;
                var next = lines[line]; var nextStart = 0;
                while (nextStart < next.Length && char.IsWhiteSpace(next[nextStart])) nextStart++;
                joined = joined.Substring(0, currentEnd) + (nextStart < next.Length && currentEnd > 0 ? lineEnding ?? string.Empty : string.Empty) + next.Substring(nextStart);
            }
            lines.RemoveRange(first, last - first + 1); lines.Insert(first, joined); Text = string.Join("\n", lines); SetCaret(first, joined.Length);
        }
        /// <summary>Duplicates the selected text, or delegates to <see cref="DuplicateLines"/> when no range is selected.</summary>
        public void DuplicateSelection()
        {
            if (CaretCount > 1)
            {
                var actions = new List<CommandTextInsertion>();
                for (var caret = 0; caret < CaretCount; caret++)
                {
                    if (HasCaretSelection(caret))
                    {
                        var selectionFrom = GetSelectionFrom(caret); var to = GetSelectionTo(caret);
                        actions.Add(new CommandTextInsertion(selectionFrom, Text.Substring(selectionFrom, to - selectionFrom), true, to - selectionFrom));
                    }
                    else
                    {
                        var line = GetCaretLine(caret); actions.Add(new CommandTextInsertion(GetTextIndexForLine(line, GetEditableLines()), GetLine(line) + "\n", false, 0));
                    }
                }
                actions.Sort((left, right) => right.Start.CompareTo(left.Start));
                var documentText = Text;
                foreach (var action in actions) documentText = documentText.Insert(action.Start, action.Text);
                actions.Sort((left, right) => left.Start.CompareTo(right.Start));
                var targets = new List<CommandTextRange>(); var offset = 0;
                foreach (var action in actions)
                {
                    var insertedStart = action.Start + offset;
                    if (action.SelectInsertedOriginal) targets.Add(new CommandTextRange(insertedStart + action.Text.Length, insertedStart + action.Text.Length + action.OriginalLength));
                    else targets.Add(new CommandTextRange(insertedStart + action.Text.Length, insertedStart + action.Text.Length));
                    offset += action.Text.Length;
                }
                SetTextAtCarets(documentText, targets);
                return;
            }
            if (!HasSelection) { DuplicateLines(); return; }
            var from = SelectionFrom; var text = SelectedText; Text = Text.Insert(SelectionTo, text); Select(SelectionTo, SelectionTo + text.Length);
        }
        /// <summary>Duplicates the selected lines, or the caret line, immediately below the original range.</summary>
        public void DuplicateLines()
        {
            if (CaretCount > 1)
            {
                var editableLines = GetEditableLines(); var ranges = GetCommandLineRanges(mergeAdjacent: false); var targets = new List<CommandLineRange>();
                for (var index = ranges.Count - 1; index >= 0; index--)
                {
                    var range = ranges[index]; var copiedBlock = editableLines.GetRange(range.First, range.Last - range.First + 1);
                    editableLines.InsertRange(range.Last + 1, copiedBlock); targets.Add(new CommandLineRange(range.Last + 1, range.Last + copiedBlock.Count));
                }
                targets.Sort((left, right) => left.First.CompareTo(right.First));
                SetEditableLinesAtCarets(editableLines, targets, true);
                return;
            }
            GetCommandLineRange(out var first, out var last);
            var lines = GetEditableLines(); var block = lines.GetRange(first, last - first + 1); lines.InsertRange(last + 1, block); SetEditableLines(lines, last + 1, last + block.Count);
        }
        protected internal override void PointerPressed(Point position)
        {
            _lastSymbolLookupPosition = position;
            if (DrawMinimap && GetMinimapBounds().Contains(position))
            {
                _draggingMinimap = true;
                ScrollMinimapTo(position.Y);
                return;
            }
            if (IsCodeCompletionActive && _codeCompletionBounds.Contains(position))
            {
                var rowHeight = EffectiveUIFont == null ? 16 : Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont));
                var row = MathHelper.Clamp((position.Y - _codeCompletionBounds.Y) / rowHeight, 0, _codeCompletionOptions.Count - 1);
                SetCodeCompletionSelectedIndex(row);
                ConfirmCodeCompletion();
                return;
            }
            if (DrawFoldGutter && EffectiveUIFont != null)
            {
                var foldLeft = Bounds.X + (int)MathF.Ceiling(base.TextContentLeftInset + GetLineNumberGutterWidth());
                if (new Rectangle(foldLeft, Bounds.Y, 14, Bounds.Height).Contains(position))
                {
                    var row = (int)((position.Y - GlobalPosition.Y - Padding.Top) / Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont)));
                    if (GetLineWrapIndexAtVisibleRow(row) == 0)
                    {
                        ToggleFoldableLine(GetLineAtVisibleRow(row));
                        return;
                    }
                }
            }
            base.PointerPressed(position);
            if (SymbolLookupOnClickEnabled && HasCommandModifier())
            {
                _pendingSymbolLookupWord = GetLookupWord(CaretLine, CaretColumnInLine);
                if (!string.IsNullOrEmpty(_pendingSymbolLookupWord)) SymbolLookupRequested?.Invoke(this, _pendingSymbolLookupWord, CaretLine, CaretColumnInLine);
            }
        }
        protected internal override void PointerMoved(Point position)
        {
            if (_draggingMinimap) { ScrollMinimapTo(position.Y); return; }
            base.PointerMoved(position);
        }
        protected internal override void PointerReleased(Point position, bool isInside)
        {
            _draggingMinimap = false;
            base.PointerReleased(position, isInside);
        }
        internal override void CancelInput() { _draggingMinimap = false; base.CancelInput(); }
        /// <summary>Indents the current selection, or the caret line when no selection exists.</summary>
        public void IndentLines() => ChangeIndent(true);
        /// <summary>Removes one configured indentation level from the current selection or caret line.</summary>
        public void UnindentLines() => ChangeIndent(false);
        internal override void KeyPressed(Keys key)
        {
            if (IsCodeCompletionActive)
            {
                if (key == Keys.Up) { SetCodeCompletionSelectedIndex((_codeCompletionSelectedIndex + _codeCompletionOptions.Count - 1) % _codeCompletionOptions.Count); return; }
                if (key == Keys.Down) { SetCodeCompletionSelectedIndex((_codeCompletionSelectedIndex + 1) % _codeCompletionOptions.Count); return; }
                if (key == Keys.PageUp) { SetCodeCompletionSelectedIndex(Math.Max(0, _codeCompletionSelectedIndex - Math.Max(1, CodeCompletionMaxLines))); return; }
                if (key == Keys.PageDown) { SetCodeCompletionSelectedIndex(Math.Min(_codeCompletionOptions.Count - 1, _codeCompletionSelectedIndex + Math.Max(1, CodeCompletionMaxLines))); return; }
                if (key == Keys.Enter || key == Keys.Tab) { ConfirmCodeCompletion(); return; }
                if (key == Keys.Escape) { CancelCodeCompletion(); return; }
            }
            if (key == Keys.Space && HasCommandModifier()) { RequestCodeCompletion(true); return; }
            if (key == Keys.Enter) InsertAutoIndentedNewline();
            else base.KeyPressed(key);
            LineChanged?.Invoke(this, Text.Substring(0, CaretColumn).Split('\n').Length - 1);
        }
        /// <summary>Returns the retained minimap lane, or an empty rectangle while minimap drawing is disabled.</summary>
        public Rectangle GetMinimapBounds() => DrawMinimap ? new Rectangle(Bounds.Right - 28, Bounds.Y + 2, 26, Math.Max(0, Bounds.Height - 4)) : Rectangle.Empty;
        /// <summary>Returns the minimap rectangle representing the current wrap-aware code viewport.</summary>
        public Rectangle GetMinimapViewportBounds()
        {
            var minimap = GetMinimapBounds(); if (minimap.IsEmpty) return Rectangle.Empty;
            var rowCount = Math.Max(1, GetTotalVisibleLineCount()); var firstRow = GetScrollPosForLine(FirstVisibleLine, FirstVisibleLineWrapIndex); var visibleRows = GetVisibleLineCount();
            var top = minimap.Y + (int)MathF.Floor(firstRow * minimap.Height / (float)rowCount);
            var bottom = minimap.Y + (int)MathF.Ceiling(Math.Min(rowCount, firstRow + visibleRows) * minimap.Height / (float)rowCount);
            return new Rectangle(minimap.X + 1, top, Math.Max(1, minimap.Width - 2), Math.Max(1, bottom - top));
        }
        private void ScrollMinimapTo(int pointerY)
        {
            var minimap = GetMinimapBounds();
            if (minimap.IsEmpty) return;
            var totalRows = Math.Max(1, GetTotalVisibleLineCount());
            var targetRow = MathHelper.Clamp((int)MathF.Floor((pointerY - minimap.Y) * totalRows / (float)Math.Max(1, minimap.Height)), 0, totalRows - 1);
            var firstRow = Math.Max(0, targetRow - GetVisibleLineCount() / 2);
            SetLineAsFirstVisible(GetLineAtVisibleRow(firstRow), GetLineWrapIndexAtVisibleRow(firstRow));
        }
        internal override void DrawEditor(UIRenderContext context)
        {
            base.DrawEditor(context);
            DrawLineLengthGuidelines(context);
            var gutterWidth = (int)MathF.Ceiling(GetGutterWidth());
            var gutterLeft = Bounds.X + (int)MathF.Ceiling(base.TextContentLeftInset);
            if (gutterWidth > 0) context.Fill(new Rectangle(gutterLeft, Bounds.Y, gutterWidth, Bounds.Height), context.Theme.PanelColor);
            if (EffectiveUIFont != null && gutterWidth > 0)
            {
                var numberWidth = GetLineNumberGutterWidth();
                var y = Bounds.Y + Padding.Top;
                for (var line = FirstVisibleLine; line >= 0 && line < LineCount && y + TextMetrics.LineHeight(EffectiveUIFont) <= Bounds.Bottom; line++)
                {
                    if (IsLineHiddenForDisplay(line)) continue;
                    var firstWrap = line == FirstVisibleLine ? FirstVisibleLineWrapIndex : 0;
                    if (firstWrap == 0 && DrawLineNumbers)
                    {
                        var number = (line + 1).ToString(LineNumbersZeroPadded ? new string('0', Math.Max(1, LineNumbersMinDigits)) : null, System.Globalization.CultureInfo.InvariantCulture);
                        var measured = TextMetrics.Measure(EffectiveUIFont, number).X;
                        context.Text(EffectiveUIFont, number, new Vector2(gutterLeft + numberWidth - measured - 4, y), context.Theme.DisabledTextColor);
                    }
                    var markerX = (int)(gutterLeft + numberWidth + 2);
                    var markerName = DrawBreakpointsGutter && _breakpointedLines.Contains(line) ? "breakpoint" : DrawBookmarksGutter && _bookmarkedLines.Contains(line) ? "bookmark" : DrawExecutingLinesGutter && _executingLines.Contains(line) ? "executing_line" : null;
                    if (firstWrap == 0 && markerName != null)
                    {
                        var marker = GetThemeIcon(markerName); var markerBounds = new Rectangle(markerX, (int)y + Math.Max(1, (TextMetrics.LineHeight(EffectiveUIFont) - 8) / 2), 8, 8);
                        if (marker.HasValue) context.Icon(marker.Value, new Vector2(markerX, y + (TextMetrics.LineHeight(EffectiveUIFont) - marker.Value.LogicalSize.Y) / 2), Color.White);
                        else context.Fill(markerBounds, markerName == "breakpoint" ? BreakpointColor : markerName == "bookmark" ? BookmarkColor : ExecutingLineColor);
                    }
                    if (firstWrap == 0 && DrawFoldGutter && (CanFoldLine(line) || IsLineFolded(line)))
                    {
                        var foldX = markerX + (DrawBreakpointsGutter || DrawBookmarksGutter || DrawExecutingLinesGutter ? 12 : 0);
                        var codeRegion = IsLineCodeRegionStart(line);
                        var foldName = IsLineFolded(line) ? codeRegion ? "folded_code_region" : "folded" : codeRegion ? "can_fold_code_region" : "can_fold";
                        var fold = GetThemeIcon(foldName);
                        if (fold.HasValue) context.Icon(fold.Value, new Vector2(foldX, y + (TextMetrics.LineHeight(EffectiveUIFont) - fold.Value.LogicalSize.Y) / 2), Color.White);
                        else if (IsLineFolded(line)) context.Fill(new Rectangle(foldX + 3, (int)y + 4, 7, 7), context.Theme.DisabledTextColor);
                        else context.Border(new Rectangle(foldX + 3, (int)y + 4, 7, 7), context.Theme.DisabledTextColor);
                    }
                    if (firstWrap == 0 && IsLineFolded(line))
                    {
                        var foldedEol = GetThemeIcon("folded_eol_icon");
                        if (foldedEol.HasValue)
                        {
                            var textX = GlobalPosition.X + Padding.Left + TextContentLeftInset + TextMetrics.Measure(EffectiveUIFont, GetLine(line)).X + 2;
                            context.Icon(foldedEol.Value, new Vector2(textX, y + (TextMetrics.LineHeight(EffectiveUIFont) - foldedEol.Value.LogicalSize.Y) / 2), context.Theme.DisabledTextColor);
                        }
                    }
                    y += TextMetrics.LineHeight(EffectiveUIFont) * (GetLineWrapCount(line) + 1 - firstWrap);
                }
            }
            if (DrawMinimap)
            {
                var minimap = GetMinimapBounds();
                context.Fill(minimap, context.Theme.PanelColor.WithAlpha(210));
                var rowCount = Math.Max(1, GetTotalVisibleLineCount()); var row = 0;
                context.Border(GetMinimapViewportBounds(), context.Theme.FocusColor);
                for (var line = 0; line < LineCount; line++)
                {
                    if (IsLineHiddenForDisplay(line)) continue;
                    var wrappedRows = GetLineWrappedText(line);
                    for (var wrapIndex = 0; wrapIndex < wrappedRows.Count; wrapIndex++)
                    {
                        var y = minimap.Y + (int)MathF.Floor(row * minimap.Height / (float)rowCount);
                        var width = GetLineWidth(line, wrapIndex);
                        context.Fill(new Rectangle(minimap.X + 3, y, Math.Max(1, Math.Min(minimap.Width - 6, (int)MathF.Ceiling(width / 4))), 1), context.Theme.DisabledTextColor);
                        row++;
                    }
                }
            }
            DrawCodeHint(context);
            DrawCodeCompletion(context);
        }
        private void InsertAutoIndentedNewline()
        {
            if (!Editable) return;
            if (!AutoIndentEnabled) { base.InsertText("\n"); return; }
            var source = GetLine(CaretLine);
            var prefixLength = 0;
            while (prefixLength < source.Length && char.IsWhiteSpace(source[prefixLength]) && source[prefixLength] != '\n') prefixLength++;
            var indent = source.Substring(0, prefixLength);
            var trimmed = source.TrimEnd();
            foreach (var suffix in _autoIndentPrefixes) if (trimmed.EndsWith(suffix, StringComparison.Ordinal)) { indent += IndentText; break; }
            base.InsertText("\n" + indent);
        }
        private void ChangeIndent(bool indent)
        {
            var hadSelection = HasSelection;
            var first = hadSelection ? GetLineFromTextIndex(SelectionFrom) : CaretLine;
            var last = hadSelection ? GetLineFromTextIndex(SelectionTo) : first;
            if (hadSelection && SelectionTo == Text.Length && Text.EndsWith("\n", StringComparison.Ordinal)) last--;
            var lines = new List<string>(); for (var line = 0; line < LineCount; line++) lines.Add(GetLine(line));
            for (var line = Math.Max(0, first); line <= Math.Max(0, last); line++)
            {
                if (indent) lines[line] = IndentText + lines[line];
                else if (lines[line].StartsWith("\t", StringComparison.Ordinal)) lines[line] = lines[line].Substring(1);
                else
                {
                    var remove = 0; while (remove < lines[line].Length && remove < IndentSize && lines[line][remove] == ' ') remove++;
                    lines[line] = lines[line].Substring(remove);
                }
            }
            Text = string.Join("\n", lines);
            if (hadSelection) Select(GetTextIndexForLine(first, lines), GetTextIndexForLine(last, lines) + lines[last].Length);
            else SetCaret(first, 0);
        }
        private float GetGutterWidth()
        {
            var width = GetLineNumberGutterWidth();
            if (DrawBreakpointsGutter || DrawBookmarksGutter || DrawExecutingLinesGutter) width += 14;
            if (DrawFoldGutter) width += 14;
            return width;
        }
        private float GetLineNumberGutterWidth()
        {
            if (!DrawLineNumbers) return 0;
            var digits = Math.Max(Math.Max(1, LineNumbersMinDigits), LineCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
            return (EffectiveUIFont == null ? 8 : TextMetrics.Measure(EffectiveUIFont, new string('0', digits)).X) + 10;
        }
        private int ValidateLineNumber(int line)
        {
            if (line < 0 || line >= LineCount) throw new ArgumentOutOfRangeException(nameof(line));
            return line;
        }
        private static int GetIndentLevel(string line)
        {
            var indent = 0;
            foreach (var character in line)
            {
                if (character == ' ') indent++;
                else if (character == '\t') indent += 4;
                else break;
            }
            return indent;
        }
        private int FindFoldHeader(int line)
        {
            if (_foldedLines.Contains(line)) return line;
            for (var header = line - 1; header >= 0; header--) if (_foldedLines.Contains(header) && _hiddenLines.Contains(line)) return header;
            return -1;
        }
        private void GetCommandLineRange(out int first, out int last)
        {
            first = CaretLine; last = first;
            if (!HasSelection) return;
            first = GetLineFromTextIndex(SelectionFrom);
            var selectionEnd = Math.Max(SelectionFrom, SelectionTo - 1);
            last = GetLineFromTextIndex(selectionEnd);
        }
        /// <summary>Gets Godot-style, source-ordered line ranges covered by the primary and secondary carets.</summary>
        private List<CommandLineRange> GetCommandLineRanges(bool onlySelections = false, bool mergeAdjacent = true)
        {
            var caretIndexes = new List<int>();
            for (var caret = 0; caret < CaretCount; caret++) if (!onlySelections || HasCaretSelection(caret)) caretIndexes.Add(caret);
            caretIndexes.Sort((left, right) => GetSelectionFrom(left).CompareTo(GetSelectionFrom(right)));
            var ranges = new List<CommandLineRange>(); var lastToLine = int.MinValue;
            foreach (var caret in caretIndexes)
            {
                var first = GetLineFromTextIndex(GetSelectionFrom(caret)); var to = GetSelectionTo(caret); var last = GetLineFromTextIndex(to);
                var toColumn = to - GetTextIndexForLine(last, GetEditableLines());
                if (HasCaretSelection(caret) && toColumn == 0) last--;
                if (last < first) last = first;
                if (ranges.Count > 0 && (first == lastToLine || (mergeAdjacent && first - 1 == lastToLine))) ranges[ranges.Count - 1].Last = Math.Max(ranges[ranges.Count - 1].Last, last);
                else ranges.Add(new CommandLineRange(first, last));
                lastToLine = Math.Max(lastToLine, last);
            }
            return ranges;
        }
        /// <summary>Commits a line command and restores one caret or line selection per resulting range.</summary>
        private void SetEditableLinesAtCarets(List<string> lines, List<CommandLineRange> ranges, bool selectRanges)
        {
            Text = string.Join("\n", lines); RemoveSecondaryCarets();
            if (ranges.Count == 0) { SetCaret(0, 0); return; }
            ranges.Sort((left, right) => left.First.CompareTo(right.First));
            for (var index = 0; index < ranges.Count; index++)
            {
                var range = ranges[index]; var first = MathHelper.Clamp(range.First, 0, lines.Count - 1); var last = MathHelper.Clamp(range.Last, first, lines.Count - 1);
                if (index == 0) SetCaret(first, 0); else AddCaret(first, 0);
                if (selectRanges && (first != last || lines[first].Length > 0)) Select(first, 0, last, lines[last].Length, index);
            }
        }
        /// <summary>Commits a text command and restores all resulting carets, including duplicate-selection ranges.</summary>
        private void SetTextAtCarets(string text, List<CommandTextRange> ranges)
        {
            Text = text; RemoveSecondaryCarets();
            if (ranges.Count == 0) { SetCaret(0, 0); return; }
            var lines = GetEditableLines();
            for (var index = 0; index < ranges.Count; index++)
            {
                var range = ranges[index]; var from = MathHelper.Clamp(range.From, 0, Text.Length); var to = MathHelper.Clamp(range.To, from, Text.Length);
                var fromLine = GetLineFromTextIndex(from); var fromColumn = from - GetTextIndexForLine(fromLine, lines);
                var toLine = GetLineFromTextIndex(to); var toColumn = to - GetTextIndexForLine(toLine, lines);
                if (index == 0) SetCaret(fromLine, fromColumn); else AddCaret(fromLine, fromColumn);
                if (to > from) Select(fromLine, fromColumn, toLine, toColumn, index);
            }
        }
        private List<string> GetEditableLines()
        {
            var lines = new List<string>(); for (var line = 0; line < LineCount; line++) lines.Add(GetLine(line)); return lines;
        }
        private void SetEditableLines(List<string> lines, int first, int last)
        {
            Text = string.Join("\n", lines);
            if (first == last) SetCaret(first, 0);
            else Select(GetTextIndexForLine(first, lines), GetTextIndexForLine(last, lines) + lines[last].Length);
        }
        private sealed class CommandLineRange
        {
            public CommandLineRange(int first, int last) { First = first; Last = last; }
            public int First;
            public int Last;
        }
        private sealed class CommandTextInsertion
        {
            public CommandTextInsertion(int start, string text, bool selectInsertedOriginal, int originalLength) { Start = start; Text = text; SelectInsertedOriginal = selectInsertedOriginal; OriginalLength = originalLength; }
            public int Start;
            public string Text;
            public bool SelectInsertedOriginal;
            public int OriginalLength;
        }
        private sealed class CommandTextRange
        {
            public CommandTextRange(int from, int to) { From = from; To = to; }
            public int From;
            public int To;
        }
        private string GetCodeCompletionBase()
        {
            var start = CaretColumn;
            while (start > 0 && IsCompletionSymbol(Text[start - 1])) start--;
            return Text.Substring(start, CaretColumn - start);
        }
        private static bool TryGetCompletionMatch(string candidate, string query, out IReadOnlyList<Point> segments)
        {
            var matches = new List<Point>();
            var candidateIndex = 0;
            for (var queryIndex = 0; queryIndex < query.Length; queryIndex++)
            {
                while (candidateIndex < candidate.Length && char.ToUpperInvariant(candidate[candidateIndex]) != char.ToUpperInvariant(query[queryIndex])) candidateIndex++;
                if (candidateIndex >= candidate.Length) { segments = Array.Empty<Point>(); return false; }
                if (matches.Count > 0 && matches[matches.Count - 1].X + matches[matches.Count - 1].Y == candidateIndex)
                {
                    var previous = matches[matches.Count - 1];
                    matches[matches.Count - 1] = new Point(previous.X, previous.Y + 1);
                }
                else matches.Add(new Point(candidateIndex, 1));
                candidateIndex++;
            }
            segments = matches;
            return true;
        }
        private static int CompareCompletionOptions(CodeCompletionOption left, CodeCompletionOption right)
        {
            var leftSegments = left.MatchSegments; var rightSegments = right.MatchSegments;
            var leftStart = leftSegments.Count == 0 ? 0 : leftSegments[0].X; var rightStart = rightSegments.Count == 0 ? 0 : rightSegments[0].X;
            var comparison = leftStart.CompareTo(rightStart);
            if (comparison != 0) return comparison;
            comparison = leftSegments.Count.CompareTo(rightSegments.Count);
            if (comparison != 0) return comparison;
            comparison = left.Location.CompareTo(right.Location);
            if (comparison != 0) return comparison;
            return string.Compare(left.DisplayText, right.DisplayText, StringComparison.OrdinalIgnoreCase);
        }
        private bool IsCompletionPrefix(string text)
        {
            foreach (var prefix in _codeCompletionPrefixes) if (text.EndsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }
        private void DrawCodeCompletion(UIRenderContext context)
        {
            _codeCompletionBounds = Rectangle.Empty;
            if (!IsCodeCompletionActive || EffectiveUIFont == null) return;
            var rowHeight = Math.Max(1, TextMetrics.LineHeight(EffectiveUIFont)); var rows = Math.Min(_codeCompletionOptions.Count, Math.Max(1, CodeCompletionMaxLines));
            var width = 80;
            foreach (var option in _codeCompletionOptions) width = Math.Max(width, (int)MathF.Ceiling(TextMetrics.Measure(EffectiveUIFont, option.DisplayText).X) + 12);
            var wrapIndex = GetLineWrapIndexAtColumn(CaretLine, CaretColumnInLine); var wrapStart = GetLineWrapStartColumn(CaretLine, wrapIndex); var lineLayout = TextMetrics.Layout(EffectiveUIFont, GetLine(CaretLine).Substring(wrapStart, GetLineWrapLength(CaretLine, wrapIndex)));
            var x = (int)(GlobalPosition.X + Padding.Left + TextContentLeftInset + lineLayout.GetCaretPosition(CaretColumnInLine - wrapStart).X - TextMetrics.Measure(EffectiveUIFont, _codeCompletionBase).X);
            var y = (int)(GlobalPosition.Y + Padding.Top + (Math.Max(0, GetVisibleRow(CaretLine, GetLineWrapIndexAtColumn(CaretLine, CaretColumnInLine))) + 1) * rowHeight);
            x = MathHelper.Clamp(x, Bounds.Left, Math.Max(Bounds.Left, Bounds.Right - width));
            if (y + rows * rowHeight > Bounds.Bottom) y = Math.Max(Bounds.Top, y - (rows + 1) * rowHeight);
            _codeCompletionBounds = new Rectangle(x, y, Math.Min(width, Bounds.Width), rows * rowHeight);
            context.Fill(_codeCompletionBounds, context.Theme.PanelColor); context.Border(_codeCompletionBounds, context.Theme.FocusColor);
            for (var row = 0; row < rows; row++)
            {
                if (row == _codeCompletionSelectedIndex) context.Fill(new Rectangle(_codeCompletionBounds.X + 1, _codeCompletionBounds.Y + row * rowHeight, Math.Max(0, _codeCompletionBounds.Width - 2), rowHeight), context.Theme.HoverColor);
                var option = _codeCompletionOptions[row]; context.Text(EffectiveUIFont, option.DisplayText, new Vector2(_codeCompletionBounds.X + 5, _codeCompletionBounds.Y + row * rowHeight), option.TextColor);
            }
        }
        private void DrawLineLengthGuidelines(UIRenderContext context)
        {
            for (var index = 0; index < _lineLengthGuidelines.Count; index++)
            {
                var x = GetLineLengthGuidelineX(_lineLengthGuidelines[index]);
                if (x <= Bounds.Left || x >= Bounds.Right) continue;
                var color = index == 0 ? LineLengthGuidelineColor : LineLengthGuidelineColor.WithAlpha((byte)Math.Min((int)LineLengthGuidelineColor.A, 90));
                context.Fill(new Rectangle(x, Bounds.Top, 1, Bounds.Height), color);
            }
        }
        private void DrawCodeHint(UIRenderContext context)
        {
            var bounds = GetCodeHintBounds();
            if (bounds.IsEmpty) return;
            context.Fill(bounds, context.Theme.PanelColor); context.Border(bounds, context.Theme.FocusColor);
            if (EffectiveUIFont == null) return;
            var y = bounds.Y + 4;
            foreach (var line in CodeHint.Split('\n')) { context.Text(EffectiveUIFont, line, new Vector2(bounds.X + 6, y), context.Theme.TextColor); y += TextMetrics.LineHeight(EffectiveUIFont); }
        }
        private static bool IsCompletionSymbol(char character) => char.IsLetterOrDigit(character) || character == '_';
        private bool HasCommandModifier()
        {
            var keyboard = Context?.CurrentKeyboardState ?? default;
            return keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl) || keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);
        }
        private bool IsCodeRegionLine(int line, string tag)
        {
            ValidateLineNumber(line);
            var text = GetLine(line).TrimStart();
            foreach (var delimiter in _lineCommentDelimiters)
            {
                var token = delimiter + tag;
                if (text.StartsWith(token, StringComparison.Ordinal) && (text.Length == token.Length || char.IsWhiteSpace(text[token.Length]))) return true;
            }
            return false;
        }
        private int FindCodeRegionEnd(int line)
        {
            if (!IsLineCodeRegionStart(line)) return -1;
            var level = 0;
            for (var next = line + 1; next < LineCount; next++)
            {
                if (IsLineCodeRegionStart(next)) level++;
                else if (IsLineCodeRegionEnd(next))
                {
                    if (level == 0) return next;
                    level--;
                }
            }
            return -1;
        }
        private bool TryFindDelimitedBlockEnd(int line, out int endLine)
        {
            foreach (var delimiter in _commentDelimiters) if (TryFindDelimitedBlockEnd(line, delimiter, out endLine)) return true;
            foreach (var delimiter in _stringDelimiters) if (TryFindDelimitedBlockEnd(line, delimiter, out endLine)) return true;
            endLine = -1;
            return false;
        }
        private bool TryFindDelimitedBlockEnd(int line, CodeDelimiter delimiter, out int endLine)
        {
            endLine = -1;
            if (delimiter.LineOnly) return false;
            var text = GetLine(line);
            var start = text.IndexOf(delimiter.Start, StringComparison.Ordinal);
            if (start < 0) return false;
            if (text.IndexOf(delimiter.End, start + delimiter.Start.Length, StringComparison.Ordinal) >= 0) return false;
            for (var next = line + 1; next < LineCount; next++)
            {
                if (GetLine(next).IndexOf(delimiter.End, StringComparison.Ordinal) >= 0) { endLine = next; return true; }
            }
            endLine = LineCount - 1;
            return endLine > line;
        }
        private static void AddDelimiter(List<CodeDelimiter> delimiters, string startKey, string endKey, bool lineOnly)
        {
            if (string.IsNullOrEmpty(startKey)) throw new ArgumentException("A delimiter start is required.", nameof(startKey));
            if (!lineOnly && string.IsNullOrEmpty(endKey)) throw new ArgumentException("A multiline delimiter end is required.", nameof(endKey));
            RemoveDelimiter(delimiters, startKey); delimiters.Add(new CodeDelimiter(startKey, endKey ?? string.Empty, lineOnly));
        }
        private static void RemoveDelimiter(List<CodeDelimiter> delimiters, string startKey)
        {
            for (var index = delimiters.Count - 1; index >= 0; index--) if (string.Equals(delimiters[index].Start, startKey, StringComparison.Ordinal)) delimiters.RemoveAt(index);
        }
        private static bool HasDelimiter(List<CodeDelimiter> delimiters, string startKey)
        {
            foreach (var delimiter in delimiters) if (string.Equals(delimiter.Start, startKey, StringComparison.Ordinal)) return true;
            return false;
        }
        private readonly struct CodeDelimiter
        {
            public CodeDelimiter(string start, string end, bool lineOnly) { Start = start; End = end; LineOnly = lineOnly; }
            public string Start { get; }
            public string End { get; }
            public bool LineOnly { get; }
        }
        private static IReadOnlyList<int> GetOrderedLines(HashSet<int> lines) { var ordered = new List<int>(lines); ordered.Sort(); return ordered; }
        private void SetLineState(HashSet<int> lines, int line, bool set) { line = ValidateLineNumber(line); if (set) lines.Add(line); else lines.Remove(line); }
        private int GetLineFromTextIndex(int index)
        {
            var line = 0;
            for (var character = 0; character < Math.Min(index, Text.Length); character++) if (Text[character] == '\n') line++;
            return line;
        }
        private static int GetTextIndexForLine(int line, List<string> lines)
        {
            var index = 0;
            for (var current = 0; current < line; current++) index += lines[current].Length + 1;
            return index;
        }
    }
}
