// Copyright (c) 2026 Igor Hipolito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public enum DataGridMode { Flat, Hierarchical }
    public enum DataGridSelectionUnit { Row, Cell }

    public readonly struct CellIndex : IEquatable<CellIndex>
    {
        public CellIndex(IndexPath rowPath, int columnIndex)
        {
            if (rowPath.Count == 0) throw new ArgumentException("A cell index requires a row path.", nameof(rowPath));
            if (columnIndex < 0) throw new ArgumentOutOfRangeException(nameof(columnIndex));
            RowPath = rowPath;
            ColumnIndex = columnIndex;
        }

        public IndexPath RowPath { get; }
        public int ColumnIndex { get; }
        public bool Equals(CellIndex other) => RowPath == other.RowPath && ColumnIndex == other.ColumnIndex;
        public override bool Equals(object obj) => obj is CellIndex other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RowPath, ColumnIndex);
        public static bool operator ==(CellIndex left, CellIndex right) => left.Equals(right);
        public static bool operator !=(CellIndex left, CellIndex right) => !left.Equals(right);
    }

    public sealed class DataGridCellSelectionChangedEventArgs : EventArgs
    {
        internal DataGridCellSelectionChangedEventArgs(IReadOnlyList<CellIndex> oldCells, IReadOnlyList<CellIndex> newCells)
        {
            OldCells = oldCells;
            NewCells = newCells;
        }

        public IReadOnlyList<CellIndex> OldCells { get; }
        public IReadOnlyList<CellIndex> NewCells { get; }
    }

    public sealed class DataGridSortBinding
    {
        private readonly Comparison<object> _comparison;

        private DataGridSortBinding(Comparison<object> comparison) =>
            _comparison = comparison ?? throw new ArgumentNullException(nameof(comparison));

        public static DataGridSortBinding Create<TItem, TKey>(
            Func<TItem, TKey> keySelector,
            IComparer<TKey> comparer = null)
        {
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            comparer ??= Comparer<TKey>.Default;
            return new DataGridSortBinding((left, right) =>
                comparer.Compare(keySelector((TItem)left), keySelector((TItem)right)));
        }

        internal int Compare(object left, object right) => _comparison(left, right);
    }

    public sealed class DataGridSortDescription
    {
        public DataGridSortDescription(DataGridColumn column, DataGridSortDirection direction)
        {
            Column = column ?? throw new ArgumentNullException(nameof(column));
            if (!Enum.IsDefined(typeof(DataGridSortDirection), direction)) throw new ArgumentOutOfRangeException(nameof(direction));
            Direction = direction;
        }

        public DataGridColumn Column { get; }
        public DataGridSortDirection Direction { get; }
    }

    public sealed class DataGridBinding<TValue>
    {
        private readonly Func<object, TValue> _read;
        private readonly Action<object, TValue> _write;

        private DataGridBinding(Func<object, TValue> read, Action<object, TValue> write, string propertyName)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write;
            PropertyName = propertyName;
        }

        public string PropertyName { get; }
        public bool CanWrite => _write != null;

        public static DataGridBinding<TValue> Create<TItem>(
            Func<TItem, TValue> read,
            string propertyName = null,
            Action<TItem, TValue> write = null)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            return new DataGridBinding<TValue>(
                item => read((TItem)item),
                write == null ? null : (item, value) => write((TItem)item, value),
                propertyName);
        }

        internal TValue Read(object item) => _read(item);

        internal void Write(object item, TValue value)
        {
            if (_write == null) throw new InvalidOperationException("The data-grid binding is read-only.");
            _write(item, value);
        }

        internal IDisposable Attach(object item, Action<TValue> update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            update(_read(item));
            if (item is not INotifyPropertyChanged notifier) return EmptyDisposable.Instance;
            PropertyChangedEventHandler handler = (_, args) =>
            {
                if (string.IsNullOrEmpty(PropertyName) || string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == PropertyName)
                    update(_read(item));
            };
            notifier.PropertyChanged += handler;
            return new DelegateDisposable(() => notifier.PropertyChanged -= handler);
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static EmptyDisposable Instance { get; } = new EmptyDisposable();
            public void Dispose() { }
        }

        private sealed class DelegateDisposable : IDisposable
        {
            private Action _dispose;
            public DelegateDisposable(Action dispose) => _dispose = dispose;
            public void Dispose() => System.Threading.Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    public abstract class DataGridColumn
    {
        private object _header;
        private DataTemplate _headerTemplate;
        private DataTemplate _cellTemplate;
        private GridTrackSize _width = GridTrackSize.Star();
        private float _minimumWidth;
        private float _maximumWidth = float.PositiveInfinity;
        private int _displayIndex = -1;
        private bool _isVisible = true;
        private bool _canUserResize = true;
        private bool _canUserSort = true;
        private DataGridSortBinding _sortBinding;
        private HorizontalAlignment _horizontalCellAlignment = HorizontalAlignment.Fill;
        private VerticalAlignment _verticalCellAlignment = VerticalAlignment.Fill;

        public object Header { get => _header; set => Set(ref _header, value); }
        public DataTemplate HeaderTemplate { get => _headerTemplate; set => Set(ref _headerTemplate, value); }
        public DataTemplate CellTemplate { get => _cellTemplate; set => Set(ref _cellTemplate, value); }
        public GridTrackSize Width { get => _width; set => Set(ref _width, value); }
        public float MinimumWidth
        {
            get => _minimumWidth;
            set
            {
                ValidateWidth(value, nameof(value));
                if (value > _maximumWidth) throw new ArgumentOutOfRangeException(nameof(value));
                Set(ref _minimumWidth, value);
            }
        }
        public float MaximumWidth
        {
            get => _maximumWidth;
            set
            {
                if ((!float.IsFinite(value) && !float.IsPositiveInfinity(value)) || value < _minimumWidth)
                    throw new ArgumentOutOfRangeException(nameof(value));
                Set(ref _maximumWidth, value);
            }
        }
        public int DisplayIndex
        {
            get => _displayIndex;
            set
            {
                if (value < -1) throw new ArgumentOutOfRangeException(nameof(value));
                Set(ref _displayIndex, value);
            }
        }
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                if (value && VisibilityValidator?.Invoke(this) == false)
                    throw new InvalidOperationException($"DataGrid supports at most {DataGrid.MaximumSupportedVisibleColumns} visible columns.");
                Set(ref _isVisible, value);
            }
        }
        public bool CanUserResize { get => _canUserResize; set => Set(ref _canUserResize, value); }
        public bool CanUserSort { get => _canUserSort; set => Set(ref _canUserSort, value); }
        public DataGridSortBinding SortBinding { get => _sortBinding; set => Set(ref _sortBinding, value); }
        public HorizontalAlignment HorizontalCellAlignment { get => _horizontalCellAlignment; set => Set(ref _horizontalCellAlignment, value); }
        public VerticalAlignment VerticalCellAlignment { get => _verticalCellAlignment; set => Set(ref _verticalCellAlignment, value); }

        internal event EventHandler Changed;
        internal Func<DataGridColumn, bool> VisibilityValidator { get; set; }

        internal void PrepareCell(DataGridCell cell, object item)
        {
            cell.Column = this;
            cell.RowItem = item;
            cell.DataContext = item;
            cell.HorizontalAlignment = HorizontalCellAlignment;
            cell.VerticalAlignment = VerticalCellAlignment;
            cell.CustomMinimumSize = new Vector2(MinimumWidth, 0);
            cell.CustomMaximumSize = new Vector2(float.IsPositiveInfinity(MaximumWidth) ? -1 : MaximumWidth, -1);
            cell.Classes.Add("data-grid-cell");
            PopulateCell(cell, item);
        }

        protected virtual void PopulateCell(DataGridCell cell, object item)
        {
            cell.Content = item;
            cell.ContentTemplate = CellTemplate;
        }

        protected void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

        private void Set<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            NotifyChanged();
        }

        private static void ValidateWidth(float value, string parameterName)
        {
            if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public class DataGridTextColumn : DataGridColumn
    {
        private DataGridBinding<string> _binding;
        private string _stringFormat;

        public DataGridBinding<string> Binding
        {
            get => _binding;
            set
            {
                if (ReferenceEquals(_binding, value)) return;
                _binding = value;
                NotifyChanged();
            }
        }
        public string StringFormat
        {
            get => _stringFormat;
            set
            {
                if (_stringFormat == value) return;
                _stringFormat = value;
                NotifyChanged();
            }
        }

        protected override void PopulateCell(DataGridCell cell, object item)
        {
            if (CellTemplate != null)
            {
                base.PopulateCell(cell, item);
                return;
            }
            var text = new TextBlock();
            cell.Content = text;
            if (Binding == null) text.Text = Format(item?.ToString());
            else cell.Track(Binding.Attach(item, value => text.Text = Format(value)));
        }

        private string Format(string value) => string.IsNullOrEmpty(StringFormat)
            ? value ?? string.Empty
            : string.Format(CultureInfo.CurrentCulture, StringFormat, value);
    }

    public class DataGridCheckBoxColumn : DataGridColumn
    {
        private DataGridBinding<bool> _binding;
        public DataGridBinding<bool> Binding
        {
            get => _binding;
            set
            {
                if (ReferenceEquals(_binding, value)) return;
                _binding = value;
                NotifyChanged();
            }
        }

        protected override void PopulateCell(DataGridCell cell, object item)
        {
            if (CellTemplate != null)
            {
                base.PopulateCell(cell, item);
                return;
            }
            var checkBox = new CheckBox();
            var updating = false;
            cell.Content = checkBox;
            if (Binding != null)
            {
                cell.Track(Binding.Attach(item, value =>
                {
                    updating = true;
                    checkBox.Checked = value;
                    updating = false;
                }));
                if (Binding.CanWrite) checkBox.Toggled += (_, value) =>
                {
                    if (!updating) Binding.Write(item, value);
                };
            }
        }
    }

    public class DataGridTemplateColumn : DataGridColumn { }

    internal sealed class DataGridExpanderButton : BaseButton
    {
        private bool _expanded;

        internal DataGridExpanderButton(bool expanded)
        {
            Flat = true;
            Padding = new Thickness(2);
            IconAlignment = HorizontalAlignment.Center;
            CustomMinimumSize = new Vector2(18, 18);
            DecorativeIconProvider = ResolveExpanderIcon;
            HideTextWhenDecorativeIconAvailable = true;
            SetExpanded(expanded);
        }

        internal bool Expanded => _expanded;

        internal void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            Text = expanded ? "-" : "+";
            AccessibilityLabel = expanded ? "Collapse row" : "Expand row";
            TooltipText = AccessibilityLabel;
            QueueLayout();
        }

        internal ThemeIcon? ResolveExpanderIcon()
        {
            var iconName = _expanded
                ? "arrow"
                : IsLayoutRtl() ? "arrow_collapsed_mirrored" : "arrow_collapsed";
            return GetThemeIcon(iconName, nameof(Tree));
        }
    }

    public class DataGridExpanderColumn : DataGridColumn
    {
        private DataGridColumn _column;
        private DataGridBinding<IEnumerable> _children;
        private DataGridBinding<bool> _hasChildren;
        private DataGridBinding<bool> _isExpanded;
        public DataGridColumn Column
        {
            get => _column;
            set
            {
                if (ReferenceEquals(_column, value)) return;
                _column = value;
                NotifyChanged();
            }
        }
        public DataGridBinding<IEnumerable> Children
        {
            get => _children;
            set
            {
                if (ReferenceEquals(_children, value)) return;
                _children = value;
                NotifyChanged();
            }
        }
        public DataGridBinding<bool> HasChildren
        {
            get => _hasChildren;
            set
            {
                if (ReferenceEquals(_hasChildren, value)) return;
                _hasChildren = value;
                NotifyChanged();
            }
        }
        public DataGridBinding<bool> IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (ReferenceEquals(_isExpanded, value)) return;
                _isExpanded = value;
                NotifyChanged();
            }
        }

        protected override void PopulateCell(DataGridCell cell, object item)
        {
            if (CellTemplate != null || Column == null) base.PopulateCell(cell, item);
            else
            {
                Column.PrepareCell(cell, item);
                var content = cell.Content;
                var contentTemplate = cell.ContentTemplate;
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                var expander = new DataGridExpanderButton(
                    cell.Grid?.HierarchySource?.IsExpanded(cell.RowPath) == true)
                {
                    Enabled = cell.Grid?.HierarchySource?.HasChildren(cell.RowPath) == true,
                };
                if (cell.Grid != null) expander.Pressed += (_, _) =>
                {
                    if (cell.Grid.Toggle(cell.RowPath))
                        expander.SetExpanded(cell.Grid.HierarchySource?.IsExpanded(cell.RowPath) == true);
                };
                panel.AddChild(expander);
                if (content is Control control) panel.AddChild(control);
                else if (content != null)
                    panel.AddChild(new ContentPresenter { Content = content, ContentTemplate = contentTemplate });
                cell.ContentTemplate = null;
                cell.Content = panel;
                cell.Column = this;
            }
        }
    }

    /// <summary>Presents one row value for a column and tracks its selected, current, and selectable states.</summary>
    public class DataGridCell : ContentControl
    {
        private readonly List<IDisposable> _attachments = new List<IDisposable>();
        private bool _isSelected;
        private bool _isCurrent;
        private bool _isSelectable = true;
        public DataGridCell() => Padding = new Thickness(10, 5, 10, 5);
        /// <summary>A cell of a <see cref="DataGrid"/>, which reports <see cref="AccessibilityRole.Grid"/>.
        /// A grid whose cells are anonymous cannot be navigated cell by cell.</summary>
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Cell;
        public DataGridColumn Column { get; internal set; }
        public object RowItem { get; internal set; }
        public int ColumnIndex { get; internal set; } = -1;
        public Thickness Padding { get; set; }
        internal DataGrid Grid { get; set; }
        internal IndexPath RowPath { get; set; }
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

        internal void SetSelectionState(bool selected, bool current)
        {
            if (_isSelected != selected)
            {
                _isSelected = selected;
                SetPseudoState("selected", selected);
                OnPropertyChanged(nameof(IsSelected));
            }
            if (_isCurrent != current)
            {
                _isCurrent = current;
                SetPseudoState("current", current);
                OnPropertyChanged(nameof(IsCurrent));
            }
        }

        internal void Track(IDisposable attachment)
        {
            if (attachment != null) _attachments.Add(attachment);
        }

        internal void ClearCell()
        {
            for (var index = _attachments.Count - 1; index >= 0; index--) _attachments[index].Dispose();
            _attachments.Clear();
            Content = null;
            ContentTemplate = null;
            Classes.Remove("data-grid-cell");
            DataContext = null;
            Column = null;
            RowItem = null;
            ColumnIndex = -1;
            IsSelectable = true;
            Grid = null;
            RowPath = default;
            SetSelectionState(false, false);
        }
    }

    /// <summary>Presents a column heading and activates supported column sorting when clicked.</summary>
    public class DataGridColumnHeader : ContentControl
    {
        private DataGridSortDirection? _sortDirection;
        public DataGridColumnHeader()
        {
            Padding = new Thickness(10, 7, 26, 7);
            FontWeight = UIFontWeight.SemiBold;
        }
        public DataGridColumn Column { get; internal set; }
        public int ColumnIndex { get; internal set; } = -1;
        public Thickness Padding { get; set; }
        public DataGridSortDirection? SortDirection => _sortDirection;
        internal DataGrid Owner { get; set; }

        internal void SetSortDirection(DataGridSortDirection? direction)
        {
            if (_sortDirection == direction) return;
            _sortDirection = direction;
            SetPseudoState("ascending", direction == DataGridSortDirection.Ascending);
            SetPseudoState("descending", direction == DataGridSortDirection.Descending);
            OnPropertyChanged(nameof(SortDirection));
        }

        protected internal override void PointerReleased(Point point, bool isInside)
        {
            base.PointerReleased(point, isInside);
            if (isInside && Owner?.CanUserSortColumns == true && Column?.CanUserSort == true && Column.SortBinding != null)
                Owner.ActivateColumnHeader(ColumnIndex);
        }
    }

    /// <summary>Builds a row of column-aligned cells and tracks hierarchical expansion state.</summary>
    public class DataGridRow : ListBoxItem
    {
        private readonly List<DataGridCell> _cells = new List<DataGridCell>();
        private bool _isExpanded;
        private bool _isCollapsed;
        /// <summary>Overrides the <see cref="AccessibilityRole.ListItem"/> inherited from
        /// <see cref="ListBoxItem"/>: inside a grid this is a row, and its cells are the items.</summary>
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Row;
        public DataGrid Owner { get; private set; }
        public IndexPath IndexPath { get; private set; }
        public int RowIndex { get; private set; } = -1;
        public IReadOnlyList<DataGridCell> Cells => _cells;
        public bool IsExpanded => _isExpanded;
        public bool IsCollapsed => _isCollapsed;

        internal void SetExpansionState(bool hasChildren, bool expanded)
        {
            var collapsed = hasChildren && !expanded;
            if (_isExpanded != expanded)
            {
                _isExpanded = expanded;
                SetPseudoState("expanded", expanded);
                OnPropertyChanged(nameof(IsExpanded));
            }
            if (_isCollapsed != collapsed)
            {
                _isCollapsed = collapsed;
                SetPseudoState("collapsed", collapsed);
                OnPropertyChanged(nameof(IsCollapsed));
            }
        }

        internal void Prepare(DataGrid owner, object item, int rowIndex)
        {
            ClearCells();
            Owner = owner;
            RowIndex = rowIndex;
            IndexPath = owner.GetRowPath(rowIndex);
            SetExpansionState(owner.HierarchySource?.HasChildren(IndexPath) == true, owner.HierarchySource?.IsExpanded(IndexPath) == true);
            var panel = new GridPanel();
            var columns = owner.GetDisplayColumns();
            owner.ConfigureColumns(panel, columns);
            for (var index = 0; index < columns.Count; index++)
            {
                var cell = new DataGridCell
                {
                    ColumnIndex = index,
                    Grid = owner,
                    RowPath = IndexPath,
                };
                columns[index].PrepareCell(cell, item);
                cell.SetSelectionState(owner.IsCellSelected(cell.RowPath, index), owner.CurrentCell == new CellIndex(cell.RowPath, index));
                GridPanel.SetColumn(cell, index);
                panel.AddChild(cell);
                _cells.Add(cell);
            }
            ContentTemplate = null;
            Content = panel;
        }

        internal void UpdatePosition(int rowIndex)
        {
            if (Owner == null) return;
            RowIndex = rowIndex;
            IndexPath = Owner.GetRowPath(rowIndex);
            SetExpansionState(Owner.HierarchySource?.HasChildren(IndexPath) == true, Owner.HierarchySource?.IsExpanded(IndexPath) == true);
            foreach (var cell in _cells)
            {
                cell.RowPath = IndexPath;
                cell.SetSelectionState(Owner.IsCellSelected(IndexPath, cell.ColumnIndex), Owner.CurrentCell == new CellIndex(IndexPath, cell.ColumnIndex));
            }
        }

        internal void ClearCells()
        {
            foreach (var cell in _cells) cell.ClearCell();
            _cells.Clear();
            Content = null;
            ContentTemplate = null;
            Owner = null;
            RowIndex = -1;
            IndexPath = default;
            SetExpansionState(false, false);
        }
    }

    /// <summary>Displays tabular data with virtualized rows, configurable columns, sorting, filtering, and row or cell selection.</summary>
    [TemplatePart(ScrollPresenterPartName, typeof(ScrollPresenter))]
    [TemplatePart(ItemsPresenterPartName, typeof(ItemsPresenter))]
    [TemplatePart(ColumnHeadersPartName, typeof(GridPanel))]
    public class DataGrid : ListBox
    {
        private sealed class ColumnCollection : ObservableCollection<DataGridColumn>
        {
            protected override void InsertItem(int index, DataGridColumn item)
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                if (item.IsVisible && this.Count(column => column.IsVisible) >= MaximumSupportedVisibleColumns)
                    throw new InvalidOperationException($"DataGrid supports at most {MaximumSupportedVisibleColumns} visible columns.");
                base.InsertItem(index, item);
            }

            protected override void SetItem(int index, DataGridColumn item)
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                if (item.IsVisible && !this[index].IsVisible && this.Count(column => column.IsVisible) >= MaximumSupportedVisibleColumns)
                    throw new InvalidOperationException($"DataGrid supports at most {MaximumSupportedVisibleColumns} visible columns.");
                base.SetItem(index, item);
            }
        }

        public const string ColumnHeadersPartName = "PART_ColumnHeaders";
        public const string HorizontalScrollBarPresenterPartName = "PART_HorizontalScrollBarPresenter";
        public const string VerticalScrollBarPresenterPartName = "PART_VerticalScrollBarPresenter";
        public const int MaximumSupportedVisibleColumns = 256;
        public const float DefaultEstimatedRowExtent = 30;
        public const float DefaultColumnHeaderHeight = 34;
        public const int DefaultAsynchronousSortThreshold = 1_000;
        private readonly HashSet<DataGridColumn> _observedColumns = new HashSet<DataGridColumn>();
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private GridPanel _columnHeaders;
        private ContentPresenter _horizontalScrollBarPresenter;
        private ContentPresenter _verticalScrollBarPresenter;
        private readonly HScrollBar _horizontalScrollBar;
        private readonly VScrollBar _verticalScrollBar;
        private DataGridMode _mode;
        private bool _canUserResizeColumns = true;
        private bool _canUserSortColumns = true;
        private IEnumerable _rootItemsSource;
        private DataGridSource<object> _projectedSource;
        private Predicate<object> _filter;
        private DataGridFilterMode _filterMode;
        private DataGridSelectionUnit _selectionUnit;
        private readonly List<CellIndex> _selectedCells = new List<CellIndex>();
        private CellIndex? _currentCell;
        private CellIndex? _cellRangeAnchor;
        private bool _alternatingRowBackground = true;
        private bool _showHorizontalGridLines = true;
        private bool _showVerticalGridLines = true;
        private ScrollBarVisibility _horizontalScrollMode = ScrollBarVisibility.Auto;
        private ScrollBarVisibility _verticalScrollMode = ScrollBarVisibility.Auto;
        private CancellationTokenSource _sortCancellation;
        private Task<DataGridSource<object>.PreparedSort> _pendingSort;
        private IReadOnlyList<IndexPath> _pendingSelectedPaths = Array.Empty<IndexPath>();
        private IndexPath? _pendingCurrentPath;
        private bool _isSorting;

        public DataGrid()
        {
            _horizontalScrollBar = new HScrollBar { Visible = false, ZIndex = 2 };
            _verticalScrollBar = new VScrollBar { Visible = false, ZIndex = 2 };
            _horizontalScrollBar.IsXamlInfrastructure = true;
            _verticalScrollBar.IsXamlInfrastructure = true;
            _horizontalScrollBar.ValueChanged += (_, value) => ScrollOffset = new Vector2(value, ScrollOffset.Y);
            _verticalScrollBar.ValueChanged += (_, value) => ScrollOffset = new Vector2(ScrollOffset.X, value);
            base.AddChild(_horizontalScrollBar);
            base.AddChild(_verticalScrollBar);
            ItemsPanel = new ItemsPanelTemplate(_ => new VirtualizingStackPanel
            {
                EstimatedItemExtent = DefaultEstimatedRowExtent,
            });
            Columns = new ColumnCollection();
            Columns.CollectionChanged += ColumnsChanged;
            SortDescriptions = new ObservableCollection<DataGridSortDescription>();
            SortDescriptions.CollectionChanged += SortDescriptionsChanged;
        }

        public ObservableCollection<DataGridColumn> Columns { get; }
        public ObservableCollection<DataGridSortDescription> SortDescriptions { get; }
        public int RealizedCellCount => RealizedCount * GetDisplayColumns().Count;
        public int AsynchronousSortThreshold { get; set; } = DefaultAsynchronousSortThreshold;
        public ScrollBarVisibility HorizontalScrollMode
        {
            get => _horizontalScrollMode;
            set
            {
                if (!Enum.IsDefined(typeof(ScrollBarVisibility), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_horizontalScrollMode == value) return;
                _horizontalScrollMode = value;
                QueueLayout();
            }
        }
        public ScrollBarVisibility VerticalScrollMode
        {
            get => _verticalScrollMode;
            set
            {
                if (!Enum.IsDefined(typeof(ScrollBarVisibility), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_verticalScrollMode == value) return;
                _verticalScrollMode = value;
                QueueLayout();
            }
        }
        public HScrollBar HorizontalScrollBar => _horizontalScrollBar;
        public VScrollBar VerticalScrollBar => _verticalScrollBar;
        public bool IsSorting
        {
            get => _isSorting;
            private set
            {
                if (_isSorting == value) return;
                _isSorting = value;
                OnPropertyChanged(nameof(IsSorting));
            }
        }
        public bool AlternatingRowBackground
        {
            get => _alternatingRowBackground;
            set { if (_alternatingRowBackground == value) return; _alternatingRowBackground = value; OnPropertyChanged(nameof(AlternatingRowBackground)); }
        }
        public bool ShowHorizontalGridLines
        {
            get => _showHorizontalGridLines;
            set { if (_showHorizontalGridLines == value) return; _showHorizontalGridLines = value; OnPropertyChanged(nameof(ShowHorizontalGridLines)); }
        }
        public bool ShowVerticalGridLines
        {
            get => _showVerticalGridLines;
            set { if (_showVerticalGridLines == value) return; _showVerticalGridLines = value; OnPropertyChanged(nameof(ShowVerticalGridLines)); }
        }
        public new ItemListSelectionMode SelectionMode
        {
            get => base.SelectionMode;
            set
            {
                base.SelectionMode = value;
                if (value != ItemListSelectionMode.Single || _selectedCells.Count <= 1) return;
                var retained = _currentCell.HasValue && _selectedCells.Contains(_currentCell.Value)
                    ? _currentCell.Value
                    : _selectedCells[0];
                var old = _selectedCells.ToArray();
                _selectedCells.Clear();
                _selectedCells.Add(retained);
                _currentCell = retained;
                RaiseCellSelectionChanged(old);
            }
        }
        public new IEnumerable ItemsSource
        {
            get => _rootItemsSource;
            set
            {
                if (ReferenceEquals(_rootItemsSource, value)) return;
                _rootItemsSource = value;
                RebuildItemsProjection();
                OnPropertyChanged(nameof(ItemsSource));
            }
        }
        public IDataGridSource HierarchySource => Mode == DataGridMode.Hierarchical ? _projectedSource : null;
        public DataGridMode Mode
        {
            get => _mode;
            set
            {
                if (!Enum.IsDefined(typeof(DataGridMode), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_mode == value) return;
                _mode = value;
                RebuildItemsProjection();
                OnPropertyChanged(nameof(Mode));
            }
        }
        public bool CanUserResizeColumns
        {
            get => _canUserResizeColumns;
            set
            {
                if (_canUserResizeColumns == value) return;
                _canUserResizeColumns = value;
                OnPropertyChanged(nameof(CanUserResizeColumns));
            }
        }
        public bool CanUserSortColumns
        {
            get => _canUserSortColumns;
            set
            {
                if (_canUserSortColumns == value) return;
                _canUserSortColumns = value;
                OnPropertyChanged(nameof(CanUserSortColumns));
            }
        }
        public Predicate<object> Filter { get => _filter; set => _filter = value; }
        public DataGridSelectionUnit SelectionUnit
        {
            get => _selectionUnit;
            set
            {
                if (!Enum.IsDefined(typeof(DataGridSelectionUnit), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_selectionUnit == value) return;
                _selectionUnit = value;
                if (value == DataGridSelectionUnit.Row) ClearCellSelection();
                else ClearSelection();
                OnPropertyChanged(nameof(SelectionUnit));
            }
        }
        public IReadOnlyList<CellIndex> SelectedCells => _selectedCells.ToArray();
        public IReadOnlyList<IndexPath> SelectedRowPaths => SelectedIndices.Select(GetRowPath).ToArray();
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Grid;
        internal override AccessibilityRole GetAccessibilityItemRole(int index) => AccessibilityRole.Row;
        public IndexPath? CurrentRowPath => CurrentIndex < 0 ? null : GetRowPath(CurrentIndex);
        public CellIndex? CurrentCell
        {
            get => _currentCell;
            set
            {
                if (value.HasValue) ValidateCell(value.Value);
                if (_currentCell == value) return;
                _currentCell = value;
                UpdateCellStates();
                OnPropertyChanged(nameof(CurrentCell));
            }
        }
        public DataGridFilterMode FilterMode
        {
            get => _filterMode;
            set
            {
                if (!Enum.IsDefined(typeof(DataGridFilterMode), value)) throw new ArgumentOutOfRangeException(nameof(value));
                _filterMode = value;
            }
        }
        public event EventHandler<DataGridCellSelectionChangedEventArgs> CellSelectionChanged;

        protected override bool CanRealizeItems => true;
        protected override Control GetContainerForItem(object item) => new DataGridRow();
        protected override bool IsContainerCompatibleForItem(Control container, object item) => container is DataGridRow;

        public IReadOnlyList<DataGridColumn> GetDisplayColumns()
        {
            var columns = Columns
                .Select((column, index) => (Column: column, Index: index))
                .Where(entry => entry.Column.IsVisible)
                .OrderBy(entry => entry.Column.DisplayIndex < 0 ? entry.Index : entry.Column.DisplayIndex)
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Column)
                .ToArray();
            if (columns.Length > MaximumSupportedVisibleColumns)
                throw new InvalidOperationException($"DataGrid supports at most {MaximumSupportedVisibleColumns} visible columns.");
            return columns;
        }

        public DataGridCell GetCell(int rowIndex, int columnIndex)
        {
            var row = GetRealizedContainer(rowIndex) as DataGridRow
                ?? throw new InvalidOperationException("The realized DataGrid container is not a DataGridRow.");
            if (columnIndex < 0 || columnIndex >= row.Cells.Count) throw new ArgumentOutOfRangeException(nameof(columnIndex));
            return row.Cells[columnIndex];
        }

        public IndexPath GetRowPath(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= ItemCount) throw new ArgumentOutOfRangeException(nameof(rowIndex));
            return _projectedSource?.GetPath(rowIndex) ?? new IndexPath(rowIndex);
        }

        public bool Expand(IndexPath path) => HierarchySource?.Expand(path) == true;
        public bool Collapse(IndexPath path) => HierarchySource?.Collapse(path) == true;
        public bool Toggle(IndexPath path) => HierarchySource?.Toggle(path) == true;
        public int ExpandAll(Func<object, bool> predicate = null) => Mode == DataGridMode.Hierarchical ? _projectedSource?.ExpandAll(predicate) ?? 0 : 0;
        public int CollapseAll(Func<object, bool> predicate = null) => Mode == DataGridMode.Hierarchical ? _projectedSource?.CollapseAll(predicate) ?? 0 : 0;

        public void ActivateColumnHeader(int columnIndex, bool additive = false)
        {
            var columns = GetDisplayColumns();
            if (columnIndex < 0 || columnIndex >= columns.Count) throw new ArgumentOutOfRangeException(nameof(columnIndex));
            var column = columns[columnIndex];
            if (!CanUserSortColumns || !column.CanUserSort) return;
            if (column.SortBinding == null)
                throw new InvalidOperationException("Sortable DataGrid columns require a typed SortBinding.");
            var existing = SortDescriptions.FirstOrDefault(description => ReferenceEquals(description.Column, column));
            if (!additive) SortDescriptions.Clear();
            else if (existing != null) SortDescriptions.Remove(existing);
            if (existing == null)
                SortDescriptions.Add(new DataGridSortDescription(column, DataGridSortDirection.Ascending));
            else if (existing.Direction == DataGridSortDirection.Ascending)
                SortDescriptions.Add(new DataGridSortDescription(column, DataGridSortDirection.Descending));
        }

        public void RefreshFilter()
        {
            if (_projectedSource == null) return;
            var selectedPaths = SelectedRowPaths;
            var currentPath = CurrentRowPath;
            _projectedSource.Filter = Filter;
            _projectedSource.FilterMode = FilterMode;
            _projectedSource.RefreshFilter();
            RestoreRowSelection(selectedPaths, currentPath);
        }

        public void SelectCell(CellIndex cell, bool additive = false)
        {
            ValidateCell(cell);
            var old = _selectedCells.ToArray();
            if (!additive || SelectionMode == ItemListSelectionMode.Single) _selectedCells.Clear();
            if (!_selectedCells.Contains(cell)) _selectedCells.Add(cell);
            _currentCell = cell;
            _cellRangeAnchor = cell;
            RaiseCellSelectionChanged(old);
        }

        public void ToggleCell(CellIndex cell)
        {
            ValidateCell(cell);
            var old = _selectedCells.ToArray();
            if (!_selectedCells.Remove(cell))
            {
                if (SelectionMode == ItemListSelectionMode.Single) _selectedCells.Clear();
                _selectedCells.Add(cell);
            }
            _currentCell = cell;
            _cellRangeAnchor = cell;
            RaiseCellSelectionChanged(old);
        }

        public void SelectCellRange(CellIndex anchor, CellIndex target)
        {
            ValidateCell(anchor);
            ValidateCell(target);
            if (SelectionMode == ItemListSelectionMode.Single)
            {
                SelectCell(target);
                return;
            }
            var old = _selectedCells.ToArray();
            _selectedCells.Clear();
            var firstRow = Math.Min(IndexOfPath(anchor.RowPath), IndexOfPath(target.RowPath));
            var lastRow = Math.Max(IndexOfPath(anchor.RowPath), IndexOfPath(target.RowPath));
            var firstColumn = Math.Min(anchor.ColumnIndex, target.ColumnIndex);
            var lastColumn = Math.Max(anchor.ColumnIndex, target.ColumnIndex);
            for (var row = firstRow; row <= lastRow; row++)
                for (var column = firstColumn; column <= lastColumn; column++)
                    _selectedCells.Add(new CellIndex(GetRowPath(row), column));
            _currentCell = target;
            _cellRangeAnchor = anchor;
            RaiseCellSelectionChanged(old);
        }

        public void ClearCellSelection()
        {
            if (_selectedCells.Count == 0 && !_currentCell.HasValue) return;
            var old = _selectedCells.ToArray();
            _selectedCells.Clear();
            _currentCell = null;
            _cellRangeAnchor = null;
            RaiseCellSelectionChanged(old);
        }

        public void SelectRowHeader(IndexPath rowPath, bool additive = false, bool extendRange = false)
        {
            var row = IndexOfPath(rowPath);
            if (row < 0) throw new ArgumentOutOfRangeException(nameof(rowPath));
            if (SelectionUnit == DataGridSelectionUnit.Row)
            {
                if (extendRange && SelectionMode == ItemListSelectionMode.Multi && CurrentIndex >= 0) SelectRange(CurrentIndex, row);
                else Select(row, additive && SelectionMode == ItemListSelectionMode.Multi);
                return;
            }
            var columns = GetDisplayColumns();
            if (columns.Count == 0) return;
            var first = new CellIndex(rowPath, 0);
            var last = new CellIndex(rowPath, columns.Count - 1);
            if (extendRange && _cellRangeAnchor.HasValue) SelectCellRange(_cellRangeAnchor.Value, last);
            else
            {
                var old = _selectedCells.ToArray();
                if (!additive || SelectionMode == ItemListSelectionMode.Single) _selectedCells.Clear();
                for (var column = 0; column < columns.Count; column++)
                    if (CanSelectCell(row, column)) _selectedCells.Add(new CellIndex(rowPath, column));
                _currentCell = first;
                _cellRangeAnchor = first;
                RaiseCellSelectionChanged(old);
            }
        }

        public DataGridColumnHeader GetColumnHeader(int columnIndex)
        {
            if (_columnHeaders == null) throw new InvalidOperationException("The DataGrid template has not been applied.");
            if (columnIndex < 0 || columnIndex >= _columnHeaders.Children.Count) throw new ArgumentOutOfRangeException(nameof(columnIndex));
            return (DataGridColumnHeader)_columnHeaders.Children[columnIndex];
        }

        public void ResizeColumn(int columnIndex, float width)
        {
            if (!float.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            var columns = GetDisplayColumns();
            if (columnIndex < 0 || columnIndex >= columns.Count) throw new ArgumentOutOfRangeException(nameof(columnIndex));
            var column = columns[columnIndex];
            if (!CanUserResizeColumns || !column.CanUserResize) return;
            column.Width = GridTrackSize.Pixels(Math.Clamp(width, column.MinimumWidth, column.MaximumWidth));
        }

        protected override void PrepareContainerForItem(Control container, object item, int index)
        {
            base.PrepareContainerForItem(container, item, index);
            ((DataGridRow)container).Prepare(this, item, index);
        }

        protected override void UpdateContainerPosition(Control container, object item, int index, int alternationIndex)
        {
            base.UpdateContainerPosition(container, item, index, alternationIndex);
            ((DataGridRow)container).UpdatePosition(index);
        }

        protected override void ClearContainerForItem(Control container, object item)
        {
            if (container is DataGridRow row) row.ClearCells();
            base.ClearContainerForItem(container, item);
        }

        protected override void OnTemplateApplied()
        {
            base.OnTemplateApplied();
            _columnHeaders = GetTemplateChild(ColumnHeadersPartName) as GridPanel
                ?? throw new InvalidOperationException($"DataGrid templates must provide a {nameof(GridPanel)} named '{ColumnHeadersPartName}'.");
            _horizontalScrollBarPresenter = GetTemplateChild(HorizontalScrollBarPresenterPartName) as ContentPresenter;
            _verticalScrollBarPresenter = GetTemplateChild(VerticalScrollBarPresenterPartName) as ContentPresenter;
            RebuildHeaders();
        }

        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            UpdateScrollBars();
        }

        protected override void OnScrollMetricsChanged(ScrollMetrics metrics)
        {
            UpdateScrollBars();
            QueueLayout();
        }

        internal override void Draw(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor);
            base.Draw(context);
            context.Border(Bounds, Context?.FocusedControl == this ? context.Theme.FocusColor : context.Theme.PanelBorderColor);
            if (IsSorting) DrawSortingOverlay(context);
        }

        protected override void OnItemsChanged()
        {
            base.OnItemsChanged();
            OnPropertyChanged(nameof(SelectedRowPaths));
            OnPropertyChanged(nameof(CurrentRowPath));
            UpdateHierarchyStates();
            if (SelectionUnit != DataGridSelectionUnit.Cell) return;
            var old = _selectedCells.ToArray();
            _selectedCells.RemoveAll(cell => IndexOfPath(cell.RowPath) < 0 || cell.ColumnIndex >= GetDisplayColumns().Count);
            if (_currentCell.HasValue && (IndexOfPath(_currentCell.Value.RowPath) < 0 || _currentCell.Value.ColumnIndex >= GetDisplayColumns().Count))
                _currentCell = null;
            if (!old.SequenceEqual(_selectedCells)) RaiseCellSelectionChanged(old);
            else UpdateCellStates();
        }

        internal override void KeyPressed(Keys key)
        {
            if (SelectionUnit != DataGridSelectionUnit.Cell)
            {
                base.KeyPressed(key);
                return;
            }
            if (ItemCount == 0 || GetDisplayColumns().Count == 0) return;
            var current = _currentCell ?? new CellIndex(GetRowPath(0), 0);
            var row = IndexOfPath(current.RowPath);
            var column = current.ColumnIndex;
            var rowStep = 0;
            var columnStep = 0;
            if (key == Keys.Left) column += IsLayoutRtl() ? 1 : -1;
            else if (key == Keys.Right) column += IsLayoutRtl() ? -1 : 1;
            else if (key == Keys.Up) row--;
            else if (key == Keys.Down) row++;
            else if (key == Keys.Home) column = 0;
            else if (key == Keys.End) column = GetDisplayColumns().Count - 1;
            else if (key == Keys.PageUp) row -= GetPageStep();
            else if (key == Keys.PageDown) row += GetPageStep();
            else if (key == Keys.Space)
            {
                if (!CanSelectCell(row, column)) return;
                if (SelectionMode == ItemListSelectionMode.Single) SelectCell(current);
                else ToggleCell(current);
                return;
            }
            else return;
            rowStep = Math.Sign(row - IndexOfPath(current.RowPath));
            columnStep = Math.Sign(column - current.ColumnIndex);
            row = Math.Clamp(row, 0, ItemCount - 1);
            column = Math.Clamp(column, 0, GetDisplayColumns().Count - 1);
            while (!CanSelectCell(row, column))
            {
                if (rowStep == 0 && columnStep == 0) return;
                row += rowStep;
                column += columnStep;
                if (row < 0 || row >= ItemCount || column < 0 || column >= GetDisplayColumns().Count) return;
            }
            var target = new CellIndex(GetRowPath(row), column);
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (shift && SelectionMode == ItemListSelectionMode.Multi)
                SelectCellRange(_cellRangeAnchor ?? current, target);
            else
            {
                _cellRangeAnchor = null;
                if (SelectionMode == ItemListSelectionMode.Single) SelectCell(target);
                else CurrentCell = target;
            }
            BringIndexIntoView(row);
        }

        internal override bool PointerWheel(int delta)
        {
            if (delta == 0) return false;
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var horizontal = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            var before = ScrollOffset;
            if (horizontal && HorizontalScrollMode != ScrollBarVisibility.Disabled)
                ScrollOffset += new Vector2(-Math.Sign(delta) * Math.Max(1, HorizontalScrollBar.Page / 8), 0);
            else if (VerticalScrollMode != ScrollBarVisibility.Disabled)
                ScrollOffset += new Vector2(0, -Math.Sign(delta) * Math.Max(1, VerticalScrollBar.Page / 8));
            return before != ScrollOffset;
        }

        internal override void Process(GameTime gameTime)
        {
            CompletePendingSort();
            var keyboard = Context?.CurrentKeyboardState ?? default;
            if (!keyboard.IsKeyDown(Keys.LeftShift) && !keyboard.IsKeyDown(Keys.RightShift)) _cellRangeAnchor = null;
            base.Process(gameTime);
        }

        protected override void SelectItemFromPointer(int index, Point point)
        {
            if (SelectionUnit == DataGridSelectionUnit.Row)
            {
                base.SelectItemFromPointer(index, point);
                return;
            }
            if (!TryGetRealizedContainer(index, out var container) || container is not DataGridRow row) return;
            foreach (var cell in row.Cells)
            {
                if (!cell.VisualBounds.Contains(point)) continue;
                SelectCellFromPointer(new CellIndex(row.IndexPath, cell.ColumnIndex));
                return;
            }
        }

        public override void Dispose()
        {
            CancelPendingSort();
            Columns.CollectionChanged -= ColumnsChanged;
            SortDescriptions.CollectionChanged -= SortDescriptionsChanged;
            foreach (var column in _observedColumns)
            {
                column.Changed -= ColumnChanged;
                column.VisibilityValidator = null;
            }
            _observedColumns.Clear();
            _projectedSource?.Dispose();
            _projectedSource = null;
            base.Dispose();
        }

        internal void ConfigureColumns(GridPanel panel, IReadOnlyList<DataGridColumn> columns)
        {
            panel.ColumnDefinitions.Clear();
            foreach (var column in columns) panel.ColumnDefinitions.Add(new ColumnDefinition { Width = column.Width });
        }

        private void ColumnsChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            foreach (var column in _observedColumns.Where(column => !Columns.Contains(column)).ToArray())
            {
                column.Changed -= ColumnChanged;
                column.VisibilityValidator = null;
                _observedColumns.Remove(column);
            }
            foreach (var column in Columns)
            {
                if (column == null) throw new InvalidOperationException("DataGrid columns cannot contain null values.");
                if (_observedColumns.Add(column))
                {
                    column.VisibilityValidator = _ => Columns.Count(candidate => candidate.IsVisible) < MaximumSupportedVisibleColumns;
                    column.Changed += ColumnChanged;
                }
            }
            RefreshColumns();
        }

        private void ColumnChanged(object sender, EventArgs args) => RefreshColumns();

        private void RefreshColumns()
        {
            if (Mode == DataGridMode.Hierarchical) RebuildItemsProjection();
            RebuildHeaders();
            for (var index = 0; index < ItemCount; index++)
                if (TryGetRealizedContainer(index, out var container) && container is DataGridRow row)
                    row.Prepare(this, GetItemAt(index), index);
            QueueLayout();
        }

        private void RebuildItemsProjection()
        {
            CancelPendingSort();
            _projectedSource?.Dispose();
            _projectedSource = null;
            if (_rootItemsSource == null)
            {
                base.ItemsSource = null;
                return;
            }
            var expanders = Columns.OfType<DataGridExpanderColumn>().ToArray();
            if (expanders.Length > 1) throw new InvalidOperationException("Hierarchical DataGrid supports exactly one expander column.");
            var expander = expanders.SingleOrDefault();
            if (Mode == DataGridMode.Hierarchical && expander?.Children == null)
            {
                base.ItemsSource = Array.Empty<object>();
                return;
            }
            _projectedSource = new DataGridSource<object>(
                _rootItemsSource.Cast<object>(),
                Mode == DataGridMode.Hierarchical
                    ? item => expander.Children.Read(item)?.Cast<object>() ?? Array.Empty<object>()
                    : _ => Array.Empty<object>(),
                Mode == DataGridMode.Hierarchical && expander.HasChildren != null ? item => expander.HasChildren.Read(item) : null,
                Mode == DataGridMode.Hierarchical && expander.IsExpanded != null ? item => expander.IsExpanded.Read(item) : null,
                Mode == DataGridMode.Hierarchical && expander.IsExpanded?.CanWrite == true
                    ? (item, expanded) => expander.IsExpanded.Write(item, expanded)
                    : null);
            ConfigureProjection();
            base.ItemsSource = _projectedSource;
        }

        private void SortDescriptionsChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            UpdateHeaderSortStates();
            if (_projectedSource != null && Context != null && _projectedSource.Count >= Math.Max(0, AsynchronousSortThreshold))
            {
                BeginPendingSort();
                return;
            }
            var selectedPaths = SelectedRowPaths;
            var currentPath = CurrentRowPath;
            ConfigureProjection();
            RestoreRowSelection(selectedPaths, currentPath);
        }

        private void ConfigureProjection()
        {
            if (_projectedSource == null) return;
            _projectedSource.ReplaceSortDescriptions(CreateSourceSortDescriptions());
            _projectedSource.Filter = Filter;
            _projectedSource.FilterMode = FilterMode;
        }

        private IReadOnlyList<DataGridSortDescription<object>> CreateSourceSortDescriptions()
        {
            var descriptions = new List<DataGridSortDescription<object>>(SortDescriptions.Count);
            foreach (var description in SortDescriptions)
            {
                if (!Columns.Contains(description.Column))
                    throw new InvalidOperationException("DataGrid sort descriptions must reference a column owned by the grid.");
                if (description.Column.SortBinding == null)
                    throw new InvalidOperationException("Sortable DataGrid columns require a typed SortBinding.");
                descriptions.Add(new DataGridSortDescription<object>(
                    description.Column.SortBinding.Compare,
                    description.Direction));
            }
            return descriptions;
        }

        private void BeginPendingSort()
        {
            CancelPendingSort(clearBusy: false);
            _pendingSelectedPaths = SelectedRowPaths;
            _pendingCurrentPath = CurrentRowPath;
            _sortCancellation = new CancellationTokenSource();
            _pendingSort = _projectedSource.PrepareSortAsync(CreateSourceSortDescriptions(), _sortCancellation.Token);
            IsSorting = true;
        }

        private void CompletePendingSort()
        {
            var pending = _pendingSort;
            if (pending == null || !pending.IsCompleted) return;
            _pendingSort = null;
            _sortCancellation?.Dispose();
            _sortCancellation = null;
            try
            {
                if (pending.IsCanceled) return;
                var prepared = pending.GetAwaiter().GetResult();
                if (!prepared.TryApply())
                {
                    if (_projectedSource != null) BeginPendingSort();
                    return;
                }
                RestoreRowSelection(_pendingSelectedPaths, _pendingCurrentPath);
            }
            finally
            {
                if (_pendingSort == null) IsSorting = false;
            }
        }

        private void CancelPendingSort(bool clearBusy = true)
        {
            _sortCancellation?.Cancel();
            _sortCancellation?.Dispose();
            _sortCancellation = null;
            _pendingSort = null;
            if (clearBusy) IsSorting = false;
        }

        private void DrawSortingOverlay(UIRenderContext context)
        {
            var shade = context.Theme.BackgroundColor;
            shade.A = 190;
            context.Fill(Bounds, shade);
            var width = Math.Min(190, Math.Max(80, Bounds.Width - 24));
            var panel = new Rectangle(Bounds.Center.X - width / 2, Bounds.Center.Y - 24, width, 48);
            context.Fill(panel, context.Theme.PanelColor);
            context.Border(panel, context.Theme.PanelBorderColor);
            var font = ResolveFont(_fontSelection);
            if (font != null)
                context.Text(font, "Sorting rows...", new Vector2(panel.X + 12, panel.Y + 8), context.Theme.TextColor);
            var track = new Rectangle(panel.X + 12, panel.Bottom - 12, Math.Max(1, panel.Width - 24), 3);
            context.Fill(track, context.Theme.PanelBorderColor);
            var segmentWidth = Math.Max(12, track.Width / 3);
            var phase = (float)((Context?.CurrentTime.TotalSeconds ?? 0) % 1);
            var segmentX = track.X + (int)MathF.Round((track.Width - segmentWidth) * phase);
            context.Fill(new Rectangle(segmentX, track.Y, segmentWidth, track.Height), context.Theme.FocusColor);
        }

        private void UpdateScrollBars()
        {
            if (_horizontalScrollBarPresenter == null || _verticalScrollBarPresenter == null) return;
            var showHorizontal = ShouldShowScrollBar(HorizontalScrollMode, Extent.X, Viewport.X);
            var showVertical = ShouldShowScrollBar(VerticalScrollMode, Extent.Y, Viewport.Y);
            var visibilityChanged = _horizontalScrollBar.Visible != showHorizontal || _verticalScrollBar.Visible != showVertical;
            _horizontalScrollBar.Visible = showHorizontal;
            _verticalScrollBar.Visible = showVertical;
            _horizontalScrollBarPresenter.Visible = showHorizontal;
            _verticalScrollBarPresenter.Visible = showVertical;
            _horizontalScrollBar.MinValue = 0;
            _horizontalScrollBar.MaxValue = Math.Max(0, Extent.X);
            _horizontalScrollBar.Page = Viewport.X;
            _horizontalScrollBar.SetValueNoSignal(ScrollOffset.X);
            _verticalScrollBar.MinValue = 0;
            _verticalScrollBar.MaxValue = Math.Max(0, Extent.Y);
            _verticalScrollBar.Page = Viewport.Y;
            _verticalScrollBar.SetValueNoSignal(ScrollOffset.Y);
            if (visibilityChanged) TemplateRoot?.QueueLayout();
        }

        private static bool ShouldShowScrollBar(ScrollBarVisibility mode, float extent, float viewport)
        {
            if (mode == ScrollBarVisibility.Disabled || mode == ScrollBarVisibility.Never) return false;
            if (mode == ScrollBarVisibility.Always || mode == ScrollBarVisibility.Reserve) return true;
            return extent > viewport + .5f;
        }

        private void RestoreRowSelection(IReadOnlyList<IndexPath> selectedPaths, IndexPath? currentPath)
        {
            if (SelectionUnit != DataGridSelectionUnit.Row) return;
            var selectedIndices = selectedPaths
                .Select(IndexOfPath)
                .Where(index => index >= 0)
                .ToArray();
            for (var index = 0; index < selectedIndices.Length; index++)
                Select(selectedIndices[index], index != 0);
            if (selectedIndices.Length == 0) ClearSelection();
            if (currentPath.HasValue)
            {
                var currentIndex = IndexOfPath(currentPath.Value);
                if (currentIndex >= 0) CurrentIndex = currentIndex;
            }
        }

        internal bool IsCellSelected(IndexPath path, int columnIndex) =>
            _selectedCells.Contains(new CellIndex(path, columnIndex));

        internal void SelectCellFromPointer(CellIndex cell)
        {
            if (SelectionUnit != DataGridSelectionUnit.Cell) return;
            var row = IndexOfPath(cell.RowPath);
            if (!CanSelectCell(row, cell.ColumnIndex)) return;
            var keyboard = Context?.CurrentKeyboardState ?? default;
            var command = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl) ||
                keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows);
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (shift && SelectionMode == ItemListSelectionMode.Multi && _currentCell.HasValue)
                SelectCellRange(_cellRangeAnchor ?? _currentCell.Value, cell);
            else if (SelectionMode == ItemListSelectionMode.Toggle ||
                (SelectionMode == ItemListSelectionMode.Multi && command)) ToggleCell(cell);
            else SelectCell(cell);
        }

        private int IndexOfPath(IndexPath path) => _projectedSource?.IndexOfPath(path) ??
            (path.Count == 1 && path[0] >= 0 && path[0] < ItemCount ? path[0] : -1);

        private void ValidateCell(CellIndex cell)
        {
            if (IndexOfPath(cell.RowPath) < 0) throw new ArgumentOutOfRangeException(nameof(cell));
            if (cell.ColumnIndex < 0 || cell.ColumnIndex >= GetDisplayColumns().Count) throw new ArgumentOutOfRangeException(nameof(cell));
        }

        private bool CanSelectCell(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= ItemCount || columnIndex < 0 || columnIndex >= GetDisplayColumns().Count) return false;
            if (!TryGetRealizedContainer(rowIndex, out var container) || container is not DataGridRow row) return true;
            var cell = row.Cells[columnIndex];
            return row.IsEffectivelyEnabled && row.IsSelectable && cell.IsEffectivelyEnabled && cell.IsSelectable;
        }

        private void RaiseCellSelectionChanged(IReadOnlyList<CellIndex> old)
        {
            UpdateCellStates();
            OnPropertyChanged(nameof(SelectedCells));
            OnPropertyChanged(nameof(CurrentCell));
            if (!old.SequenceEqual(_selectedCells))
                CellSelectionChanged?.Invoke(this, new DataGridCellSelectionChangedEventArgs(old, _selectedCells.ToArray()));
        }

        private void UpdateCellStates()
        {
            for (var index = 0; index < ItemCount; index++)
            {
                if (!TryGetRealizedContainer(index, out var container) || container is not DataGridRow row) continue;
                foreach (var cell in row.Cells)
                {
                    var cellIndex = new CellIndex(row.IndexPath, cell.ColumnIndex);
                    cell.SetSelectionState(_selectedCells.Contains(cellIndex), _currentCell == cellIndex);
                }
            }
        }

        private void UpdateHierarchyStates()
        {
            for (var index = 0; index < ItemCount; index++)
            {
                if (!TryGetRealizedContainer(index, out var container) || container is not DataGridRow row) continue;
                var source = HierarchySource;
                row.SetExpansionState(source?.HasChildren(row.IndexPath) == true, source?.IsExpanded(row.IndexPath) == true);
            }
        }

        private void UpdateHeaderSortStates()
        {
            if (_columnHeaders == null) return;
            foreach (var child in _columnHeaders.Children)
            {
                var header = (DataGridColumnHeader)child;
                header.SetSortDirection(SortDescriptions
                    .FirstOrDefault(description => ReferenceEquals(description.Column, header.Column))?.Direction);
            }
        }

        private void RebuildHeaders()
        {
            if (_columnHeaders == null) return;
            foreach (var child in _columnHeaders.Children.ToArray()) _columnHeaders.RemoveChild(child);
            var columns = GetDisplayColumns();
            ConfigureColumns(_columnHeaders, columns);
            for (var index = 0; index < columns.Count; index++)
            {
                var column = columns[index];
                var header = new DataGridColumnHeader
                {
                    Owner = this,
                    Column = column,
                    ColumnIndex = index,
                    Content = column.Header,
                    ContentTemplate = column.HeaderTemplate,
                    CustomMinimumSize = new Vector2(column.MinimumWidth, DefaultColumnHeaderHeight),
                    CustomMaximumSize = new Vector2(float.IsPositiveInfinity(column.MaximumWidth) ? -1 : column.MaximumWidth, -1),
                };
                header.Classes.Add("data-grid-column-header");
                header.SetSortDirection(SortDescriptions
                    .FirstOrDefault(description => ReferenceEquals(description.Column, column))?.Direction);
                GridPanel.SetColumn(header, index);
                _columnHeaders.AddChild(header);
            }
        }
    }
}