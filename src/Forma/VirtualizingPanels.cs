// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

namespace Forma
{
    public interface IVirtualizationPinState
    {
        bool IsVirtualizationPinned { get; }
    }

    internal static class VirtualizationEstimateCache
    {
        private static readonly ConditionalWeakTable<object, EstimateEntry> Estimates = new ConditionalWeakTable<object, EstimateEntry>();

        public static float Get(object scope, float fallback) =>
            scope != null && Estimates.TryGetValue(scope, out var entry) && entry.Samples > 0 ? entry.Value : fallback;

        public static void Record(object scope, float value)
        {
            if (scope == null || !float.IsFinite(value) || value <= 0) return;
            var entry = Estimates.GetOrCreateValue(scope);
            var retainedSamples = Math.Min(entry.Samples, 31);
            entry.Value = retainedSamples == 0 ? value : (entry.Value * retainedSamples + value) / (retainedSamples + 1);
            entry.Samples = retainedSamples + 1;
        }

        private sealed class EstimateEntry
        {
            public float Value;
            public int Samples;
        }
    }

    public enum ItemGeneratorChangeAction
    {
        Add,
        Remove,
        Replace,
        Move,
        Reset,
    }

    public sealed class ItemGeneratorChangedEventArgs : EventArgs
    {
        public ItemGeneratorChangedEventArgs(ItemGeneratorChangeAction action, int oldIndex, int newIndex, int count)
        {
            if (!Enum.IsDefined(typeof(ItemGeneratorChangeAction), action)) throw new ArgumentOutOfRangeException(nameof(action));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            Action = action;
            OldIndex = oldIndex;
            NewIndex = newIndex;
            Count = count;
        }

        public ItemGeneratorChangeAction Action { get; }
        public int OldIndex { get; }
        public int NewIndex { get; }
        public int Count { get; }
    }

    public interface IItemContainerGenerator
    {
        int Count { get; }
        Control ContainerInheritanceParent { get; }
        object EstimateScope { get; }
        /// <summary>Returns the stable reference-identity token for one source occurrence.</summary>
        object GetToken(int index);
        Control Realize(int index);
        void Recycle(int index, Control container);
        event EventHandler<ItemGeneratorChangedEventArgs> Changed;
    }

    internal interface IItemContainerGeneratorDiagnostics
    {
        int RecycledCount { get; }
        int PinnedCount { get; }
    }

    internal interface IItemContainerGeneratorAnchors
    {
        bool HasItemKeys { get; }
        object GetItemKey(int index);
        int FindIndexByKey(object key);
        void OnContainerAttached(int index, Control container);
        void OnContainerRecycling(int index, Control container);
    }

    internal readonly struct VirtualizationAnchorState
    {
        public VirtualizationAnchorState(object token, object key, bool hasKey, int index, float intraItemOffset)
        {
            Token = token;
            Key = key;
            HasKey = hasKey;
            Index = index;
            IntraItemOffset = intraItemOffset;
        }

        public object Token { get; }
        public object Key { get; }
        public bool HasKey { get; }
        public int Index { get; }
        public float IntraItemOffset { get; }
        public bool IsValid => Token != null && Index >= 0;
    }

    public abstract class VirtualizingPanel : Container, IScrollIndexProvider, IDisposable
    {
        private readonly SortedDictionary<int, Control> _realized = new SortedDictionary<int, Control>();
        private IItemContainerGenerator _generator;
        private int _overscanBefore;
        private int _overscanAfter;
        private int _pinnedCount;
        private bool _disposed;

        public IItemContainerGenerator Generator
        {
            get => _generator;
            set
            {
                // Disposal already released the generator, so detaching a disposed panel (its owner is disposed after its
                // visual children) has nothing left to do.
                if (value == null && _disposed) return;
                ThrowIfDisposed();
                if (ReferenceEquals(_generator, value)) return;
                RecycleAll();
                if (_generator != null) _generator.Changed -= GeneratorChanged;
                _generator = value;
                if (_generator != null) _generator.Changed += GeneratorChanged;
                OnGeneratorReset();
                QueueLayout();
            }
        }

        public int OverscanBefore
        {
            get => _overscanBefore;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (_overscanBefore == value) return;
                _overscanBefore = value;
                QueueLayout();
            }
        }

        public int OverscanAfter
        {
            get => _overscanAfter;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (_overscanAfter == value) return;
                _overscanAfter = value;
                QueueLayout();
            }
        }

        public int RealizedCount => _realized.Count;
        public int RecycledCount => (_generator as IItemContainerGeneratorDiagnostics)?.RecycledCount ?? 0;
        public int PinnedCount => _pinnedCount;
        public IReadOnlyDictionary<int, Control> RealizedContainers => _realized;

        public abstract bool TryGetIndexBounds(int index, out Rectangle bounds);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RecycleAll();
            if (_generator != null) _generator.Changed -= GeneratorChanged;
            _generator = null;
        }

        protected (Vector2 Viewport, Vector2 Offset, IScrollViewportOwner Owner) GetViewportState()
        {
            for (var ancestor = VisualParent; ancestor != null; ancestor = ancestor.VisualParent)
                if (ancestor is ScrollPresenter presenter)
                    return (presenter.Size, presenter.Owner?.ScrollOffset ?? presenter.Offset, presenter.Owner);
            return (Size, Vector2.Zero, null);
        }

        protected void SynchronizeRealizedRange(int firstIndex, int lastIndex)
        {
            if (_generator == null || _generator.Count == 0 || lastIndex < firstIndex)
            {
                _pinnedCount = 0;
                RecycleAll();
                return;
            }
            firstIndex = Math.Max(0, firstIndex - OverscanBefore);
            lastIndex = Math.Min(_generator.Count - 1, lastIndex + OverscanAfter);
            var recycled = new List<int>();
            _pinnedCount = 0;
            foreach (var pair in _realized)
            {
                if (pair.Key >= firstIndex && pair.Key <= lastIndex) continue;
                if (IsPinned(pair.Value)) _pinnedCount++;
                else recycled.Add(pair.Key);
            }
            foreach (var index in recycled) Recycle(index);
            for (var index = firstIndex; index <= lastIndex; index++)
            {
                if (_realized.ContainsKey(index)) continue;
                var container = _generator.Realize(index)
                    ?? throw new InvalidOperationException("An item generator returned no container.");
                try
                {
                    if (container.Parent != null || container.VisualParent != null)
                        throw new InvalidOperationException("An item generator must return a detached container.");
                    AddVisualChild(container, _generator.ContainerInheritanceParent ?? this);
                    _realized.Add(index, container);
                    (_generator as IItemContainerGeneratorAnchors)?.OnContainerAttached(index, container);
                }
                catch
                {
                    if (container.VisualParent == this) RemoveVisualChild(container);
                    _generator.Recycle(index, container);
                    throw;
                }
            }
        }

        private static bool IsPinned(Control control)
        {
            if (control.Context?.HasPinnedInteraction(control) == true) return true;
            return HasExplicitPin(control);
        }

        private static bool HasExplicitPin(Control control)
        {
            if (control is IVirtualizationPinState pinState && pinState.IsVirtualizationPinned) return true;
            foreach (var child in control.VisualChildren)
                if (HasExplicitPin(child)) return true;
            return false;
        }

        protected virtual void OnGeneratorChanged(ItemGeneratorChangedEventArgs args) => OnGeneratorReset();
        protected abstract void OnGeneratorReset();

        private protected VirtualizationAnchorState CaptureGeneratorAnchor(int index, float intraItemOffset)
        {
            if (_generator == null || index < 0 || index >= _generator.Count) return default;
            var anchors = _generator as IItemContainerGeneratorAnchors;
            return new VirtualizationAnchorState(
                _generator.GetToken(index),
                anchors?.GetItemKey(index),
                anchors?.HasItemKeys == true,
                index,
                intraItemOffset);
        }

        private protected int ResolveGeneratorAnchor(VirtualizationAnchorState anchor, ItemGeneratorChangedEventArgs args)
        {
            if (!anchor.IsValid || _generator == null || _generator.Count == 0) return -1;
            if (args.Action == ItemGeneratorChangeAction.Reset)
                return anchor.HasKey && _generator is IItemContainerGeneratorAnchors keyed
                    ? keyed.FindIndexByKey(anchor.Key)
                    : -1;

            var index = anchor.Index;
            switch (args.Action)
            {
                case ItemGeneratorChangeAction.Add:
                    if (index >= args.NewIndex) index += args.Count;
                    break;
                case ItemGeneratorChangeAction.Remove:
                    if (index >= args.OldIndex && index < args.OldIndex + args.Count) return -1;
                    if (index >= args.OldIndex + args.Count) index -= args.Count;
                    break;
                case ItemGeneratorChangeAction.Replace:
                    if (index >= args.OldIndex && index < args.OldIndex + args.Count) return -1;
                    break;
                case ItemGeneratorChangeAction.Move:
                    if (index >= args.OldIndex && index < args.OldIndex + args.Count)
                        index = args.NewIndex + index - args.OldIndex;
                    else
                    {
                        if (index >= args.OldIndex + args.Count) index -= args.Count;
                        if (index >= args.NewIndex) index += args.Count;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return index >= 0 && index < _generator.Count && ReferenceEquals(_generator.GetToken(index), anchor.Token)
                ? index
                : -1;
        }

        private void GeneratorChanged(object sender, ItemGeneratorChangedEventArgs args)
        {
            ApplyRealizedChange(args);
            OnGeneratorChanged(args);
            QueueLayout();
        }

        private void ApplyRealizedChange(ItemGeneratorChangedEventArgs args)
        {
            if (args.Action == ItemGeneratorChangeAction.Reset)
            {
                RecycleAll();
                return;
            }
            var remapped = new List<KeyValuePair<int, Control>>(_realized.Count);
            var removed = new List<int>();
            foreach (var pair in _realized)
            {
                var index = TransformRealizedIndex(pair.Key, args);
                if (index < 0) removed.Add(pair.Key);
                else remapped.Add(new KeyValuePair<int, Control>(index, pair.Value));
            }
            foreach (var index in removed) Recycle(index);
            _realized.Clear();
            foreach (var pair in remapped) _realized.Add(pair.Key, pair.Value);
        }

        private static int TransformRealizedIndex(int index, ItemGeneratorChangedEventArgs args)
        {
            switch (args.Action)
            {
                case ItemGeneratorChangeAction.Add:
                    return index >= args.NewIndex ? index + args.Count : index;
                case ItemGeneratorChangeAction.Remove:
                    if (index >= args.OldIndex && index < args.OldIndex + args.Count) return -1;
                    return index >= args.OldIndex + args.Count ? index - args.Count : index;
                case ItemGeneratorChangeAction.Replace:
                    return index >= args.OldIndex && index < args.OldIndex + args.Count ? -1 : index;
                case ItemGeneratorChangeAction.Move:
                    if (index >= args.OldIndex && index < args.OldIndex + args.Count)
                        return args.NewIndex + index - args.OldIndex;
                    if (index >= args.OldIndex + args.Count) index -= args.Count;
                    if (index >= args.NewIndex) index += args.Count;
                    return index;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void Recycle(int index)
        {
            var container = _realized[index];
            _realized.Remove(index);
            (_generator as IItemContainerGeneratorAnchors)?.OnContainerRecycling(index, container);
            if (container.VisualParent == this) RemoveVisualChild(container);
            _generator?.Recycle(index, container);
        }

        private void RecycleAll()
        {
            var indices = new List<int>(_realized.Keys);
            for (var index = indices.Count - 1; index >= 0; index--) Recycle(indices[index]);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <summary>Realizes only the visible portion of a linear item sequence while correcting estimated item extents and scroll anchors.</summary>
    public sealed class VirtualizingStackPanel : VirtualizingPanel
    {
        /// <summary>The finite default used when no item estimate is supplied by a theme or application.</summary>
        public const float DefaultEstimatedItemExtent = 32;
        public const float ExtentCorrectionTolerance = .5f;
        private DynamicExtentIndex _extents;
        private Orientation _orientation = Orientation.Vertical;
        private float _estimatedItemExtent = DefaultEstimatedItemExtent;
        private float _gap;
        private float _crossExtent;
        private float _measuredTotal;
        private readonly Dictionary<int, float> _measurements = new Dictionary<int, float>();
        private bool _hasExplicitEstimatedItemExtent;
        private VirtualizationAnchorState _anchor;

        public Orientation Orientation
        {
            get => _orientation;
            set
            {
                if (!Enum.IsDefined(typeof(Orientation), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_orientation == value) return;
                _orientation = value;
                QueueLayout();
            }
        }

        public float EstimatedItemExtent
        {
            get => _estimatedItemExtent;
            set
            {
                ValidatePositiveFinite(value, nameof(value));
                _hasExplicitEstimatedItemExtent = true;
                if (_estimatedItemExtent == value) return;
                _estimatedItemExtent = value;
                _extents?.SetEstimate(value + Gap);
                QueueLayout();
            }
        }

        public float Gap
        {
            get => _gap;
            set
            {
                if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (_gap == value) return;
                var difference = value - _gap;
                _gap = value;
                _extents?.AddToAll(difference);
                QueueLayout();
            }
        }

        public override Vector2 GetMinimumSize()
        {
            EnsureIndex();
            var main = Math.Max(0, (_extents?.Total ?? 0) - (Generator?.Count > 0 ? Gap : 0));
            return Orientation == Orientation.Horizontal
                ? Vector2.Max(CustomMinimumSize, new Vector2(main, _crossExtent))
                : Vector2.Max(CustomMinimumSize, new Vector2(_crossExtent, main));
        }

        public override bool TryGetIndexBounds(int index, out Rectangle bounds)
        {
            EnsureIndex();
            if (_extents == null || index < 0 || index >= _extents.Count)
            {
                bounds = Rectangle.Empty;
                return false;
            }
            var start = _extents.PrefixSum(index);
            var extent = Math.Max(0, _extents[index] - Gap);
            if (Orientation == Orientation.Horizontal)
            {
                if (IsLayoutRtl()) start = Math.Max(0, _extents.Total - Gap - start - extent);
                bounds = ToRectangle(start, 0, extent, Math.Max(Size.Y, _crossExtent));
            }
            else bounds = ToRectangle(0, start, Math.Max(Size.X, _crossExtent), extent);
            return true;
        }

        protected override void ArrangeChildren()
        {
            EnsureIndex();
            if (_extents == null || _extents.Count == 0)
            {
                SynchronizeRealizedRange(0, -1);
                return;
            }
            var viewport = GetViewportState();
            var mainOffset = Orientation == Orientation.Horizontal ? viewport.Offset.X : viewport.Offset.Y;
            var mainViewport = Orientation == Orientation.Horizontal ? viewport.Viewport.X : viewport.Viewport.Y;
            var first = _extents.FindIndex(mainOffset);
            var last = _extents.FindIndex(mainOffset + Math.Max(0, mainViewport));
            var anchorOffset = mainOffset - _extents.PrefixSum(first);
            _anchor = CaptureGeneratorAnchor(first, anchorOffset);
            SynchronizeRealizedRange(first, last);

            var corrected = false;
            var crossExtent = 0f;
            foreach (var pair in RealizedContainers)
            {
                var desired = pair.Value.GetBoundDesiredSize();
                var margins = pair.Value.Margins;
                var main = Orientation == Orientation.Horizontal ? desired.X + margins.Horizontal : desired.Y + margins.Vertical;
                var cross = Orientation == Orientation.Horizontal ? desired.Y + margins.Vertical : desired.X + margins.Horizontal;
                crossExtent = Math.Max(crossExtent, cross);
                var indexed = Math.Max(.001f, main + Gap);
                if (MathF.Abs(_extents[pair.Key] - indexed) > ExtentCorrectionTolerance) corrected = true;
                _extents[pair.Key] = indexed;
                if (!_measurements.TryGetValue(pair.Key, out var previousMeasurement) || previousMeasurement != main)
                {
                    if (_measurements.ContainsKey(pair.Key)) _measuredTotal -= previousMeasurement;
                    _measurements[pair.Key] = main;
                    _measuredTotal += main;
                }
            }
            _crossExtent = Math.Max(_crossExtent, crossExtent);
            if (_measurements.Count > 0)
            {
                var rollingEstimate = _measuredTotal / _measurements.Count;
                if (float.IsFinite(rollingEstimate) && rollingEstimate > 0 && MathF.Abs(rollingEstimate - _estimatedItemExtent) > ExtentCorrectionTolerance)
                {
                    _estimatedItemExtent = rollingEstimate;
                    _extents.SetEstimate(rollingEstimate + Gap);
                    VirtualizationEstimateCache.Record(Generator.EstimateScope, rollingEstimate);
                    corrected = true;
                }
            }
            if (corrected)
            {
                if (_anchor.IsValid && ReferenceEquals(Generator.GetToken(first), _anchor.Token) && viewport.Owner != null)
                {
                    var correctedOffset = Math.Max(0, _extents.PrefixSum(first) + _anchor.IntraItemOffset);
                    var offset = viewport.Owner.ScrollOffset;
                    if (Orientation == Orientation.Horizontal) offset.X = correctedOffset;
                    else offset.Y = correctedOffset;
                    viewport.Owner.ScrollOffset = offset;
                }
                QueueLayout();
            }

            foreach (var pair in RealizedContainers)
            {
                TryGetIndexBounds(pair.Key, out var bounds);
                var margins = pair.Value.Margins;
                if (Orientation == Orientation.Horizontal)
                    pair.Value.Position = new Vector2(bounds.X + margins.Left, margins.Top);
                else
                    pair.Value.Position = new Vector2(margins.Left, bounds.Y + margins.Top);
                pair.Value.Size = new Vector2(
                    Math.Max(0, bounds.Width - margins.Horizontal),
                    Math.Max(0, bounds.Height - margins.Vertical));
            }
        }

        protected override void OnGeneratorReset()
        {
            if (Generator != null && !_hasExplicitEstimatedItemExtent)
                _estimatedItemExtent = VirtualizationEstimateCache.Get(Generator.EstimateScope, DefaultEstimatedItemExtent);
            _extents = Generator == null ? null : new DynamicExtentIndex(Generator.Count, EstimatedItemExtent + Gap);
            _crossExtent = 0;
            _measuredTotal = 0;
            _measurements.Clear();
        }

        protected override void OnGeneratorChanged(ItemGeneratorChangedEventArgs args)
        {
            EnsureIndex();
            if (_extents == null) return;
            _measurements.Clear();
            _measuredTotal = 0;
            switch (args.Action)
            {
                case ItemGeneratorChangeAction.Add: _extents.Insert(args.NewIndex, args.Count); break;
                case ItemGeneratorChangeAction.Remove: _extents.Remove(args.OldIndex, args.Count); break;
                case ItemGeneratorChangeAction.Replace: break;
                case ItemGeneratorChangeAction.Move: _extents.Move(args.OldIndex, args.NewIndex, args.Count); break;
                case ItemGeneratorChangeAction.Reset: OnGeneratorReset(); break;
                default: throw new ArgumentOutOfRangeException();
            }
            RestoreCollectionAnchor(args);
        }

        private void EnsureIndex()
        {
            if (_extents == null && Generator != null) OnGeneratorReset();
        }

        private void RestoreCollectionAnchor(ItemGeneratorChangedEventArgs args)
        {
            var index = ResolveGeneratorAnchor(_anchor, args);
            var viewport = GetViewportState();
            if (viewport.Owner == null) return;
            if (index < 0)
            {
                if (args.Action == ItemGeneratorChangeAction.Reset)
                {
                    var raw = viewport.Owner.ScrollOffset;
                    var extent = Math.Max(0, _extents.Total - (Generator.Count > 0 ? Gap : 0));
                    if (Orientation == Orientation.Horizontal) raw.X = Math.Clamp(raw.X, 0, Math.Max(0, extent - viewport.Viewport.X));
                    else raw.Y = Math.Clamp(raw.Y, 0, Math.Max(0, extent - viewport.Viewport.Y));
                    viewport.Owner.ScrollOffset = raw;
                }
                return;
            }
            var correctedOffset = Math.Max(0, _extents.PrefixSum(index) + _anchor.IntraItemOffset);
            var offset = viewport.Owner.ScrollOffset;
            if (Orientation == Orientation.Horizontal) offset.X = correctedOffset;
            else offset.Y = correctedOffset;
            viewport.Owner.ScrollOffset = offset;
            _anchor = CaptureGeneratorAnchor(index, _anchor.IntraItemOffset);
        }

        private static Rectangle ToRectangle(float x, float y, float width, float height) =>
            new Rectangle((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Ceiling(width), (int)MathF.Ceiling(height));

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    /// <summary>Realizes visible rows of fixed-width cells while adapting column count and estimated row heights to the viewport.</summary>
    public sealed class VirtualizingGridPanel : VirtualizingPanel
    {
        public const float DefaultCellWidth = 100;
        public const float DefaultEstimatedCellHeight = 32;
        public const float ExtentCorrectionTolerance = .5f;
        private DynamicExtentIndex _rows;
        private float _cellWidth = DefaultCellWidth;
        private float _cellHeight = float.NaN;
        private float _estimatedCellHeight = DefaultEstimatedCellHeight;
        private float _columnGap;
        private float _rowGap;
        private int _overscanRows;
        private int _columns = 1;
        private readonly Dictionary<int, float> _measurements = new Dictionary<int, float>();
        private float _measuredTotal;
        private bool _hasExplicitEstimatedCellHeight;
        private VirtualizationAnchorState _anchor;

        public float CellWidth
        {
            get => _cellWidth;
            set
            {
                ValidatePositiveFinite(value, nameof(value));
                if (_cellWidth == value) return;
                _cellWidth = value;
                RebuildRows();
                QueueLayout();
            }
        }

        public float CellHeight
        {
            get => _cellHeight;
            set
            {
                if (!float.IsNaN(value)) ValidatePositiveFinite(value, nameof(value));
                if (BothNaNOrEqual(_cellHeight, value)) return;
                _cellHeight = value;
                RebuildRows();
                QueueLayout();
            }
        }

        public float EstimatedCellHeight
        {
            get => _estimatedCellHeight;
            set
            {
                ValidatePositiveFinite(value, nameof(value));
                _hasExplicitEstimatedCellHeight = true;
                if (_estimatedCellHeight == value) return;
                _estimatedCellHeight = value;
                if (float.IsNaN(CellHeight)) _rows?.SetEstimate(value + RowGap);
                QueueLayout();
            }
        }

        public float ColumnGap
        {
            get => _columnGap;
            set
            {
                ValidateGap(value, nameof(value));
                if (_columnGap == value) return;
                _columnGap = value;
                RebuildRows();
                QueueLayout();
            }
        }

        public float RowGap
        {
            get => _rowGap;
            set
            {
                ValidateGap(value, nameof(value));
                if (_rowGap == value) return;
                _rowGap = value;
                RebuildRows();
                QueueLayout();
            }
        }

        public int OverscanRows
        {
            get => _overscanRows;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (_overscanRows == value) return;
                _overscanRows = value;
                QueueLayout();
            }
        }

        public int ColumnCount => _columns;

        public override Vector2 GetMinimumSize()
        {
            EnsureRows(GetAvailableWidth());
            var width = _columns * CellWidth + Math.Max(0, _columns - 1) * ColumnGap;
            var height = Math.Max(0, (_rows?.Total ?? 0) - ((_rows?.Count ?? 0) > 0 ? RowGap : 0));
            return Vector2.Max(CustomMinimumSize, new Vector2(width, height));
        }

        public override bool TryGetIndexBounds(int index, out Rectangle bounds)
        {
            EnsureRows(GetAvailableWidth());
            if (Generator == null || index < 0 || index >= Generator.Count || _rows == null)
            {
                bounds = Rectangle.Empty;
                return false;
            }
            var row = index / _columns;
            var column = index % _columns;
            if (IsLayoutRtl()) column = _columns - column - 1;
            var x = column * (CellWidth + ColumnGap);
            var y = _rows.PrefixSum(row);
            bounds = ToRectangle(x, y, CellWidth, Math.Max(0, _rows[row] - RowGap));
            return true;
        }

        protected override void ArrangeChildren()
        {
            var viewport = GetViewportState();
            EnsureRows(viewport.Viewport.X);
            if (_rows == null || _rows.Count == 0)
            {
                SynchronizeRealizedRange(0, -1);
                return;
            }
            var visibleFirstRow = _rows.FindIndex(viewport.Offset.Y);
            var firstRow = Math.Max(0, visibleFirstRow - OverscanRows);
            var lastRow = Math.Min(_rows.Count - 1,
                _rows.FindIndex(viewport.Offset.Y + Math.Max(0, viewport.Viewport.Y)) + OverscanRows);
            var firstIndex = firstRow * _columns;
            var lastIndex = Math.Min(Generator.Count - 1, (lastRow + 1) * _columns - 1);
            var anchorIndex = Math.Min(Generator.Count - 1, visibleFirstRow * _columns);
            var anchorOffset = viewport.Offset.Y - _rows.PrefixSum(visibleFirstRow);
            _anchor = CaptureGeneratorAnchor(anchorIndex, anchorOffset);
            SynchronizeRealizedRange(firstIndex, lastIndex);

            var rowHeights = new Dictionary<int, float>();
            foreach (var pair in RealizedContainers)
            {
                var desired = pair.Value.GetBoundDesiredSize();
                var height = desired.Y + pair.Value.Margins.Vertical;
                var row = pair.Key / _columns;
                if (!rowHeights.TryGetValue(row, out var current) || height > current) rowHeights[row] = height;
            }

            var corrected = false;
            if (float.IsNaN(CellHeight))
            {
                foreach (var pair in rowHeights)
                {
                    var indexed = Math.Max(.001f, pair.Value + RowGap);
                    if (MathF.Abs(_rows[pair.Key] - indexed) > ExtentCorrectionTolerance) corrected = true;
                    if (!_measurements.TryGetValue(pair.Key, out var previous) || previous != pair.Value)
                    {
                        if (_measurements.ContainsKey(pair.Key)) _measuredTotal -= previous;
                        _measurements[pair.Key] = pair.Value;
                        _measuredTotal += pair.Value;
                    }
                }
                if (_measurements.Count > 0)
                {
                    var rolling = _measuredTotal / _measurements.Count;
                    if (float.IsFinite(rolling) && rolling > 0 && MathF.Abs(rolling - _estimatedCellHeight) > ExtentCorrectionTolerance)
                    {
                        _estimatedCellHeight = rolling;
                        _rows.SetAll(rolling + RowGap);
                        VirtualizationEstimateCache.Record(Generator.EstimateScope, rolling);
                        corrected = true;
                    }
                }
            }

            if (corrected && _anchor.IsValid && ReferenceEquals(Generator.GetToken(anchorIndex), _anchor.Token) && viewport.Owner != null)
            {
                var offset = viewport.Owner.ScrollOffset;
                offset.Y = Math.Max(0, _rows.PrefixSum(visibleFirstRow) + _anchor.IntraItemOffset);
                viewport.Owner.ScrollOffset = offset;
                QueueLayout();
            }

            foreach (var pair in RealizedContainers)
            {
                TryGetIndexBounds(pair.Key, out var bounds);
                var margins = pair.Value.Margins;
                pair.Value.Position = new Vector2(bounds.X + margins.Left, bounds.Y + margins.Top);
                pair.Value.Size = new Vector2(
                    Math.Max(0, bounds.Width - margins.Horizontal),
                    Math.Max(0, bounds.Height - margins.Vertical));
            }
        }

        protected override void OnGeneratorReset() => RebuildRows();

        protected override void OnGeneratorChanged(ItemGeneratorChangedEventArgs args)
        {
            var previousRows = _rows?.Count ?? 0;
            var nextRows = GetRowCount();
            if (_rows == null)
            {
                RebuildRows();
                return;
            }
            if (nextRows > previousRows) _rows.Insert(previousRows, nextRows - previousRows);
            else if (nextRows < previousRows) _rows.Remove(nextRows, previousRows - nextRows);
            if (float.IsNaN(CellHeight)) _rows.SetAll(EstimatedCellHeight + RowGap);
            _measurements.Clear();
            _measuredTotal = 0;
            RestoreCollectionAnchor(args);
        }

        private void RestoreCollectionAnchor(ItemGeneratorChangedEventArgs args)
        {
            var index = ResolveGeneratorAnchor(_anchor, args);
            var viewport = GetViewportState();
            if (viewport.Owner == null) return;
            if (index < 0)
            {
                if (args.Action == ItemGeneratorChangeAction.Reset)
                {
                    var raw = viewport.Owner.ScrollOffset;
                    var extent = Math.Max(0, _rows.Total - (_rows.Count > 0 ? RowGap : 0));
                    raw.Y = Math.Clamp(raw.Y, 0, Math.Max(0, extent - viewport.Viewport.Y));
                    viewport.Owner.ScrollOffset = raw;
                }
                return;
            }
            var row = index / _columns;
            var offset = viewport.Owner.ScrollOffset;
            offset.Y = Math.Max(0, _rows.PrefixSum(row) + _anchor.IntraItemOffset);
            viewport.Owner.ScrollOffset = offset;
            _anchor = CaptureGeneratorAnchor(index, _anchor.IntraItemOffset);
        }

        private void EnsureRows(float availableWidth)
        {
            var columns = Math.Max(1, (int)MathF.Floor((Math.Max(0, availableWidth) + ColumnGap) / (CellWidth + ColumnGap)));
            if (columns == _columns && _rows != null) return;
            _columns = columns;
            RebuildRows();
        }

        private void RebuildRows()
        {
            if (Generator != null && float.IsNaN(CellHeight) && !_hasExplicitEstimatedCellHeight)
                _estimatedCellHeight = VirtualizationEstimateCache.Get(Generator.EstimateScope, DefaultEstimatedCellHeight);
            _rows = Generator == null ? null : new DynamicExtentIndex(GetRowCount(), EffectiveCellHeight + RowGap);
            _measurements.Clear();
            _measuredTotal = 0;
        }

        private int GetRowCount() => Generator == null ? 0 : (Generator.Count + _columns - 1) / _columns;
        private float EffectiveCellHeight => float.IsNaN(CellHeight) ? EstimatedCellHeight : CellHeight;
        private float GetAvailableWidth() => Math.Max(Size.X, GetViewportState().Viewport.X);

        private static Rectangle ToRectangle(float x, float y, float width, float height) =>
            new Rectangle((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Ceiling(width), (int)MathF.Ceiling(height));
        private static bool BothNaNOrEqual(float left, float right) => left == right || float.IsNaN(left) && float.IsNaN(right);
        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(parameterName);
        }
        private static void ValidateGap(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    internal sealed class DynamicExtentIndex
    {
        private const int MaximumChunkSize = 256;
        private const int MinimumChunkSize = MaximumChunkSize / 2;
        private readonly List<Chunk> _chunks = new List<Chunk>();
        private readonly FenwickTree _chunkCounts = new FenwickTree();
        private readonly FenwickTree _chunkTotals = new FenwickTree();
        private float _estimate;
        private int _count;

        public DynamicExtentIndex(int count, float estimate)
        {
            ValidateExtent(estimate, nameof(estimate));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            _estimate = estimate;
            Insert(0, count);
        }

        public int Count => _count;
        public float Estimate => _estimate;
        public float Total => _chunkTotals.Total;

        public void SetEstimate(float estimate)
        {
            ValidateExtent(estimate, nameof(estimate));
            if (_estimate == estimate) return;
            _estimate = estimate;
            foreach (var chunk in _chunks) chunk.SetEstimate(estimate);
            RebuildFenwickTrees();
        }

        public void SetAll(float extent)
        {
            ValidateExtent(extent, nameof(extent));
            foreach (var chunk in _chunks) chunk.SetAll(extent);
            RebuildFenwickTrees();
        }

        public void AddToAll(float difference)
        {
            if (!float.IsFinite(difference)) throw new ArgumentOutOfRangeException(nameof(difference));
            foreach (var chunk in _chunks) chunk.AddToAll(difference);
            _estimate += difference;
            ValidateExtent(_estimate, nameof(difference));
            RebuildFenwickTrees();
        }

        public float this[int index]
        {
            get
            {
                var location = Find(index, false);
                return location.Chunk.Values[location.Offset];
            }
            set
            {
                ValidateExtent(value, nameof(value));
                var location = Find(index, false);
                var previousTotal = location.Chunk.Total;
                location.Chunk.Set(location.Offset, value, true);
                _chunkTotals.Add(location.ChunkIndex, location.Chunk.Total - previousTotal);
            }
        }

        public float PrefixSum(int count)
        {
            if (count < 0 || count > _count) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return 0;
            if (count == _count) return Total;
            var location = Find(count - 1, false);
            return _chunkTotals.PrefixSum(location.ChunkIndex) + location.Chunk.PrefixSum(location.Offset + 1);
        }

        public int FindIndex(float position)
        {
            if (_count == 0) return -1;
            if (!float.IsFinite(position)) throw new ArgumentOutOfRangeException(nameof(position));
            if (position <= 0) return 0;
            if (position >= Total) return _count - 1;
            var chunkIndex = _chunkTotals.FindIndex(position);
            var precedingTotal = _chunkTotals.PrefixSum(chunkIndex);
            var precedingCount = (int)_chunkCounts.PrefixSum(chunkIndex);
            return precedingCount + _chunks[chunkIndex].FindIndex(position - precedingTotal);
        }

        public void Insert(int index, int count)
        {
            if (index < 0 || index > _count) throw new ArgumentOutOfRangeException(nameof(index));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            while (count > 0)
            {
                var amount = Math.Min(count, MaximumChunkSize);
                var location = Find(index, true);
                if (location.Chunk != null && location.Chunk.Count + amount <= MaximumChunkSize)
                {
                    location.Chunk.Insert(location.Offset, amount, _estimate);
                }
                else
                {
                    var chunk = new Chunk(amount, _estimate);
                    var chunkIndex = location.Chunk == null ? _chunks.Count : location.ChunkIndex;
                    if (location.Chunk != null && location.Offset == location.Chunk.Count) chunkIndex++;
                    else if (location.Chunk != null && location.Offset > 0)
                    {
                        var tail = location.Chunk.Split(location.Offset);
                        _chunks.Insert(++chunkIndex, tail);
                    }
                    _chunks.Insert(chunkIndex, chunk);
                }
                _count += amount;
                index += amount;
                count -= amount;
            }
            Rebalance();
            RebuildFenwickTrees();
        }

        public void Remove(int index, int count)
        {
            ValidateRange(index, count);
            var remaining = count;
            while (remaining > 0)
            {
                var location = Find(index, false);
                var amount = Math.Min(remaining, location.Chunk.Count - location.Offset);
                location.Chunk.Remove(location.Offset, amount);
                if (location.Chunk.Count == 0) _chunks.Remove(location.Chunk);
                _count -= amount;
                remaining -= amount;
            }
            Rebalance();
            RebuildFenwickTrees();
        }

        public void Move(int oldIndex, int newIndex, int count)
        {
            ValidateRange(oldIndex, count);
            if (newIndex < 0 || newIndex > _count - count) throw new ArgumentOutOfRangeException(nameof(newIndex));
            if (count == 0 || oldIndex == newIndex) return;
            var values = new float[count];
            var measured = new bool[count];
            for (var offset = 0; offset < count; offset++)
            {
                var location = Find(oldIndex + offset, false);
                values[offset] = location.Chunk.Values[location.Offset];
                measured[offset] = location.Chunk.IsMeasured(location.Offset);
            }
            Remove(oldIndex, count);
            Insert(newIndex, count);
            for (var offset = 0; offset < count; offset++)
            {
                var location = Find(newIndex + offset, false);
                var previousTotal = location.Chunk.Total;
                location.Chunk.Set(location.Offset, values[offset], measured[offset]);
                _chunkTotals.Add(location.ChunkIndex, location.Chunk.Total - previousTotal);
            }
        }

        private (Chunk Chunk, int ChunkIndex, int Offset) Find(int index, bool allowEnd)
        {
            if (index < 0 || index > _count || (!allowEnd && index == _count)) throw new ArgumentOutOfRangeException(nameof(index));
            if (_chunks.Count == 0) return (null, 0, 0);
            if (index == _count) return (_chunks[^1], _chunks.Count - 1, _chunks[^1].Count);
            var chunkIndex = _chunkCounts.FindIndex(index);
            var precedingCount = (int)_chunkCounts.PrefixSum(chunkIndex);
            return (_chunks[chunkIndex], chunkIndex, index - precedingCount);
        }

        private void ValidateRange(int index, int count)
        {
            if (index < 0 || count < 0 || index > _count - count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private void Rebalance()
        {
            for (var index = 0; index + 1 < _chunks.Count; index++)
            {
                var current = _chunks[index];
                var next = _chunks[index + 1];
                if (current.Count >= MinimumChunkSize || current.Count + next.Count > MaximumChunkSize) continue;
                current.Append(next);
                _chunks.RemoveAt(index + 1);
                index--;
            }
        }

        private void RebuildFenwickTrees()
        {
            _chunkCounts.Rebuild(_chunks, chunk => chunk.Count);
            _chunkTotals.Rebuild(_chunks, chunk => chunk.Total);
        }

        private static void ValidateExtent(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(parameterName);
        }

        private sealed class Chunk
        {
            private readonly List<float> _values;
            private readonly List<bool> _measured;

            public Chunk(int count, float estimate)
            {
                _values = new List<float>(count);
                _measured = new List<bool>(count);
                Insert(0, count, estimate);
            }

            public IReadOnlyList<float> Values => _values;
            public int Count => _values.Count;
            public float Total { get; private set; }

            public bool IsMeasured(int index) => _measured[index];

            public void Set(int index, float value, bool measured)
            {
                Total += value - _values[index];
                _values[index] = value;
                _measured[index] = measured;
            }

            public void SetEstimate(float estimate)
            {
                for (var index = 0; index < _values.Count; index++)
                    if (!_measured[index]) Set(index, estimate, false);
            }

            public void SetAll(float extent)
            {
                for (var index = 0; index < _values.Count; index++) Set(index, extent, false);
            }

            public void AddToAll(float difference)
            {
                for (var index = 0; index < _values.Count; index++)
                {
                    var value = _values[index] + difference;
                    if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(difference));
                    Set(index, value, _measured[index]);
                }
            }

            public float PrefixSum(int count)
            {
                var total = 0f;
                for (var index = 0; index < count; index++) total += _values[index];
                return total;
            }

            public int FindIndex(float position)
            {
                var total = 0f;
                for (var index = 0; index < _values.Count; index++)
                {
                    total += _values[index];
                    if (position < total) return index;
                }
                return _values.Count - 1;
            }

            public void Insert(int index, int count, float estimate)
            {
                for (var offset = 0; offset < count; offset++) _values.Insert(index + offset, estimate);
                for (var offset = 0; offset < count; offset++) _measured.Insert(index + offset, false);
                Total += count * estimate;
            }

            public void Remove(int index, int count)
            {
                for (var offset = 0; offset < count; offset++) Total -= _values[index + offset];
                _values.RemoveRange(index, count);
                _measured.RemoveRange(index, count);
            }

            public Chunk Split(int index)
            {
                var tail = new Chunk(0, 1);
                for (var offset = index; offset < _values.Count; offset++) tail._values.Add(_values[offset]);
                for (var offset = index; offset < _measured.Count; offset++) tail._measured.Add(_measured[offset]);
                tail.Total = tail.PrefixSum(tail.Count);
                Remove(index, _values.Count - index);
                return tail;
            }

            public void Append(Chunk other)
            {
                _values.AddRange(other._values);
                _measured.AddRange(other._measured);
                Total += other.Total;
            }
        }

        private sealed class FenwickTree
        {
            private float[] _tree = Array.Empty<float>();

            public float Total => PrefixSum(_tree.Length - 1);

            public void Rebuild(IReadOnlyList<Chunk> chunks, Func<Chunk, float> selector)
            {
                _tree = new float[chunks.Count + 1];
                for (var index = 0; index < chunks.Count; index++) Add(index, selector(chunks[index]));
            }

            public void Add(int index, float delta)
            {
                for (var treeIndex = index + 1; treeIndex < _tree.Length; treeIndex += treeIndex & -treeIndex)
                    _tree[treeIndex] += delta;
            }

            public float PrefixSum(int count)
            {
                var total = 0f;
                for (var treeIndex = Math.Min(count, _tree.Length - 1); treeIndex > 0; treeIndex -= treeIndex & -treeIndex)
                    total += _tree[treeIndex];
                return total;
            }

            public int FindIndex(float position)
            {
                var index = 0;
                var total = 0f;
                var bit = HighestPowerOfTwo(_tree.Length - 1);
                while (bit != 0)
                {
                    var next = index + bit;
                    if (next < _tree.Length && total + _tree[next] <= position)
                    {
                        index = next;
                        total += _tree[next];
                    }
                    bit >>= 1;
                }
                return Math.Min(index, _tree.Length - 2);
            }

            private static int HighestPowerOfTwo(int value)
            {
                var result = 1;
                while (result <= value / 2) result <<= 1;
                return value == 0 ? 0 : result;
            }
        }
    }
}