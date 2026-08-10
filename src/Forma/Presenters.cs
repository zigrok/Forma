// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace Forma
{
    public interface IItemsPresenterOwner
    {
        Control ItemsPresenterInheritanceParent { get; }
        void AttachItemsPanel(ItemsPresenter presenter, Container panel);
        void DetachItemsPanel(ItemsPresenter presenter, Container panel);
    }

    public interface IScrollViewportOwner
    {
        Vector2 ScrollOffset { get; set; }
        void OnScrollMetricsChanged(ScrollPresenter presenter, ScrollMetrics metrics);
        void BringIntoView(ScrollPresenter presenter, Control target, Rectangle targetBounds);
    }

    public interface IScrollIndexProvider
    {
        bool TryGetIndexBounds(int index, out Rectangle bounds);
    }

    public readonly struct ScrollMetrics : IEquatable<ScrollMetrics>
    {
        public ScrollMetrics(Vector2 viewport, Vector2 extent, Vector2 offset)
        {
            Viewport = Vector2.Max(Vector2.Zero, viewport);
            Extent = Vector2.Max(Vector2.Zero, extent);
            MaxOffset = Vector2.Max(Vector2.Zero, Extent - Viewport);
            Offset = Vector2.Min(Vector2.Max(Vector2.Zero, offset), MaxOffset);
        }

        public Vector2 Viewport { get; }
        public Vector2 Extent { get; }
        public Vector2 Offset { get; }
        public Vector2 MaxOffset { get; }

        public bool Equals(ScrollMetrics other) =>
            Viewport == other.Viewport && Extent == other.Extent && Offset == other.Offset;
        public override bool Equals(object value) => value is ScrollMetrics other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Viewport, Extent, Offset);
        public static bool operator ==(ScrollMetrics left, ScrollMetrics right) => left.Equals(right);
        public static bool operator !=(ScrollMetrics left, ScrollMetrics right) => !left.Equals(right);
    }

    /// <summary>Realizes a content object as a control or data template and aligns the resulting visual.</summary>
    public sealed class ContentPresenter : Control, IDisposable
    {
        private object _content;
        private DataTemplate _contentTemplate;
        private Control _presentedControl;
        private TemplateInstance _templateInstance;
        private HorizontalAlignment _horizontalContentAlignment = HorizontalAlignment.Fill;
        private VerticalAlignment _verticalContentAlignment = VerticalAlignment.Fill;
        private bool _hasHorizontalContentAlignment;
        private bool _hasVerticalContentAlignment;
        private bool _disposed;

        public object Content
        {
            get => _content;
            set => SetContent(value, false);
        }

        public DataTemplate ContentTemplate
        {
            get => _contentTemplate;
            set
            {
                ThrowIfDisposed();
                if (ReferenceEquals(_contentTemplate, value)) return;
                ReplaceContent(_content, value);
            }
        }

        public Control PresentedControl => _presentedControl;

        internal void SetGeneratedContent(object content) => SetContent(content, true);

        internal void SetContentAfterFailedReplacement(object content) => SetContent(content, false);

        internal bool CanRebindGeneratedContent(DataTemplate template) =>
            _templateInstance != null && !_templateInstance.IsActive && ReferenceEquals(_contentTemplate, template);

        internal bool TryDeactivateGeneratedContentForRecycle()
        {
            if (_templateInstance == null || !_templateInstance.IsActive) return false;
            var recyclingState = _templateInstance.Root as IDataTemplateRecyclingState;
            if (recyclingState == null &&
                (_templateInstance.Root is TemplatedControl || _templateInstance.Root.GetType().Assembly != typeof(Control).Assembly)) return false;
            _templateInstance.Deactivate();
            recyclingState?.OnRecycling();
            _content = null;
            return true;
        }

        internal void RebindGeneratedContentAfterRecycle(object content)
        {
            if (_templateInstance == null || _templateInstance.IsActive)
                throw new InvalidOperationException("Generated template content is not inactive and available for reuse.");
            _templateInstance.Rebind(content);
            (_templateInstance.Root as IDataTemplateRecyclingState)?.OnReused(content);
            _content = content;
            _templateInstance.Activate();
            QueueLayout();
        }

        public HorizontalAlignment HorizontalContentAlignment
        {
            get => _hasHorizontalContentAlignment ? _horizontalContentAlignment : HorizontalAlignment;
            set
            {
                if (_hasHorizontalContentAlignment && _horizontalContentAlignment == value) return;
                _horizontalContentAlignment = value;
                _hasHorizontalContentAlignment = true;
                QueueLayout();
            }
        }

        public VerticalAlignment VerticalContentAlignment
        {
            get => _hasVerticalContentAlignment ? _verticalContentAlignment : VerticalAlignment;
            set
            {
                if (_hasVerticalContentAlignment && _verticalContentAlignment == value) return;
                _verticalContentAlignment = value;
                _hasVerticalContentAlignment = true;
                QueueLayout();
            }
        }

        public override Vector2 GetMinimumSize() =>
            Vector2.Max(base.GetMinimumSize(), _presentedControl?.GetMinimumSize() ?? Vector2.Zero);

        protected override void ArrangeChildren()
        {
            if (_presentedControl == null) return;
            var desired = _presentedControl.GetMinimumSize();
            var width = HorizontalContentAlignment == HorizontalAlignment.Fill ? Size.X : Math.Min(Size.X, desired.X);
            var height = VerticalContentAlignment == VerticalAlignment.Fill ? Size.Y : Math.Min(Size.Y, desired.Y);
            var x = HorizontalContentAlignment == HorizontalAlignment.Center ? (Size.X - width) * .5f
                : HorizontalContentAlignment == HorizontalAlignment.Right ? Size.X - width : 0;
            var y = VerticalContentAlignment == VerticalAlignment.Center ? (Size.Y - height) * .5f
                : VerticalContentAlignment == VerticalAlignment.Bottom ? Size.Y - height : 0;
            _presentedControl.Position = new Vector2(x, y);
            _presentedControl.Size = new Vector2(width, height);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearPresentedContent();
            _content = null;
            _contentTemplate = null;
        }

        private void SetContent(object content, bool forceNullTemplate)
        {
            ThrowIfDisposed();
            if (!forceNullTemplate && ReferenceEquals(_content, content)) return;
            ReplaceContent(content, _contentTemplate, forceNullTemplate);
        }

        private void ReplaceContent(object content, DataTemplate contentTemplate, bool forceNullTemplate = false)
        {
            TemplateInstance candidateInstance = null;
            Control candidate = null;
            if (contentTemplate != null && (content != null || forceNullTemplate))
            {
                candidateInstance = contentTemplate.CreateInstance(content, this);
                candidate = candidateInstance.Root;
            }
            else if (content is Control control)
            {
                ValidateProjectedControl(control);
                candidate = control;
            }
            else if (content != null)
            {
                candidate = new TextBlock
                {
                    Text = Convert.ToString(content, CultureInfo.InvariantCulture) ?? string.Empty,
                    MouseFilter = MouseFilter.Ignore,
                };
            }

            var previous = _presentedControl;
            var previousInstance = _templateInstance;
            try
            {
                DetachPresentation(previous, previousInstance);
                if (candidate != null)
                {
                    if (candidate.Parent != null) ProjectVisualChild(candidate);
                    else AddVisualChild(candidate);
                    candidateInstance?.Activate();
                }
            }
            catch
            {
                CleanupCandidate(candidate, candidateInstance);
                try { RestorePresentation(previous, previousInstance); } catch { }
                throw;
            }

            _content = content;
            _contentTemplate = contentTemplate;
            _presentedControl = candidate;
            _templateInstance = candidateInstance;
            DisposePresentation(previous, previousInstance);
            QueueLayout();
        }

        private void ValidateProjectedControl(Control control)
        {
            if (control.Parent == null)
                throw new InvalidOperationException("Control content must retain a logical owner while projected by a ContentPresenter.");
            if (control.VisualParent != null && control.VisualParent != control.Parent && control.VisualParent != this)
                throw new InvalidOperationException("Control content is already projected into another visual host.");
        }

        private void DetachPresentation(Control presented, TemplateInstance instance)
        {
            if (presented == null) return;
            instance?.Deactivate();
            if (presented.VisualParent == this) RemoveVisualChild(presented);
        }

        private void RestorePresentation(Control presented, TemplateInstance instance)
        {
            if (presented == null) return;
            if (presented.VisualParent == null)
            {
                if (presented.Parent != null) ProjectVisualChild(presented);
                else AddVisualChild(presented);
            }
            if (instance != null && !instance.IsActive) instance.Activate();
        }

        private void CleanupCandidate(Control candidate, TemplateInstance instance)
        {
            if (instance != null)
            {
                try { instance.Dispose(); } catch { }
                return;
            }
            if (candidate?.VisualParent == this)
            {
                try { RemoveVisualChild(candidate); } catch { }
            }
            if (candidate?.Parent == null && candidate is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
        }

        private static void DisposePresentation(Control presented, TemplateInstance instance)
        {
            if (instance != null)
            {
                instance.Dispose();
                return;
            }
            if (presented?.Parent == null && presented is IDisposable disposable) disposable.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ContentPresenter));
        }

        private void ClearPresentedContent()
        {
            var presented = _presentedControl;
            var instance = _templateInstance;
            _presentedControl = null;
            _templateInstance = null;
            if (presented != null)
            {
                if (presented.VisualParent == this) RemoveVisualChild(presented);
                if (instance != null)
                {
                    instance.Dispose();
                }
                else if (presented.Parent == null && presented is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            QueueLayout();
        }
    }

    internal class BaseButtonPresenter : Container
    {
        private readonly BaseButton _owner;

        internal BaseButtonPresenter(BaseButton owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            ContentPresenter = new ContentPresenter
            {
                Name = ContentControl.ContentPresenterPartName,
                Content = owner.Content,
                ContentTemplate = owner.ContentTemplate,
                HorizontalContentAlignment = owner.HorizontalContentAlignment,
                VerticalContentAlignment = owner.VerticalContentAlignment,
                IsHitTestVisible = false,
            };
            AddChild(ContentPresenter);
        }

        internal ContentPresenter ContentPresenter { get; }

        public override Vector2 GetMinimumSize() =>
            Vector2.Max(CustomMinimumSize, ContentPresenter.GetMinimumSize());

        protected override void ArrangeChildren()
        {
            ContentPresenter.Position = Vector2.Zero;
            ContentPresenter.Size = Size;
        }

        internal override void Draw(UIRenderContext context)
        {
            var visuallyPressed = _owner.IsVisuallyPressed;
            var styleName = !_owner.Enabled ? "disabled" : visuallyPressed ? "pressed" : _owner.IsHovering ? "hover" : "normal";
            var style = _owner.GetThemeStyleBox(styleName);
            if (style != null) style.Draw(context, Bounds);
            else if (_owner.Flat)
            {
                if (_owner.Context?.FocusedControl == _owner) context.Border(Bounds, context.Theme.FocusColor);
            }
            else
            {
                var color = visuallyPressed ? context.Theme.PressedColor : _owner.IsHovering ? context.Theme.HoverColor : context.Theme.PanelColor;
                context.Fill(Bounds, color);
                context.Border(Bounds, _owner.Context?.FocusedControl == _owner ? context.Theme.FocusColor : context.Theme.PanelBorderColor);
            }
            var decorativeIcon = _owner.Icon == null ? _owner.DecorativeIconProvider?.Invoke() : null;
            if (_owner.Icon != null)
            {
                var icon = _owner.GetIconRectangle(new Vector2(_owner.Icon.Width, _owner.Icon.Height));
                if (icon.Width > 0 && icon.Height > 0)
                    context.SpriteBatch.Draw(_owner.Icon, new Rectangle(Bounds.X + icon.X, Bounds.Y + icon.Y, icon.Width, icon.Height), _owner.Enabled ? _owner.IconModulate : context.Theme.DisabledTextColor);
            }
            else if (decorativeIcon.HasValue)
            {
                var icon = _owner.GetIconRectangle(decorativeIcon.Value.LogicalSize.ToVector2());
                if (icon.Width > 0 && icon.Height > 0)
                    context.Icon(decorativeIcon.Value, new Rectangle(Bounds.X + icon.X, Bounds.Y + icon.Y, icon.Width, icon.Height), _owner.Enabled ? _owner.IconModulate : context.Theme.DisabledTextColor);
            }
            if (_owner.EffectiveUIFont != null && !string.IsNullOrEmpty(_owner.Text) && (!decorativeIcon.HasValue || !_owner.HideTextWhenDecorativeIconAvailable))
            {
                var textSize = TextMetrics.Measure(_owner.EffectiveUIFont, _owner.Text);
                var iconSize = _owner.Icon != null
                    ? new Vector2(_owner.Icon.Width, _owner.Icon.Height)
                    : decorativeIcon?.LogicalSize.ToVector2() ?? Vector2.Zero;
                var position = _owner.GlobalPosition + _owner.GetTextPosition(textSize, iconSize);
                context.Text(_owner.EffectiveUIFont, _owner.Text, position, _owner.Enabled ? context.Theme.TextColor : context.Theme.DisabledTextColor);
            }
            base.Draw(context);
        }
    }

    internal sealed class LinkButtonPresenter : Container
    {
        private readonly LinkButton _owner;
        private readonly BaseButtonPresenter _buttonPresenter;

        internal LinkButtonPresenter(LinkButton owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            _buttonPresenter = new BaseButtonPresenter(owner);
            AddChild(_buttonPresenter);
        }

        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, _buttonPresenter.GetMinimumSize());

        protected override void ArrangeChildren()
        {
            _buttonPresenter.Position = Vector2.Zero;
            _buttonPresenter.Size = Size;
        }

        internal override void Draw(UIRenderContext context)
        {
            base.Draw(context);
            var underline = _owner.UnderlineMode == LinkButtonUnderlineMode.Always ||
                (_owner.UnderlineMode == LinkButtonUnderlineMode.OnHover && (_owner.IsHovering || _owner.IsPressing));
            if (!underline || _owner.EffectiveUIFont == null || string.IsNullOrEmpty(_owner.Text)) return;
            var width = (int)TextMetrics.Measure(_owner.EffectiveUIFont, _owner.Text).X;
            var x = Bounds.X + Math.Max((int)_owner.Padding.Left, (Bounds.Width - width) / 2);
            context.Fill(new Rectangle(x, Bounds.Bottom - Math.Max(2, (int)_owner.Padding.Bottom), Math.Max(0, width), 1), context.Theme.AccentColor);
        }
    }

    internal sealed class TextureButtonPresenter : Container
    {
        private readonly TextureButton _owner;
        private readonly BaseButtonPresenter _buttonPresenter;

        internal TextureButtonPresenter(TextureButton owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            _buttonPresenter = new BaseButtonPresenter(owner);
            AddChild(_buttonPresenter);
        }

        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, _buttonPresenter.GetMinimumSize());

        protected override void ArrangeChildren()
        {
            _buttonPresenter.Position = Vector2.Zero;
            _buttonPresenter.Size = Size;
        }

        internal override void Draw(UIRenderContext context)
        {
            var texture = _owner.GetCurrentTexture();
            if (texture != null)
            {
                _owner.DrawTemplateTexture(context, texture, _owner.GetTextureLayout(new Vector2(texture.Width, texture.Height)));
                _buttonPresenter.ContentPresenter.DrawTree(context);
            }
            else
            {
                _buttonPresenter.DrawTree(context);
            }
            if (_owner.Context?.FocusedControl != _owner) return;
            var focusLayout = _owner.GetFocusOverlayLayout();
            if (focusLayout.HasValue) _owner.DrawTemplateTexture(context, _owner.TextureFocused, focusLayout.Value);
        }
    }

    internal sealed class ColorButtonPresenter : Container
    {
        private readonly BaseButtonPresenter _buttonPresenter;
        private readonly Func<Color> _getColor;
        private readonly int _inset;

        internal ColorButtonPresenter(BaseButton owner, Func<Color> getColor, int inset)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _getColor = getColor ?? throw new ArgumentNullException(nameof(getColor));
            _inset = Math.Max(0, inset);
            MouseFilter = MouseFilter.Pass;
            _buttonPresenter = new BaseButtonPresenter(owner);
            AddChild(_buttonPresenter);
        }

        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, _buttonPresenter.GetMinimumSize());

        protected override void ArrangeChildren()
        {
            _buttonPresenter.Position = Vector2.Zero;
            _buttonPresenter.Size = Size;
        }

        internal override void Draw(UIRenderContext context)
        {
            base.Draw(context);
            context.Fill(
                new Rectangle(Bounds.X + _inset, Bounds.Y + _inset, Math.Max(0, Bounds.Width - _inset * 2), Math.Max(0, Bounds.Height - _inset * 2)),
                _getColor());
        }
    }

    internal sealed class CheckBoxPresenter : Container
    {
        private readonly CheckBox _owner;
        private readonly BaseButtonPresenter _buttonPresenter;

        internal CheckBoxPresenter(CheckBox owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            _buttonPresenter = new BaseButtonPresenter(owner);
            AddChild(_buttonPresenter);
        }

        public override Vector2 GetMinimumSize() =>
            Vector2.Max(CustomMinimumSize, _buttonPresenter.GetMinimumSize());

        protected override void ArrangeChildren()
        {
            _buttonPresenter.Position = Vector2.Zero;
            _buttonPresenter.Size = Size;
        }

        internal override void Draw(UIRenderContext context)
        {
            var icon = _owner.GetStateIcon();
            var originalPadding = _owner.Padding;
            if (icon.HasValue)
            {
                var reserve = icon.Value.LogicalSize.X + _owner.IconSeparation;
                _owner.Padding = _owner.IsLayoutRtl()
                    ? new Thickness(originalPadding.Left, originalPadding.Top, originalPadding.Right + reserve, originalPadding.Bottom)
                    : new Thickness(originalPadding.Left + reserve, originalPadding.Top, originalPadding.Right, originalPadding.Bottom);
            }
            base.Draw(context);
            _owner.Padding = originalPadding;
            if (!icon.HasValue) return;
            var x = _owner.IsLayoutRtl() ? Bounds.Right - (int)originalPadding.Right - icon.Value.LogicalSize.X : Bounds.X + (int)originalPadding.Left;
            var y = Bounds.Center.Y - icon.Value.LogicalSize.Y / 2;
            context.Icon(icon.Value, new Vector2(x, y), Color.White);
        }
    }

    internal sealed class OptionButtonPresenter : Container
    {
        private readonly OptionButton _owner;
        private readonly BaseButtonPresenter _buttonPresenter;

        internal OptionButtonPresenter(OptionButton owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            _buttonPresenter = new BaseButtonPresenter(owner);
            AddChild(_buttonPresenter);
        }

        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, _buttonPresenter.GetMinimumSize());

        protected override void ArrangeChildren()
        {
            _buttonPresenter.Position = Vector2.Zero;
            _buttonPresenter.Size = Size;
        }

        internal override void Draw(UIRenderContext context)
        {
            var arrow = _owner.GetThemeIcon("arrow");
            var originalPadding = _owner.Padding;
            if (arrow.HasValue)
            {
                var reserve = arrow.Value.LogicalSize.X + _owner.IconSeparation;
                _owner.Padding = _owner.IsLayoutRtl()
                    ? new Thickness(originalPadding.Left + reserve, originalPadding.Top, originalPadding.Right, originalPadding.Bottom)
                    : new Thickness(originalPadding.Left, originalPadding.Top, originalPadding.Right + reserve, originalPadding.Bottom);
            }
            base.Draw(context);
            _owner.Padding = originalPadding;
            if (!arrow.HasValue) return;
            var x = _owner.IsLayoutRtl() ? Bounds.X + (int)originalPadding.Left : Bounds.Right - (int)originalPadding.Right - arrow.Value.LogicalSize.X;
            var y = Bounds.Center.Y - arrow.Value.LogicalSize.Y / 2;
            context.Icon(arrow.Value, new Vector2(x, y), _owner.Enabled ? Color.White : context.Theme.DisabledTextColor);
        }
    }

    internal sealed class TabBarPresenter : Control
    {
        private readonly TabBar _owner;
        internal TabBarPresenter(TabBar owner) { _owner = owner ?? throw new ArgumentNullException(nameof(owner)); MouseFilter = MouseFilter.Pass; }
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, _owner.CustomMinimumSize);
        internal override void Draw(UIRenderContext context) => _owner.DrawTabStrip(context);
    }

    internal sealed class TabContainerPresenter : Control
    {
        private readonly TabContainer _owner;
        internal TabContainerPresenter(TabContainer owner) { _owner = owner ?? throw new ArgumentNullException(nameof(owner)); MouseFilter = MouseFilter.Ignore; }
        internal override void Draw(UIRenderContext context) => _owner.DrawTabContainerChrome(context);
    }

    internal abstract class DataGridContentPresenter<TOwner> : Container where TOwner : ContentControl
    {
        protected DataGridContentPresenter(TOwner owner)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            ContentPresenter = new ContentPresenter
            {
                Name = ContentControl.ContentPresenterPartName,
                Content = owner.Content,
                ContentTemplate = owner.ContentTemplate,
                HorizontalContentAlignment = owner.HorizontalContentAlignment,
                VerticalContentAlignment = owner.VerticalContentAlignment,
            };
            AddChild(ContentPresenter);
        }

        protected TOwner Owner { get; }
        protected ContentPresenter ContentPresenter { get; }
        protected abstract Thickness ContentPadding { get; }

        public override Vector2 GetMinimumSize()
        {
            var padding = ContentPadding;
            return Vector2.Max(CustomMinimumSize, ContentPresenter.GetMinimumSize() + new Vector2(padding.Horizontal, padding.Vertical));
        }

        protected override void ArrangeChildren()
        {
            var padding = ContentPadding;
            ContentPresenter.Position = new Vector2(padding.Left, padding.Top);
            ContentPresenter.Size = new Vector2(
                Math.Max(0, Size.X - padding.Horizontal),
                Math.Max(0, Size.Y - padding.Vertical));
        }
    }

    internal sealed class DataGridColumnHeaderPresenter : DataGridContentPresenter<DataGridColumnHeader>
    {
        internal DataGridColumnHeaderPresenter(DataGridColumnHeader owner) : base(owner) { }
        protected override Thickness ContentPadding => Owner.Padding;

        internal override void Draw(UIRenderContext context)
        {
            var grid = Owner.Owner;
            var hovered = Owner.IsEffectivelyEnabled && Bounds.Contains(Owner.Context?.PointerPosition ?? Point.Zero);
            context.Fill(Bounds, hovered ? context.Theme.HoverColor : context.Theme.PanelColor);
            base.Draw(context);
            if (grid?.ShowVerticalGridLines != false)
                context.Fill(new Rectangle(Bounds.Right - 1, Bounds.Y, 1, Bounds.Height), context.Theme.PanelBorderColor.WithAlpha(150));
            context.Fill(new Rectangle(Bounds.X, Bounds.Bottom - 2, Bounds.Width, 2), context.Theme.PanelBorderColor);
            DrawSortIndicator(context, hovered);
        }

        private void DrawSortIndicator(UIRenderContext context, bool hovered)
        {
            var sortable = Owner.Owner?.CanUserSortColumns == true && Owner.Column?.CanUserSort == true && Owner.Column.SortBinding != null;
            if (!sortable || (!Owner.SortDirection.HasValue && !hovered)) return;
            var color = Owner.SortDirection.HasValue ? context.Theme.AccentColor : context.Theme.DisabledTextColor.WithAlpha(150);
            var x = Bounds.Right - 17;
            var y = Bounds.Center.Y - 3;
            var descending = Owner.SortDirection == DataGridSortDirection.Descending;
            for (var row = 0; row < 3; row++)
            {
                var width = descending ? 6 - row * 2 : 2 + row * 2;
                context.Fill(new Rectangle(x + (6 - width) / 2, y + row * 2, width, 1), color);
            }
        }
    }

    internal sealed class DataGridRowPresenter : DataGridContentPresenter<DataGridRow>
    {
        internal DataGridRowPresenter(DataGridRow owner) : base(owner) { }
        protected override Thickness ContentPadding => default;
        public override Vector2 GetMinimumSize() => Vector2.Max(base.GetMinimumSize(), new Vector2(0, DataGrid.DefaultEstimatedRowExtent));

        internal override void Draw(UIRenderContext context)
        {
            var grid = Owner.Owner;
            if (Owner.IsSelected)
                context.Fill(Bounds, context.Theme.AccentColor.WithAlpha(70));
            else if (Bounds.Contains(Owner.Context?.PointerPosition ?? Point.Zero))
                context.Fill(Bounds, context.Theme.HoverColor.WithAlpha(105));
            else if (grid?.AlternatingRowBackground == true && Owner.RowIndex % 2 != 0)
                context.Fill(Bounds, context.Theme.PanelColor.WithAlpha(72));
            base.Draw(context);
            if (grid?.ShowHorizontalGridLines != false)
                context.Fill(new Rectangle(Bounds.X, Bounds.Bottom - 1, Bounds.Width, 1), context.Theme.PanelBorderColor.WithAlpha(105));
            if (Owner.IsCurrent)
                context.Fill(new Rectangle(Bounds.X, Bounds.Y, 2, Bounds.Height), context.Theme.FocusColor);
        }
    }

    internal sealed class DataGridCellPresenter : DataGridContentPresenter<DataGridCell>
    {
        internal DataGridCellPresenter(DataGridCell owner) : base(owner) { }
        protected override Thickness ContentPadding => Owner.Padding;

        internal override void Draw(UIRenderContext context)
        {
            if (Owner.IsSelected)
                context.Fill(Bounds, context.Theme.AccentColor.WithAlpha(85));
            else if (Bounds.Contains(Owner.Context?.PointerPosition ?? Point.Zero))
                context.Fill(Bounds, context.Theme.HoverColor.WithAlpha(80));
            base.Draw(context);
            if (Owner.Grid?.ShowVerticalGridLines != false)
                context.Fill(new Rectangle(Bounds.Right - 1, Bounds.Y, 1, Bounds.Height), context.Theme.PanelBorderColor.WithAlpha(90));
            if (Owner.IsCurrent)
                context.Border(Bounds, context.Theme.FocusColor);
        }
    }

    internal sealed class SliderPresenter : Control
    {
        private readonly Slider _owner;

        internal SliderPresenter(Slider owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
        }

        internal override void Draw(UIRenderContext context)
        {
            var rect = Bounds;
            var track = _owner.Orientation == Orientation.Horizontal
                ? new Rectangle(rect.X, rect.Center.Y - 2, rect.Width, 4)
                : new Rectangle(rect.Center.X - 2, rect.Y, 4, rect.Height);
            context.Fill(track, context.Theme.PanelBorderColor);
            var grabber = _owner.GetSliderThemeIcon(!_owner.Enabled || !_owner.Editable ? "grabber_disabled" : _owner.IsGrabberHighlighted ? "grabber_highlight" : "grabber");
            if (grabber.HasValue)
            {
                var mainLength = _owner.Orientation == Orientation.Horizontal ? rect.Width - grabber.Value.LogicalSize.X : rect.Height - grabber.Value.LogicalSize.Y;
                var ratio = _owner.Orientation == Orientation.Horizontal && _owner.IsLayoutRtl() ? 1 - _owner.Ratio : _owner.Orientation == Orientation.Vertical ? 1 - _owner.Ratio : _owner.Ratio;
                var x = _owner.Orientation == Orientation.Horizontal ? rect.X + (int)MathF.Round(ratio * Math.Max(0, mainLength)) : rect.Center.X - grabber.Value.LogicalSize.X / 2;
                var y = _owner.Orientation == Orientation.Vertical ? rect.Y + (int)MathF.Round(ratio * Math.Max(0, mainLength)) : rect.Center.Y - grabber.Value.LogicalSize.Y / 2;
                context.Icon(grabber.Value, new Vector2(x, y), Color.White);
            }
            var tickIcon = _owner.GetSliderThemeIcon("tick");
            if (tickIcon.HasValue)
                foreach (var tick in _owner.GetTickRectangles())
                    context.Icon(tickIcon.Value, new Vector2(rect.X + tick.Center.X - tickIcon.Value.LogicalSize.X / 2, rect.Y + tick.Center.Y - tickIcon.Value.LogicalSize.Y / 2), Color.White);
            base.Draw(context);
        }
    }

    internal sealed class ProgressBarPresenter : Control
    {
        private readonly ProgressBar _owner;

        internal ProgressBarPresenter(ProgressBar owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
        }

        internal override void Draw(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor);
            context.Border(Bounds, context.Theme.PanelBorderColor);
            if (_owner.Indeterminate)
            {
                var segment = Math.Max(1, (int)MathF.Round(Math.Min(Bounds.Width, Bounds.Height) * 2));
                Rectangle fill;
                switch (_owner.FillMode)
                {
                    case ProgressBarFillMode.EndToBegin: fill = new Rectangle(Bounds.Right - (int)_owner.IndeterminateOffset, Bounds.Y + 1, segment, Math.Max(0, Bounds.Height - 2)); break;
                    case ProgressBarFillMode.TopToBottom: fill = new Rectangle(Bounds.X + 1, Bounds.Y + (int)_owner.IndeterminateOffset - segment, Math.Max(0, Bounds.Width - 2), segment); break;
                    case ProgressBarFillMode.BottomToTop: fill = new Rectangle(Bounds.X + 1, Bounds.Bottom - (int)_owner.IndeterminateOffset, Math.Max(0, Bounds.Width - 2), segment); break;
                    default: fill = new Rectangle(Bounds.X + (int)_owner.IndeterminateOffset - segment, Bounds.Y + 1, segment, Math.Max(0, Bounds.Height - 2)); break;
                }
                context.Fill(Rectangle.Intersect(Bounds, fill), context.Theme.AccentColor);
            }
            else
            {
                var fill = _owner.GetFillRectangle(_owner.Ratio);
                context.Fill(new Rectangle(Bounds.X + fill.X, Bounds.Y + fill.Y, fill.Width, fill.Height), context.Theme.AccentColor);
            }
            if (!_owner.Indeterminate && _owner.ShowPercentage && _owner.EffectiveUIFont != null)
            {
                var text = $"{(int)(_owner.Ratio * 100)}%";
                var measure = TextMetrics.Measure(_owner.EffectiveUIFont, text);
                context.Text(_owner.EffectiveUIFont, text, _owner.GlobalPosition + (_owner.Size - measure) / 2, context.Theme.TextColor);
            }
            base.Draw(context);
        }
    }

    internal sealed class ScrollBarPresenter : Control
    {
        private readonly ScrollBar _owner;

        internal ScrollBarPresenter(ScrollBar owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
        }

        internal override void Draw(UIRenderContext context)
        {
            context.Fill(Bounds, context.Theme.BackgroundColor);
            var decrement = _owner.GetDecrementButtonRectangle();
            var increment = _owner.GetIncrementButtonRectangle();
            context.Fill(ToGlobal(decrement), _owner.IsDecrementActive ? context.Theme.AccentColor : _owner.IsDecrementHighlighted ? context.Theme.HoverColor : context.Theme.PanelColor);
            context.Fill(ToGlobal(increment), _owner.IsIncrementActive ? context.Theme.AccentColor : _owner.IsIncrementHighlighted ? context.Theme.HoverColor : context.Theme.PanelColor);
            var grabber = ToGlobal(_owner.GetGrabberRectangle());
            context.Fill(grabber, _owner.IsDraggingGrabber ? context.Theme.AccentColor : _owner.IsRangeHighlighted ? context.Theme.HoverColor : context.Theme.PanelBorderColor);
            context.Border(grabber, context.Theme.FocusColor);
            base.Draw(context);
        }

        private Rectangle ToGlobal(Rectangle local) =>
            new Rectangle(Bounds.X + local.X, Bounds.Y + local.Y, local.Width, local.Height);
    }

    internal sealed class TextureProgressBarPresenter : Control
    {
        private readonly TextureProgressBar _owner;

        internal TextureProgressBarPresenter(TextureProgressBar owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
        }

        internal override void Draw(UIRenderContext context)
        {
            _owner.DrawTemplate(context);
            base.Draw(context);
        }
    }

    internal sealed class SplitContainerPresenter : Container, IDisposable
    {
        private readonly SplitContainer _owner;
        private readonly List<ContentPresenter> _presenters = new List<ContentPresenter>();
        private bool _disposed;

        internal SplitContainerPresenter(SplitContainer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            SyncChildren();
        }

        internal void SyncChildren()
        {
            if (_disposed) return;
            var unchanged = _presenters.Count == _owner.Children.Count;
            for (var index = 0; unchanged && index < _presenters.Count; index++)
                unchanged = ReferenceEquals(_presenters[index].Content, _owner.Children[index]);
            if (unchanged) return;

            for (var index = _presenters.Count - 1; index >= 0; index--)
            {
                var presenter = _presenters[index];
                RemoveChild(presenter);
                presenter.Dispose();
            }
            _presenters.Clear();
            foreach (var child in _owner.Children)
            {
                var presenter = new ContentPresenter
                {
                    Content = child,
                    HorizontalContentAlignment = HorizontalAlignment.Fill,
                    VerticalContentAlignment = VerticalAlignment.Fill,
                };
                _presenters.Add(presenter);
                AddChild(presenter);
            }
            QueueLayout();
        }

        public override Vector2 GetMinimumSize()
        {
            if (_presenters.Count == 0) return CustomMinimumSize;
            var main = Math.Max(0, _owner.DragAreaSize) * Math.Max(0, _presenters.Count - 1);
            var cross = 0f;
            foreach (var presenter in _presenters)
            {
                var minimum = presenter.GetMinimumSize();
                main += _owner.Orientation == Orientation.Horizontal ? minimum.X : minimum.Y;
                cross = Math.Max(cross, _owner.Orientation == Orientation.Horizontal ? minimum.Y : minimum.X);
            }
            var size = _owner.Orientation == Orientation.Horizontal ? new Vector2(main, cross) : new Vector2(cross, main);
            return Vector2.Max(CustomMinimumSize, size);
        }

        protected override void ArrangeChildren() => _owner.ArrangePresentedChildren(_presenters, Size);

        internal override void Draw(UIRenderContext context)
        {
            if (_owner.DraggerVisibility == SplitContainerDraggerVisibility.Visible || (!_owner.Collapsed && _owner.DraggerVisibility != SplitContainerDraggerVisibility.Hidden))
                for (var index = 0; index < _owner.ResolvedDraggerCount; index++)
                {
                    var bounds = _owner.TouchDraggerEnabled ? _owner.GetTouchDraggerBounds(index) : _owner.GetDividerBounds(index);
                    var name = _owner.Orientation == Orientation.Horizontal
                        ? _owner.TouchDraggerEnabled ? "h_touch_dragger" : "h_grabber"
                        : _owner.TouchDraggerEnabled ? "v_touch_dragger" : "v_grabber";
                    var grabber = _owner.GetThemeIcon(name);
                    if (grabber.HasValue)
                        context.Icon(grabber.Value, new Vector2(bounds.Center.X - grabber.Value.LogicalSize.X / 2, bounds.Center.Y - grabber.Value.LogicalSize.Y / 2), Color.White);
                    else
                    {
                        context.Fill(_owner.GetDividerBounds(index), context.Theme.PanelBorderColor);
                        if (_owner.TouchDraggerEnabled) context.Fill(_owner.GetTouchDraggerBounds(index), context.Theme.AccentColor);
                    }
                }
            base.Draw(context);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var index = _presenters.Count - 1; index >= 0; index--) _presenters[index].Dispose();
            _presenters.Clear();
        }
    }

    internal sealed class FoldableContainerPresenter : Container, IDisposable
    {
        private readonly FoldableContainer _owner;
        private readonly List<ContentPresenter> _presenters = new List<ContentPresenter>();
        private bool _disposed;

        internal FoldableContainerPresenter(FoldableContainer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            SyncChildren();
        }

        internal void SyncChildren()
        {
            if (_disposed) return;
            var unchanged = _presenters.Count == _owner.Children.Count;
            for (var index = 0; unchanged && index < _presenters.Count; index++)
                unchanged = ReferenceEquals(_presenters[index].Content, _owner.Children[index]);
            if (unchanged) return;
            for (var index = _presenters.Count - 1; index >= 0; index--)
            {
                var presenter = _presenters[index];
                RemoveChild(presenter);
                presenter.Dispose();
            }
            _presenters.Clear();
            foreach (var child in _owner.Children)
            {
                var presenter = new ContentPresenter
                {
                    Content = child,
                    HorizontalContentAlignment = HorizontalAlignment.Fill,
                    VerticalContentAlignment = VerticalAlignment.Fill,
                };
                _presenters.Add(presenter);
                AddChild(presenter);
            }
            QueueLayout();
        }

        public override Vector2 GetMinimumSize()
        {
            var size = new Vector2(CustomMinimumSize.X, Math.Max(CustomMinimumSize.Y, _owner.HeaderHeight));
            if (_owner.Folded) return size;
            foreach (var presenter in _presenters)
            {
                var minimum = presenter.GetMinimumSize();
                size.X = Math.Max(size.X, minimum.X);
                size.Y += minimum.Y;
            }
            return size;
        }

        protected override void ArrangeChildren() => _owner.ArrangePresentedChildren(_presenters, Size);

        internal override void Draw(UIRenderContext context)
        {
            var header = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, (int)_owner.HeaderHeight);
            context.Fill(header, context.Theme.PanelColor);
            context.Border(header, context.Theme.PanelBorderColor);
            var arrow = _owner.GetThemeIcon(_owner.GetArrowIconName());
            if (arrow.HasValue)
                context.Icon(arrow.Value, new Vector2(Bounds.X + 6, Bounds.Y + (_owner.HeaderHeight - arrow.Value.LogicalSize.Y) / 2), Color.White);
            else
                context.Fill(new Rectangle(Bounds.X + 6, Bounds.Y + (int)_owner.HeaderHeight / 2 - 3, _owner.Folded ? 6 : 3, _owner.Folded ? 3 : 6), context.Theme.AccentColor);
            base.Draw(context);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var index = _presenters.Count - 1; index >= 0; index--) _presenters[index].Dispose();
            _presenters.Clear();
        }
    }

    internal sealed class SubViewportPresenter : Control
    {
        private readonly SubViewportContainer _owner;

        internal SubViewportPresenter(SubViewportContainer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
        }

        internal override void Draw(UIRenderContext context)
        {
            _owner.RenderViewport(context);
            base.Draw(context);
        }
    }

    internal sealed class VirtualJoystickPresenter : Control
    {
        private readonly VirtualJoystick _owner;

        internal VirtualJoystickPresenter(VirtualJoystick owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
        }

        internal override void Draw(UIRenderContext context)
        {
            var rect = Bounds;
            context.Fill(rect, (_owner.BackgroundColor ?? context.Theme.PanelColor).WithAlpha(150));
            context.Border(rect, _owner.BorderColor ?? context.Theme.PanelBorderColor);
            var radius = Math.Max(4, Math.Min(rect.Width, rect.Height) / 5);
            var center = new Vector2(rect.Center.X, rect.Center.Y) + _owner.Value * (Math.Min(rect.Width, rect.Height) / 2 - radius);
            context.Fill(new Rectangle((int)center.X - radius, (int)center.Y - radius, radius * 2, radius * 2), _owner.KnobColor ?? context.Theme.AccentColor);
            base.Draw(context);
        }
    }

    /// <summary>Instantiates an owner's items panel template and attaches the generated panel for item realization.</summary>
    public sealed class ItemsPresenter : Control, IDisposable
    {
        private IItemsPresenterOwner _owner;
        private ItemsPanelTemplate _itemsPanel;
        private TemplateInstance _panelInstance;
        private Container _panel;
        private bool _disposed;

        public IItemsPresenterOwner Owner
        {
            get => _owner;
            set
            {
                ThrowIfDisposed();
                if (ReferenceEquals(_owner, value)) return;
                ReplacePanel(value, _itemsPanel);
            }
        }

        public ItemsPanelTemplate ItemsPanel
        {
            get => _itemsPanel;
            set
            {
                ThrowIfDisposed();
                if (ReferenceEquals(_itemsPanel, value)) return;
                ReplacePanel(_owner, value);
            }
        }

        public Container Panel => _panel;

        public override Vector2 GetMinimumSize() =>
            Vector2.Max(base.GetMinimumSize(), _panel?.GetMinimumSize() ?? Vector2.Zero);

        protected override void ArrangeChildren()
        {
            if (_panel == null) return;
            _panel.Position = Vector2.Zero;
            _panel.Size = Size;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearPanel();
            _owner = null;
            _itemsPanel = null;
        }

        private void ReplacePanel(IItemsPresenterOwner owner, ItemsPanelTemplate itemsPanel)
        {
            TemplateInstance candidateInstance = null;
            Container candidate = null;
            if (owner != null && itemsPanel != null)
            {
                candidateInstance = itemsPanel.CreateInstance(this);
                candidate = (Container)candidateInstance.Root;
            }

            var previousOwner = _owner;
            var previousPanel = _panel;
            var previousInstance = _panelInstance;
            var previousDetached = false;
            var candidateAttached = false;
            try
            {
                if (previousPanel != null)
                {
                    if (!previousInstance.IsDisposed) previousInstance.Deactivate();
                    previousDetached = RemoveVisualChild(previousPanel);
                    previousOwner.DetachItemsPanel(this, previousPanel);
                }
                if (candidate != null)
                {
                    AddVisualChild(candidate, owner.ItemsPresenterInheritanceParent ?? throw new InvalidOperationException("An items presenter owner must provide an inheritance parent."));
                    candidateAttached = true;
                    owner.AttachItemsPanel(this, candidate);
                    candidateInstance.Activate();
                }
            }
            catch
            {
                if (candidateAttached)
                {
                    if (candidate.VisualParent == this)
                    {
                        try { RemoveVisualChild(candidate); } catch { }
                    }
                    try { owner.DetachItemsPanel(this, candidate); } catch { }
                }
                try { candidateInstance?.Dispose(); } catch { }
                if (previousPanel != null)
                {
                    try
                    {
                        if (previousPanel.VisualParent == null)
                            AddVisualChild(previousPanel, previousOwner.ItemsPresenterInheritanceParent);
                        if (previousDetached) previousOwner.AttachItemsPanel(this, previousPanel);
                        if (!previousInstance.IsActive) previousInstance.Activate();
                    }
                    catch { }
                }
                throw;
            }

            _owner = owner;
            _itemsPanel = itemsPanel;
            _panel = candidate;
            _panelInstance = candidateInstance;
            previousInstance?.Dispose();
            QueueLayout();
        }

        private void ClearPanel()
        {
            if (_panel == null) return;
            var owner = _owner;
            var panel = _panel;
            var instance = _panelInstance;
            _panel = null;
            _panelInstance = null;
            Exception failure = null;
            try
            {
                if (instance?.IsDisposed == false) instance.Deactivate();
            }
            catch (Exception exception) { failure = exception; }
            if (panel.VisualParent == this)
            {
                try { RemoveVisualChild(panel); } catch (Exception exception) { failure ??= exception; }
            }
            try { owner?.DetachItemsPanel(this, panel); } catch (Exception exception) { failure ??= exception; }
            try { instance?.Dispose(); } catch (Exception exception) { failure ??= exception; }
            QueueLayout();
            if (failure != null) throw failure;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ItemsPresenter));
        }
    }

    /// <summary>Clips and offsets scroll content while reporting viewport, extent, and constrained offset metrics.</summary>
    public sealed class ScrollPresenter : Control, IDisposable
    {
        private IScrollViewportOwner _owner;
        private Control _content;
        private ScrollMetrics _metrics;
        private bool _disposed;

        public ScrollPresenter()
        {
            ClipToBounds = true;
            BringIntoViewRequested += HandleBringIntoView;
        }

        public IScrollViewportOwner Owner
        {
            get => _owner;
            set
            {
                ThrowIfDisposed();
                if (ReferenceEquals(_owner, value)) return;
                _owner = value;
                QueueLayout();
            }
        }

        public Control Content
        {
            get => _content;
            set
            {
                ThrowIfDisposed();
                if (ReferenceEquals(_content, value)) return;
                if (value != null)
                {
                    if (value.Parent == null)
                        throw new InvalidOperationException("Scroll content must retain a logical owner while projected by a ScrollPresenter.");
                    if (value.VisualParent != null && value.VisualParent != value.Parent && value.VisualParent != this)
                        throw new InvalidOperationException("Scroll content is already projected into another visual host.");
                }
                var previous = _content;
                if (previous?.VisualParent == this) RemoveVisualChild(previous);
                try
                {
                    if (value != null) ProjectVisualChild(value);
                }
                catch
                {
                    if (value?.VisualParent == this)
                    {
                        try { RemoveVisualChild(value); } catch { }
                    }
                    if (previous != null && previous.VisualParent == null)
                    {
                        try { ProjectVisualChild(previous); } catch { }
                    }
                    throw;
                }
                _content = value;
                QueueLayout();
            }
        }

        public ScrollMetrics Metrics => _metrics;
        public Vector2 Viewport => _metrics.Viewport;
        public Vector2 Extent => _metrics.Extent;
        public Vector2 Offset => _metrics.Offset;

        public override Vector2 GetMinimumSize() => base.GetMinimumSize();

        protected override void ArrangeChildren()
        {
            var viewport = Size;
            var extent = _content == null
                ? Vector2.Zero
                : Vector2.Max(viewport, _content.GetMinimumSize());
            var requestedOffset = _owner?.ScrollOffset ?? Vector2.Zero;
            var metrics = new ScrollMetrics(viewport, extent, requestedOffset);
            if (_owner != null && _owner.ScrollOffset != metrics.Offset) _owner.ScrollOffset = metrics.Offset;
            if (_content != null)
            {
                _content.Position = -metrics.Offset;
                var minimum = _content.GetMinimumSize();
                _content.Size = new Vector2(
                    (_content.HorizontalSizeFlags & SizeFlags.Expand) != 0 ? Math.Max(viewport.X, minimum.X) : minimum.X,
                    (_content.VerticalSizeFlags & SizeFlags.Expand) != 0 ? Math.Max(viewport.Y, minimum.Y) : minimum.Y);
            }
            if (_metrics != metrics)
            {
                _metrics = metrics;
                _owner?.OnScrollMetricsChanged(this, metrics);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            BringIntoViewRequested -= HandleBringIntoView;
            ClearContent();
            _owner = null;
        }

        private void ClearContent()
        {
            if (_content == null) return;
            RemoveVisualChild(_content);
            _content = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ScrollPresenter));
        }

        private void HandleBringIntoView(object sender, BringIntoViewRequestedEventArgs args)
        {
            if (_owner == null || args.Handled || args.Target == this) return;
            args.Handled = true;
            _owner.BringIntoView(this, args.Target, args.TargetBounds);
        }
    }

    /// <summary>Measures and draws the editable text surface owned by a <see cref="LineEdit"/>.</summary>
    public sealed class LineEditPresenter : Control
    {
        private LineEdit _owner;

        public LineEditPresenter()
        {
            Name = LineEdit.EditorPresenterPartName;
            MouseFilter = MouseFilter.Pass;
        }

        internal LineEditPresenter(LineEdit owner) : this()
        {
            Owner = owner;
        }

        public LineEdit Owner
        {
            get => _owner;
            set
            {
                if (ReferenceEquals(_owner, value)) return;
                _owner = value;
                QueueLayout();
            }
        }

        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, _owner?.GetEditorMinimumSize() ?? Vector2.Zero);

        internal override void Draw(UIRenderContext context) => _owner?.DrawEditor(context);
    }

    internal sealed class SpinBoxPresenter : Container, IDisposable
    {
        private readonly SpinBox _owner;
        private readonly ContentPresenter _editorPresenter;

        internal SpinBoxPresenter(SpinBox owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            _editorPresenter = new ContentPresenter
            {
                Name = SpinBox.EditorPartName,
                HorizontalContentAlignment = HorizontalAlignment.Fill,
                VerticalContentAlignment = VerticalAlignment.Fill,
            };
            AddChild(_editorPresenter);
        }

        internal void Activate() => _editorPresenter.Content = _owner.LineEdit;
        internal void Deactivate() => _editorPresenter.Content = null;

        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, new Vector2(72, 24));

        protected override void ArrangeChildren()
        {
            _editorPresenter.Position = Vector2.Zero;
            _editorPresenter.Size = new Vector2(Math.Max(0, Size.X - 16), Size.Y);
        }

        internal override void Draw(UIRenderContext context)
        {
            _owner.DrawSpinBoxChrome(context);
            base.Draw(context);
        }

        public void Dispose()
        {
            Deactivate();
            _editorPresenter.Dispose();
        }
    }

    internal class PopupPresenter : Container, IDisposable
    {
        protected readonly Popup Owner;
        private bool _disposed;

        internal PopupPresenter(Popup owner)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            foreach (var child in Owner.Children)
                ProjectVisualChild(child);
            Owner.ChildAdded += HandleChildAdded;
        }

        private void HandleChildAdded(Control owner, Control child) => ProjectVisualChild(child);

        protected void DrawPanel(UIRenderContext context)
        {
            var style = Owner.GetThemeStyleBox("panel");
            if (style != null) style.Draw(context, Bounds);
            else
            {
                context.Fill(Bounds, Owner.BackgroundColor ?? context.Theme.PanelColor);
                context.Border(Bounds, Owner.BorderColor ?? context.Theme.PanelBorderColor, Owner.BorderWidth);
            }
        }

        internal override void Draw(UIRenderContext context)
        {
            DrawPanel(context);
            DrawProjectedChildren(context);
        }

        protected void DrawProjectedChildren(UIRenderContext context) => base.Draw(context);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Owner.ChildAdded -= HandleChildAdded;
            foreach (var child in Owner.Children)
                Owner.ProjectVisualChild(child);
        }
    }

    internal sealed class PopupMenuPresenter : PopupPresenter
    {
        internal PopupMenuPresenter(PopupMenu owner) : base(owner)
        {
            owner.ItemsControl.Name = PopupMenu.ItemsPartName;
            ProjectVisualChild(owner.ItemsControl);
        }

        internal override void Draw(UIRenderContext context)
        {
            DrawPanel(context);
            DrawProjectedChildren(context);
            ((PopupMenu)Owner).DrawMenuContent(context);
        }
    }

    internal sealed class MenuBarPresenter : BoxContainer, IDisposable
    {
        private readonly MenuBar _owner;
        private bool _disposed;

        internal MenuBarPresenter(MenuBar owner) : base(Orientation.Horizontal)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Ignore;
            foreach (var child in owner.Children) ProjectVisualChild(child);
            owner.ChildAdded += HandleChildAdded;
        }

        public override Vector2 GetMinimumSize()
        {
            SynchronizeLayoutProperties();
            return base.GetMinimumSize();
        }

        protected override void ArrangeChildren()
        {
            SynchronizeLayoutProperties();
            base.ArrangeChildren();
        }

        private void SynchronizeLayoutProperties()
        {
            Separation = _owner.Separation;
            Alignment = _owner.Alignment;
            ReverseSort = _owner.ReverseSort;
        }

        private void HandleChildAdded(Control owner, Control child) => ProjectVisualChild(child);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.ChildAdded -= HandleChildAdded;
            foreach (var child in _owner.Children) _owner.ProjectVisualChild(child);
        }
    }

    internal sealed class AcceptDialogPresenter : PopupPresenter
    {
        private readonly AcceptDialog _owner;
        private readonly Label _titlePresenter;
        private readonly Label _contentPresenter;
        private readonly Button _acceptButton;
        private readonly Button _cancelButton;

        internal AcceptDialogPresenter(AcceptDialog owner) : base(owner)
        {
            _owner = owner;
            _titlePresenter = new Label { Name = AcceptDialog.TitlePresenterPartName, MouseFilter = MouseFilter.Pass };
            _contentPresenter = new Label { Name = ContentControl.ContentPresenterPartName, MouseFilter = MouseFilter.Pass };
            _acceptButton = new Button { Name = AcceptDialog.AcceptButtonPartName };
            _cancelButton = new Button { Name = AcceptDialog.CancelButtonPartName };
            AddChild(_titlePresenter);
            AddChild(_contentPresenter);
            AddChild(_acceptButton);
            AddChild(_cancelButton);
        }

        protected override void ArrangeChildren()
        {
            var titleHeight = Math.Min(28, Size.Y);
            _titlePresenter.Text = _owner.Title;
            _titlePresenter.Font = _owner.Font;
            _titlePresenter.UIFont = _owner.UIFont;
            _titlePresenter.Position = new Vector2(8, 0);
            _titlePresenter.Size = new Vector2(Math.Max(0, Size.X - 16), titleHeight);
            _contentPresenter.Text = _owner.DialogText;
            _contentPresenter.Font = _owner.Font;
            _contentPresenter.UIFont = _owner.UIFont;
            _contentPresenter.Visible = _owner is not FileDialog && !string.IsNullOrEmpty(_owner.DialogText);
            _contentPresenter.Position = new Vector2(10, titleHeight + 10);
            _contentPresenter.Size = new Vector2(Math.Max(0, Size.X - 20), Math.Max(0, Size.Y - titleHeight - _owner.ButtonHeight - 26));
            ArrangeAction(_acceptButton, _owner.DialogOkButtonBounds, _owner.OkText, !_owner.OkButtonDisabled);
            ArrangeAction(_cancelButton, _owner.DialogCancelButtonBounds, _owner.DialogCancelLabelText, _owner.DialogHasCancelButton);
        }

        private void ArrangeAction(Button button, Rectangle bounds, string text, bool visible)
        {
            button.Text = text;
            button.Font = _owner.Font;
            button.Enabled = visible;
            button.Visible = visible;
            button.Position = new Vector2(bounds.X - _owner.Bounds.X, bounds.Y - _owner.Bounds.Y);
            button.Size = new Vector2(bounds.Width, bounds.Height);
        }

        internal override void Draw(UIRenderContext context)
        {
            DrawPanel(context);
            context.Fill(new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, Math.Min(28, Bounds.Height)), context.Theme.AccentColor);
            _owner.DrawDialogBody(context);
            DrawProjectedChildren(context);
        }
    }

    internal sealed class ColorFieldPresenter : Control
    {
        private readonly ColorPicker _owner;
        internal ColorFieldPresenter(ColorPicker owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Name = ColorPicker.ColorFieldPartName;
            MouseFilter = MouseFilter.Pass;
        }
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, _owner.CustomMinimumSize);
        internal override void Draw(UIRenderContext context) => _owner.DrawColorField(context);
    }

    internal sealed class GraphElementPresenter : Container, IDisposable
    {
        private readonly GraphElement _owner;
        private bool _disposed;

        internal GraphElementPresenter(GraphElement owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Ignore;
            foreach (var child in owner.Children) ProjectVisualChild(child);
            owner.ChildAdded += HandleChildAdded;
        }

        internal override void Draw(UIRenderContext context)
        {
            _owner.DrawGraphElementChrome(context);
            base.Draw(context);
        }

        private void HandleChildAdded(Control owner, Control child) => ProjectVisualChild(child);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.ChildAdded -= HandleChildAdded;
            foreach (var child in _owner.Children) _owner.ProjectVisualChild(child);
        }
    }

    internal sealed class GraphCanvasPresenter : Container, IDisposable
    {
        private readonly GraphEdit _owner;
        private bool _disposed;

        internal GraphCanvasPresenter(GraphEdit owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Name = GraphEdit.GraphCanvasPartName;
            MouseFilter = MouseFilter.Ignore;
            foreach (var child in owner.Children) ProjectVisualChild(child);
            owner.ChildAdded += HandleChildAdded;
        }

        internal override void Draw(UIRenderContext context) => _owner.DrawGraphCanvas(context);

        private void HandleChildAdded(Control owner, Control child) => ProjectVisualChild(child);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.ChildAdded -= HandleChildAdded;
            foreach (var child in _owner.Children) _owner.ProjectVisualChild(child);
        }
    }

    internal sealed class ItemListPresenter : Control
    {
        private readonly ItemList _owner;
        internal ItemListPresenter(ItemList owner) { _owner = owner ?? throw new ArgumentNullException(nameof(owner)); MouseFilter = MouseFilter.Ignore; }
        internal override void Draw(UIRenderContext context) => _owner.DrawItemList(context);
    }

    internal sealed class RichTextPresenter : Container, IDisposable
    {
        private readonly RichTextLabel _owner;
        private bool _disposed;

        internal RichTextPresenter(RichTextLabel owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Name = RichTextLabel.RichTextPresenterPartName;
            MouseFilter = MouseFilter.Ignore;
            foreach (var child in owner.Children) ProjectVisualChild(child);
            owner.ChildAdded += HandleChildAdded;
        }

        internal override void Draw(UIRenderContext context)
        {
            _owner.DrawRichText(context);
            base.Draw(context);
        }

        private void HandleChildAdded(Control owner, Control child) => ProjectVisualChild(child);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.ChildAdded -= HandleChildAdded;
            foreach (var child in _owner.Children) _owner.ProjectVisualChild(child);
        }
    }

    /// <summary>Projects a tree's logical child controls and draws its retained hierarchical content.</summary>
    public sealed class TreePresenter : Container, IDisposable
    {
        private Tree _owner;
        private bool _active;
        private bool _disposed;

        public TreePresenter()
        {
            Name = Tree.TreePresenterPartName;
            MouseFilter = MouseFilter.Ignore;
        }

        internal TreePresenter(Tree owner) : this() => Owner = owner;

        public Tree Owner
        {
            get => _owner;
            set
            {
                if (ReferenceEquals(_owner, value)) return;
                Deactivate();
                _owner = value;
            }
        }

        internal void Activate()
        {
            if (_active || _owner == null) return;
            _active = true;
            foreach (var child in _owner.Children) ProjectVisualChild(child);
            _owner.ChildAdded += HandleChildAdded;
        }

        internal void Deactivate()
        {
            if (!_active) return;
            _active = false;
            _owner.ChildAdded -= HandleChildAdded;
            foreach (var child in _owner.Children) _owner.ProjectVisualChild(child);
        }

        internal override void Draw(UIRenderContext context)
        {
            _owner?.DrawTreeContent(context);
            base.Draw(context);
        }

        private void HandleChildAdded(Control owner, Control child) => ProjectVisualChild(child);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Deactivate();
        }
    }

    internal sealed class ScrollContainerChromePresenter : Container, IDisposable
    {
        private readonly ScrollContainer _owner;
        private readonly ContentPresenter _horizontalScrollBarPresenter;
        private readonly ContentPresenter _verticalScrollBarPresenter;
        private bool _disposed;

        internal ScrollContainerChromePresenter(ScrollContainer owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            ScrollPresenter = new ScrollPresenter
            {
                Name = ScrollContainer.ScrollPresenterPartName,
                Owner = owner,
            };
            _horizontalScrollBarPresenter = new ContentPresenter
            {
                HorizontalContentAlignment = HorizontalAlignment.Fill,
                VerticalContentAlignment = VerticalAlignment.Fill,
            };
            _verticalScrollBarPresenter = new ContentPresenter
            {
                HorizontalContentAlignment = HorizontalAlignment.Fill,
                VerticalContentAlignment = VerticalAlignment.Fill,
            };
            AddChild(ScrollPresenter);
            AddChild(_horizontalScrollBarPresenter);
            AddChild(_verticalScrollBarPresenter);
        }

        internal ScrollPresenter ScrollPresenter { get; }

        internal void Activate()
        {
            _horizontalScrollBarPresenter.Content = _owner.HorizontalScrollBar;
            _verticalScrollBarPresenter.Content = _owner.VerticalScrollBar;
        }

        internal void Deactivate()
        {
            _horizontalScrollBarPresenter.Content = null;
            _verticalScrollBarPresenter.Content = null;
        }

        protected override void ArrangeChildren()
        {
            ScrollPresenter.Position = _owner.ScrollPresenterPosition;
            ScrollPresenter.Size = _owner.ScrollPresenterSize;
            _horizontalScrollBarPresenter.Position = _owner.HorizontalScrollBar.Position;
            _horizontalScrollBarPresenter.Size = _owner.HorizontalScrollBar.Size;
            _verticalScrollBarPresenter.Position = _owner.VerticalScrollBar.Position;
            _verticalScrollBarPresenter.Size = _owner.VerticalScrollBar.Size;
        }

        internal override void Draw(UIRenderContext context)
        {
            var panel = context.Theme.GetStyleBox("panel", nameof(ScrollContainer));
            if (panel != null) panel.Draw(context, Bounds);
            else context.Fill(Bounds, context.Theme.BackgroundColor);
            base.Draw(context);
            var hintColor = context.Theme.FocusColor;
            hintColor.A = 96;
            foreach (var rectangle in _owner.GetVisibleScrollHintRectangles())
            {
                var icon = _owner.GetThemeIcon(rectangle.Width >= rectangle.Height ? "scroll_hint_horizontal" : "scroll_hint_vertical");
                if (icon.HasValue) context.Icon(icon.Value, rectangle, hintColor);
                else context.Fill(rectangle, hintColor);
            }
            if (!_owner.IsFocusBorderVisible) return;
            var focus = context.Theme.GetStyleBox("focus", nameof(ScrollContainer));
            if (focus != null) focus.Draw(context, Bounds);
            else context.Border(Bounds, context.Theme.FocusColor, 2);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Deactivate();
            _horizontalScrollBarPresenter.Dispose();
            _verticalScrollBarPresenter.Dispose();
            ScrollPresenter.Dispose();
        }
    }
}