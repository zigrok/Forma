// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;

namespace Forma.Xaml
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class PseudoStateAttribute : Attribute
    {
        public PseudoStateAttribute(string name, Type controlType, bool inherited, string providerMember)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A pseudo-state identifier is required.", nameof(name));
            Name = name;
            ControlType = controlType ?? throw new ArgumentNullException(nameof(controlType));
            Inherited = inherited;
            ProviderMember = string.IsNullOrWhiteSpace(providerMember)
                ? throw new ArgumentException("A provider member is required.", nameof(providerMember))
                : providerMember;
        }

        public string Name { get; }
        public Type ControlType { get; }
        public bool Inherited { get; }
        public string ProviderMember { get; }
    }

    public sealed class Style
    {
        private static long _nextGeneration;

        public Style(string selector) : this(StyleSelector.Parse(selector)) { }
        public Style(StyleSelector selector)
        {
            Selector = selector ?? throw new ArgumentNullException(nameof(selector));
            Generation = Interlocked.Increment(ref _nextGeneration);
        }
        public StyleSelector Selector { get; }
        public long Generation { get; }
        /// <summary>Where the rule was written, for example <c>theme.fcss:12</c>; set by generated code and reported by <see cref="StyleInspector"/>.</summary>
        public string Origin { get; set; }
        public AdaptiveCondition Condition { get; set; }
        public IList<IStyleSetter> Setters { get; } = new List<IStyleSetter>();
        public IList<IStyleTransition> Transitions { get; } = new List<IStyleTransition>();
        public void AddSetter(IStyleSetter setter) => Setters.Add(setter ?? throw new ArgumentNullException(nameof(setter)));
        public void AddTransition(IStyleTransition transition) => Transitions.Add(transition ?? throw new ArgumentNullException(nameof(transition)));
    }

    public sealed class AdaptiveCondition
    {
        public float? MinViewportWidth { get; set; }
        public float? MaxViewportWidth { get; set; }
        public float? MinViewportHeight { get; set; }
        public float? MaxViewportHeight { get; set; }
        public float? DisplayScale { get; set; }
        public ThemeVariant? ThemeVariant { get; set; }
        public InputModality? InputModality { get; set; }
        /// <summary>When set, the rule applies only while <see cref="UIContext.ReducedMotion"/> equals this value (CSS prefers-reduced-motion).</summary>
        public bool? ReducedMotion { get; set; }

        public void SetCompiledValue(string property, string value)
        {
            switch (property)
            {
                case nameof(MinViewportWidth): MinViewportWidth = ParseSingle(value); break;
                case nameof(MaxViewportWidth): MaxViewportWidth = ParseSingle(value); break;
                case nameof(MinViewportHeight): MinViewportHeight = ParseSingle(value); break;
                case nameof(MaxViewportHeight): MaxViewportHeight = ParseSingle(value); break;
                case nameof(DisplayScale): DisplayScale = ParseSingle(value); break;
                case nameof(ThemeVariant): ThemeVariant = Enum.Parse<ThemeVariant>(value, false); break;
                case nameof(InputModality): InputModality = Enum.Parse<InputModality>(value, false); break;
                case nameof(ReducedMotion): ReducedMotion = bool.Parse(value); break;
                default: throw new ArgumentOutOfRangeException(nameof(property), property, "Unsupported adaptive condition property.");
            }
        }

        public bool Matches(UIContext context)
        {
            if (context == null) return false;
            return (!MinViewportWidth.HasValue || context.ViewportSize.X >= MinViewportWidth.Value) &&
                (!MaxViewportWidth.HasValue || context.ViewportSize.X <= MaxViewportWidth.Value) &&
                (!MinViewportHeight.HasValue || context.ViewportSize.Y >= MinViewportHeight.Value) &&
                (!MaxViewportHeight.HasValue || context.ViewportSize.Y <= MaxViewportHeight.Value) &&
                (!DisplayScale.HasValue || Math.Abs(context.DisplayScale - DisplayScale.Value) < .0001f) &&
                (!ThemeVariant.HasValue || context.ThemeVariant == ThemeVariant.Value) &&
                (!InputModality.HasValue || context.InputModality == InputModality.Value) &&
                (!ReducedMotion.HasValue || context.ReducedMotion == ReducedMotion.Value);
        }

        private static float ParseSingle(string value) =>
            float.Parse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
    }

    public interface IStyleSetter
    {
        IDisposable Apply(Control control, long priority);
    }

    public interface IStyleTransition { }

    internal interface IStyleTransitionRuntime
    {
        bool Matches(IStyleSetter setter);
        IDisposable Apply(Control control, IStyleSetter setter, long priority);
    }

    internal interface IStyleSetterInfo
    {
        string PropertyName { get; }
        object ValueFor(Control control);
    }

    public sealed class StyleSetter<T> : IStyleSetter, IStyleSetterInfo
    {
        private readonly XamlProperty<T> _property;
        private readonly Func<Control, T> _value;

        public StyleSetter(XamlProperty<T> property, T value) : this(property, _ => value) { }
        public StyleSetter(XamlProperty<T> property, Func<Control, T> value)
        {
            _property = property ?? throw new ArgumentNullException(nameof(property));
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        string IStyleSetterInfo.PropertyName => _property.Name;
        object IStyleSetterInfo.ValueFor(Control control) => _value(control);

        public IDisposable Apply(Control control, long priority) =>
            XamlValues.Set(control, _property, XamlValueLayer.Style, _value(control), priority);

        internal bool UsesProperty(XamlProperty<T> property) =>
            ReferenceEquals(_property, property) || string.Equals(_property.Name, property.Name, StringComparison.Ordinal);

        internal IDisposable ApplyTransitioned(Control control, long priority, StyleTransition<T> transition)
        {
            var from = XamlValues.GetEffectiveValue(control, _property);
            var contribution = XamlValues.Set(control, _property, XamlValueLayer.Style, _value(control), priority);
            var to = XamlValues.GetEffectiveValue(control, _property);
            return new TransitionedStyleValue<T>(control, _property, contribution, transition, from, to);
        }
    }

    public abstract class StyleTransition<T> : IStyleTransition, IStyleTransitionRuntime
    {
        protected StyleTransition(XamlProperty<T> property, TimeSpan duration, Easing easing = Easing.Linear)
        {
            Property = property ?? throw new ArgumentNullException(nameof(property));
            if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            Duration = duration;
            Easing = easing;
        }

        public XamlProperty<T> Property { get; }
        public TimeSpan Duration { get; }
        public Easing Easing { get; }
        protected abstract T Interpolate(T from, T to, float progress);
        internal T Sample(T from, T to, float progress) => Interpolate(from, to, progress);

        bool IStyleTransitionRuntime.Matches(IStyleSetter setter) =>
            setter is StyleSetter<T> typed && typed.UsesProperty(Property);

        IDisposable IStyleTransitionRuntime.Apply(Control control, IStyleSetter setter, long priority) =>
            ((StyleSetter<T>)setter).ApplyTransitioned(control, priority, this);
    }

    public sealed class FloatTransition : StyleTransition<float>
    {
        public FloatTransition(XamlProperty<float> property, TimeSpan duration, Easing easing = Easing.Linear)
            : base(property, duration, easing) { }
        protected override float Interpolate(float from, float to, float progress) => from + (to - from) * progress;
    }

    public sealed class ColorTransition : StyleTransition<Color>
    {
        public ColorTransition(XamlProperty<Color> property, TimeSpan duration, Easing easing = Easing.Linear)
            : base(property, duration, easing) { }
        protected override Color Interpolate(Color from, Color to, float progress) => Color.Lerp(from, to, progress);
    }

    public sealed class Vector2Transition : StyleTransition<Vector2>
    {
        public Vector2Transition(XamlProperty<Vector2> property, TimeSpan duration, Easing easing = Easing.Linear)
            : base(property, duration, easing) { }
        protected override Vector2 Interpolate(Vector2 from, Vector2 to, float progress) => Vector2.Lerp(from, to, progress);
    }

    public sealed class ThicknessTransition : StyleTransition<Thickness>
    {
        public ThicknessTransition(XamlProperty<Thickness> property, TimeSpan duration, Easing easing = Easing.Linear)
            : base(property, duration, easing) { }
        protected override Thickness Interpolate(Thickness from, Thickness to, float progress) => new Thickness(
            from.Left + (to.Left - from.Left) * progress,
            from.Top + (to.Top - from.Top) * progress,
            from.Right + (to.Right - from.Right) * progress,
            from.Bottom + (to.Bottom - from.Bottom) * progress);
    }

    internal interface IStyleAttachmentValue : IDisposable
    {
        void DisposeImmediately();
    }

    internal sealed class TransitionedStyleValue<T> : IStyleAttachmentValue
    {
        private readonly Control _control;
        private readonly XamlProperty<T> _property;
        private readonly StyleTransition<T> _transition;
        private XamlValueContribution<T> _styleValue;
        private StyleTransitionClock<T> _clock;

        public TransitionedStyleValue(
            Control control,
            XamlProperty<T> property,
            XamlValueContribution<T> styleValue,
            StyleTransition<T> transition,
            T from,
            T to)
        {
            _control = control;
            _property = property;
            _styleValue = styleValue;
            _transition = transition;
            _clock = StyleTransitionClock<T>.Start(control, property, transition, from, to);
        }

        public void Dispose() => Dispose(animateExit: true);

        public void DisposeImmediately() => Dispose(animateExit: false);

        private void Dispose(bool animateExit)
        {
            var styleValue = _styleValue;
            if (styleValue == null) return;
            var from = XamlValues.GetEffectiveValue(_control, _property);
            _clock?.Dispose();
            _clock = null;
            styleValue.Dispose();
            _styleValue = null;
            var to = XamlValues.GetEffectiveValue(_control, _property);
            if (animateExit) StyleTransitionClock<T>.Start(_control, _property, _transition, from, to);
        }
    }

    internal sealed class StyleTransitionClock<T> : IXamlUpdateParticipant, IDisposable
    {
        private readonly Control _control;
        private readonly StyleTransition<T> _transition;
        private readonly T _from;
        private readonly T _to;
        private XamlValueContribution<T> _animationValue;
        private TimeSpan? _startedAt;
        private bool _disposed;

        private StyleTransitionClock(Control control, XamlProperty<T> property, StyleTransition<T> transition, T from, T to)
        {
            _control = control;
            _transition = transition;
            _from = from;
            _to = to;
            _animationValue = XamlValues.Set(control, property, XamlValueLayer.Animation, from);
            XamlAttachment.RegisterUpdateParticipant(control, this);
        }

        public static StyleTransitionClock<T> Start(
            Control control,
            XamlProperty<T> property,
            StyleTransition<T> transition,
            T from,
            T to)
        {
            var clock = new StyleTransitionClock<T>(control, property, transition, from, to);
            if (transition.Duration == TimeSpan.Zero) clock.Dispose();
            return clock;
        }

        public void Update(GameTime gameTime)
        {
            if (_disposed) return;
            _startedAt ??= gameTime.TotalGameTime;
            var elapsed = gameTime.TotalGameTime - _startedAt.Value;
            var progress = _transition.Duration == TimeSpan.Zero
                ? 1f
                : MathHelper.Clamp((float)(elapsed.TotalSeconds / _transition.Duration.TotalSeconds), 0, 1);
            _animationValue.Value = _transition.Sample(_from, _to, ApplyEasing(progress, _transition.Easing));
            if (progress >= 1f) Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            XamlAttachment.UnregisterActivation(_control, null, this);
            _animationValue?.Dispose();
            _animationValue = null;
        }

        private static float ApplyEasing(float value, Easing easing) => easing switch
        {
            Easing.CubicIn => value * value * value,
            Easing.CubicOut => 1f - MathF.Pow(1f - value, 3),
            Easing.CubicInOut => value < .5f ? 4f * value * value * value : 1f - MathF.Pow(-2f * value + 2f, 3) / 2f,
            _ => value,
        };
    }

    public enum StyleSelectorCombinator { Descendant, Child, TemplateChild }

    /// <summary>An attribute (<c>[data-state="open"]</c>) or structural (<c>:first-child</c>, <c>:nth-child(2n+1)</c>) condition of a compound selector.</summary>
    public sealed class StyleSelectorPredicate
    {
        /// <summary>Creates a predicate of the given kind with its name, value and An+B coefficients.</summary>
        public StyleSelectorPredicate(string kind, string name, string value, int a, int b)
        {
            Kind = kind;
            Name = name;
            Value = value;
            A = a;
            B = b;
        }

        /// <summary>One of attribute, first-child, last-child, only-child, empty, nth-child.</summary>
        public string Kind { get; }
        /// <summary>Attribute name for an attribute predicate.</summary>
        public string Name { get; }
        /// <summary>Expected attribute value, if any.</summary>
        public string Value { get; }
        /// <summary>The A coefficient of an An+B expression.</summary>
        public int A { get; }
        /// <summary>The B coefficient of an An+B expression.</summary>
        public int B { get; }

        internal bool Matches(Control control)
        {
            switch (Kind)
            {
                case "attribute":
                    var current = control.GetData(Name);
                    return Value == null ? current != null : current == Value;
                case "empty":
                    return control.VisualChildren.Count == 0;
            }
            var siblings = control.VisualParent?.VisualChildren;
            if (siblings == null) return false;
            var index = 0;
            for (; index < siblings.Count && !ReferenceEquals(siblings[index], control); index++) { }
            if (index == siblings.Count) return false;
            switch (Kind)
            {
                case "first-child": return index == 0;
                case "last-child": return index == siblings.Count - 1;
                case "only-child": return siblings.Count == 1;
                case "nth-child":
                    var position = index + 1;
                    if (A == 0) return position == B;
                    var steps = (position - B) / (double)A;
                    return steps >= 0 && Math.Abs(steps - Math.Round(steps)) < 1e-9;
            }
            return false;
        }
    }

    public sealed class StyleSelectorCompound
    {
        /// <summary>Creates a compound selector without attribute or structural predicates.</summary>
        public StyleSelectorCompound(string typeName, bool universal, string name, IReadOnlyList<string> classes,
            IReadOnlyList<string> pseudoStates, IReadOnlyList<StyleSelectorCompound> negations)
            : this(typeName, universal, name, classes, pseudoStates, negations, null)
        {
        }

        public StyleSelectorCompound(string typeName, bool universal, string name, IReadOnlyList<string> classes,
            IReadOnlyList<string> pseudoStates, IReadOnlyList<StyleSelectorCompound> negations, IReadOnlyList<StyleSelectorPredicate> predicates)
        {
            Predicates = Array.AsReadOnly((predicates ?? Array.Empty<StyleSelectorPredicate>()).ToArray());
            TypeName = typeName;
            IsUniversal = universal;
            Name = name;
            Classes = Array.AsReadOnly((classes ?? throw new ArgumentNullException(nameof(classes))).ToArray());
            PseudoStates = Array.AsReadOnly((pseudoStates ?? throw new ArgumentNullException(nameof(pseudoStates))).ToArray());
            Negations = Array.AsReadOnly((negations ?? throw new ArgumentNullException(nameof(negations))).ToArray());
            Specificity = (name == null ? 0 : 1_000_000) +
                (classes.Count + pseudoStates.Count + Predicates.Count) * 1_000 +
                (typeName == null ? 0 : 1) + negations.Sum(negation => negation.Specificity);
        }

        public string TypeName { get; }
        public bool IsUniversal { get; }
        public string Name { get; }
        public IReadOnlyList<string> Classes { get; }
        public IReadOnlyList<string> PseudoStates { get; }
        public IReadOnlyList<StyleSelectorCompound> Negations { get; }
        /// <summary>Attribute and structural predicates such as [data-x] or :nth-child().</summary>
        public IReadOnlyList<StyleSelectorPredicate> Predicates { get; }
        public int Specificity { get; }
        internal bool HasStructuralPredicates => Predicates.Any(p => p.Kind != "attribute");
    }

    public sealed class StyleSelectorArm
    {
        public StyleSelectorArm(IReadOnlyList<StyleSelectorCompound> compounds, IReadOnlyList<StyleSelectorCombinator> combinators)
        {
            if (compounds == null) throw new ArgumentNullException(nameof(compounds));
            if (combinators == null) throw new ArgumentNullException(nameof(combinators));
            if (compounds.Count == 0 || combinators.Count != compounds.Count - 1)
                throw new ArgumentException("A selector arm requires one fewer combinator than compounds.");
            Compounds = Array.AsReadOnly(compounds.ToArray());
            Combinators = Array.AsReadOnly(combinators.ToArray());
            Specificity = compounds.Sum(compound => compound.Specificity);
        }

        public IReadOnlyList<StyleSelectorCompound> Compounds { get; }
        public IReadOnlyList<StyleSelectorCombinator> Combinators { get; }
        public int Specificity { get; }
        public StyleSelectorCompound Subject => Compounds[Compounds.Count - 1];
    }

    public sealed class StyleSelector
    {
        private static readonly HashSet<string> SupportedPseudoStates = new HashSet<string>(StringComparer.Ordinal)
        {
            "hover", "focus", "focus-within", "disabled", "pressed", "checked", "selected", "current",
        };

        public StyleSelector(IReadOnlyList<StyleSelectorArm> arms)
        {
            if (arms == null) throw new ArgumentNullException(nameof(arms));
            if (arms.Count == 0) throw new ArgumentException("A selector requires at least one arm.", nameof(arms));
            Arms = Array.AsReadOnly(arms.ToArray());
            Specificity = arms.Max(arm => arm.Specificity);
        }

        public IReadOnlyList<StyleSelectorArm> Arms { get; }
        public string TypeName => Arms.Count == 1 ? Arms[0].Subject.TypeName : null;
        public string Name => Arms.Count == 1 ? Arms[0].Subject.Name : null;
        public IReadOnlyList<string> Classes => Arms.Count == 1 ? Arms[0].Subject.Classes : Array.Empty<string>();
        public IReadOnlyList<string> PseudoStates => Arms.Count == 1 ? Arms[0].Subject.PseudoStates : Array.Empty<string>();
        public int Specificity { get; }
        /// <summary>The selector as written, when it was produced by <see cref="Parse"/>.</summary>
        public string Text { get; internal set; }
        /// <summary>Returns the selector as text.</summary>
        public override string ToString() => Text ?? string.Join(", ", Arms.Select(FormatArm));

        private static string FormatArm(StyleSelectorArm arm)
        {
            var builder = new System.Text.StringBuilder();
            for (var index = 0; index < arm.Compounds.Count; index++)
            {
                if (index > 0)
                    builder.Append(arm.Combinators[index - 1] switch
                    {
                        StyleSelectorCombinator.Child => " > ",
                        StyleSelectorCombinator.TemplateChild => " >> ",
                        _ => " ",
                    });
                builder.Append(FormatCompound(arm.Compounds[index]));
            }
            return builder.ToString();
        }

        private static string FormatCompound(StyleSelectorCompound compound)
        {
            var text = compound.TypeName ?? (compound.IsUniversal ? "*" : string.Empty);
            if (compound.Name != null) text += "#" + compound.Name;
            foreach (var className in compound.Classes) text += "." + className;
            foreach (var pseudo in compound.PseudoStates) text += ":" + pseudo;
            foreach (var negation in compound.Negations) text += ":not(" + FormatCompound(negation) + ")";
            return text;
        }
        internal bool HasAncestorDependencies => Arms.Any(arm => arm.Compounds.Count > 1);

        internal bool CouldMatchSubject(Control control) =>
            Arms.Any(arm => CouldMatchCompound(arm.Subject, control));

        internal bool CouldDependOnAncestor(Control control) => Arms.Any(arm =>
            arm.Compounds.Take(arm.Compounds.Count - 1).Any(compound => CouldMatchCompound(compound, control)));

        public static StyleSelector Parse(string selector) => new Parser(selector).Parse();

        public static bool IsStandardPseudoState(string state) => SupportedPseudoStates.Contains(state);

        internal bool TryMatch(Control control, Control scopeRoot, Func<Control, StyleControlState> stateFor, out int specificity)
        {
            specificity = -1;
            foreach (var arm in Arms)
                if (MatchesArm(arm, control, scopeRoot, stateFor)) specificity = Math.Max(specificity, arm.Specificity);
            return specificity >= 0;
        }

        private static bool MatchesArm(StyleSelectorArm arm, Control subject, Control scopeRoot, Func<Control, StyleControlState> stateFor)
        {
            var candidate = subject;
            var compoundIndex = arm.Compounds.Count - 1;
            if (!MatchesCompound(arm.Compounds[compoundIndex], candidate, stateFor(candidate))) return false;
            while (compoundIndex > 0)
            {
                var left = arm.Compounds[compoundIndex - 1];
                var combinator = arm.Combinators[compoundIndex - 1];
                if (combinator == StyleSelectorCombinator.TemplateChild)
                {
                    if (!StyleBoundary.TryGetContaining(candidate, out _, out var boundary) ||
                        boundary.Kind != StyleBoundaryKind.ControlTemplate || boundary.Owner == null)
                        return false;
                    candidate = boundary.Owner;
                    if (!MatchesCompound(left, candidate, stateFor(candidate))) return false;
                }
                else if (combinator == StyleSelectorCombinator.Child)
                {
                    candidate = StyleBoundary.GetOrdinaryParent(candidate);
                    if (candidate == null || !MatchesCompound(left, candidate, stateFor(candidate))) return false;
                }
                else
                {
                    candidate = StyleBoundary.GetOrdinaryParent(candidate);
                    while (candidate != null && !MatchesCompound(left, candidate, stateFor(candidate)))
                        candidate = StyleBoundary.GetOrdinaryParent(candidate);
                    if (candidate == null) return false;
                }
                compoundIndex--;
            }
            return ReferenceEquals(StyleBoundary.GetStyleScope(candidate), StyleBoundary.GetStyleScope(scopeRoot));
        }

        private static bool MatchesCompound(StyleSelectorCompound compound, Control control, StyleControlState state)
        {
            if (compound.TypeName != null && !MatchesType(control.GetType(), compound.TypeName)) return false;
            if (compound.Name != null && control.Name != compound.Name) return false;
            foreach (var className in compound.Classes) if (!control.Classes.Contains(className)) return false;
            foreach (var pseudoState in compound.PseudoStates)
                if (!control.IsPseudoStateActive(pseudoState)) return false;
            foreach (var predicate in compound.Predicates)
                if (!predicate.Matches(control)) return false;
            foreach (var negation in compound.Negations)
                if (MatchesCompound(negation, control, state)) return false;
            return true;
        }

        private static bool CouldMatchCompound(StyleSelectorCompound compound, Control control)
        {
            if (compound.TypeName != null && !MatchesType(control.GetType(), compound.TypeName)) return false;
            if (compound.Name != null && control.Name != compound.Name) return false;
            foreach (var className in compound.Classes) if (!control.Classes.Contains(className)) return false;
            foreach (var pseudoState in compound.PseudoStates)
                if (!control.IsPseudoStateActive(pseudoState)) return false;
            foreach (var predicate in compound.Predicates)
                if (!predicate.Matches(control)) return false;
            foreach (var negation in compound.Negations)
                if (CouldMatchCompound(negation, control)) return false;
            return true;
        }

        private static bool MatchesType(Type type, string typeName)
        {
            for (var current = type; current != null && typeof(Control).IsAssignableFrom(current); current = current.BaseType)
                if (current.Name == typeName || current.FullName == typeName) return true;
            return false;
        }

        private sealed class Parser
        {
            private readonly string _source;
            private int _index;

            public Parser(string source) => _source = source;

            public StyleSelector Parse()
            {
                if (string.IsNullOrWhiteSpace(_source)) throw new FormatException("A style selector is required.");
                var arms = new List<StyleSelectorArm>();
                SkipWhitespace();
                while (true)
                {
                    arms.Add(ParseArm());
                    SkipWhitespace();
                    if (_index == _source.Length) break;
                    if (_source[_index] != ',') throw Error("contains an unexpected token");
                    _index++;
                    SkipWhitespace();
                    if (_index == _source.Length || _source[_index] == ',') throw Error("contains an empty selector-list arm");
                }
                return new StyleSelector(arms) { Text = _source.Trim() };
            }

            private StyleSelectorArm ParseArm()
            {
                var compounds = new List<StyleSelectorCompound> { ParseCompound() };
                var combinators = new List<StyleSelectorCombinator>();
                while (true)
                {
                    var hadWhitespace = SkipWhitespace();
                    if (_index == _source.Length || _source[_index] == ',') break;
                    StyleSelectorCombinator combinator;
                    if (StartsWith(">>"))
                    {
                        combinator = StyleSelectorCombinator.TemplateChild;
                        _index += 2;
                        SkipWhitespace();
                    }
                    else if (_source[_index] == '>')
                    {
                        combinator = StyleSelectorCombinator.Child;
                        _index++;
                        SkipWhitespace();
                    }
                    else if (hadWhitespace) combinator = StyleSelectorCombinator.Descendant;
                    else throw Error("is missing a combinator");
                    if (_index == _source.Length || _source[_index] is ',' or '>') throw Error("contains a malformed combinator");
                    combinators.Add(combinator);
                    compounds.Add(ParseCompound());
                }
                return new StyleSelectorArm(compounds, combinators);
            }

            private StyleSelectorCompound ParseCompound()
            {
                string typeName = null;
                string name = null;
                var universal = false;
                var classes = new List<string>();
                var pseudoStates = new List<string>();
                var negations = new List<StyleSelectorCompound>();
                var predicates = new List<StyleSelectorPredicate>();
                var hasTerm = false;
                if (_index < _source.Length && _source[_index] == '*')
                {
                    universal = true;
                    hasTerm = true;
                    _index++;
                }
                else if (_index < _source.Length && IsIdentifierStart(_source[_index]))
                {
                    typeName = ParseIdentifier();
                    if (_index + 1 < _source.Length && _source[_index] == ':' && char.IsUpper(_source[_index + 1]))
                    {
                        _index++;
                        typeName += ":" + ParseIdentifier();
                    }
                    hasTerm = true;
                }
                while (_index < _source.Length && _source[_index] is '.' or '#' or ':' or '[')
                {
                    hasTerm = true;
                    var marker = _source[_index++];
                    if (marker == '[')
                    {
                        var close = _source.IndexOf(']', _index);
                        if (close < 0) throw Error("contains an unterminated [attribute]");
                        var body = _source.Substring(_index, close - _index).Trim();
                        _index = close + 1;
                        var eq = body.IndexOf('=');
                        var attribute = (eq < 0 ? body : body.Substring(0, eq)).Trim();
                        var attributeValue = eq < 0 ? null : body.Substring(eq + 1).Trim().Trim('"', '\'');
                        if (attribute.Length == 0) throw Error("contains an empty [attribute]");
                        // [data-state] reads the data attribute "state", as dataset.state does in a browser.
                        if (attribute.StartsWith("data-", StringComparison.Ordinal)) attribute = attribute.Substring(5);
                        predicates.Add(new StyleSelectorPredicate("attribute", attribute, attributeValue, 0, 0));
                        continue;
                    }
                    if (marker == ':' && (StartsWith("first-child") || StartsWith("last-child") || StartsWith("only-child") || StartsWith("empty")))
                    {
                        var structural = ParseIdentifier();
                        predicates.Add(new StyleSelectorPredicate(structural, null, null, 0, 0));
                        continue;
                    }
                    if (marker == ':' && StartsWith("nth-child("))
                    {
                        _index += "nth-child(".Length;
                        var close = _source.IndexOf(')', _index);
                        if (close < 0) throw Error("contains an unterminated :nth-child(...)");
                        var formula = _source.Substring(_index, close - _index).Replace(" ", string.Empty);
                        _index = close + 1;
                        int a = 0, b = 0;
                        if (formula == "odd") { a = 2; b = 1; }
                        else if (formula == "even") { a = 2; b = 0; }
                        else if (formula.Contains('n'))
                        {
                            var n = formula.IndexOf('n');
                            var left = formula.Substring(0, n);
                            a = left == string.Empty || left == "+" ? 1 : left == "-" ? -1 : int.Parse(left, System.Globalization.CultureInfo.InvariantCulture);
                            var right = formula.Substring(n + 1);
                            b = right.Length == 0 ? 0 : int.Parse(right, System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else b = int.Parse(formula, System.Globalization.CultureInfo.InvariantCulture);
                        predicates.Add(new StyleSelectorPredicate("nth-child", null, null, a, b));
                        continue;
                    }
                    if (marker == ':' && StartsWith("not("))
                    {
                        _index += 4;
                        var negation = ParseCompound();
                        if (_index >= _source.Length || _source[_index] != ')') throw Error("contains an unterminated :not(...)");
                        _index++;
                        negations.Add(negation);
                        continue;
                    }
                    var value = ParseIdentifier();
                    if (marker == '.') classes.Add(value);
                    else if (marker == '#')
                    {
                        if (name != null) throw Error("can contain only one name per compound");
                        name = value;
                    }
                    else
                    {
                        pseudoStates.Add(value);
                    }
                }
                if (!hasTerm) throw Error("contains an empty compound selector");
                return new StyleSelectorCompound(typeName, universal, name, classes, pseudoStates, negations, predicates);
            }

            private string ParseIdentifier()
            {
                if (_index >= _source.Length || !IsIdentifierStart(_source[_index])) throw Error("contains an empty or invalid term");
                var start = _index++;
                while (_index < _source.Length && IsIdentifierPart(_source[_index])) _index++;
                return _source.Substring(start, _index - start);
            }

            private bool SkipWhitespace()
            {
                var start = _index;
                while (_index < _source.Length && char.IsWhiteSpace(_source[_index])) _index++;
                return _index != start;
            }

            private bool StartsWith(string value) =>
                _index + value.Length <= _source.Length && string.CompareOrdinal(_source, _index, value, 0, value.Length) == 0;

            private FormatException Error(string message) => new FormatException($"Selector '{_source}' {message} at column {_index + 1}.");
            private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';
            private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '-';
        }
    }

    internal enum StyleBoundaryKind { ControlTemplate, DataTemplate, ItemsPanelTemplate }

    internal sealed class StyleBoundaryInfo
    {
        public StyleBoundaryInfo(StyleBoundaryKind kind, TemplatedControl owner, bool transparent = false)
        {
            Kind = kind;
            Owner = owner;
            Transparent = transparent;
        }

        /// <summary>A transparent boundary does not isolate styles: rules from outside reach through it.</summary>
        public bool Transparent { get; }

        public StyleBoundaryKind Kind { get; }
        public TemplatedControl Owner { get; }
    }

    internal static class StyleBoundary
    {
        private static readonly ConditionalWeakTable<Control, StyleBoundaryInfo> Boundaries = new ConditionalWeakTable<Control, StyleBoundaryInfo>();

        public static void Set(Control root, FrameworkTemplate template, TemplatedControl owner)
        {
            Boundaries.Remove(root);
            var kind = template switch
            {
                ControlTemplate => StyleBoundaryKind.ControlTemplate,
                DataTemplate => StyleBoundaryKind.DataTemplate,
                ItemsPanelTemplate => StyleBoundaryKind.ItemsPanelTemplate,
                _ => throw new ArgumentOutOfRangeException(nameof(template)),
            };
            Boundaries.Add(root, new StyleBoundaryInfo(kind, owner, template is DataTemplate { IsolateStyles: false }));
        }

        public static void Clear(Control root) => Boundaries.Remove(root);

        public static Control GetContainingRoot(Control control) =>
            TryGetContaining(control, out var root, out _) ? root : null;

        public static bool TryGetContaining(Control control, out Control root, out StyleBoundaryInfo boundary) =>
            TryGetContaining(control, lenient: false, out root, out boundary);

        /// <summary>The style scope a control's rules come from: like the containing boundary, but presented content and transparent data templates belong to the scope where they are used.</summary>
        public static Control GetStyleScope(Control control) =>
            TryGetContaining(control, lenient: true, out var root, out _) ? root : null;

        private static bool TryGetContaining(Control control, bool lenient, out Control root, out StyleBoundaryInfo boundary)
        {
            // Content that a templated control presents (a GroupBox body, a scroll viewport, a list row) is written in the
            // scope where the control is used, not inside the control's template, so styles from that scope reach it.
            var presentedContent = false;
            for (var current = control; current != null; current = current.VisualParent)
            {
                if (lenient && ((current.VisualParent is ContentPresenter presenter && ReferenceEquals(presenter.PresentedControl, current)) || current.VisualParent is ScrollPresenter))
                    presentedContent = true;
                if (!Boundaries.TryGetValue(current, out boundary)) continue;
                if (!lenient) { root = current; return true; }
                if (boundary.Transparent)
                {
                    presentedContent = true;
                    continue;
                }

                if (boundary.Kind == StyleBoundaryKind.ItemsPanelTemplate)
                {
                    // An items panel only holds the item containers, which belong to the items control's own scope.
                    presentedContent = true;
                    continue;
                }

                if (presentedContent)
                {
                    if (current is ContentPresenter || current is ItemsPresenter || current is ScrollPresenter)
                        continue;
                    if (boundary.Kind == StyleBoundaryKind.ControlTemplate && boundary.Owner != null)
                    {
                        presentedContent = false;
                        current = boundary.Owner;
                        if (Boundaries.TryGetValue(current, out var ownerBoundary) && !ownerBoundary.Transparent)
                        {
                            root = current;
                            boundary = ownerBoundary;
                            return true;
                        }
                        continue;
                    }
                }

                root = current;
                return true;
            }
            root = null;
            boundary = null;
            return false;
        }

        public static Control GetOrdinaryParent(Control control)
        {
            var boundaryRoot = GetContainingRoot(control);
            if (ReferenceEquals(control, boundaryRoot)) return null;
            var parent = control.VisualParent;
            return ReferenceEquals(GetContainingRoot(parent), boundaryRoot) ? parent : null;
        }
    }

    public static class StyleEngine
    {
        public static IDisposable Attach(Control root, IEnumerable<Style> styles)
        {
            var snapshot = styles?.ToArray() ?? throw new ArgumentNullException(nameof(styles));
            return XamlAttachment.RegisterReactivatable(root, () => new StyleAttachment(root, snapshot));
        }

        /// <summary>
        /// Attaches every <see cref="Style"/> found in <paramref name="source"/>'s resources, including merged dictionaries,
        /// to <paramref name="root"/> and makes the source's resources visible to it. A data template, item row or compiled view is its own style boundary, so styles
        /// attached higher in the tree never reach it; calling this from the view opts it in to a shared style set.
        /// </summary>
        public static IDisposable AttachFrom(Control root, Control source)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (source == null) throw new ArgumentNullException(nameof(source));
            var styles = new List<Style>();
            CollectStyles(source.Resources, styles, new HashSet<ResourceDictionary>());
            // The styles' StaticResource setters resolve lexically from the styled control, and a template-built view is
            // not yet under the source, so its resources must be reachable from the view itself.
            if (!ReferenceEquals(root.Resources, source.Resources) && !root.Resources.MergedDictionaries.Contains(source.Resources))
                root.Resources.MergedDictionaries.Add(source.Resources);
            return Attach(root, styles);
        }

        private static void CollectStyles(ResourceDictionary dictionary, List<Style> styles, HashSet<ResourceDictionary> seen)
        {
            if (!seen.Add(dictionary)) return;
            foreach (var merged in dictionary.MergedDictionaries) CollectStyles(merged, styles, seen);
            foreach (var value in dictionary.Values)
                if (value is Style style) styles.Add(style);
        }
    }

    internal sealed class StyleControlState
    {
    }

    internal sealed class StyleAttachment : IDisposable
    {
        internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, List<StyleAttachment>> Registry =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, List<StyleAttachment>>();
        internal Control Root => _root;
        internal Style[] Styles => _styles;
        internal bool Contains(Control control) => _controls.ContainsKey(control);
        internal bool IsApplied(Control control, int styleIndex) =>
            _controls.TryGetValue(control, out var registration) && registration.Applied.ContainsKey(styleIndex);
        internal bool Disposed => _disposed;
        internal bool Matches(Control control, int styleIndex, out int specificity)
        {
            specificity = -1;
            var style = _styles[styleIndex];
            return (style.Condition == null || style.Condition.Matches(_root.Context)) && style.Selector.TryMatch(control, _root, StateFor, out specificity);
        }

        private sealed class ControlRegistration
        {
            public readonly StyleControlState State = new StyleControlState();
            public readonly Dictionary<int, AppliedStyle> Applied = new Dictionary<int, AppliedStyle>();
            public readonly List<Action> Unsubscribe = new List<Action>();
        }

        private sealed class AppliedStyle
        {
            public AppliedStyle(int specificity, List<IDisposable> values)
            {
                Specificity = specificity;
                Values = values;
            }

            public int Specificity { get; }
            public List<IDisposable> Values { get; }
        }

        private readonly Style[] _styles;
        private readonly Control _root;
        private readonly Dictionary<Control, ControlRegistration> _controls = new Dictionary<Control, ControlRegistration>();
        private UIContext _context;
        private bool _disposed;

        public StyleAttachment(Control root, Style[] styles)
        {
            _root = root;
            _styles = styles;
            _hasStructural = styles.Any(style => style.Selector.Arms.Any(arm => arm.Compounds.Any(compound => compound.HasStructuralPredicates)));
            Registry.GetOrCreateValue(root).Add(this);
            try
            {
                AttachTree(root);
                RefreshContext();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void AttachTree(Control control)
        {
            if (_controls.ContainsKey(control)) return;
            var registration = new ControlRegistration();
            _controls.Add(control, registration);
            Subscribe(control, registration);
            Evaluate(control, registration);
            foreach (var child in control.VisualChildren) AttachTree(child);
        }

        private void DetachTree(Control control)
        {
            ExceptionDispatchInfo failure = null;
            foreach (var child in control.VisualChildren.ToArray())
            {
                try { DetachTree(child); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            if (!_controls.Remove(control, out var registration)) return;
            foreach (var unsubscribe in registration.Unsubscribe)
            {
                try { unsubscribe(); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            foreach (var applied in registration.Applied.Values)
                for (var index = applied.Values.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        if (applied.Values[index] is IStyleAttachmentValue attachmentValue) attachmentValue.DisposeImmediately();
                        else applied.Values[index].Dispose();
                    }
                    catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
                }
            failure?.Throw();
        }

        private void Subscribe(Control control, ControlRegistration registration)
        {
            EventHandler changed = (_, _) => EvaluateChangedControl(control, registration);
            EventHandler<ControlPseudoStateChangedEventArgs> pseudoStateChanged = (_, _) => EvaluateChangedControl(control, registration);
            Action<Control, Control> added = (_, child) => { AttachTree(child); if (_hasStructural) EvaluateChildren(control); };
            Action<Control, Control> removed = (_, child) => { DetachTree(child); if (_hasStructural) EvaluateChildren(control); };
            EventHandler<ControlParentChangedEventArgs> parentChanged = (_, _) => EvaluateTree(control);
            EventHandler attached = (_, _) => { if (ReferenceEquals(control, _root)) RefreshContext(); };
            EventHandler detached = (_, _) => { if (ReferenceEquals(control, _root)) RefreshContext(); };
            control.Classes.Changed += changed;
            control.DataChanged += changed;
            control.NameChanged += changed;
            control.PseudoStateChanged += pseudoStateChanged;
            control.VisualChildAdded += added;
            control.VisualChildRemoved += removed;
            control.ParentChanged += parentChanged;
            control.Attached += attached;
            control.Detached += detached;
            registration.Unsubscribe.Add(() => control.Classes.Changed -= changed);
            registration.Unsubscribe.Add(() => control.DataChanged -= changed);
            registration.Unsubscribe.Add(() => control.NameChanged -= changed);
            registration.Unsubscribe.Add(() => control.PseudoStateChanged -= pseudoStateChanged);
            registration.Unsubscribe.Add(() => control.VisualChildAdded -= added);
            registration.Unsubscribe.Add(() => control.VisualChildRemoved -= removed);
            registration.Unsubscribe.Add(() => control.ParentChanged -= parentChanged);
            registration.Unsubscribe.Add(() => control.Attached -= attached);
            registration.Unsubscribe.Add(() => control.Detached -= detached);
        }

        private bool _hasStructural;

        // Structural selectors (:first-child, :nth-child, :empty) depend on siblings, so a child list change re-evaluates the children.
        private void EvaluateChildren(Control parent)
        {
            if (_controls.TryGetValue(parent, out var parentRegistration)) Evaluate(parent, parentRegistration, false);
            foreach (var child in parent.VisualChildren.ToArray())
                if (_controls.TryGetValue(child, out var registration)) Evaluate(child, registration, false);
        }

        private void EvaluateChangedControl(Control control, ControlRegistration registration)
        {
            Evaluate(control, registration, false);
            foreach (var child in control.VisualChildren) EvaluateAncestorDependents(child, control);
        }

        private void EvaluateAncestorDependents(Control control, Control changedAncestor)
        {
            if (_controls.TryGetValue(control, out var registration)) Evaluate(control, registration, true, changedAncestor);
            foreach (var child in control.VisualChildren) EvaluateAncestorDependents(child, changedAncestor);
        }

        private void EvaluateTree(Control control)
        {
            if (_controls.TryGetValue(control, out var registration)) Evaluate(control, registration, false);
            foreach (var child in control.VisualChildren) EvaluateTree(child);
        }

        private void Evaluate(Control control, ControlRegistration registration, bool ancestorOnly = false, Control changedAncestor = null)
        {
            if (_disposed) return;
            for (var styleIndex = 0; styleIndex < _styles.Length; styleIndex++)
            {
                if (ancestorOnly && !_styles[styleIndex].Selector.HasAncestorDependencies) continue;
                if (ancestorOnly && changedAncestor != null &&
                    !_styles[styleIndex].Selector.CouldDependOnAncestor(changedAncestor) &&
                    !registration.Applied.ContainsKey(styleIndex)) continue;
                if (!ancestorOnly && !_styles[styleIndex].Selector.CouldMatchSubject(control) &&
                    !registration.Applied.ContainsKey(styleIndex)) continue;
                var specificity = -1;
                var matches = (_styles[styleIndex].Condition == null || _styles[styleIndex].Condition.Matches(_root.Context)) &&
                    _styles[styleIndex].Selector.TryMatch(control, _root, StateFor, out specificity);
                if (matches && registration.Applied.TryGetValue(styleIndex, out var current) && current.Specificity == specificity) continue;
                if (!matches)
                {
                    if (!registration.Applied.Remove(styleIndex, out var old)) continue;
                    for (var index = old.Values.Count - 1; index >= 0; index--) old.Values[index].Dispose();
                    continue;
                }
                if (registration.Applied.Remove(styleIndex, out var previous))
                    for (var index = previous.Values.Count - 1; index >= 0; index--) previous.Values[index].Dispose();
                var priority = (long)specificity * (_styles.Length + 1L) + styleIndex;
                var applied = new List<IDisposable>();
                using var scope = StyleApplicationScope.Enter(_root);
                try
                {
                    foreach (var setter in _styles[styleIndex].Setters)
                    {
                        var transition = _styles[styleIndex].Transitions
                            .OfType<IStyleTransitionRuntime>()
                            .FirstOrDefault(candidate => candidate.Matches(setter));
                        applied.Add(transition == null
                            ? setter.Apply(control, priority)
                            : transition.Apply(control, setter, priority));
                    }
                }
                catch
                {
                    for (var index = applied.Count - 1; index >= 0; index--)
                    {
                        try { applied[index].Dispose(); }
                        catch { }
                    }
                    throw;
                }
                registration.Applied.Add(styleIndex, new AppliedStyle(specificity, applied));
            }
        }

        private StyleControlState StateFor(Control control) =>
            _controls.TryGetValue(control, out var registration) ? registration.State : new StyleControlState();

        private void RefreshContext()
        {
            if (ReferenceEquals(_context, _root.Context)) return;
            if (_context != null) _context.AdaptiveEnvironmentChanged -= AdaptiveEnvironmentChanged;
            _context = _root.Context;
            if (_context != null) _context.AdaptiveEnvironmentChanged += AdaptiveEnvironmentChanged;
            EvaluateTree(_root);
        }

        private void AdaptiveEnvironmentChanged(object sender, EventArgs args) => EvaluateTree(_root);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Registry.TryGetValue(_root, out var registered)) registered.Remove(this);
            if (_context != null) _context.AdaptiveEnvironmentChanged -= AdaptiveEnvironmentChanged;
            ExceptionDispatchInfo failure = null;
            foreach (var control in _controls.Keys.ToArray())
            {
                try { DetachTree(control); }
                catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            failure?.Throw();
        }
    }
}