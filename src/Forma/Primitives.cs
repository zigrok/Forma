// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// UI enums map concepts and values from Godot Engine's control.h, scroll_container.h,
// input_enums.h, and math_defs.h; see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Forma
{
    /// <summary>Specifies how a control participates in pointer hit testing.</summary>
    public enum MouseFilter { Stop, Pass, Ignore }
    /// <summary>Specifies whether a control can receive keyboard focus.</summary>
    public enum FocusMode { None, Click, All }
    public enum Visibility { Visible, Hidden, Collapsed }
    public enum PixelSnapping { Inherited, Enabled, Disabled }
    public enum Cursor { Inherited, Arrow, IBeam, Hand, Crosshair, Wait, SizeHorizontal, SizeVertical, SizeDiagonalNorthwestSoutheast, SizeDiagonalNortheastSouthwest }
    /// <summary>Physical pointer buttons using Godot's <c>MouseButton</c> numeric values. Wheel directions are routed through <see cref="Control.PointerWheel"/> instead.</summary>
    public enum PointerButton { None = 0, Left = 1, Right = 2, Middle = 3, XButton1 = 8, XButton2 = 9 }
    /// <summary>Godot-style button mouse mask flags for controls that activate from pointer buttons.</summary>
    [Flags]
    public enum ButtonMouseMask { None = 0, Left = 1, Right = 2, Middle = 4 }
    public enum HorizontalAlignment { Left, Center, Right, Fill }
    public enum VerticalAlignment { Top, Center, Bottom, Fill }
    public enum Orientation { Horizontal, Vertical }
    /// <summary>Godot ScrollContainer display/interaction modes for an axis.</summary>
    public enum ScrollBarVisibility { Auto, Always, Never, Disabled, Reserve, MaximizeFirst }
    public enum Side { Left, Top, Right, Bottom }

    [Flags]
    public enum SizeFlags
    {
        ShrinkBegin = 0,
        Fill = 1,
        Expand = 2,
        ShrinkCenter = 4,
        ShrinkEnd = 8
    }

    /// <summary>Margins, padding and separation values used by UI controls.</summary>
    public readonly struct Thickness : IEquatable<Thickness>
    {
        public Thickness(float uniform) : this(uniform, uniform, uniform, uniform) { }
        public Thickness(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public float Left { get; }
        public float Top { get; }
        public float Right { get; }
        public float Bottom { get; }
        public float Horizontal => Left + Right;
        public float Vertical => Top + Bottom;
        public static Thickness Zero => default;
        public bool Equals(Thickness other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
        public override bool Equals(object obj) => obj is Thickness other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
        public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);
        public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);
    }

    /// <summary>
    /// Describes a non-owning bitmap atlas region or compiled vector rendered at a stable logical size.
    /// The resource cache owns any texture; copying or discarding this value never disposes resources.
    /// </summary>
    public readonly struct ThemeIcon : IEquatable<ThemeIcon>
    {
        public ThemeIcon(Texture2D texture, Rectangle sourceRectangle, Point logicalSize, int density = 1)
        {
            Texture = texture ?? throw new ArgumentNullException(nameof(texture));
            if (sourceRectangle.Width <= 0 || sourceRectangle.Height <= 0) throw new ArgumentOutOfRangeException(nameof(sourceRectangle));
            if (logicalSize.X <= 0 || logicalSize.Y <= 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));
            if (density <= 0) throw new ArgumentOutOfRangeException(nameof(density));
            SourceRectangle = sourceRectangle;
            LogicalSize = logicalSize;
            Density = density;
        }

        public ThemeIcon(DrawingImage vectorSource, Point logicalSize, int density = 1)
        {
            VectorSource = vectorSource ?? throw new ArgumentNullException(nameof(vectorSource));
            if (logicalSize.X <= 0 || logicalSize.Y <= 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));
            if (density <= 0) throw new ArgumentOutOfRangeException(nameof(density));
            Texture = null;
            SourceRectangle = Rectangle.Empty;
            LogicalSize = logicalSize;
            Density = density;
        }

        public ThemeIcon(ScalableImageSource scalableSource, Point logicalSize, int density = 1)
        {
            ScalableSource = scalableSource ?? throw new ArgumentNullException(nameof(scalableSource));
            if (logicalSize.X <= 0 || logicalSize.Y <= 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));
            if (density <= 0) throw new ArgumentOutOfRangeException(nameof(density));
            Texture = null;
            VectorSource = null;
            SourceRectangle = Rectangle.Empty;
            LogicalSize = logicalSize;
            Density = density;
        }

        public ThemeIcon(ScalableImageSource scalableSource, Texture2D fallbackTexture, Rectangle fallbackSourceRectangle, Point logicalSize, int density = 1)
        {
            ScalableSource = scalableSource ?? throw new ArgumentNullException(nameof(scalableSource));
            Texture = fallbackTexture ?? throw new ArgumentNullException(nameof(fallbackTexture));
            if (fallbackSourceRectangle.Width <= 0 || fallbackSourceRectangle.Height <= 0) throw new ArgumentOutOfRangeException(nameof(fallbackSourceRectangle));
            if (logicalSize.X <= 0 || logicalSize.Y <= 0) throw new ArgumentOutOfRangeException(nameof(logicalSize));
            if (density <= 0) throw new ArgumentOutOfRangeException(nameof(density));
            VectorSource = null;
            SourceRectangle = fallbackSourceRectangle;
            LogicalSize = logicalSize;
            Density = density;
        }

        public Texture2D Texture { get; }
        public DrawingImage VectorSource { get; }
        public ScalableImageSource ScalableSource { get; }
        public Rectangle SourceRectangle { get; }
        public Point LogicalSize { get; }
        public int Density { get; }
        public bool Equals(ThemeIcon other) => ReferenceEquals(Texture, other.Texture) && ReferenceEquals(VectorSource, other.VectorSource) && ReferenceEquals(ScalableSource, other.ScalableSource) && SourceRectangle == other.SourceRectangle && LogicalSize == other.LogicalSize && Density == other.Density;
        public override bool Equals(object obj) => obj is ThemeIcon other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Texture, VectorSource, ScalableSource, SourceRectangle, LogicalSize, Density);
        public static bool operator ==(ThemeIcon left, ThemeIcon right) => left.Equals(right);
        public static bool operator !=(ThemeIcon left, ThemeIcon right) => !left.Equals(right);
    }

    /// <summary>A small, deterministic theme used when an application does not provide custom drawing.</summary>
    public sealed class Theme
    {
        private readonly Dictionary<string, StyleBox> _styleBoxes = new Dictionary<string, StyleBox>(StringComparer.Ordinal);
        private readonly Dictionary<string, ThemeIcon?> _icons = new Dictionary<string, ThemeIcon?>(StringComparer.Ordinal);
        private readonly Dictionary<Type, ControlTemplate> _controlTemplates = new Dictionary<Type, ControlTemplate>();
        private Color? _panelColor, _panelBorderColor, _textColor, _disabledTextColor, _accentColor, _hoverColor, _pressedColor, _focusColor, _backgroundColor, _connectionActivityColor, _tabSelectedColor, _tabSelectedIndicatorColor;
        private float? _separation, _borderWidth, _tabSelectedIndicatorHeight, _tabSelectedLift;
        private UIFontFamily _fontFamily;
        private float? _fontSize;
        private IReadOnlyList<UIFontOpenTypeFeature> _fontOpenTypeFeatures;
        private UIFontHinting? _fontHinting;
        private Theme _parent;
        public event EventHandler Changed;
        public long Version { get; private set; }

        /// <summary>Optional parent. Unspecified colors, constants and style items resolve through it.</summary>
        public Theme Parent
        {
            get => _parent;
            set => SetParent(value, true);
        }
        public Color PanelColor { get => _panelColor ?? Parent?.PanelColor ?? new Color(52, 58, 70); set => SetThemeValue(ref _panelColor, value); }
        public Color PanelBorderColor { get => _panelBorderColor ?? Parent?.PanelBorderColor ?? new Color(92, 101, 119); set => SetThemeValue(ref _panelBorderColor, value); }
        public Color TextColor { get => _textColor ?? Parent?.TextColor ?? Color.White; set => SetThemeValue(ref _textColor, value); }
        public Color DisabledTextColor { get => _disabledTextColor ?? Parent?.DisabledTextColor ?? new Color(160, 166, 178); set => SetThemeValue(ref _disabledTextColor, value); }
        public Color AccentColor { get => _accentColor ?? Parent?.AccentColor ?? new Color(70, 145, 235); set => SetThemeValue(ref _accentColor, value); }
        public Color HoverColor { get => _hoverColor ?? Parent?.HoverColor ?? new Color(73, 82, 99); set => SetThemeValue(ref _hoverColor, value); }
        public Color PressedColor { get => _pressedColor ?? Parent?.PressedColor ?? new Color(42, 48, 58); set => SetThemeValue(ref _pressedColor, value); }
        public Color FocusColor { get => _focusColor ?? Parent?.FocusColor ?? new Color(112, 178, 255); set => SetThemeValue(ref _focusColor, value); }
        public Color BackgroundColor { get => _backgroundColor ?? Parent?.BackgroundColor ?? new Color(35, 39, 47); set => SetThemeValue(ref _backgroundColor, value); }
        /// <summary>Godot-style GraphEdit activity tint applied to active connection lines.</summary>
        public Color ConnectionActivityColor { get => _connectionActivityColor ?? Parent?.ConnectionActivityColor ?? new Color(255, 190, 86); set => SetThemeValue(ref _connectionActivityColor, value); }
        /// <summary>Fill used by the selected tab header. Defaults to <see cref="PressedColor"/>.</summary>
        public Color TabSelectedColor { get => _tabSelectedColor ?? Parent?.TabSelectedColor ?? PressedColor; set => SetThemeValue(ref _tabSelectedColor, value); }
        /// <summary>Color of the selected tab's outer-edge indicator. Defaults to <see cref="AccentColor"/>.</summary>
        public Color TabSelectedIndicatorColor { get => _tabSelectedIndicatorColor ?? Parent?.TabSelectedIndicatorColor ?? AccentColor; set => SetThemeValue(ref _tabSelectedIndicatorColor, value); }
        /// <summary>Thickness of the selected tab's outer-edge indicator.</summary>
        public float TabSelectedIndicatorHeight { get => _tabSelectedIndicatorHeight ?? Parent?.TabSelectedIndicatorHeight ?? 3; set => SetNonNegativeThemeValue(ref _tabSelectedIndicatorHeight, value); }
        /// <summary>Distance the selected tab projects beyond the normal tab strip.</summary>
        public float TabSelectedLift { get => _tabSelectedLift ?? Parent?.TabSelectedLift ?? 3; set => SetNonNegativeThemeValue(ref _tabSelectedLift, value); }
        public float Separation { get => _separation ?? Parent?.Separation ?? 4; set => SetThemeValue(ref _separation, value); }
        public float BorderWidth { get => _borderWidth ?? Parent?.BorderWidth ?? 1; set => SetThemeValue(ref _borderWidth, value); }
        /// <summary>Inherited default font family used by text controls without a local font override.</summary>
        public UIFontFamily FontFamily { get => _fontFamily ?? Parent?.FontFamily; set { if (ReferenceEquals(_fontFamily, value)) return; _fontFamily = value; OnChanged(); } }
        /// <summary>Inherited logical font size. Zero keeps the selected family's primary font size.</summary>
        public float FontSize { get => _fontSize ?? Parent?.FontSize ?? 0; set { if (value < 0 || !float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); if (_fontSize == value) return; _fontSize = value; OnChanged(); } }
        /// <summary>Inherited OpenType feature defaults used unless a control supplies explicit features.</summary>
        public IReadOnlyList<UIFontOpenTypeFeature> FontOpenTypeFeatures { get => _fontOpenTypeFeatures ?? Parent?.FontOpenTypeFeatures ?? Array.Empty<UIFontOpenTypeFeature>(); set { _fontOpenTypeFeatures = value == null ? null : new List<UIFontOpenTypeFeature>(value).AsReadOnly(); OnChanged(); } }
        /// <summary>Inherited dynamic glyph hinting policy.</summary>
        public UIFontHinting FontHinting { get => _fontHinting ?? Parent?.FontHinting ?? UIFontHinting.Default; set { if (!Enum.IsDefined(typeof(UIFontHinting), value)) throw new ArgumentOutOfRangeException(nameof(value)); if (_fontHinting == value) return; _fontHinting = value; OnChanged(); } }
        internal void SetInheritedParent(Theme parent) => SetParent(parent, false);
        private void SetParent(Theme value, bool notify)
        {
            if (ReferenceEquals(value, this)) throw new ArgumentException("A theme cannot inherit from itself.", nameof(value));
            for (var ancestor = value; ancestor != null; ancestor = ancestor.Parent)
                if (ReferenceEquals(ancestor, this)) throw new ArgumentException("Theme inheritance cannot contain a cycle.", nameof(value));
            if (ReferenceEquals(_parent, value)) return;
            if (_parent != null) _parent.Changed -= ParentChanged;
            _parent = value;
            if (_parent != null) _parent.Changed += ParentChanged;
            if (notify) OnChanged();
        }
        private void ParentChanged(object sender, EventArgs args) => OnChanged();
        private void OnChanged()
        {
            Version++;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        private void SetThemeValue<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnChanged();
        }
        private void SetNonNegativeThemeValue(ref float? field, float value)
        {
            if (value < 0 || !float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (field == value) return;
            field = value;
            OnChanged();
        }
        public void SetControlTemplate(Type controlType, ControlTemplate template)
        {
            ValidateTemplatedControlType(controlType);
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (!template.TargetType.IsAssignableFrom(controlType))
                throw new ArgumentException($"Template target type {template.TargetType.FullName} cannot be applied to {controlType.FullName}.", nameof(template));
            if (_controlTemplates.TryGetValue(controlType, out var current) && ReferenceEquals(current, template)) return;
            _controlTemplates[controlType] = template;
            OnChanged();
        }
        public void SetControlTemplate<TControl>(ControlTemplate template) where TControl : TemplatedControl =>
            SetControlTemplate(typeof(TControl), template);
        public bool RemoveControlTemplate(Type controlType)
        {
            ValidateTemplatedControlType(controlType);
            if (!_controlTemplates.Remove(controlType)) return false;
            OnChanged();
            return true;
        }
        public ControlTemplate GetControlTemplate(Type controlType)
        {
            ValidateTemplatedControlType(controlType);
            for (var current = controlType; current != null && typeof(TemplatedControl).IsAssignableFrom(current); current = current.BaseType)
                if (_controlTemplates.TryGetValue(current, out var template)) return template;
            return Parent?.GetControlTemplate(controlType);
        }
        /// <summary>Assigns a style item, optionally scoped to a control type name.</summary>
        public void SetStyleBox(string itemName, StyleBox styleBox, string typeName = null)
        {
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("A theme item name is required.", nameof(itemName));
            var key = MakeStyleKey(typeName, itemName);
            if (styleBox == null)
            {
                if (_styleBoxes.Remove(key)) OnChanged();
            }
            else if (!_styleBoxes.TryGetValue(key, out var current) || !ReferenceEquals(current, styleBox))
            {
                _styleBoxes[key] = styleBox;
                OnChanged();
            }
        }
        /// <summary>Gets a style item, first considering the requested control type then the shared item.</summary>
        public StyleBox GetStyleBox(string itemName, string typeName = null)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return null;
            if (!string.IsNullOrEmpty(typeName) && _styleBoxes.TryGetValue(MakeStyleKey(typeName, itemName), out var typed)) return typed;
            if (_styleBoxes.TryGetValue(MakeStyleKey(null, itemName), out var shared)) return shared;
            return Parent?.GetStyleBox(itemName, typeName);
        }
        internal StyleBox GetStyleBox(string itemName, IEnumerable<string> typeNames)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return null;
            if (typeNames != null)
                foreach (var typeName in typeNames)
                    if (!string.IsNullOrEmpty(typeName) && _styleBoxes.TryGetValue(MakeStyleKey(typeName, itemName), out var typed)) return typed;
            if (_styleBoxes.TryGetValue(MakeStyleKey(null, itemName), out var shared)) return shared;
            return Parent?.GetStyleBox(itemName, typeNames);
        }
        /// <summary>Assigns a non-owning icon value, optionally scoped to a control type name.</summary>
        public void SetIcon(string itemName, ThemeIcon icon, string typeName = null)
        {
            ValidateItemName(itemName);
            var key = MakeStyleKey(typeName, itemName);
            if (_icons.TryGetValue(key, out var current) && current == icon) return;
            _icons[key] = icon;
            OnChanged();
        }
        /// <summary>Removes a local icon entry so parent and default themes become visible again.</summary>
        public void RemoveIcon(string itemName, string typeName = null)
        {
            if (itemName != null && _icons.Remove(MakeStyleKey(typeName, itemName))) OnChanged();
        }
        /// <summary>Suppresses an inherited icon without supplying a replacement.</summary>
        public void SuppressIcon(string itemName, string typeName = null)
        {
            ValidateItemName(itemName);
            var key = MakeStyleKey(typeName, itemName);
            if (_icons.TryGetValue(key, out var current) && current == null) return;
            _icons[key] = null;
            OnChanged();
        }
        /// <summary>Gets a typed or shared icon, including parent-theme inheritance.</summary>
        public ThemeIcon? GetIcon(string itemName, string typeName = null)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return null;
            TryGetIcon(itemName, string.IsNullOrEmpty(typeName) ? null : new[] { typeName }, out var icon);
            return icon;
        }
        internal bool TryGetIcon(string itemName, IEnumerable<string> typeNames, out ThemeIcon? icon)
        {
            if (string.IsNullOrWhiteSpace(itemName)) { icon = null; return false; }
            if (typeNames != null)
                foreach (var typeName in typeNames)
                    if (!string.IsNullOrEmpty(typeName) && _icons.TryGetValue(MakeStyleKey(typeName, itemName), out icon)) return true;
            if (_icons.TryGetValue(MakeStyleKey(null, itemName), out icon)) return true;
            if (Parent != null) return Parent.TryGetIcon(itemName, typeNames, out icon);
            icon = null;
            return false;
        }
        private static void ValidateItemName(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("A theme item name is required.", nameof(itemName));
        }
        private static void ValidateTemplatedControlType(Type controlType)
        {
            if (controlType == null) throw new ArgumentNullException(nameof(controlType));
            if (!typeof(TemplatedControl).IsAssignableFrom(controlType))
                throw new ArgumentException("Control templates can only be resolved for TemplatedControl types.", nameof(controlType));
        }
        private static string MakeStyleKey(string typeName, string itemName) => (typeName ?? string.Empty) + ":" + itemName;
    }
}
