// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public sealed class ListBoxSelectionChangedEventArgs : EventArgs
    {
        internal ListBoxSelectionChangedEventArgs(
            IReadOnlyList<int> oldIndices,
            IReadOnlyList<object> oldItems,
            IReadOnlyList<int> newIndices,
            IReadOnlyList<object> newItems)
        {
            OldIndices = oldIndices;
            OldItems = oldItems;
            NewIndices = newIndices;
            NewItems = newItems;
        }

        public IReadOnlyList<int> OldIndices { get; }
        public IReadOnlyList<object> OldItems { get; }
        public IReadOnlyList<int> NewIndices { get; }
        public IReadOnlyList<object> NewItems { get; }
    }

    public sealed class ListBoxItemEventArgs : EventArgs
    {
        internal ListBoxItemEventArgs(int index, object item)
        {
            Index = index;
            Item = item;
        }

        public int Index { get; }
        public object Item { get; }
    }

    /// <summary>Hosts one selectable list item and exposes its selected and current interaction states.</summary>
    public class ListBoxItem : ContentControl, IRecyclableItemContainer
    {
        private bool _isSelected;
        private bool _isCurrent;
        private bool _isSelectable = true;

        public ListBoxItem() => FocusMode = FocusMode.All;
        /// <summary>A row of a <see cref="ListBox"/>, which reports <see cref="AccessibilityRole.List"/>.
        /// Without this the list announces itself but its rows are anonymous.</summary>
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.ListItem;

        public bool IsSelected => _isSelected;
        public bool IsCurrent => _isCurrent;
        public bool IsSelectable
        {
            get => _isSelectable;
            set
            {
                if (_isSelectable == value) return;
                _isSelectable = value;
                OnPropertyChanged(nameof(IsSelectable));
            }
        }

        internal void SetSelectionState(bool isSelected, bool isCurrent)
        {
            if (_isSelected != isSelected)
            {
                _isSelected = isSelected;
                SetPseudoState("selected", isSelected);
                OnPropertyChanged(nameof(IsSelected));
            }
            if (_isCurrent != isCurrent)
            {
                _isCurrent = isCurrent;
                SetPseudoState("current", isCurrent);
                OnPropertyChanged(nameof(IsCurrent));
            }
        }

        void IRecyclableItemContainer.OnRecycling()
        {
            IsSelectable = true;
            SetSelectionState(false, false);
        }
        void IRecyclableItemContainer.OnReused(object item) { }
    }

    /// <summary>Presents a scrollable item collection with single or multiple selection, keyboard navigation, and incremental search.</summary>
    [TemplatePart(ScrollPresenterPartName, typeof(ScrollPresenter))]
    [TemplatePart(ItemsPresenterPartName, typeof(ItemsPresenter))]
    public class ListBox : ItemsControl, IScrollViewportOwner
    {
        public const string ScrollPresenterPartName = "PART_ScrollPresenter";
        public const string ItemsPresenterPartName = "PART_ItemsPresenter";
        private readonly ScrollViewportController _viewportController = new ScrollViewportController();
        private readonly List<SelectionEntry> _selection = new List<SelectionEntry>();
        private ItemListSelectionMode _selectionMode;
        private object _currentToken;
        private object _currentItem;
        private object _rangeAnchorToken;
        private ScrollPresenter _scrollPresenter;
        private ItemsPresenter _itemsPresenter;
        private int[] _selectedIndices = Array.Empty<int>();
        private string _searchText = string.Empty;
        private TimeSpan _lastSearchTime = TimeSpan.MinValue;
        private TimeSpan _lastClickTime = TimeSpan.MinValue;
        private Point _lastClickPosition;
        private int _lastClickIndex = -1;
        private bool _doubleClickPending;
        private static readonly TimeSpan DoubleClickTimeout = TimeSpan.FromMilliseconds(600);
        private const int DoubleClickTolerance = 5;

        public ListBox()
        {
            FocusMode = FocusMode.All;
            _viewportController.MetricsChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(ScrollOffset));
                OnPropertyChanged(nameof(Viewport));
                OnPropertyChanged(nameof(Extent));
            };
        }

        public ItemListSelectionMode SelectionMode
        {
            get => _selectionMode;
            set
            {
                if (!Enum.IsDefined(typeof(ItemListSelectionMode), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_selectionMode == value) return;
                _selectionMode = value;
                if (value == ItemListSelectionMode.Single && _selection.Count > 1)
                {
                    var retained = FindSelection(_currentToken) ?? _selection[0];
                    ChangeSelection(() =>
                    {
                        _selection.Clear();
                        _selection.Add(retained);
                    });
                }
                OnPropertyChanged(nameof(SelectionMode));
            }
        }

        public bool AllowReselect { get; set; }
        public bool AllowRightMouseSelect { get; set; }
        /// <summary>Gets or sets whether one pointer click selects and activates an item.</summary>
        public bool ActivateOnSingleClick { get; set; }
        public bool IsTextSearchEnabled { get; set; } = true;
        public bool WrapNavigation { get; set; } = true;
        public TimeSpan IncrementalSearchTimeout { get; set; } = TimeSpan.FromSeconds(1);
        public bool HasSelection => _selection.Count != 0;
        public int SelectedIndex
        {
            get => _selectedIndices.Length == 0 ? -1 : _selectedIndices[0];
            set
            {
                if (value == -1) ClearSelection();
                else
                {
                    ValidateIndex(value);
                    ChangeSelection(() =>
                    {
                        _selection.Clear();
                        _selection.Add(CreateSelection(value));
                    });
                    CurrentIndex = value;
                }
            }
        }
        public object SelectedItem
        {
            get => SelectedIndex < 0 ? null : GetItemAt(SelectedIndex);
            set
            {
                if (value == null)
                {
                    ClearSelection();
                    return;
                }
                var currentIndex = CurrentIndex;
                if (currentIndex >= 0 && Equals(GetItemAt(currentIndex), value))
                {
                    SelectedIndex = currentIndex;
                    return;
                }
                for (var index = 0; index < ItemCount; index++)
                {
                    if (!Equals(GetItemAt(index), value)) continue;
                    SelectedIndex = index;
                    return;
                }
                ClearSelection();
            }
        }
        public IReadOnlyList<int> SelectedIndices => _selectedIndices;
        public IReadOnlyList<object> SelectedItems
        {
            get
            {
                var items = new object[_selection.Count];
                for (var index = 0; index < _selection.Count; index++) items[index] = _selection[index].Item;
                return items;
            }
        }
        public int CurrentIndex
        {
            get => _currentToken == null ? -1 : IndexOfItemToken(_currentToken);
            set
            {
                if (value < -1 || value >= ItemCount) throw new ArgumentOutOfRangeException(nameof(value));
                var token = value < 0 ? null : GetItemToken(value);
                if (ReferenceEquals(_currentToken, token)) return;
                _currentToken = token;
                _currentItem = value < 0 ? null : GetItemAt(value);
                UpdateRealizedSelectionStates();
                OnPropertyChanged(nameof(CurrentIndex));
                OnPropertyChanged(nameof(CurrentItem));
            }
        }
        public object CurrentItem => CurrentIndex < 0 ? null : _currentItem;
        public Vector2 ScrollOffset
        {
            get => _viewportController.Offset;
            set
            {
                _viewportController.Offset = value;
                _itemsPresenter?.Panel?.QueueLayout();
                QueueLayout();
            }
        }
        public Vector2 Viewport => _viewportController.Viewport;
        public Vector2 Extent => _viewportController.Extent;
        internal override bool IsAccessibilityItemSelected(int index) => Array.BinarySearch(_selectedIndices, index) >= 0;
        internal override bool IsAccessibilityItemCurrent(int index) => CurrentIndex == index;

        public event EventHandler<ListBoxSelectionChangedEventArgs> SelectionChanged;
        public event EventHandler<ListBoxItemEventArgs> ItemActivated;

        public void Select(int index, bool additive = false)
        {
            ValidateIndex(index);
            if (!additive || SelectionMode == ItemListSelectionMode.Single)
            {
                var alreadyExclusive = _selection.Count == 1 && ReferenceEquals(_selection[0].Token, GetItemToken(index));
                if (alreadyExclusive && !AllowReselect) return;
                ChangeSelection(() =>
                {
                    _selection.Clear();
                    _selection.Add(CreateSelection(index));
                }, forceEvent: alreadyExclusive && AllowReselect);
            }
            else if (FindSelection(GetItemToken(index)) == null)
            {
                ChangeSelection(() => _selection.Add(CreateSelection(index)));
            }
            CurrentIndex = index;
        }

        public void ToggleSelection(int index)
        {
            ValidateIndex(index);
            var token = GetItemToken(index);
            var existing = FindSelection(token);
            ChangeSelection(() =>
            {
                if (existing != null) _selection.Remove(existing);
                else
                {
                    if (SelectionMode == ItemListSelectionMode.Single) _selection.Clear();
                    _selection.Add(CreateSelection(index));
                }
            });
            CurrentIndex = index;
        }

        public void SelectRange(int anchorIndex, int targetIndex)
        {
            ValidateIndex(anchorIndex);
            ValidateIndex(targetIndex);
            if (SelectionMode == ItemListSelectionMode.Single)
            {
                Select(targetIndex);
                return;
            }
            var low = Math.Min(anchorIndex, targetIndex);
            var high = Math.Max(anchorIndex, targetIndex);
            ChangeSelection(() =>
            {
                _selection.Clear();
                for (var index = low; index <= high; index++)
                    if (IsIndexSelectable(index)) _selection.Add(CreateSelection(index));
            });
            _rangeAnchorToken = GetItemToken(anchorIndex);
            CurrentIndex = targetIndex;
        }

        public void ClearSelection() => ChangeSelection(_selection.Clear);

        public void Activate(int index)
        {
            ValidateIndex(index);
            CurrentIndex = index;
            ItemActivated?.Invoke(this, new ListBoxItemEventArgs(index, GetItemAt(index)));
        }

        public int FindNextItem(string prefix, int startIndex = -1, bool wrap = true)
        {
            if (!IsTextSearchEnabled || string.IsNullOrEmpty(prefix) || ItemCount == 0) return -1;
            if (startIndex < -1 || startIndex >= ItemCount) throw new ArgumentOutOfRangeException(nameof(startIndex));
            var count = wrap ? ItemCount : ItemCount - startIndex - 1;
            for (var offset = 1; offset <= count; offset++)
            {
                var index = startIndex + offset;
                if (wrap) index %= ItemCount;
                if (index >= ItemCount) break;
                if ((GetItemAt(index)?.ToString() ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return index;
            }
            return -1;
        }

        public bool BringIndexIntoView(int index)
        {
            ValidateIndex(index);
            if (_itemsPresenter?.Panel is not IScrollIndexProvider provider || !provider.TryGetIndexBounds(index, out var bounds)) return false;
            var viewport = new Rectangle(
                (int)MathF.Round(ScrollOffset.X),
                (int)MathF.Round(ScrollOffset.Y),
                (int)MathF.Round(Viewport.X),
                (int)MathF.Round(Viewport.Y));
            _viewportController.BringIntoView(viewport, bounds);
            QueueLayout();
            return true;
        }

        protected override Control GetContainerForItem(object item) => new ListBoxItem();

        protected override bool IsContainerCompatibleForItem(Control container, object item) => container is ListBoxItem;

        protected override void PrepareContainerForItem(Control container, object item, int index)
        {
            base.PrepareContainerForItem(container, item);
            if (container is ListBoxItem listBoxItem)
            {
                var token = GetItemToken(index);
                listBoxItem.SetSelectionState(IsSelectedToken(token), ReferenceEquals(_currentToken, token));
            }
        }

        protected override void ClearContainerForItem(Control container, object item)
        {
            if (container is ListBoxItem listBoxItem) listBoxItem.SetSelectionState(false, false);
            base.ClearContainerForItem(container, item);
        }

        protected override void OnItemsChanged()
        {
            var oldIndices = _selectedIndices;
            var oldItems = SelectedItems;
            var selectionChanged = false;
            for (var index = _selection.Count - 1; index >= 0; index--)
            {
                if (IndexOfItemToken(_selection[index].Token) >= 0) continue;
                _selection.RemoveAt(index);
                selectionChanged = true;
            }
            if (_currentToken != null && IndexOfItemToken(_currentToken) < 0)
            {
                _currentToken = null;
                _currentItem = null;
                OnPropertyChanged(nameof(CurrentIndex));
                OnPropertyChanged(nameof(CurrentItem));
            }
            if (_rangeAnchorToken != null && IndexOfItemToken(_rangeAnchorToken) < 0) _rangeAnchorToken = null;
            RefreshSelectionProjections();
            UpdateRealizedSelectionStates();
            if (selectionChanged) RaiseSelectionChanged(oldIndices, oldItems);
        }

        protected override void OnTemplateApplied()
        {
            _scrollPresenter = GetTemplateChild(ScrollPresenterPartName) as ScrollPresenter
                ?? throw new InvalidOperationException($"ListBox templates must provide a {nameof(ScrollPresenter)} named '{ScrollPresenterPartName}'.");
            _itemsPresenter = GetTemplateChild(ItemsPresenterPartName) as ItemsPresenter
                ?? throw new InvalidOperationException($"ListBox templates must provide an {nameof(ItemsPresenter)} named '{ItemsPresenterPartName}'.");
            _scrollPresenter.Owner = this;
            base.OnTemplateApplied();
        }

        protected internal override void PointerPressed(Point point)
        {
            base.PointerPressed(point);
            var index = GetIndexAtPosition(point);
            if (index < 0) return;
            var clickTime = Context?.CurrentTime ?? TimeSpan.Zero;
            var withinTimeout = _lastClickTime != TimeSpan.MinValue && clickTime - _lastClickTime <= DoubleClickTimeout;
            var withinTolerance = Vector2.DistanceSquared(point.ToVector2(), _lastClickPosition.ToVector2()) <=
                DoubleClickTolerance * DoubleClickTolerance;
            _doubleClickPending = withinTimeout && withinTolerance && index == _lastClickIndex;
            _lastClickTime = clickTime;
            _lastClickPosition = point;
            _lastClickIndex = index;
            SelectItemFromPointer(index, point);
        }

        internal override void PointerButtonPressed(Point point, PointerButton button)
        {
            base.PointerButtonPressed(point, button);
            if (button != PointerButton.Right || !AllowRightMouseSelect) return;
            var index = GetIndexAtPosition(point);
            if (index >= 0) SelectItemFromPointer(index, point);
        }

        protected internal override void PointerReleased(Point point, bool isInside)
        {
            var index = GetIndexAtPosition(point);
            if (isInside && index >= 0 && index == _lastClickIndex && (ActivateOnSingleClick || _doubleClickPending)) Activate(index);
            _doubleClickPending = false;
        }
        internal override void CancelInput()
        {
            _doubleClickPending = false;
            _lastClickTime = TimeSpan.MinValue;
            _lastClickIndex = -1;
            base.CancelInput();
        }

        internal override void Process(GameTime gameTime)
        {
            var keyboard = Context?.CurrentKeyboardState ?? default;
            if (!keyboard.IsKeyDown(Keys.LeftShift) && !keyboard.IsKeyDown(Keys.RightShift)) _rangeAnchorToken = null;
            base.Process(gameTime);
        }

        internal override void KeyPressed(Keys key)
        {
            if (ItemCount == 0) return;
            if (key == Keys.Enter)
            {
                if (CurrentIndex >= 0 && IsIndexSelectable(CurrentIndex)) Activate(CurrentIndex);
                return;
            }
            if (key == Keys.Space)
            {
                if (CurrentIndex < 0 || !IsIndexSelectable(CurrentIndex)) return;
                if (SelectionMode == ItemListSelectionMode.Single) Select(CurrentIndex);
                else ToggleSelection(CurrentIndex);
                return;
            }

            var current = CurrentIndex < 0 ? FindSelectable(0, 1, false) : CurrentIndex;
            if (current < 0) return;
            int target;
            if (key == Keys.Home) target = FindSelectable(0, 1, false);
            else if (key == Keys.End) target = FindSelectable(ItemCount - 1, -1, false);
            else if (key == Keys.PageUp) target = FindSelectable(Math.Max(0, current - GetPageStep()), -1, false);
            else if (key == Keys.PageDown) target = FindSelectable(Math.Min(ItemCount - 1, current + GetPageStep()), 1, false);
            else
            {
                var step = GetNavigationStep(key);
                if (step == 0) return;
                target = FindSelectable(current + step, step, WrapNavigation);
            }
            if (target < 0) return;

            var keyboard = Context?.CurrentKeyboardState ?? default;
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (shift && SelectionMode == ItemListSelectionMode.Multi)
            {
                var anchor = _rangeAnchorToken == null ? current : IndexOfItemToken(_rangeAnchorToken);
                if (anchor < 0) anchor = current;
                SelectRange(anchor, target);
            }
            else
            {
                _rangeAnchorToken = null;
                if (SelectionMode == ItemListSelectionMode.Single) Select(target);
                else CurrentIndex = target;
            }
            BringIndexIntoView(target);
        }

        protected virtual void SelectItemFromPointer(int index, Point point) => SelectFromPointer(index);

        internal override void TextInput(char character)
        {
            if (!IsTextSearchEnabled || ItemCount == 0 || char.IsControl(character)) return;
            var now = Context?.CurrentTime ?? TimeSpan.Zero;
            if (_lastSearchTime == TimeSpan.MinValue || now - _lastSearchTime > IncrementalSearchTimeout) _searchText = string.Empty;
            _lastSearchTime = now;
            var input = character.ToString();
            if (_searchText.Length == 1 && string.Equals(_searchText, input, StringComparison.OrdinalIgnoreCase))
            {
                MoveToSearchResult(_searchText, CurrentIndex);
                return;
            }
            _searchText += input;
            if (!MoveToSearchResult(_searchText, CurrentIndex < 0 ? -1 : CurrentIndex - 1) && _searchText.Length > 1)
            {
                _searchText = input;
                MoveToSearchResult(_searchText, CurrentIndex);
            }
        }

        void IScrollViewportOwner.OnScrollMetricsChanged(ScrollPresenter presenter, ScrollMetrics metrics)
        {
            if (!ReferenceEquals(presenter, _scrollPresenter)) return;
            _viewportController.UpdateMetrics(metrics.Viewport, metrics.Extent);
            if (metrics.Offset != ScrollOffset) ScrollOffset = metrics.Offset;
            OnScrollMetricsChanged(metrics);
        }

        protected virtual void OnScrollMetricsChanged(ScrollMetrics metrics) { }

        void IScrollViewportOwner.BringIntoView(ScrollPresenter presenter, Control target, Rectangle targetBounds)
        {
            if (!ReferenceEquals(presenter, _scrollPresenter)) return;
            var viewport = new Rectangle(
                (int)MathF.Round(ScrollOffset.X),
                (int)MathF.Round(ScrollOffset.Y),
                (int)MathF.Round(Viewport.X),
                (int)MathF.Round(Viewport.Y));
            _viewportController.BringIntoView(viewport, targetBounds);
            QueueLayout();
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= ItemCount) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private SelectionEntry CreateSelection(int index) => new SelectionEntry(GetItemToken(index), GetItemAt(index));

        private SelectionEntry FindSelection(object token)
        {
            foreach (var selection in _selection)
                if (ReferenceEquals(selection.Token, token)) return selection;
            return null;
        }

        private bool IsSelectedToken(object token) => FindSelection(token) != null;

        private bool IsIndexSelectable(int index)
        {
            if (!TryGetRealizedContainer(index, out var container)) return true;
            return container.IsEffectivelyEnabled && (container is not ListBoxItem item || item.IsSelectable);
        }

        private void SelectFromPointer(int index)
        {
            if (!IsIndexSelectable(index)) return;
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var command = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl) ||
                keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (SelectionMode == ItemListSelectionMode.Multi && shift && CurrentIndex >= 0)
            {
                var anchor = _rangeAnchorToken == null ? CurrentIndex : IndexOfItemToken(_rangeAnchorToken);
                SelectRange(anchor < 0 ? CurrentIndex : anchor, index);
            }
            else if (SelectionMode == ItemListSelectionMode.Toggle ||
                (SelectionMode == ItemListSelectionMode.Multi && command && IsSelectedToken(GetItemToken(index))))
            {
                _rangeAnchorToken = null;
                ToggleSelection(index);
            }
            else
            {
                _rangeAnchorToken = null;
                Select(index, SelectionMode == ItemListSelectionMode.Multi && command);
            }
            _searchText = string.Empty;
        }

        private int GetIndexAtPosition(Point point)
        {
            for (var index = 0; index < ItemCount; index++)
                if (TryGetRealizedContainer(index, out var container) && container.VisualBounds.Contains(point)) return index;
            return -1;
        }

        private int GetNavigationStep(Keys key)
        {
            if (_itemsPresenter?.Panel is VirtualizingGridPanel grid)
            {
                if (key == Keys.Up) return -Math.Max(1, grid.ColumnCount);
                if (key == Keys.Down) return Math.Max(1, grid.ColumnCount);
                if (key == Keys.Left) return IsLayoutRtl() ? 1 : -1;
                if (key == Keys.Right) return IsLayoutRtl() ? -1 : 1;
                return 0;
            }
            var horizontal = (_itemsPresenter?.Panel is VirtualizingStackPanel stack && stack.Orientation == Orientation.Horizontal) ||
                (_itemsPresenter?.Panel is StackPanel regularStack && regularStack.Orientation == Orientation.Horizontal);
            if (horizontal)
            {
                if (key == Keys.Left) return IsLayoutRtl() ? 1 : -1;
                if (key == Keys.Right) return IsLayoutRtl() ? -1 : 1;
                return 0;
            }
            if (key == Keys.Up) return -1;
            if (key == Keys.Down) return 1;
            return 0;
        }

        private int FindSelectable(int start, int step, bool wrap)
        {
            if (ItemCount == 0 || step == 0) return -1;
            var index = start;
            for (var visited = 0; visited < ItemCount; visited++)
            {
                if (wrap)
                {
                    index %= ItemCount;
                    if (index < 0) index += ItemCount;
                }
                else if (index < 0 || index >= ItemCount) return -1;
                if (IsIndexSelectable(index)) return index;
                index += Math.Sign(step);
            }
            return -1;
        }

        protected int GetPageStep()
        {
            var realized = 0;
            for (var index = 0; index < ItemCount; index++)
                if (TryGetRealizedContainer(index, out _)) realized++;
            return Math.Max(1, realized > 1 ? realized - 1 : 4);
        }

        private bool MoveToSearchResult(string query, int startIndex)
        {
            var index = FindNextItem(query, startIndex, wrap: true);
            if (index < 0 || !IsIndexSelectable(index)) return false;
            if (SelectionMode == ItemListSelectionMode.Single) Select(index);
            else CurrentIndex = index;
            BringIndexIntoView(index);
            return true;
        }

        private void ChangeSelection(Action change, bool forceEvent = false)
        {
            var oldIndices = _selectedIndices;
            var oldItems = SelectedItems;
            change();
            RefreshSelectionProjections();
            UpdateRealizedSelectionStates();
            if (forceEvent || !SameIndices(oldIndices, _selectedIndices)) RaiseSelectionChanged(oldIndices, oldItems);
        }

        private void RefreshSelectionProjections()
        {
            _selection.Sort((left, right) => IndexOfItemToken(left.Token).CompareTo(IndexOfItemToken(right.Token)));
            var indices = new int[_selection.Count];
            for (var index = 0; index < _selection.Count; index++) indices[index] = IndexOfItemToken(_selection[index].Token);
            _selectedIndices = indices;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedIndex));
            OnPropertyChanged(nameof(SelectedItem));
            OnPropertyChanged(nameof(SelectedIndices));
            OnPropertyChanged(nameof(SelectedItems));
        }

        private void UpdateRealizedSelectionStates()
        {
            for (var index = 0; index < ItemCount; index++)
            {
                if (!TryGetRealizedContainer(index, out var container) || container is not ListBoxItem listBoxItem) continue;
                var token = GetItemToken(index);
                listBoxItem.SetSelectionState(IsSelectedToken(token), ReferenceEquals(_currentToken, token));
            }
        }

        private void RaiseSelectionChanged(IReadOnlyList<int> oldIndices, IReadOnlyList<object> oldItems) =>
            SelectionChanged?.Invoke(this, new ListBoxSelectionChangedEventArgs(oldIndices, oldItems, _selectedIndices, SelectedItems));

        private static bool SameIndices(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++) if (left[index] != right[index]) return false;
            return true;
        }

        private sealed class SelectionEntry
        {
            public SelectionEntry(object token, object item)
            {
                Token = token;
                Item = item;
            }

            public object Token { get; }
            public object Item { get; }
        }
    }
}