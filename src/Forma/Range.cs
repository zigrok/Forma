// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// This control API and behavior are adapted from Godot Engine's Range implementation;
// see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    public abstract class Range : TemplatedControl
    {
        public override string AccessibilityValue => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions |
            AccessibilityActions.Increment | AccessibilityActions.Decrement | AccessibilityActions.SetValue;
        private sealed class SharedState
        {
            public float Value, MinValue, MaxValue = 100, Step = 1, Page;
            public bool ExpRatio, AllowGreater, AllowLesser;
            public readonly List<Range> Owners = new List<Range>();
        }
        private SharedState _shared;
        protected Range() { _shared = new SharedState(); _shared.Owners.Add(this); }
        public float MinValue { get => _shared.MinValue; set { _shared.MinValue = value; _shared.MaxValue = Math.Max(_shared.MaxValue, value); _shared.Page = MathHelper.Clamp(_shared.Page, 0, _shared.MaxValue - _shared.MinValue); Value = _shared.Value; } }
        public float MaxValue { get => _shared.MaxValue; set { _shared.MaxValue = Math.Max(value, _shared.MinValue); _shared.Page = MathHelper.Clamp(_shared.Page, 0, _shared.MaxValue - _shared.MinValue); Value = _shared.Value; } }
        public float Step { get => _shared.Step; set => _shared.Step = value; }
        public float Page { get => _shared.Page; set { _shared.Page = MathHelper.Clamp(value, 0, MaxValue - MinValue); Value = _shared.Value; } }
        public bool AllowGreater { get => _shared.AllowGreater; set => _shared.AllowGreater = value; }
        public bool AllowLesser { get => _shared.AllowLesser; set => _shared.AllowLesser = value; }
        public bool ExpRatio { get => _shared.ExpRatio; set => _shared.ExpRatio = value; }
        public bool UseRoundedValues { get; set; }
        public float Value
        {
            get => _shared.Value;
            set
            {
                var clamped = CalculateValue(value); if (_shared.Value == clamped) return;
                _shared.Value = clamped; foreach (var owner in _shared.Owners) owner.ValueChanged?.Invoke(owner, clamped);
            }
        }
        public float Ratio { get => GetAsRatio(); set => SetAsRatio(value); }
        public event Action<Range, float> ValueChanged;
        public void SetValueNoSignal(float value) => _shared.Value = CalculateValue(value);
        public void SetAsRatio(float ratio)
        {
            ratio = MathHelper.Clamp(ratio, 0, 1);
            if (ExpRatio && MinValue >= 0 && MaxValue > 0)
            {
                var minExponent = MinValue == 0 ? 0 : Log2(MinValue); Value = MathF.Pow(2, minExponent + (Log2(MaxValue) - minExponent) * ratio);
            }
            else Value = MinValue + (MaxValue - MinValue) * ratio;
        }
        public float GetAsRatio()
        {
            if (MaxValue == MinValue) return 1;
            if (ExpRatio && MinValue >= 0 && MaxValue > 0 && Value > 0) { var minExponent = MinValue == 0 ? 0 : Log2(MinValue); return MathHelper.Clamp((Log2(Value) - minExponent) / (Log2(MaxValue) - minExponent), 0, 1); }
            return MathHelper.Clamp((Value - MinValue) / (MaxValue - MinValue), 0, 1);
        }
        private static float Log2(float value) => (float)(Math.Log(value) / Math.Log(2));
        public void Share(Range range)
        {
            if (range == null) throw new ArgumentNullException(nameof(range)); if (ReferenceEquals(_shared, range._shared)) return;
            _shared.Owners.Remove(this); _shared = range._shared; _shared.Owners.Add(this);
        }
        public void Unshare()
        {
            if (_shared.Owners.Count == 1) return;
            var copy = new SharedState { Value = _shared.Value, MinValue = _shared.MinValue, MaxValue = _shared.MaxValue, Step = _shared.Step, Page = _shared.Page, ExpRatio = _shared.ExpRatio, AllowGreater = _shared.AllowGreater, AllowLesser = _shared.AllowLesser };
            _shared.Owners.Remove(this); _shared = copy; _shared.Owners.Add(this);
        }
        /// <summary>Snaps a value to the nearest multiple of step anchored at MinValue, matching Godot's
        /// Range::_calc_value / _snapped_r128 formula (floor(x/step + 0.5) * step) - round-half-up, not
        /// .NET's default round-half-to-even.</summary>
        protected float SnapToStep(float value, float step) => step > 0 ? MathF.Floor((value - MinValue) / step + 0.5f) * step + MinValue : value;
        /// <summary>Snaps a value to the nearest multiple of step with no anchor, matching Godot's
        /// anchor-free Math::snapped (used e.g. to pre-snap SpinBox's arrow_step itself).</summary>
        protected static float SnapToMultiple(float value, float step) => step > 0 ? MathF.Floor(value / step + 0.5f) * step : value;
        private float CalculateValue(float value)
        {
            if (Step > 0) value = SnapToStep(value, Step);
            // Godot's Math::round is round-half-away-from-zero; .NET's default MathF.Round is round-half-to-even.
            if (UseRoundedValues) value = MathF.Round(value, MidpointRounding.AwayFromZero);
            if (!AllowGreater) value = Math.Min(MaxValue - Page, value);
            if (!AllowLesser) value = Math.Max(MinValue, value);
            return value;
        }
    }

}
