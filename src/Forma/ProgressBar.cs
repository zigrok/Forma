// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's ProgressBar implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public enum ProgressBarFillMode { BeginToEnd, EndToBegin, TopToBottom, BottomToTop }

    /// <summary>Visualizes determinate or animated indeterminate progress with directional fill and optional percentage text.</summary>
    public sealed class ProgressBar : Range
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.ProgressBar;
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions &
            ~(AccessibilityActions.Increment | AccessibilityActions.Decrement | AccessibilityActions.SetValue);
        private readonly UIFontSelection _fontSelection = new UIFontSelection();
        private ProgressBarFillMode _fillMode;
        private bool _indeterminate;
        // Godot's ProgressBar constructor calls set_step(0.01) - near-continuous, not Range's own
        // integer-snapping default of 1 - so setting a fractional Value like 0.37 isn't silently rounded.
        public ProgressBar() { Step = 0.01f; }
        public bool ShowPercentage { get; set; } = true;
        public ProgressBarFillMode FillMode { get => _fillMode; set => SetFillMode(value); }
        public bool Indeterminate { get => _indeterminate; set => SetIndeterminate(value); }
        public bool EditorPreviewIndeterminate { get; set; }
        /// <summary>Pixels per second used by the indeterminate segment.</summary>
        public float IndeterminateSpeed { get; set; } = 200;
        public float IndeterminateOffset { get; private set; }
        public SpriteFont Font { get => _fontSelection.SpriteFont; set { _fontSelection.SetSpriteFont(value); QueueLayout(); } }
        public UIFont UIFont { get => _fontSelection.UIFont; set { _fontSelection.SetUIFont(value); QueueLayout(); } }
        internal UIFont EffectiveUIFont => ResolveFont(_fontSelection);
        public void SetFillMode(ProgressBarFillMode mode)
        {
            if (!Enum.IsDefined(typeof(ProgressBarFillMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            _fillMode = mode;
            IndeterminateOffset = 0;
        }
        public ProgressBarFillMode GetFillMode() => FillMode;
        public void SetShowPercentage(bool visible) => ShowPercentage = visible;
        public bool IsPercentageShown() => ShowPercentage;
        public void SetIndeterminate(bool indeterminate)
        {
            if (_indeterminate == indeterminate) return;
            _indeterminate = indeterminate;
            IndeterminateOffset = 0;
            QueueLayout();
        }
        public bool IsIndeterminate() => Indeterminate;
        public void SetEditorPreviewIndeterminate(bool previewIndeterminate)
        {
            if (EditorPreviewIndeterminate == previewIndeterminate) return;
            EditorPreviewIndeterminate = previewIndeterminate;
            IndeterminateOffset = 0;
        }
        public bool IsEditorPreviewIndeterminateEnabled() => EditorPreviewIndeterminate;
        public override Vector2 GetMinimumSize() => Vector2.Max(CustomMinimumSize, new Vector2(48, 20));
        /// <summary>Returns the current local fill rectangle for determinate state.</summary>
        public Rectangle GetFillRectangle(float ratio)
        {
            ratio = MathHelper.Clamp(ratio, 0, 1);
            var width = Math.Max(0, (int)MathF.Round(Size.X - 2));
            var height = Math.Max(0, (int)MathF.Round(Size.Y - 2));
            var filledWidth = (int)MathF.Round(width * ratio);
            var filledHeight = (int)MathF.Round(height * ratio);
            switch (FillMode)
            {
                case ProgressBarFillMode.EndToBegin: return new Rectangle(1 + width - filledWidth, 1, filledWidth, height);
                case ProgressBarFillMode.TopToBottom: return new Rectangle(1, 1, width, filledHeight);
                case ProgressBarFillMode.BottomToTop: return new Rectangle(1, 1 + height - filledHeight, width, filledHeight);
                default: return new Rectangle(1, 1, filledWidth, height);
            }
        }
        internal override void Process(GameTime gameTime)
        {
            if (Indeterminate)
            {
                var extent = FillMode == ProgressBarFillMode.TopToBottom || FillMode == ProgressBarFillMode.BottomToTop ? Size.Y : Size.X;
                var segment = Math.Min(Size.X, Size.Y) * 2;
                IndeterminateOffset += Math.Max(1, IndeterminateSpeed) * (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (IndeterminateOffset > extent + segment) IndeterminateOffset = 0;
            }
            base.Process(gameTime);
        }
    }
}
