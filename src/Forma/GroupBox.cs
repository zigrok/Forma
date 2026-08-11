// Copyright (c) 2026 Igor Hipolito Vieira
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>Groups related content inside a titled border.</summary>
    [TemplatePart(HeaderPresenterPartName, typeof(Label), false)]
    [TemplatePart(ContentPresenterPartName, typeof(ContentPresenter), false)]
    public sealed class GroupBox : ContentControl
    {
        /// <summary>Identifies the optional label that presents the group title.</summary>
        public const string HeaderPresenterPartName = "PART_HeaderPresenter";
        private string _title = string.Empty;
        private Thickness _padding = new Thickness(12);
        private float _headerIndent = 12;
        private float _headerGap = 6;
        private int _borderWidth = 1;

        /// <summary>Initializes a group that allows pointer input to pass to its content.</summary>
        public GroupBox()
        {
            MouseFilter = MouseFilter.Pass;
        }

        /// <summary>Gets the group accessibility role.</summary>
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Group;
        /// <summary>Gets the explicit accessibility label, title, or inherited name in that order.</summary>
        public override string AccessibilityName => !string.IsNullOrEmpty(AccessibilityLabel)
            ? AccessibilityLabel
            : !string.IsNullOrEmpty(Title) ? Title : base.AccessibilityName;

        /// <summary>Gets or sets the title displayed in the border header.</summary>
        public string Title
        {
            get => _title;
            set
            {
                value ??= string.Empty;
                if (_title == value) return;
                _title = value;
                if (GetTemplateChild(HeaderPresenterPartName) is Label presenter) presenter.Text = value;
                OnPropertyChanged(nameof(Title));
                QueueLayout();
            }
        }

        /// <summary>Gets or sets the group body background, or transparent to leave it unfilled.</summary>
        public Color BackgroundColor { get; set; } = Color.Transparent;
        /// <summary>Gets or sets the border color, or transparent to use the theme panel border.</summary>
        public Color BorderColor { get; set; } = Color.Transparent;
        /// <summary>Gets or sets the title color, or transparent to use the label foreground.</summary>
        public Color HeaderForeground { get; set; } = Color.Transparent;
        /// <summary>Gets or sets the non-negative border width.</summary>
        public int BorderWidth
        {
            get => _borderWidth;
            set
            {
                var normalized = Math.Max(0, value);
                if (_borderWidth == normalized) return;
                _borderWidth = normalized;
                QueueLayout();
            }
        }
        /// <summary>Gets or sets the spacing between the border and group content.</summary>
        public Thickness Padding
        {
            get => _padding;
            set
            {
                if (_padding.Equals(value)) return;
                _padding = value;
                QueueLayout();
            }
        }
        /// <summary>Gets or sets the non-negative title inset from the leading border edge.</summary>
        public float HeaderIndent
        {
            get => _headerIndent;
            set
            {
                var normalized = MathF.Max(0, value);
                if (_headerIndent == normalized) return;
                _headerIndent = normalized;
                QueueLayout();
            }
        }
        /// <summary>Gets or sets the non-negative gap cut around the title in the top border.</summary>
        public float HeaderGap
        {
            get => _headerGap;
            set
            {
                var normalized = MathF.Max(0, value);
                if (_headerGap == normalized) return;
                _headerGap = normalized;
                QueueLayout();
            }
        }

        /// <summary>Synchronizes the title presenter after the control template is applied.</summary>
        protected override void OnTemplateApplied()
        {
            base.OnTemplateApplied();
            if (GetTemplateChild(HeaderPresenterPartName) is Label presenter) presenter.Text = Title;
        }
    }

    internal sealed class GroupBoxPresenter : Container
    {
        private readonly GroupBox _owner;
        private readonly Label _headerPresenter;
        private readonly ContentPresenter _contentPresenter;

        internal GroupBoxPresenter(GroupBox owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            MouseFilter = MouseFilter.Pass;
            _contentPresenter = new ContentPresenter
            {
                Name = ContentControl.ContentPresenterPartName,
                Content = owner.Content,
                ContentTemplate = owner.ContentTemplate,
                HorizontalContentAlignment = owner.HorizontalContentAlignment,
                VerticalContentAlignment = owner.VerticalContentAlignment,
            };
            _headerPresenter = new Label
            {
                Name = GroupBox.HeaderPresenterPartName,
                Text = owner.Title,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilter.Ignore,
            };
            AddChild(_contentPresenter);
            AddChild(_headerPresenter);
        }

        private bool HasHeader => !string.IsNullOrEmpty(_owner.Title);
        private Vector2 HeaderMinimumSize => HasHeader ? _headerPresenter.GetMinimumSize() : Vector2.Zero;

        public override Vector2 GetMinimumSize()
        {
            var border = _owner.BorderWidth * 2;
            var header = HeaderMinimumSize;
            var content = _contentPresenter.GetMinimumSize();
            var width = MathF.Max(
                content.X + _owner.Padding.Horizontal + border,
                header.X + (_owner.HeaderIndent + _owner.HeaderGap) * 2 + border);
            var height = HasHeader
                ? header.Y + _owner.Padding.Vertical + content.Y + _owner.BorderWidth
                : content.Y + _owner.Padding.Vertical + border;
            return Vector2.Max(CustomMinimumSize, new Vector2(width, height));
        }

        protected override void ArrangeChildren()
        {
            var border = _owner.BorderWidth;
            var header = HeaderMinimumSize;
            var rtl = _owner.IsLayoutRtl();
            if (HasHeader)
            {
                var headerX = rtl
                    ? MathF.Max(border, Size.X - border - _owner.HeaderIndent - header.X)
                    : border + _owner.HeaderIndent;
                _headerPresenter.Position = new Vector2(headerX, 0);
                _headerPresenter.Size = header;
            }
            else
            {
                _headerPresenter.Position = Vector2.Zero;
                _headerPresenter.Size = Vector2.Zero;
            }

            var contentTop = (HasHeader ? header.Y : border) + _owner.Padding.Top;
            var contentLeft = border + _owner.Padding.Left;
            var available = new Vector2(
                MathF.Max(0, Size.X - contentLeft - border - _owner.Padding.Right),
                MathF.Max(0, Size.Y - contentTop - border - _owner.Padding.Bottom));
            FitChildInRect(_contentPresenter, new Vector2(contentLeft, contentTop), available, rtl);
        }

        internal override void Draw(UIRenderContext context)
        {
            var borderWidth = _owner.BorderWidth;
            var header = HeaderMinimumSize;
            var top = HasHeader ? (int)MathF.Round(header.Y * .5f) : 0;
            var body = new Rectangle(Bounds.X, Bounds.Y + top, Bounds.Width, Math.Max(0, Bounds.Height - top));
            if (_owner.BackgroundColor.A > 0) context.Fill(body, _owner.BackgroundColor);
            if (borderWidth > 0 && body.Width > 0 && body.Height > 0)
            {
                var color = _owner.BorderColor.A > 0 ? _owner.BorderColor : context.Theme.PanelBorderColor;
                context.Fill(new Rectangle(body.X, body.Y, borderWidth, body.Height), color);
                context.Fill(new Rectangle(body.Right - borderWidth, body.Y, borderWidth, body.Height), color);
                context.Fill(new Rectangle(body.X, body.Bottom - borderWidth, body.Width, borderWidth), color);
                var gapLeft = HasHeader ? Math.Max(body.Left, _headerPresenter.Bounds.Left - (int)MathF.Ceiling(_owner.HeaderGap)) : body.Right;
                var gapRight = HasHeader ? Math.Min(body.Right, _headerPresenter.Bounds.Right + (int)MathF.Ceiling(_owner.HeaderGap)) : body.Right;
                if (gapLeft > body.Left) context.Fill(new Rectangle(body.Left, body.Top, gapLeft - body.Left, borderWidth), color);
                if (gapRight < body.Right) context.Fill(new Rectangle(gapRight, body.Top, body.Right - gapRight, borderWidth), color);
                if (!HasHeader) context.Fill(new Rectangle(body.Left, body.Top, body.Width, borderWidth), color);
            }
            _headerPresenter.Foreground = _owner.HeaderForeground.A > 0 ? _owner.HeaderForeground : null;
            base.Draw(context);
        }
    }
}