// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// Control geometry, layout-direction, sizing, and input semantics are adapted from
// Godot Engine's scene/gui/control.cpp, control.h, and container.cpp;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Forma.Xaml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Godot-compatible layout direction, including application and system locale resolution through <see cref="UIContext"/>.</summary>
    public enum LayoutDirection { Inherited, ApplicationLocale, LeftToRight, RightToLeft, SystemLocale }

    /// <summary>Godot-compatible text shaping direction. Actual Unicode shaping is provided by the active text renderer.</summary>
    public enum TextDirection { Auto, LeftToRight, RightToLeft, Inherited }

    /// <summary>Matches Godot's Control.GrowDirection: how a control's position compensates when an anchor-resolved size is clamped up to its minimum size.</summary>
    public enum GrowDirection { Begin, End, Both }

    /// <summary>
    /// Retained-mode UI element modelled after Godot's Control. Coordinates are relative to the parent;
    /// anchors and offsets are resolved during layout.
    /// </summary>
    public class Control : IAddChild<Control>, INotifyPropertyChanged
    {
        private readonly List<Control> _children = new List<Control>();
        private readonly List<Control> _visualChildren = new List<Control>();
        private readonly List<Control> _inheritanceChildren = new List<Control>();
        private readonly ReadOnlyCollection<Control> _readOnlyChildren;
        private Vector2 _position;
        private Vector2 _size;
        private Vector2 _customMinimumSize;
        private Vector2 _customMaximumSize = new Vector2(-1, -1);
        private float _width = float.NaN;
        private float _height = float.NaN;
        private float _minWidth;
        private float _minHeight;
        private float _maxWidth = float.PositiveInfinity;
        private float _maxHeight = float.PositiveInfinity;
        private float _aspectRatio = float.NaN;
        private float _opacity = 1;
        private Color? _foreground;
        private UIFontFamily _fontFamily;
        private float? _fontSize;
        private UIFontWeight? _fontWeight;
        private UIFontStyle? _fontStyle;
        private UIFontStretch? _fontStretch;
        private string _language;
        private LayoutDirection _layoutDirection;
        private bool _layoutDirty = true;
        private int _zIndex;
        private long _treeOrder;
        private long _nextChildOrder;
        private bool _childOrderDirty = true;
        private readonly List<Control> _childrenInDrawOrder = new List<Control>();
        private readonly ObservableStyleDictionary _styleOverrides;
        private readonly Dictionary<string, ThemeIcon?> _iconOverrides = new Dictionary<string, ThemeIcon?>(StringComparer.Ordinal);
        private Theme _themeOverride;
        private object _dataContext;
        private bool _hasLocalDataContext;
        private bool _enabled = true;
        private bool _isHovered;
        private bool _isFocused;
        private readonly HashSet<string> _pseudoStates = new HashSet<string>(StringComparer.Ordinal);
        private string _name;
        private Visibility _visibility = Visibility.Visible;
        private string _tooltipText = string.Empty;
        private MouseFilter _mouseFilter;
        private FocusMode _focusMode;
        private Control _focusNext;
        private Control _focusPrevious;
        private Control _focusNeighborLeft;
        private Control _focusNeighborTop;
        private Control _focusNeighborRight;
        private Control _focusNeighborBottom;
        private bool _clipContents;
        private bool _clipToBounds;
        private Geometry _clip;
        private Brush _opacityMask;
        private VisualEffect _effect;
        private Transform _renderTransform;
        private Vector2 _transformOrigin = new Vector2(.5f);
        private bool _isHitTestVisible = true;
        private PixelSnapping _pixelSnapping = PixelSnapping.Inherited;
        private Cursor _cursor = Cursor.Inherited;
        private object _toolTip;
        private SizeFlags _horizontalSizeFlags;
        private SizeFlags _verticalSizeFlags;
        private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Fill;
        private VerticalAlignment _verticalAlignment = VerticalAlignment.Fill;
        private float _sizeFlagsStretchRatio = 1f;
        private Thickness _margins;
        private GrowDirection _hGrowDirection = GrowDirection.End;
        private GrowDirection _vGrowDirection = GrowDirection.End;
        private AccessibilityPeer _accessibilityPeer;
        private static int _nextAccessibilityId;
        private string _automationId = string.Empty;
        private string _accessibilityLabel;

        public Control()
        {
            _readOnlyChildren = _children.AsReadOnly();
            _styleOverrides = new ObservableStyleDictionary(ThemeStyleOverridesChanged);
            MouseFilter = MouseFilter.Stop;
            FocusMode = FocusMode.None;
            Visible = true;
            HorizontalSizeFlags = SizeFlags.Fill;
            VerticalSizeFlags = SizeFlags.Fill;
            Classes.Changed += ClassesChanged;
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                NameChanged?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(Name));
            }
        }
            public Control Parent { get; private set; }
        public Control VisualParent { get; private set; }
        internal Control InheritanceParent { get; private set; }
        internal bool IsXamlInfrastructure { get; set; }
        public ReadOnlyCollection<Control> Children => _readOnlyChildren;
        public UIContext Context { get; private set; }
        public ResourceDictionary Resources { get; } = new ResourceDictionary();
        public ControlClassList Classes { get; } = new ControlClassList();
        public IDictionary<string, StyleBox> ThemeStyleOverrides => _styleOverrides;
        public bool HasLocalDataContext => _hasLocalDataContext;
        public object DataContext
        {
            get => _hasLocalDataContext ? _dataContext : InheritanceParent?.DataContext;
            set
            {
                var previous = DataContext;
                var hadLocalValue = _hasLocalDataContext;
                _dataContext = value;
                _hasLocalDataContext = true;
                if (!hadLocalValue) OnPropertyChanged(nameof(HasLocalDataContext));
                NotifyDataContextChanged(previous, DataContext);
            }
        }
        /// <summary>Optional theme applied to this control and inherited by its descendants while drawing.</summary>
        public Theme ThemeOverride
        {
            get => _themeOverride;
            set
            {
                if (ReferenceEquals(_themeOverride, value)) return;
                if (_themeOverride != null) _themeOverride.Changed -= ThemeOverrideChanged;
                _themeOverride = value;
                if (_themeOverride != null) _themeOverride.Changed += ThemeOverrideChanged;
                MarkThemeDirty();
                QueueLayout();
                OnPropertyChanged(nameof(ThemeOverride));
            }
        }
        /// <summary>Text presented by <see cref="UIContext"/> after the pointer rests over this control.</summary>
        public string TooltipText { get => _tooltipText; set => SetValue(ref _tooltipText, value ?? string.Empty, nameof(TooltipText)); }
        private bool _visible;
        /// <summary>Matches Godot's Control.visible: toggling it requeues the parent's layout, since Container wires each child's visibility_changed signal to queue_sort().</summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                var visibility = value ? Visibility.Visible : Visibility.Collapsed;
                if (_visible == value && _visibility == visibility) return;
                var visibleChanged = _visible != value;
                var visibilityChanged = _visibility != visibility;
                _visible = value;
                _visibility = visibility;
                if (!value) Context?.ResetInteractionState(this);
                QueueLayout();
                if (visibleChanged) OnPropertyChanged(nameof(Visible));
                if (visibilityChanged) OnPropertyChanged(nameof(Visibility));
            }
        }
        public Visibility Visibility
        {
            get => _visibility;
            set
            {
                if (!Enum.IsDefined(typeof(Visibility), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_visibility == value) return;
                var visible = value != Visibility.Collapsed;
                var visibleChanged = _visible != visible;
                _visibility = value;
                _visible = visible;
                if (value != Visibility.Visible) Context?.ResetInteractionState(this);
                QueueLayout();
                OnPropertyChanged(nameof(Visibility));
                if (visibleChanged) OnPropertyChanged(nameof(Visible));
            }
        }
        internal bool IsRendered => _visibility == Visibility.Visible;
        internal bool ParticipatesInLayout => _visibility != Visibility.Collapsed;
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                var previous = CaptureInheritedStates();
                _enabled = value;
                if (!value) Context?.ResetInteractionState(this);
                EnabledChanged?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(Enabled));
                NotifyInheritedStateChanges(previous);
            }
        }
        public bool IsEffectivelyEnabled => Enabled && (InheritanceParent?.IsEffectivelyEnabled ?? true);
        public virtual bool IsPseudoStateActive(string state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state switch
            {
                "hover" => _isHovered,
                "focus" => _isFocused,
                "focus-within" => HasFocusedVisualDescendant(),
                "disabled" => !IsEffectivelyEnabled,
                _ => _pseudoStates.Contains(state),
            };
        }
        public MouseFilter MouseFilter { get => _mouseFilter; set => SetValue(ref _mouseFilter, value, nameof(MouseFilter)); }
        public FocusMode FocusMode { get => _focusMode; set => SetValue(ref _focusMode, value, nameof(FocusMode)); }
        /// <summary>Optional explicit focus order used before tree traversal.</summary>
        public Control FocusNext { get => _focusNext; set => SetValue(ref _focusNext, value, nameof(FocusNext)); }
        /// <summary>Optional reverse focus order used before tree traversal.</summary>
        public Control FocusPrevious { get => _focusPrevious; set => SetValue(ref _focusPrevious, value, nameof(FocusPrevious)); }
        /// <summary>Optional directional focus neighbor.</summary>
        public Control FocusNeighborLeft { get => _focusNeighborLeft; set => SetValue(ref _focusNeighborLeft, value, nameof(FocusNeighborLeft)); }
        /// <summary>Optional directional focus neighbor.</summary>
        public Control FocusNeighborTop { get => _focusNeighborTop; set => SetValue(ref _focusNeighborTop, value, nameof(FocusNeighborTop)); }
        /// <summary>Optional directional focus neighbor.</summary>
        public Control FocusNeighborRight { get => _focusNeighborRight; set => SetValue(ref _focusNeighborRight, value, nameof(FocusNeighborRight)); }
        /// <summary>Optional directional focus neighbor.</summary>
        public Control FocusNeighborBottom { get => _focusNeighborBottom; set => SetValue(ref _focusNeighborBottom, value, nameof(FocusNeighborBottom)); }
        public bool ClipContents { get => _clipContents; set => SetValue(ref _clipContents, value, nameof(ClipContents)); }
        public bool ClipToBounds { get => _clipToBounds; set => SetValue(ref _clipToBounds, value, nameof(ClipToBounds)); }
        public Geometry Clip { get => _clip; set => SetValue(ref _clip, value, nameof(Clip)); }
        public Brush OpacityMask { get => _opacityMask; set => SetValue(ref _opacityMask, value, nameof(OpacityMask)); }
        public VisualEffect Effect { get => _effect; set => SetValue(ref _effect, value, nameof(Effect)); }
        public Transform RenderTransform { get => _renderTransform; set => SetGeometryValue(ref _renderTransform, value, nameof(RenderTransform)); }
        public Vector2 TransformOrigin { get => _transformOrigin; set => SetGeometryValue(ref _transformOrigin, value, nameof(TransformOrigin)); }
        public float Opacity
        {
            get => _opacity;
            set
            {
                if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
                if (_opacity == value) return;
                _opacity = value;
                OnPropertyChanged(nameof(Opacity));
            }
        }
        public bool IsHitTestVisible { get => _isHitTestVisible; set => SetValue(ref _isHitTestVisible, value, nameof(IsHitTestVisible)); }
        public PixelSnapping PixelSnapping
        {
            get => _pixelSnapping;
            set
            {
                if (_pixelSnapping == value) return;
                var previous = CaptureInheritedValues(control => control.IsPixelSnappingEnabled);
                _pixelSnapping = value;
                OnPropertyChanged(nameof(PixelSnapping));
                NotifyInheritedValueChanges(previous, control => control.IsPixelSnappingEnabled, nameof(IsPixelSnappingEnabled));
            }
        }
        public bool IsPixelSnappingEnabled => PixelSnapping switch { PixelSnapping.Enabled => true, PixelSnapping.Disabled => false, _ => InheritanceParent?.IsPixelSnappingEnabled ?? true };
        public Cursor Cursor
        {
            get => _cursor;
            set
            {
                if (_cursor == value) return;
                var previous = CaptureInheritedValues(control => control.EffectiveCursor);
                _cursor = value;
                OnPropertyChanged(nameof(Cursor));
                NotifyInheritedValueChanges(previous, control => control.EffectiveCursor, nameof(EffectiveCursor));
            }
        }
        public Cursor EffectiveCursor => Cursor == Cursor.Inherited ? InheritanceParent?.EffectiveCursor ?? Cursor.Arrow : Cursor;
        public object ToolTip { get => _toolTip; set => SetValue(ref _toolTip, value, nameof(ToolTip)); }
        public Color? Foreground { get => _foreground ?? InheritanceParent?.Foreground; set { if (_foreground == value) return; var previous = CaptureInheritedValues(control => control.Foreground); _foreground = value; QueueLayout(); NotifyInheritedValueChanges(previous, control => control.Foreground, nameof(Foreground)); } }
        public UIFontFamily FontFamily { get => _fontFamily ?? InheritanceParent?.FontFamily; set { if (ReferenceEquals(_fontFamily, value)) return; var previous = CaptureInheritedValues(control => control.FontFamily); _fontFamily = value; QueueLayout(); NotifyInheritedValueChanges(previous, control => control.FontFamily, nameof(FontFamily)); } }
        public float FontSize
        {
            get => _fontSize ?? InheritanceParent?.FontSize ?? 0;
            set
            {
                if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (_fontSize == value) return;
                var previous = CaptureInheritedValues(control => control.FontSize);
                _fontSize = value;
                QueueLayout();
                NotifyInheritedValueChanges(previous, control => control.FontSize, nameof(FontSize));
            }
        }
        public UIFontWeight FontWeight { get => _fontWeight ?? InheritanceParent?.FontWeight ?? UIFontWeight.Normal; set { if (!Enum.IsDefined(typeof(UIFontWeight), value)) throw new ArgumentOutOfRangeException(nameof(value)); if (_fontWeight == value) return; var previous = CaptureInheritedValues(control => control.FontWeight); _fontWeight = value; QueueLayout(); NotifyInheritedValueChanges(previous, control => control.FontWeight, nameof(FontWeight)); } }
        public UIFontStyle FontStyle { get => _fontStyle ?? InheritanceParent?.FontStyle ?? UIFontStyle.Normal; set { if (!Enum.IsDefined(typeof(UIFontStyle), value)) throw new ArgumentOutOfRangeException(nameof(value)); if (_fontStyle == value) return; var previous = CaptureInheritedValues(control => control.FontStyle); _fontStyle = value; QueueLayout(); NotifyInheritedValueChanges(previous, control => control.FontStyle, nameof(FontStyle)); } }
        public UIFontStretch FontStretch { get => _fontStretch ?? InheritanceParent?.FontStretch ?? UIFontStretch.Normal; set { if (!Enum.IsDefined(typeof(UIFontStretch), value)) throw new ArgumentOutOfRangeException(nameof(value)); if (_fontStretch == value) return; var previous = CaptureInheritedValues(control => control.FontStretch); _fontStretch = value; QueueLayout(); NotifyInheritedValueChanges(previous, control => control.FontStretch, nameof(FontStretch)); } }
        public string Language { get => _language ?? InheritanceParent?.Language ?? string.Empty; set { value ??= string.Empty; if (_language == value) return; var previous = CaptureInheritedValues(control => control.Language); _language = value; QueueLayout(); NotifyInheritedValueChanges(previous, control => control.Language, nameof(Language)); } }
        /// <summary>Controls bidirectional layout inheritance for containers and alignment-aware controls.</summary>
        public LayoutDirection LayoutDirection
        {
            get => _layoutDirection;
            set
            {
                if (_layoutDirection == value) return;
                var previous = CaptureInheritedStates();
                _layoutDirection = value;
                QueueLayout();
                OnPropertyChanged(nameof(LayoutDirection));
                NotifyInheritedStateChanges(previous);
            }
        }
        /// <summary>Whether this control resolves to right-to-left layout.</summary>
        public bool IsLayoutRtl()
        {
            if (LayoutDirection == Forma.LayoutDirection.RightToLeft) return true;
            if (LayoutDirection == Forma.LayoutDirection.LeftToRight) return false;
            if (LayoutDirection == Forma.LayoutDirection.ApplicationLocale || LayoutDirection == Forma.LayoutDirection.SystemLocale)
                return Context?.ResolveLayoutDirection(LayoutDirection) ?? System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
            return InheritanceParent?.IsLayoutRtl() ?? (Context?.ResolveLayoutDirection(Forma.LayoutDirection.Inherited) ?? System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft);
        }
        public LayoutDirection EffectiveLayoutDirection => IsLayoutRtl() ? LayoutDirection.RightToLeft : LayoutDirection.LeftToRight;
        /// <summary>
        /// Drawing and pointer-picking order. Higher values are painted above and receive input before
        /// lower values; equal values retain tree insertion order.
        /// </summary>
        public int ZIndex
        {
            get => _zIndex;
            set
            {
                if (_zIndex == value) return;
                _zIndex = value;
                if (VisualParent != null) VisualParent._childOrderDirty = true;
                else Context?.MarkRootOrderDirty();
                OnPropertyChanged(nameof(ZIndex));
            }
        }
        public SizeFlags HorizontalSizeFlags { get => _horizontalSizeFlags; set => SetValue(ref _horizontalSizeFlags, value, nameof(HorizontalSizeFlags), true); }
        public SizeFlags VerticalSizeFlags { get => _verticalSizeFlags; set => SetValue(ref _verticalSizeFlags, value, nameof(VerticalSizeFlags), true); }
        public HorizontalAlignment HorizontalAlignment { get => _horizontalAlignment; set => SetValue(ref _horizontalAlignment, value, nameof(HorizontalAlignment), true); }
        public VerticalAlignment VerticalAlignment { get => _verticalAlignment; set => SetValue(ref _verticalAlignment, value, nameof(VerticalAlignment), true); }
        public float SizeFlagsStretchRatio { get => _sizeFlagsStretchRatio; set => SetValue(ref _sizeFlagsStretchRatio, value, nameof(SizeFlagsStretchRatio), true); }
        public Thickness Margins
        {
            get => _margins;
            set
            {
                if (!SetValue(ref _margins, value, nameof(Margins), true)) return;
                OnPropertyChanged(nameof(Margin));
            }
        }
        public Thickness Margin { get => Margins; set => Margins = value; }
        public float Width { get => _width; set => SetValue(ref _width, ValidateDimension(value, nameof(value), true), nameof(Width), true); }
        public float Height { get => _height; set => SetValue(ref _height, ValidateDimension(value, nameof(value), true), nameof(Height), true); }
        public float MinWidth { get => _minWidth; set => SetValue(ref _minWidth, ValidateDimension(value, nameof(value), false), nameof(MinWidth), true); }
        public float MinHeight { get => _minHeight; set => SetValue(ref _minHeight, ValidateDimension(value, nameof(value), false), nameof(MinHeight), true); }
        public float MaxWidth { get => _maxWidth; set => SetValue(ref _maxWidth, ValidateMaximum(value, nameof(value)), nameof(MaxWidth), true); }
        public float MaxHeight { get => _maxHeight; set => SetValue(ref _maxHeight, ValidateMaximum(value, nameof(value)), nameof(MaxHeight), true); }
        public float AspectRatio
        {
            get => _aspectRatio;
            set
            {
                if (!float.IsNaN(value) && (!float.IsFinite(value) || value <= 0)) throw new ArgumentOutOfRangeException(nameof(value));
                SetValue(ref _aspectRatio, value, nameof(AspectRatio), true);
            }
        }
        public Vector2 CustomMinimumSize
        {
            get => _customMinimumSize;
            set => SetValue(ref _customMinimumSize, Vector2.Max(Vector2.Zero, value), nameof(CustomMinimumSize), true);
        }
        public Vector2 CustomMaximumSize
        {
            get => _customMaximumSize;
            set
            {
                if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) return;
                SetValue(ref _customMaximumSize, new Vector2(value.X < 0 ? -1 : value.X, value.Y < 0 ? -1 : value.Y), nameof(CustomMaximumSize), true);
            }
        }
        public Vector2 Position { get => _position; set => SetGeometryValue(ref _position, value, nameof(Position), true); }
        public Vector2 Size { get => _size; set => SetGeometryValue(ref _size, Vector2.Max(Vector2.Zero, value), nameof(Size), true); }
        public float AnchorLeft { get; private set; }
        public float AnchorTop { get; private set; }
        public float AnchorRight { get; private set; }
        public float AnchorBottom { get; private set; }
        public float OffsetLeft { get; private set; }
        public float OffsetTop { get; private set; }
        public float OffsetRight { get; private set; }
        public float OffsetBottom { get; private set; }
        /// <summary>Matches Godot's Control.GrowHorizontal (default GROW_DIRECTION_END): how the horizontal position compensates when the anchor-resolved width is clamped up to the minimum size.</summary>
        public GrowDirection HGrowDirection { get => _hGrowDirection; set => SetValue(ref _hGrowDirection, value, nameof(HGrowDirection), true); }
        /// <summary>Matches Godot's Control.GrowVertical (default GROW_DIRECTION_END): how the vertical position compensates when the anchor-resolved height is clamped up to the minimum size.</summary>
        public GrowDirection VGrowDirection { get => _vGrowDirection; set => SetValue(ref _vGrowDirection, value, nameof(VGrowDirection), true); }
        public Rectangle Bounds => new Rectangle((int)MathF.Round(GlobalPosition.X), (int)MathF.Round(GlobalPosition.Y), (int)MathF.Round(Size.X), (int)MathF.Round(Size.Y));
        public Rectangle VisualBounds => TransformBounds(Bounds, GetWorldRenderTransformMatrix());
        public Rectangle FocusBounds => VisualBounds;
        public Rectangle AccessibilityBounds => VisualBounds;
        public AccessibilityPeer AccessibilityPeer => _accessibilityPeer ??= CreateAccessibilityPeer();
        /// <summary>
        /// Identity for this control instance, unique within the process and stable for its whole
        /// lifetime. Assigned at construction and never reassigned, so it survives a template reload:
        /// reloading rebuilds a template's visual children, not the templated control itself.
        /// <para>
        /// This is what lets an accessibility tree be diffed rather than re-walked, and what lets an
        /// out-of-process client refer to a node it saw earlier. It is deliberately not an
        /// author-facing identifier - use <see cref="AutomationId"/> for that, since this value is
        /// allocation-ordered and so differs between runs.
        /// </para>
        /// </summary>
        public int AccessibilityId { get; } = AllocateAccessibilityId();
        /// <summary>
        /// Next value from the identity sequence. Peers that are not backed by a control of their
        /// own - a virtualized item's peer, say - draw from the same sequence so every node in an
        /// accessibility tree has a distinct id, which is what lets a snapshot be diffed by id.
        /// </summary>
        internal static int AllocateAccessibilityId() => System.Threading.Interlocked.Increment(ref _nextAccessibilityId);
        /// <summary>
        /// Stable, author-assigned identifier for tests and automation to target, independent of
        /// anything the user sees. <see cref="Name"/> doubles as the accessible name and so changes
        /// when a control is relabelled or localized; an automation id is a contract that does not.
        /// Empty by default, in which case callers fall back to role and name.
        /// </summary>
        public string AutomationId
        {
            get => _automationId;
            set => SetValue(ref _automationId, value, nameof(AutomationId));
        }
        public string AccessibilityLabel
        {
            get => _accessibilityLabel;
            set => SetValue(ref _accessibilityLabel, value, nameof(AccessibilityLabel));
        }
        public virtual AccessibilityRole AccessibilityRole => AccessibilityRole.Generic;
        public virtual string AccessibilityName => string.IsNullOrEmpty(AccessibilityLabel) ? Name ?? string.Empty : AccessibilityLabel;
        public virtual string AccessibilityValue => string.Empty;
        public virtual AccessibilityActions AccessibilityActions => FocusMode == FocusMode.None ? AccessibilityActions.None : AccessibilityActions.Focus;
        public virtual AccessibilityStates AccessibilityStates =>
            (!IsEffectivelyEnabled ? AccessibilityStates.Disabled : AccessibilityStates.None) |
            (IsPseudoStateActive("focus") ? AccessibilityStates.Focused : AccessibilityStates.None);
        public Vector2 GlobalPosition => VisualParent == null ? Position : VisualParent.GlobalPosition + Position;

        public event EventHandler LayoutChanged;
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler MouseEntered;
        public event EventHandler MouseExited;
        public event EventHandler FocusEntered;
        public event EventHandler FocusExited;
        public event EventHandler Attached;
        public event EventHandler Detached;
        public event EventHandler EnabledChanged;
        public event EventHandler<ControlPseudoStateChangedEventArgs> PseudoStateChanged;
        public event EventHandler NameChanged;
        public event EventHandler<DataContextChangedEventArgs> DataContextChanged;
        public event EventHandler<ControlParentChangedEventArgs> ParentChanged;
        public event EventHandler<BringIntoViewRequestedEventArgs> BringIntoViewRequested;
        public event EventHandler<AccessibilityChangedEventArgs> AccessibilityChanged;
        public event Action<Control, Control> ChildAdded;
        public event Action<Control, Control> ChildRemoved;
        internal event Action<Control, Control> VisualChildAdded;
        internal event Action<Control, Control> VisualChildRemoved;
        /// <summary>Raised when this control supplies drag data after the pointer passes the drag threshold.</summary>
        public event Action<Control, object> DragStarted;
        /// <summary>Raised when a drag started by this control ends; the boolean indicates whether it was accepted.</summary>
        public event Action<Control, bool> DragEnded;

        public virtual void AddChild(Control child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (child == this) throw new InvalidOperationException("A control cannot be its own child.");
            for (var ancestor = this; ancestor != null; ancestor = ancestor.VisualParent)
                if (ancestor == child) throw new InvalidOperationException("A control cannot be added below one of its descendants.");
            child.RemoveFromParent();
            var previousDataContext = child.DataContext;
            _children.Add(child);
            child.Parent = this;
            AddVisualChildCore(child, this);
            child.NotifyInheritedDataContextChanged(previousDataContext, child.DataContext);
            ChildAdded?.Invoke(this, child);
            QueueLayout();
        }

        protected virtual AccessibilityPeer CreateAccessibilityPeer() => new AccessibilityPeer(this);

        /// <summary>
        /// Performs one advertised accessibility action, returning whether it was handled.
        /// <para>
        /// Overrides must route through the same code the corresponding real input runs, never a
        /// parallel implementation. An invoked press that takes a shortcut would make a test pass
        /// while telling you nothing about whether a person clicking the thing works.
        /// </para>
        /// </summary>
        public virtual bool PerformAccessibilityAction(AccessibilityActions action, object argument = null)
        {
            if (action != AccessibilityActions.Focus) return false;
            if (FocusMode == FocusMode.None || !IsEffectivelyEnabled || !IsRendered) return false;

            GrabFocus();
            return true;
        }

        public virtual IReadOnlyList<AccessibilityPeer> GetAccessibilityChildren()
        {
            if (_children.Count == 0) return Array.Empty<AccessibilityPeer>();
            var peers = new AccessibilityPeer[_children.Count];
            for (var index = 0; index < _children.Count; index++) peers[index] = _children[index].AccessibilityPeer;
            return peers;
        }

        public bool RemoveChild(Control child)
        {
            if (child == null || !_children.Remove(child)) return false;
            var previousDataContext = child.DataContext;
            var previousLogicalParent = child.Parent;
            child.Parent = null;
            if (child.VisualParent != null) child.VisualParent.RemoveVisualChildCore(child, previousLogicalParent);
            else child.ParentChanged?.Invoke(child, new ControlParentChangedEventArgs(previousLogicalParent, null, null, null, child.InheritanceParent, null));
            child.NotifyInheritedDataContextChanged(previousDataContext, child.DataContext);
            ChildRemoved?.Invoke(this, child);
            QueueLayout();
            return true;
        }

        internal void AddVisualChild(Control child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (child.Parent != null) throw new InvalidOperationException("A logical child cannot also be attached as a projected visual child.");
            child.RemoveFromParent();
            var previousDataContext = child.DataContext;
            AddVisualChildCore(child, this);
            child.NotifyInheritedDataContextChanged(previousDataContext, child.DataContext);
            QueueLayout();
        }

        internal void AddVisualChild(Control child, Control inheritanceParent)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (inheritanceParent == null) throw new ArgumentNullException(nameof(inheritanceParent));
            if (child.Parent != null) throw new InvalidOperationException("A logical child cannot also be attached as a generated visual child.");
            child.RemoveFromParent();
            var previousDataContext = child.DataContext;
            AddVisualChildCore(child, inheritanceParent);
            child.NotifyInheritedDataContextChanged(previousDataContext, child.DataContext);
            QueueLayout();
        }

        internal bool RemoveVisualChild(Control child)
        {
            if (child == null || child.VisualParent != this) return false;
            var previousDataContext = child.DataContext;
            RemoveVisualChildCore(child, child.Parent);
            child.NotifyInheritedDataContextChanged(previousDataContext, child.DataContext);
            QueueLayout();
            return true;
        }

        internal void MoveVisualChild(Control child, int toIndex)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            var fromIndex = _visualChildren.IndexOf(child);
            if (fromIndex < 0) throw new ArgumentException("The control is not a visual child of this control.", nameof(child));
            if (toIndex < 0 || toIndex >= _visualChildren.Count) throw new ArgumentOutOfRangeException(nameof(toIndex));
            if (fromIndex == toIndex) return;
            _visualChildren.RemoveAt(fromIndex);
            _visualChildren.Insert(toIndex, child);
            foreach (var sibling in _visualChildren) sibling._treeOrder = ++_nextChildOrder;
            _childOrderDirty = true;
            QueueLayout();
        }

        internal void ProjectVisualChild(Control child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (child.Parent == null) throw new InvalidOperationException("A projected visual child must retain a logical owner.");
            if (child.VisualParent == this) return;
            for (var ancestor = this; ancestor != null; ancestor = ancestor.VisualParent)
                if (ancestor == child) throw new InvalidOperationException("A control cannot create a visual cycle.");
            var inheritanceParent = child.Parent;
            for (var ancestor = inheritanceParent; ancestor != null; ancestor = ancestor.InheritanceParent)
                if (ancestor == child) throw new InvalidOperationException("A control cannot create an inheritance cycle.");

            var previousDataContext = child.DataContext;
            var previousVisualParent = child.VisualParent;
            var previousInheritanceParent = child.InheritanceParent;
            var previousInheritedStates = child.CaptureInheritedStates();
            var previousGeometry = child.CaptureGeometryStates();
            if (previousVisualParent != null)
            {
                previousVisualParent._visualChildren.Remove(child);
                previousVisualParent._childOrderDirty = true;
                previousVisualParent.VisualChildRemoved?.Invoke(previousVisualParent, child);
                previousVisualParent.QueueLayout();
            }
            _visualChildren.Add(child);
            child.VisualParent = this;
            if (previousInheritanceParent != inheritanceParent)
            {
                previousInheritanceParent?._inheritanceChildren.Remove(child);
                inheritanceParent._inheritanceChildren.Add(child);
            }
            child.InheritanceParent = inheritanceParent;
            child._treeOrder = ++_nextChildOrder;
            _childOrderDirty = true;
            child.SetContext(Context);
            child.NotifyInheritedDataContextChanged(previousDataContext, child.DataContext);
            child.NotifyInheritedStateChanges(previousInheritedStates);
            NotifyGeometryChanges(previousGeometry);
            child.ParentChanged?.Invoke(child, new ControlParentChangedEventArgs(child.Parent, child.Parent, previousVisualParent, this, previousInheritanceParent, inheritanceParent));
            child.NotifyInheritanceAncestryChanged();
            VisualChildAdded?.Invoke(this, child);
            QueueLayout();
        }

        private void AddVisualChildCore(Control child, Control inheritanceParent)
        {
            var previousVisualParent = child.VisualParent;
            var previousInheritanceParent = child.InheritanceParent;
            var previousInheritedStates = child.CaptureInheritedStates();
            var previousGeometry = child.CaptureGeometryStates();
            for (var ancestor = this; ancestor != null; ancestor = ancestor.VisualParent)
                if (ancestor == child) throw new InvalidOperationException("A control cannot create a visual cycle.");
            for (var ancestor = inheritanceParent; ancestor != null; ancestor = ancestor.InheritanceParent)
                if (ancestor == child) throw new InvalidOperationException("A control cannot create an inheritance cycle.");
            _visualChildren.Add(child);
            child.VisualParent = this;
            previousInheritanceParent?._inheritanceChildren.Remove(child);
            inheritanceParent._inheritanceChildren.Add(child);
            child.InheritanceParent = inheritanceParent;
            child._treeOrder = ++_nextChildOrder;
            _childOrderDirty = true;
            child.SetContext(Context);
            child.NotifyInheritedStateChanges(previousInheritedStates);
            NotifyGeometryChanges(previousGeometry);
            child.ParentChanged?.Invoke(child, new ControlParentChangedEventArgs(null, child.Parent, previousVisualParent, this, previousInheritanceParent, inheritanceParent));
            child.NotifyInheritanceAncestryChanged();
            VisualChildAdded?.Invoke(this, child);
        }

        private void RemoveVisualChildCore(Control child, Control previousLogicalParent)
        {
            if (!_visualChildren.Remove(child)) return;
            var previousVisualParent = child.VisualParent;
            var previousInheritanceParent = child.InheritanceParent;
            var previousInheritedStates = child.CaptureInheritedStates();
            var previousGeometry = child.CaptureGeometryStates();
            child.VisualParent = null;
            previousInheritanceParent?._inheritanceChildren.Remove(child);
            child.InheritanceParent = null;
            _childOrderDirty = true;
            child.SetContext(null);
            child.NotifyInheritedStateChanges(previousInheritedStates);
            NotifyGeometryChanges(previousGeometry);
            child.ParentChanged?.Invoke(child, new ControlParentChangedEventArgs(previousLogicalParent, child.Parent, previousVisualParent, null, previousInheritanceParent, null));
            child.NotifyInheritanceAncestryChanged();
            VisualChildRemoved?.Invoke(this, child);
        }

        private void NotifyInheritanceAncestryChanged()
        {
            NotifyAncestryChanged(new HashSet<Control>());
        }

        private void NotifyAncestryChanged(HashSet<Control> visited)
        {
            foreach (var child in _visualChildren)
            {
                if (!visited.Add(child)) continue;
                child.ParentChanged?.Invoke(child, new ControlParentChangedEventArgs(child.Parent, child.Parent, child.VisualParent, child.VisualParent, child.InheritanceParent, child.InheritanceParent, true));
                child.NotifyAncestryChanged(visited);
            }
            foreach (var child in _inheritanceChildren)
            {
                if (!visited.Add(child)) continue;
                child.ParentChanged?.Invoke(child, new ControlParentChangedEventArgs(child.Parent, child.Parent, child.VisualParent, child.VisualParent, child.InheritanceParent, child.InheritanceParent, true));
                child.NotifyAncestryChanged(visited);
            }
        }

        void IAddChild.AddChild(object child) => AddChild((Control)child);

        public void ClearDataContext()
        {
            if (!_hasLocalDataContext) return;
            var previous = DataContext;
            _dataContext = null;
            _hasLocalDataContext = false;
            OnPropertyChanged(nameof(HasLocalDataContext));
            NotifyDataContextChanged(previous, DataContext);
        }

        public bool TryFindResource(string key, out object value)
        {
            if (Resources.TryFind(key, out value)) return true;
            if (InheritanceParent != null) return InheritanceParent.TryFindResource(key, out value);
            if (Context != null) return Context.Resources.TryFind(key, out value);
            value = null;
            return false;
        }

        public T FindName<T>(string name) where T : class => NameScope.GetNameScope(this)?.Find<T>(name);

        private void NotifyDataContextChanged(object previous, object current)
        {
            if (ReferenceEquals(previous, current)) return;
            DataContextChanged?.Invoke(this, new DataContextChangedEventArgs(previous, current));
            OnPropertyChanged(nameof(DataContext));
            foreach (var child in _inheritanceChildren)
                child.NotifyInheritedDataContextChanged(previous, current);
        }

        private void NotifyInheritedDataContextChanged(object previous, object current)
        {
            if (_hasLocalDataContext || ReferenceEquals(previous, current)) return;
            DataContextChanged?.Invoke(this, new DataContextChangedEventArgs(previous, current));
            OnPropertyChanged(nameof(DataContext));
            foreach (var child in _inheritanceChildren)
                child.NotifyInheritedDataContextChanged(previous, current);
        }

        /// <summary>Moves an existing child to a different sibling index without changing its parent or UI context.</summary>
        public void MoveChild(Control child, int toIndex)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            var fromIndex = _children.IndexOf(child);
            if (fromIndex < 0) throw new ArgumentException("The control is not a child of this control.", nameof(child));
            if (toIndex < 0 || toIndex >= _children.Count) throw new ArgumentOutOfRangeException(nameof(toIndex));
            if (fromIndex == toIndex) return;
            _children.RemoveAt(fromIndex);
            _children.Insert(toIndex, child);
            foreach (var sibling in _children) sibling._treeOrder = ++_nextChildOrder;
            _childOrderDirty = true;
            QueueLayout();
        }

        public void RemoveFromParent()
        {
            if (Parent != null) Parent.RemoveChild(this);
            else VisualParent?.RemoveVisualChild(this);
        }

        public Control GetNodeOrNull(string name)
        {
            foreach (var child in _children)
            {
                if (child.Name == name) return child;
                var nested = child.GetNodeOrNull(name);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>Sets a local StyleBox override, equivalent to Godot's add_theme_style_override.</summary>
        public void AddThemeStyleOverride(string itemName, StyleBox styleBox)
        {
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("A theme item name is required.", nameof(itemName));
            if (styleBox == null) _styleOverrides.Remove(itemName); else _styleOverrides[itemName] = styleBox;
        }
        public void RemoveThemeStyleOverride(string itemName)
        {
            if (itemName != null) _styleOverrides.Remove(itemName);
        }
        public StyleBox GetThemeStyleBox(string itemName)
        {
            if (itemName == null) return null;
            if (_styleOverrides.TryGetValue(itemName, out var local)) return local;
            var typeNames = GetThemeTypeNames();
            for (var control = this; control != null; control = control.InheritanceParent)
            {
                var style = control.ThemeOverride?.GetStyleBox(itemName, typeNames);
                if (style != null) return style;
            }
            return Context?.Theme.GetStyleBox(itemName, typeNames);
        }

        /// <summary>Adds a local decorative theme-icon override. Existing content-icon properties are unaffected.</summary>
        public void AddThemeIconOverride(string itemName, ThemeIcon icon)
        {
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("A theme item name is required.", nameof(itemName));
            _iconOverrides[itemName] = icon;
        }
        /// <summary>Removes a local icon override so theme inheritance becomes visible again.</summary>
        public void RemoveThemeIconOverride(string itemName)
        {
            if (itemName != null) _iconOverrides.Remove(itemName);
        }
        /// <summary>Suppresses a decorative theme icon on this control without affecting content icons.</summary>
        public void SuppressThemeIcon(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("A theme item name is required.", nameof(itemName));
            _iconOverrides[itemName] = null;
        }
        /// <summary>Resolves a decorative icon from local overrides, ancestor themes, and the context theme.</summary>
        public ThemeIcon? GetThemeIcon(string itemName) => GetThemeIcon(itemName, null);
        protected ThemeIcon? GetThemeIcon(string itemName, string preferredTypeName)
        {
            if (itemName == null) return null;
            if (_iconOverrides.TryGetValue(itemName, out var local)) return local;
            var typeNames = GetThemeTypeNames(preferredTypeName);
            for (var control = this; control != null; control = control.InheritanceParent)
                if (control.ThemeOverride != null && control.ThemeOverride.TryGetIcon(itemName, typeNames, out var inherited)) return inherited;
            if (Context?.Theme.TryGetIcon(itemName, typeNames, out var contextual) == true) return contextual;
            return Context?.TryGetDefaultThemeIcon(itemName, typeNames, out var fallback) == true ? fallback : null;
        }

        private IEnumerable<string> GetThemeTypeNames() => GetThemeTypeNames(null);
        private IEnumerable<string> GetThemeTypeNames(string preferredTypeName)
        {
            if (!string.IsNullOrEmpty(preferredTypeName)) yield return preferredTypeName;
            for (var type = GetType(); type != null && typeof(Control).IsAssignableFrom(type); type = type.BaseType)
            {
                if (type.Name == preferredTypeName) continue;
                yield return type.Name;
            }
        }

        private static Side Opposite(Side side) => (Side)(((int)side + 2) % 4);

        private float GetAnchorRaw(Side side)
        {
            switch (side)
            {
                case Side.Left: return AnchorLeft;
                case Side.Top: return AnchorTop;
                case Side.Right: return AnchorRight;
                default: return AnchorBottom;
            }
        }

        private void SetAnchorRaw(Side side, float value)
        {
            switch (side)
            {
                case Side.Left: if (AnchorLeft != value) { AnchorLeft = value; OnPropertyChanged(nameof(AnchorLeft)); } break;
                case Side.Top: if (AnchorTop != value) { AnchorTop = value; OnPropertyChanged(nameof(AnchorTop)); } break;
                case Side.Right: if (AnchorRight != value) { AnchorRight = value; OnPropertyChanged(nameof(AnchorRight)); } break;
                case Side.Bottom: if (AnchorBottom != value) { AnchorBottom = value; OnPropertyChanged(nameof(AnchorBottom)); } break;
            }
        }

        private float GetOffsetRaw(Side side)
        {
            switch (side)
            {
                case Side.Left: return OffsetLeft;
                case Side.Top: return OffsetTop;
                case Side.Right: return OffsetRight;
                default: return OffsetBottom;
            }
        }

        private void SetOffsetRaw(Side side, float value)
        {
            switch (side)
            {
                case Side.Left: if (OffsetLeft != value) { OffsetLeft = value; OnPropertyChanged(nameof(OffsetLeft)); } break;
                case Side.Top: if (OffsetTop != value) { OffsetTop = value; OnPropertyChanged(nameof(OffsetTop)); } break;
                case Side.Right: if (OffsetRight != value) { OffsetRight = value; OnPropertyChanged(nameof(OffsetRight)); } break;
                case Side.Bottom: if (OffsetBottom != value) { OffsetBottom = value; OnPropertyChanged(nameof(OffsetBottom)); } break;
            }
        }

        /// <summary>
        /// Matches Godot's Control::set_anchor exactly: by default (<paramref name="keepOffset"/> false) the offset is
        /// recomputed so the control's resolved position does not jump when only the anchor changes, an anchor is never
        /// allowed to cross its opposite anchor (clamped to it, or pushing the opposite anchor along when
        /// <paramref name="pushOppositeAnchor"/> is set), and geometry is refreshed via <see cref="QueueLayout"/>.
        /// </summary>
        public void SetAnchor(Side side, float anchor, bool keepOffset = false, bool pushOppositeAnchor = false)
        {
            anchor = MathHelper.Clamp(anchor, 0, 1);
            var parentRange = VisualParent != null ? ((side == Side.Left || side == Side.Right) ? VisualParent.Size.X : VisualParent.Size.Y) : 0f;
            var oppositeSide = Opposite(side);
            var previousPos = GetOffsetRaw(side) + GetAnchorRaw(side) * parentRange;
            var previousOppositePos = GetOffsetRaw(oppositeSide) + GetAnchorRaw(oppositeSide) * parentRange;

            SetAnchorRaw(side, anchor);

            var crossed = (side == Side.Left || side == Side.Top)
                ? GetAnchorRaw(side) > GetAnchorRaw(oppositeSide)
                : GetAnchorRaw(side) < GetAnchorRaw(oppositeSide);
            if (crossed)
            {
                if (pushOppositeAnchor) SetAnchorRaw(oppositeSide, GetAnchorRaw(side));
                else SetAnchorRaw(side, GetAnchorRaw(oppositeSide));
            }

            if (!keepOffset)
            {
                SetOffsetRaw(side, previousPos - GetAnchorRaw(side) * parentRange);
                if (pushOppositeAnchor) SetOffsetRaw(oppositeSide, previousOppositePos - GetAnchorRaw(oppositeSide) * parentRange);
            }

            QueueLayout();
        }

        public void SetOffset(Side side, float offset)
        {
            SetOffsetRaw(side, offset);
            QueueLayout();
        }

        public void SetAnchorsAndOffsets(float left, float top, float right, float bottom)
        {
            SetAnchorRaw(Side.Left, MathHelper.Clamp(left, 0, 1)); SetAnchorRaw(Side.Top, MathHelper.Clamp(top, 0, 1));
            SetAnchorRaw(Side.Right, MathHelper.Clamp(right, 0, 1)); SetAnchorRaw(Side.Bottom, MathHelper.Clamp(bottom, 0, 1));
            SetOffsetRaw(Side.Left, 0); SetOffsetRaw(Side.Top, 0); SetOffsetRaw(Side.Right, 0); SetOffsetRaw(Side.Bottom, 0);
            QueueLayout();
        }

        private bool _eventAccepted;
        /// <summary>
        /// Matches Godot's Control::accept_event(): marks the current input event as handled for just this one
        /// dispatch, stopping propagation to ancestors independent of <see cref="MouseFilter"/> (which only governs
        /// every future event). Unlike setting <see cref="MouseFilter"/> to Stop, this does not affect later events.
        /// </summary>
        protected void AcceptEvent() => _eventAccepted = true;
        /// <summary>Consumes and returns whether <see cref="AcceptEvent"/> was called since the last consume, for use by a single dispatch loop iteration.</summary>
        internal bool ConsumeEventAccepted()
        {
            if (!_eventAccepted) return false;
            _eventAccepted = false;
            return true;
        }

        public virtual Vector2 GetMinimumSize() => CustomMinimumSize;
        /// <summary>Returns this control's preferred size before minimum-size clamping, matching Godot's virtual get_desired_size().</summary>
        public virtual Vector2 GetDesiredSize() => Vector2.Zero;
        /// <summary>Returns this control's intrinsic maximum size; negative components are unbounded.</summary>
        public virtual Vector2 GetMaximumSize() => new Vector2(-1, -1);
        public Vector2 GetCombinedMaximumSize()
        {
            var maximum = GetMaximumSize();
            if (CustomMaximumSize.X >= 0) maximum.X = maximum.X >= 0 ? Math.Min(maximum.X, CustomMaximumSize.X) : CustomMaximumSize.X;
            if (CustomMaximumSize.Y >= 0) maximum.Y = maximum.Y >= 0 ? Math.Min(maximum.Y, CustomMaximumSize.Y) : CustomMaximumSize.Y;
            if (!float.IsPositiveInfinity(MaxWidth)) maximum.X = maximum.X >= 0 ? Math.Min(maximum.X, MaxWidth) : MaxWidth;
            if (!float.IsPositiveInfinity(MaxHeight)) maximum.Y = maximum.Y >= 0 ? Math.Min(maximum.Y, MaxHeight) : MaxHeight;
            return maximum;
        }
        /// <summary>Returns the desired size clamped to this control's minimum and combined maximum sizes.</summary>
        public Vector2 GetBoundDesiredSize()
        {
            var desired = Vector2.Max(GetDesiredSize(), GetMinimumSize());
            desired = Vector2.Max(desired, new Vector2(MinWidth, MinHeight));
            var hasWidth = !float.IsNaN(Width);
            var hasHeight = !float.IsNaN(Height);
            if (hasWidth) desired.X = Width;
            if (hasHeight) desired.Y = Height;
            if (!float.IsNaN(AspectRatio) && hasWidth != hasHeight)
            {
                if (hasWidth) desired.Y = desired.X / AspectRatio;
                else desired.X = desired.Y * AspectRatio;
            }
            var maximum = GetCombinedMaximumSize();
            if (maximum.X >= 0) desired.X = Math.Min(desired.X, maximum.X);
            if (maximum.Y >= 0) desired.Y = Math.Min(desired.Y, maximum.Y);
            desired = Vector2.Max(desired, new Vector2(MinWidth, MinHeight));
            return desired;
        }

        private static float ValidateDimension(float value, string parameterName, bool allowAutomatic)
        {
            if (allowAutomatic && float.IsNaN(value)) return value;
            if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }

        private static float ValidateMaximum(float value, string parameterName)
        {
            if (float.IsPositiveInfinity(value)) return value;
            return ValidateDimension(value, parameterName, false);
        }
        public virtual bool ContainsPoint(Point point) => Bounds.Contains(point);
        internal bool ContainsComposedClipPoint(Point point)
        {
            if (ClipToBounds && !Bounds.Contains(point)) return false;
            return Clip == null || Clip.CreatePath(Size).ContainsPoint(point.ToVector2(), Matrix.CreateTranslation(GlobalPosition.X, GlobalPosition.Y, 0), Clip.FillRule);
        }
        internal bool TryTransformHitTestPoint(Point point, out Point transformedPoint)
        {
            if (RenderTransform == null)
            {
                transformedPoint = point;
                return true;
            }
            var transform = GetRenderTransformMatrix();
            if (MathF.Abs(transform.Determinant()) < float.Epsilon)
            {
                transformedPoint = default;
                return false;
            }
            var transformed = Vector2.Transform(point.ToVector2(), Matrix.Invert(transform));
            transformedPoint = new Point((int)MathF.Round(transformed.X), (int)MathF.Round(transformed.Y));
            return float.IsFinite(transformed.X) && float.IsFinite(transformed.Y);
        }
        internal Matrix GetRenderTransformMatrix()
        {
            if (RenderTransform == null) return Matrix.Identity;
            var origin = GlobalPosition + TransformOrigin * Size;
            return Matrix.CreateTranslation(-origin.X, -origin.Y, 0) * RenderTransform.Value * Matrix.CreateTranslation(origin.X, origin.Y, 0);
        }
        internal Matrix GetWorldRenderTransformMatrix()
        {
            var transform = Matrix.Identity;
            for (var control = this; control != null; control = control.VisualParent)
                transform *= control.GetRenderTransformMatrix();
            return transform;
        }
        internal virtual bool HitTestBeforeChildren(Point point) => false;
        /// <summary>Returns the tooltip at a global pointer position. Override to provide dynamic help text.</summary>
        public virtual string GetTooltip(Point position) => TooltipText;
        public void GrabFocus() => Context?.SetFocus(this);
        public void ReleaseFocus() { if (Context?.FocusedControl == this) Context.SetFocus(null); }
        public void BringIntoView(Rectangle? targetBounds = null)
        {
            var request = new BringIntoViewRequestedEventArgs(this, targetBounds ?? VisualBounds);
            for (var control = this; control != null && !request.Handled; control = control.VisualParent)
                control.BringIntoViewRequested?.Invoke(control, request);
        }
        /// <summary>Marks this control and every ancestor dirty, matching Godot's Control::update_minimum_size walking the full parent chain so a deeply nested size change reaches the root container.</summary>
        /// <summary>
        /// Whether this control still owes a layout pass. Part of the settle check: acting on a
        /// tree mid-layout reads bounds that are about to change.
        /// </summary>
        internal bool IsLayoutDirty => _layoutDirty;

        /// <summary>True when this control and everything under it have settled.</summary>
        internal bool IsSubtreeSettled()
        {
            if (_layoutDirty) return false;
            foreach (var child in _visualChildren)
                if (!child.IsSubtreeSettled()) return false;
            return true;
        }

        public void QueueLayout()
        {
            for (var control = this; control != null; control = control.VisualParent)
                control._layoutDirty = true;
        }

        internal Dictionary<Control, LayoutDirection> CaptureEffectiveLayoutDirections()
        {
            var values = new Dictionary<Control, LayoutDirection>();
            CaptureInheritedValues(this, control => control.EffectiveLayoutDirection, values);
            return values;
        }

        internal void MarkInheritedLayoutDirectionDirty(Dictionary<Control, LayoutDirection> previous)
        {
            foreach (var pair in previous)
            {
                pair.Key._layoutDirty = true;
                if (pair.Value != pair.Key.EffectiveLayoutDirection) pair.Key.OnPropertyChanged(nameof(EffectiveLayoutDirection));
            }
        }

        private bool SetValue<T>(ref T field, T value, string propertyName, bool queueLayout = false)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            if (queueLayout) QueueLayout();
            OnPropertyChanged(propertyName);
            return true;
        }

        private bool SetGeometryValue<T>(ref T field, T value, string propertyName, bool queueLayout = false)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            var previous = CaptureGeometryStates();
            field = value;
            if (queueLayout) QueueLayout();
            OnPropertyChanged(propertyName);
            NotifyGeometryChanges(previous);
            return true;
        }

        private Dictionary<Control, GeometryState> CaptureGeometryStates()
        {
            var states = new Dictionary<Control, GeometryState>();
            CaptureGeometryStates(this, states);
            return states;
        }

        private static void CaptureGeometryStates(Control control, Dictionary<Control, GeometryState> states)
        {
            states.Add(control, new GeometryState(control));
            foreach (var child in control._visualChildren) CaptureGeometryStates(child, states);
        }

        private static void NotifyGeometryChanges(Dictionary<Control, GeometryState> previous)
        {
            foreach (var pair in previous)
            {
                var control = pair.Key;
                var state = pair.Value;
                if (state.Bounds != control.Bounds) control.OnPropertyChanged(nameof(Bounds));
                if (state.GlobalPosition != control.GlobalPosition) control.OnPropertyChanged(nameof(GlobalPosition));
                if (state.VisualBounds != control.VisualBounds) control.OnPropertyChanged(nameof(VisualBounds));
                if (state.FocusBounds != control.FocusBounds) control.OnPropertyChanged(nameof(FocusBounds));
                if (state.AccessibilityBounds != control.AccessibilityBounds) control.OnPropertyChanged(nameof(AccessibilityBounds));
            }
        }

        private Dictionary<Control, T> CaptureInheritedValues<T>(Func<Control, T> getValue)
        {
            var values = new Dictionary<Control, T>();
            CaptureInheritedValues(this, getValue, values);
            return values;
        }

        private static void CaptureInheritedValues<T>(Control control, Func<Control, T> getValue, Dictionary<Control, T> values)
        {
            values.Add(control, getValue(control));
            foreach (var child in control._inheritanceChildren) CaptureInheritedValues(child, getValue, values);
        }

        private static void NotifyInheritedValueChanges<T>(Dictionary<Control, T> previous, Func<Control, T> getValue, string propertyName)
        {
            foreach (var pair in previous)
                if (!EqualityComparer<T>.Default.Equals(pair.Value, getValue(pair.Key))) pair.Key.OnPropertyChanged(propertyName);
        }

        private Dictionary<Control, InheritedState> CaptureInheritedStates()
        {
            var states = new Dictionary<Control, InheritedState>();
            CaptureInheritedStates(this, states);
            return states;
        }

        private static void CaptureInheritedStates(Control control, Dictionary<Control, InheritedState> states)
        {
            states.Add(control, new InheritedState(control));
            foreach (var child in control._inheritanceChildren) CaptureInheritedStates(child, states);
        }

        private void NotifyInheritedStateChanges(Dictionary<Control, InheritedState> previous)
        {
            foreach (var pair in previous)
            {
                var control = pair.Key;
                var state = pair.Value;
                if (state.IsEffectivelyEnabled != control.IsEffectivelyEnabled)
                {
                    control.OnPropertyChanged(nameof(IsEffectivelyEnabled));
                    control.NotifyPseudoStateChanged("disabled");
                }
                if (state.Foreground != control.Foreground) control.OnPropertyChanged(nameof(Foreground));
                if (!ReferenceEquals(state.FontFamily, control.FontFamily)) control.OnPropertyChanged(nameof(FontFamily));
                if (state.FontSize != control.FontSize) control.OnPropertyChanged(nameof(FontSize));
                if (state.FontWeight != control.FontWeight) control.OnPropertyChanged(nameof(FontWeight));
                if (state.FontStyle != control.FontStyle) control.OnPropertyChanged(nameof(FontStyle));
                if (state.FontStretch != control.FontStretch) control.OnPropertyChanged(nameof(FontStretch));
                if (state.Language != control.Language) control.OnPropertyChanged(nameof(Language));
                if (state.LayoutDirection != control.EffectiveLayoutDirection) control.OnPropertyChanged(nameof(EffectiveLayoutDirection));
                if (state.PixelSnapping != control.IsPixelSnappingEnabled) control.OnPropertyChanged(nameof(IsPixelSnappingEnabled));
                if (state.Cursor != control.EffectiveCursor) control.OnPropertyChanged(nameof(EffectiveCursor));
            }
        }

        private readonly struct InheritedState
        {
            public InheritedState(Control control)
            {
                IsEffectivelyEnabled = control.IsEffectivelyEnabled;
                Foreground = control.Foreground;
                FontFamily = control.FontFamily;
                FontSize = control.FontSize;
                FontWeight = control.FontWeight;
                FontStyle = control.FontStyle;
                FontStretch = control.FontStretch;
                Language = control.Language;
                LayoutDirection = control.EffectiveLayoutDirection;
                PixelSnapping = control.IsPixelSnappingEnabled;
                Cursor = control.EffectiveCursor;
            }

            public bool IsEffectivelyEnabled { get; }
            public Color? Foreground { get; }
            public UIFontFamily FontFamily { get; }
            public float FontSize { get; }
            public UIFontWeight FontWeight { get; }
            public UIFontStyle FontStyle { get; }
            public UIFontStretch FontStretch { get; }
            public string Language { get; }
            public LayoutDirection LayoutDirection { get; }
            public bool PixelSnapping { get; }
            public Cursor Cursor { get; }
        }

        private readonly struct GeometryState
        {
            public GeometryState(Control control)
            {
                Bounds = control.Bounds;
                GlobalPosition = control.GlobalPosition;
                VisualBounds = control.VisualBounds;
                FocusBounds = control.FocusBounds;
                AccessibilityBounds = control.AccessibilityBounds;
            }

            public Rectangle Bounds { get; }
            public Vector2 GlobalPosition { get; }
            public Rectangle VisualBounds { get; }
            public Rectangle FocusBounds { get; }
            public Rectangle AccessibilityBounds { get; }
        }

        private void ClassesChanged(object sender, EventArgs args) => OnPropertyChanged(nameof(Classes));

        private void ThemeStyleOverridesChanged()
        {
            MarkThemeDirty();
            QueueLayout();
            OnPropertyChanged(nameof(ThemeStyleOverrides));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            AccessibilityChanged?.Invoke(this, new AccessibilityChangedEventArgs(propertyName));
        }

        internal void MarkDisplayScaleLayoutDirty()
        {
            if (this is IDisplayScaleLayout) _layoutDirty = true;
            foreach (var child in _visualChildren) child.MarkDisplayScaleLayoutDirty();
        }

        private sealed class ObservableStyleDictionary : IDictionary<string, StyleBox>
        {
            private readonly Dictionary<string, StyleBox> _values = new Dictionary<string, StyleBox>(StringComparer.Ordinal);
            private readonly Action _changed;

            public ObservableStyleDictionary(Action changed) => _changed = changed;
            public StyleBox this[string key]
            {
                get => _values[key];
                set
                {
                    if (_values.TryGetValue(key, out var current) && ReferenceEquals(current, value)) return;
                    _values[key] = value;
                    _changed();
                }
            }
            public ICollection<string> Keys => _values.Keys;
            public ICollection<StyleBox> Values => _values.Values;
            public int Count => _values.Count;
            public bool IsReadOnly => false;
            public void Add(string key, StyleBox value) { _values.Add(key, value); _changed(); }
            public void Add(KeyValuePair<string, StyleBox> item) { ((ICollection<KeyValuePair<string, StyleBox>>)_values).Add(item); _changed(); }
            public void Clear() { if (_values.Count == 0) return; _values.Clear(); _changed(); }
            public bool Contains(KeyValuePair<string, StyleBox> item) => ((ICollection<KeyValuePair<string, StyleBox>>)_values).Contains(item);
            public bool ContainsKey(string key) => _values.ContainsKey(key);
            public void CopyTo(KeyValuePair<string, StyleBox>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, StyleBox>>)_values).CopyTo(array, arrayIndex);
            public IEnumerator<KeyValuePair<string, StyleBox>> GetEnumerator() => _values.GetEnumerator();
            public bool Remove(string key) { if (!_values.Remove(key)) return false; _changed(); return true; }
            public bool Remove(KeyValuePair<string, StyleBox> item) { if (!((ICollection<KeyValuePair<string, StyleBox>>)_values).Remove(item)) return false; _changed(); return true; }
            public bool TryGetValue(string key, out StyleBox value) => _values.TryGetValue(key, out value);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        internal void MarkThemeDirty()
        {
            _layoutDirty = true;
            OnThemeChanged();
            foreach (var child in _inheritanceChildren) child.MarkThemeDirty();
        }

        protected virtual void OnThemeChanged() { }

        internal ControlTemplate ResolveThemeControlTemplate()
        {
            var controlType = GetType();
            for (var control = this; control != null; control = control.InheritanceParent)
            {
                var template = control.ThemeOverride?.GetControlTemplate(controlType);
                if (template != null) return template;
            }
            return Context?.Theme.GetControlTemplate(controlType);
        }

        private void ThemeOverrideChanged(object sender, EventArgs args)
        {
            Context?.TextLayoutEngine.Clear();
            MarkThemeDirty();
            QueueLayout();
        }

        internal UIFont ResolveFont(UIFontSelection selection, UIFontFamily fontFamily = null, float fontSize = 0,
            UIFontWeight weight = UIFontWeight.Normal, UIFontStyle style = UIFontStyle.Normal, UIFontStretch stretch = UIFontStretch.Normal)
        {
            var themes = new List<Theme>();
            for (var control = this; control != null; control = control.InheritanceParent)
                if (control.ThemeOverride != null) themes.Add(control.ThemeOverride);
            var inherited = new List<Theme>();
            var effective = Context?.Theme;
            for (var index = themes.Count - 1; index >= 0; index--)
            {
                var theme = themes[index];
                if (theme.Parent == null && effective != null) { theme.SetInheritedParent(effective); inherited.Add(theme); }
                effective = theme;
            }
            try { return selection.Resolve(effective, fontFamily, fontSize, weight, style, stretch); }
            finally
            {
                for (var index = inherited.Count - 1; index >= 0; index--) inherited[index].SetInheritedParent(null);
            }
        }

        internal void SetContext(UIContext context)
        {
            var previous = Context;
            ExceptionDispatchInfo failure = null;
            if (previous != null && previous != context)
            {
                try { previous.ResetInteractionState(this); }
                catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
            }
            Context = context;
            if (previous != context)
            {
                try { OnContextChanged(previous, context); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
                if (previous != null)
                {
                    try { Detached?.Invoke(this, EventArgs.Empty); }
                    catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
                }
                try { XamlAttachment.ContextChanged(this, previous, context); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
                if (context != null)
                {
                    try { Attached?.Invoke(this, EventArgs.Empty); }
                    catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
                }
            }
            foreach (var child in _visualChildren.ToArray())
            {
                if (Context != context) break;
                if (child.VisualParent != this) continue;
                try { child.SetContext(context); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            failure?.Throw();
        }

        /// <summary>Called when this control enters, exits, or moves between retained UI contexts.</summary>
        protected virtual void OnContextChanged(UIContext previous, UIContext current) { }

        internal void SetTreeOrder(long order) => _treeOrder = order;
        internal long TreeOrder => _treeOrder;

        internal IReadOnlyList<Control> GetChildrenInDrawOrder()
        {
            if (!_childOrderDirty) return _childrenInDrawOrder;
            _childrenInDrawOrder.Clear();
            _childrenInDrawOrder.AddRange(_visualChildren);
            _childrenInDrawOrder.Sort((left, right) =>
            {
                var zOrder = left.ZIndex.CompareTo(right.ZIndex);
                return zOrder != 0 ? zOrder : left._treeOrder.CompareTo(right._treeOrder);
            });
            _childOrderDirty = false;
            return _childrenInDrawOrder;
        }

        internal IReadOnlyList<Control> VisualChildren => _visualChildren;

        internal void LayoutTree()
        {
            if (VisualParent != null && (AnchorLeft != 0 || AnchorTop != 0 || AnchorRight != 0 || AnchorBottom != 0 || OffsetLeft != 0 || OffsetTop != 0 || OffsetRight != 0 || OffsetBottom != 0))
            {
                var parentSize = VisualParent.Size;
                var newPosition = new Vector2(parentSize.X * AnchorLeft + OffsetLeft, parentSize.Y * AnchorTop + OffsetTop);
                var newSize = new Vector2(parentSize.X * AnchorRight + OffsetRight - newPosition.X, parentSize.Y * AnchorBottom + OffsetBottom - newPosition.Y);

                // Matches Godot's Control::_size_changed(): clamp each axis up to the combined minimum size,
                // compensating position per GrowDirection, then mirror horizontally under RTL — in that exact order.
                var minimumSize = GetMinimumSize();
                if (minimumSize.X > newSize.X)
                {
                    if (HGrowDirection == GrowDirection.Begin) newPosition.X += newSize.X - minimumSize.X;
                    else if (HGrowDirection == GrowDirection.Both) newPosition.X += 0.5f * (newSize.X - minimumSize.X);
                    newSize.X = minimumSize.X;
                }
                if (IsLayoutRtl()) newPosition.X = parentSize.X - newPosition.X - newSize.X;
                if (minimumSize.Y > newSize.Y)
                {
                    if (VGrowDirection == GrowDirection.Begin) newPosition.Y += newSize.Y - minimumSize.Y;
                    else if (VGrowDirection == GrowDirection.Both) newPosition.Y += 0.5f * (newSize.Y - minimumSize.Y);
                    newSize.Y = minimumSize.Y;
                }

                // Matches Godot's Container::_notification(NOTIFICATION_RESIZED) => queue_sort(): a control whose own
                // resolved size just changed (even purely from a parent resize, bypassing the Size setter's QueueLayout)
                // must re-arrange its own children this same pass, or nested containers go stale after any ancestor resize.
                var positionChanged = newPosition != _position;
                var sizeChanged = newSize != _size;
                var previousGeometry = positionChanged || sizeChanged ? CaptureGeometryStates() : null;
                if (sizeChanged) _layoutDirty = true;
                _position = newPosition;
                _size = newSize;
                if (positionChanged) OnPropertyChanged(nameof(Position));
                if (sizeChanged) OnPropertyChanged(nameof(Size));
                if (previousGeometry != null) NotifyGeometryChanges(previousGeometry);
            }
            if (_layoutDirty)
            {
                ArrangeChildren();
                _layoutDirty = false;
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            }
            foreach (var child in _visualChildren) child.LayoutTree();
        }

        protected virtual void ArrangeChildren() { }
        internal virtual void Process(GameTime gameTime) { foreach (var child in _visualChildren) if (child.IsRendered) child.Process(gameTime); }
        internal void DrawTree(UIRenderContext context)
        {
            if ((Clip != null || ClipToBounds) && (Size.X <= 0 || Size.Y <= 0)) return;
            context.PushTheme(ThemeOverride);
            try
            {
                Action draw = () => Draw(context);
                if (Clip != null)
                {
                    var content = draw;
                    draw = () => context.DrawClipped(Clip.CreatePath(Size), Matrix.CreateTranslation(GlobalPosition.X, GlobalPosition.Y, 0), Bounds, content);
                }
                if (ClipToBounds)
                {
                    var content = draw;
                    draw = () =>
                    {
                        context.PushClip(Bounds);
                        try { content(); }
                        finally { context.PopClip(); }
                    };
                }
                if (OpacityMask != null)
                {
                    var content = draw;
                    draw = () => context.DrawOpacityMask(OpacityMask, Bounds, content);
                }
                if (Effect != null)
                {
                    var content = draw;
                    draw = () => context.DrawEffect(Effect, Bounds, content);
                }
                if (Opacity < 1)
                {
                    var content = draw;
                    draw = () => context.DrawOpacity(Opacity, Bounds, content);
                }
                if (RenderTransform != null)
                {
                    var content = draw;
                    draw = () => context.DrawTransformed(GetRenderTransformMatrix(), Bounds, content);
                }
                draw();
            }
            finally { context.PopTheme(); }
        }
        internal virtual void Draw(UIRenderContext context)
        {
            if (ClipContents) context.PushClip(Bounds);
            try { foreach (var child in GetChildrenInDrawOrder()) if (child.IsRendered) child.DrawTree(context); }
            finally { if (ClipContents) context.PopClip(); }
        }
        internal virtual void PointerEntered()
        {
            _isHovered = true;
            MouseEntered?.Invoke(this, EventArgs.Empty);
            NotifyPseudoStateChanged("hover");
        }
        internal virtual void PointerExited()
        {
            _isHovered = false;
            MouseExited?.Invoke(this, EventArgs.Empty);
            NotifyPseudoStateChanged("hover");
        }
        internal virtual void FocusGained()
        {
            _isFocused = true;
            FocusEntered?.Invoke(this, EventArgs.Empty);
            NotifyPseudoStateChanged("focus");
            NotifyFocusWithinAncestors();
        }
        internal virtual void FocusLost()
        {
            _isFocused = false;
            FocusExited?.Invoke(this, EventArgs.Empty);
            NotifyPseudoStateChanged("focus");
            NotifyFocusWithinAncestors();
        }
        protected void SetPseudoState(string state, bool active)
        {
            if (string.IsNullOrWhiteSpace(state)) throw new ArgumentException("A pseudo-state identifier is required.", nameof(state));
            if (active ? !_pseudoStates.Add(state) : !_pseudoStates.Remove(state)) return;
            NotifyPseudoStateChanged(state);
        }
        protected void NotifyPseudoStateChanged(string state) =>
            PseudoStateChanged?.Invoke(this, new ControlPseudoStateChangedEventArgs(state));

        private bool HasFocusedVisualDescendant()
        {
            for (var current = Context?.FocusedControl; current != null; current = StyleBoundary.GetOrdinaryParent(current))
                if (ReferenceEquals(current, this)) return true;
            return false;
        }

        private void NotifyFocusWithinAncestors()
        {
            for (var current = this; current != null; current = StyleBoundary.GetOrdinaryParent(current))
                current.NotifyPseudoStateChanged("focus-within");
        }
        /// <summary>
        /// A pointer press landed on this control, in global coordinates.
        /// <para>
        /// Protected as well as internal so a control defined outside this assembly can take part in
        /// pointer input. Without that, an app hosting its own drawing surface has to read raw mouse
        /// state instead, which bypasses hit-testing, modal gating and the injection side-channel —
        /// so such a surface cannot be driven by a test at all.
        /// </para>
        /// </summary>
        protected internal virtual void PointerPressed(Point position) { if (FocusMode != FocusMode.None) GrabFocus(); }
        /// <summary>Receives a physical pointer press. The primary button additionally routes through <see cref="PointerPressed"/> for compatibility with existing controls.</summary>
        internal virtual void PointerButtonPressed(Point position, PointerButton button) { if (button == PointerButton.Right) PointerRightPressed(position); }
        /// <summary>Receives a secondary/right pointer press. Controls that present a context menu can override this independently of primary activation.</summary>
        internal virtual void PointerRightPressed(Point position) { }
        /// <summary>Receives a physical pointer release at the current hit-tested position.</summary>
        internal virtual void PointerButtonReleased(Point position, PointerButton button) { }
        /// <summary>The pointer moved over this control, in global coordinates.</summary>
        protected internal virtual void PointerMoved(Point position) { }
        /// <summary>The pointer was released, with whether it was still inside this control.</summary>
        protected internal virtual void PointerReleased(Point position, bool isInside) { }
        internal virtual bool PointerWheel(int delta) => false;
        internal virtual bool ShortcutInput(Keys key, KeyboardState keyboard) => false;
        internal virtual void KeyPressed(Keys key) { }
        /// <summary>Notifies the focused control that a previously-pressed key was released. Purely additive over <see cref="KeyPressed"/>; most controls have no need to override it.</summary>
        internal virtual void KeyReleased(Keys key) { }
        internal virtual void CancelInput()
        {
            if (Context == null) return;
            foreach (var key in Context.CurrentKeyboardState.GetPressedKeys()) KeyReleased(key);
        }
        internal virtual void TextInput(char character) { }
        internal virtual void TextInput(string text)
        {
            foreach (var character in text) TextInput(character);
        }
        internal virtual void TextComposition(string text, int selectionStart, int selectionLength) { }
        /// <summary>Returns data to drag, or <see langword="null"/> to decline starting a drag.</summary>
        public virtual object GetDragData(Point position) => null;
        /// <summary>Returns whether this control accepts the supplied data at the screen position.</summary>
        public virtual bool CanDropData(Point position, object data) => false;
        /// <summary>Receives data accepted by <see cref="CanDropData"/>.</summary>
        public virtual void DropData(Point position, object data) { }
        internal void NotifyDragStarted(object data) => DragStarted?.Invoke(this, data);
        internal void NotifyDragEnded(bool succeeded) => DragEnded?.Invoke(this, succeeded);

        internal static Rectangle TransformBounds(Rectangle bounds, Matrix transform)
        {
            var topLeft = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), transform);
            var topRight = Vector2.Transform(new Vector2(bounds.Right, bounds.Top), transform);
            var bottomRight = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), transform);
            var bottomLeft = Vector2.Transform(new Vector2(bounds.Left, bounds.Bottom), transform);
            var minimum = Vector2.Min(Vector2.Min(topLeft, topRight), Vector2.Min(bottomRight, bottomLeft));
            var maximum = Vector2.Max(Vector2.Max(topLeft, topRight), Vector2.Max(bottomRight, bottomLeft));
            return new Rectangle(
                (int)MathF.Floor(minimum.X),
                (int)MathF.Floor(minimum.Y),
                Math.Max(0, (int)MathF.Ceiling(maximum.X) - (int)MathF.Floor(minimum.X)),
                Math.Max(0, (int)MathF.Ceiling(maximum.Y) - (int)MathF.Floor(minimum.Y)));
        }
    }

    public sealed class ControlPseudoStateChangedEventArgs : EventArgs
    {
        public ControlPseudoStateChangedEventArgs(string state) => State = state;
        public string State { get; }
    }

    public sealed class ControlParentChangedEventArgs : EventArgs
    {
        public ControlParentChangedEventArgs(Control previousParent, Control parent, Control previousVisualParent, Control visualParent, Control previousInheritanceParent, Control inheritanceParent, bool isAncestryInvalidation = false)
        {
            PreviousParent = previousParent;
            Parent = parent;
            PreviousVisualParent = previousVisualParent;
            VisualParent = visualParent;
            PreviousInheritanceParent = previousInheritanceParent;
            InheritanceParent = inheritanceParent;
            IsAncestryInvalidation = isAncestryInvalidation;
        }

        public Control PreviousParent { get; }
        public Control Parent { get; }
        public Control PreviousVisualParent { get; }
        public Control VisualParent { get; }
        public Control PreviousInheritanceParent { get; }
        public Control InheritanceParent { get; }
        public bool IsAncestryInvalidation { get; }
    }

    public sealed class BringIntoViewRequestedEventArgs : EventArgs
    {
        internal BringIntoViewRequestedEventArgs(Control target, Rectangle targetBounds)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            TargetBounds = targetBounds;
        }

        public Control Target { get; }
        public Rectangle TargetBounds { get; }
        public bool Handled { get; set; }
    }
}
