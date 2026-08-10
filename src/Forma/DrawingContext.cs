// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using Clipper2Lib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ClipperFillRule = Clipper2Lib.FillRule;

namespace Forma
{
    /// <summary>Backend-neutral retained drawing operations exposed by the active UI renderer.</summary>
    public sealed class DrawingContext
    {
        private readonly UIRenderContext _renderer;
        private readonly Stack<DrawingState> _states = new Stack<DrawingState>();
        private DrawingState _state = DrawingState.Default;

        internal DrawingContext(UIRenderContext renderer)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        }

        /// <summary>Fills a transformed simple path with a solid color.</summary>
        public void FillPath(DrawingPath path, Color color, Matrix transform, float tolerance = .25f)
        {
            var mesh = DrawingPathTessellator.TessellateFill(path, transform, tolerance, FillRule.NonZero);
            Draw(mesh, _ => color);
        }

        /// <summary>Fills a transformed simple path with a backend-neutral brush.</summary>
        public void FillPath(DrawingPath path, Brush brush, Rectangle brushBounds, Matrix transform, float tolerance = .25f)
            => FillPath(path, brush, brushBounds, transform, FillRule.NonZero, tolerance);

        /// <summary>Fills a transformed path using the selected winding rule.</summary>
        public void FillPath(DrawingPath path, Brush brush, Rectangle brushBounds, Matrix transform, FillRule fillRule, float tolerance = .25f)
        {
            if (brush == null) throw new ArgumentNullException(nameof(brush));
            var mesh = DrawingPathTessellator.TessellateFill(path, transform, tolerance, fillRule);
            if (brush is ImageBrush imageBrush)
            {
                FillImageBrush(mesh, imageBrush, brushBounds);
                return;
            }
            DrawBrush(mesh, brush, brushBounds);
        }

        /// <summary>Fills a simple path with a solid color.</summary>
        public void FillPath(DrawingPath path, Color color, float tolerance = .25f) =>
            FillPath(path, color, Matrix.Identity, tolerance);

        /// <summary>Strokes a transformed path with a linear gradient.</summary>
        public void StrokePath(DrawingPath path, DrawingLinearGradient gradient, float thickness, Matrix transform, float tolerance = .25f)
        {
            if (gradient == null) throw new ArgumentNullException(nameof(gradient));
            var mesh = DrawingPathTessellator.TessellateStroke(path, transform, thickness, tolerance, null);
            Draw(mesh, gradient.Sample);
        }

        /// <summary>Strokes a transformed path with a backend-neutral brush.</summary>
        public void StrokePath(DrawingPath path, Brush brush, Rectangle brushBounds, float thickness, Matrix transform, float tolerance = .25f)
            => StrokePath(path, brush, brushBounds, thickness, transform, null, tolerance);

        /// <summary>Strokes a transformed path with bounded cap, join, dash, and alignment semantics.</summary>
        public void StrokePath(DrawingPath path, Brush brush, Rectangle brushBounds, float thickness, Matrix transform, StrokeStyle style, float tolerance = .25f)
        {
            if (brush == null) throw new ArgumentNullException(nameof(brush));
            var mesh = DrawingPathTessellator.TessellateStroke(path, transform, thickness, tolerance, style);
            DrawBrush(mesh, brush, brushBounds);
        }

        /// <summary>Saves the current drawing state.</summary>
        public void Save()
        {
            if (_states.Count >= DrawingContextLimits.MaximumStateDepth)
                throw new InvalidOperationException($"Drawing state depth cannot exceed {DrawingContextLimits.MaximumStateDepth}.");
            _states.Push(_state);
        }

        /// <summary>Restores the most recently saved drawing state.</summary>
        public void Restore()
        {
            if (_states.Count == 0) throw new InvalidOperationException("No drawing state has been saved.");
            _state = _states.Pop();
        }

        /// <summary>Intersects subsequent meshes with one transformed geometry path.</summary>
        public void Clip(DrawingPath path, Matrix transform, float tolerance = .25f)
            => Clip(path, transform, FillRule.NonZero, tolerance);

        /// <summary>Intersects subsequent meshes with one transformed geometry path using the selected winding rule.</summary>
        public void Clip(DrawingPath path, Matrix transform, FillRule fillRule, float tolerance = .25f)
        {
            var contours = DrawingPathFlattener.Flatten(path, transform, tolerance);
            var clip = DrawingPathClipper.NormalizeContours(contours, fillRule);
            if (_state.Clip != null) clip = Clipper.Intersect(_state.Clip, clip, ClipperFillRule.NonZero, GeometryClipper.DecimalPrecision);
            _state = _state.WithClip(clip);
        }

        /// <summary>Multiplies subsequent vertex alpha by a linear brush mask.</summary>
        public void SetOpacityMask(DrawingLinearGradient mask)
        {
            _state = _state.WithOpacityMask(new DrawingOpacityMask(mask ?? throw new ArgumentNullException(nameof(mask))));
        }

        /// <summary>Multiplies subsequent vertex alpha by a retained brush mask sampled in the supplied bounds.</summary>
        public void SetOpacityMask(Brush mask, Rectangle bounds)
        {
            _state = _state.WithOpacityMask(new DrawingOpacityMask(mask ?? throw new ArgumentNullException(nameof(mask)), bounds));
        }

        /// <summary>Applies one bounded color-matrix effect to subsequent drawing.</summary>
        public void SetEffect(DrawingColorMatrixEffect effect)
        {
            _state = _state.WithEffect(effect ?? throw new ArgumentNullException(nameof(effect)));
        }

        /// <summary>Multiplies subsequent drawing opacity within the current saved state.</summary>
        public void MultiplyOpacity(float opacity)
        {
            if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
            _state = _state.WithOpacity(_state.Opacity * opacity);
        }

        /// <summary>Measures one unwrapped text run using the same layout engine as retained controls.</summary>
        public Vector2 MeasureText(UIFont font, string text)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            return TextMetrics.Measure(font, text ?? string.Empty);
        }

        /// <summary>Draws one unwrapped text run through the active retained text backend.</summary>
        public void DrawText(UIFont font, string text, Vector2 position, Color color)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            _renderer.Text(font, text ?? string.Empty, position, color);
        }

        /// <summary>Draws a bitmap or atlas region into a transformed logical rectangle.</summary>
        public void DrawImage(Texture2D source, Rectangle? sourceRectangle, Rectangle bounds, Matrix transform, Color tint, ImageSamplingMode samplingMode = ImageSamplingMode.Linear)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            DrawImageCore(source, sourceRectangle, bounds, transform, tint, samplingMode);
        }

        private void Draw(DrawingMesh mesh, Func<Vector2, Color> colorAt)
        {
            if (_state.Clip != null) mesh = DrawingPathClipper.Clip(mesh, _state.Clip);
            _renderer.Draw(mesh, point =>
            {
                var color = colorAt(point);
                if (_state.OpacityMask != null)
                {
                    color *= _state.OpacityMask.Sample(point).A / 255f;
                }
                color *= _state.Opacity;
                return _state.Effect?.Apply(color) ?? color;
            });
        }

        private void DrawBrush(DrawingMesh mesh, Brush brush, Rectangle bounds)
        {
            if (brush is not RadialGradientBrush && brush is not ConicGradientBrush)
            {
                Draw(mesh, point => brush.Sample(point, bounds));
                return;
            }
            if (_state.Clip != null) mesh = DrawingPathClipper.Clip(mesh, _state.Clip);
            _renderer.DrawSampled(mesh, bounds, point =>
            {
                var color = brush.Sample(point, bounds);
                if (_state.OpacityMask != null) color *= _state.OpacityMask.Sample(point).A / 255f;
                color *= _state.Opacity;
                return _state.Effect?.Apply(color) ?? color;
            });
        }

        private void DrawImageCore(Texture2D source, Rectangle? sourceRectangle, Rectangle bounds, Matrix transform, Color tint, ImageSamplingMode samplingMode = ImageSamplingMode.Linear)
        {
            if (source == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            var path = new DrawingPath().MoveTo(Vector2.Zero).LineTo(new Vector2(bounds.Width, 0)).LineTo(new Vector2(bounds.Width, bounds.Height)).LineTo(new Vector2(0, bounds.Height)).Close();
            var mesh = DrawingPathTessellator.TessellateFill(path, transform, .25f);
            if (_state.Clip != null) mesh = DrawingPathClipper.Clip(mesh, _state.Clip);
            var inverse = Matrix.Invert(transform);
            var sourceBounds = sourceRectangle ?? new Rectangle(0, 0, source.Width, source.Height);
            _renderer.DrawTextured(mesh, source, point =>
            {
                var local = Vector2.Transform(point, inverse);
                var coordinate = new Vector2(
                    (sourceBounds.X + local.X / bounds.Width * sourceBounds.Width) / source.Width,
                    (sourceBounds.Y + local.Y / bounds.Height * sourceBounds.Height) / source.Height);
                var color = tint;
                if (_state.OpacityMask != null) color *= _state.OpacityMask.Sample(point).A / 255f;
                color *= _state.Opacity;
                return (coordinate, _state.Effect?.Apply(color) ?? color);
            }, samplingMode, ImageTileMode.None);
        }

        internal void DrawImageUncomposited(Texture2D source, Rectangle bounds, DrawingPath clipPath = null, Matrix clipTransform = default)
            => DrawImageUncomposited(source, null, bounds, ImageSamplingMode.Nearest, clipPath, clipTransform);

        internal void DrawImageUncomposited(Texture2D source, Rectangle? sourceRectangle, Rectangle bounds, ImageSamplingMode samplingMode, DrawingPath clipPath = null, Matrix clipTransform = default)
            => DrawImageUncomposited(source, sourceRectangle, new Vector4(bounds.X, bounds.Y, bounds.Width, bounds.Height), samplingMode, clipPath, clipTransform);

        internal void DrawImageUncomposited(Texture2D source, Rectangle? sourceRectangle, Vector4 bounds, ImageSamplingMode samplingMode, DrawingPath clipPath = null, Matrix clipTransform = default)
        {
            var savedState = _state;
            _state = DrawingState.Default;
            try
            {
                if (clipPath != null) Clip(clipPath, clipTransform);
                if (source == null || bounds.Z <= 0 || bounds.W <= 0) return;
                var path = new DrawingPath().MoveTo(Vector2.Zero).LineTo(new Vector2(bounds.Z, 0)).LineTo(new Vector2(bounds.Z, bounds.W)).LineTo(new Vector2(0, bounds.W)).Close();
                var transform = Matrix.CreateTranslation(bounds.X, bounds.Y, 0);
                var mesh = DrawingPathTessellator.TessellateFill(path, transform, .25f);
                if (_state.Clip != null) mesh = DrawingPathClipper.Clip(mesh, _state.Clip);
                var inverse = Matrix.Invert(transform);
                var sourceBounds = sourceRectangle ?? new Rectangle(0, 0, source.Width, source.Height);
                _renderer.DrawTextured(mesh, source, point =>
                {
                    var local = Vector2.Transform(point, inverse);
                    return (new Vector2(
                        (sourceBounds.X + local.X / bounds.Z * sourceBounds.Width) / source.Width,
                        (sourceBounds.Y + local.Y / bounds.W * sourceBounds.Height) / source.Height), Color.White);
                }, samplingMode, ImageTileMode.None);
            }
            finally { _state = savedState; }
        }

        private void FillImageBrush(DrawingMesh mesh, ImageBrush brush, Rectangle bounds)
        {
            if (brush.Source == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            var placement = brush.GetPlacement(bounds);
            if (placement.Width <= 0 || placement.Height <= 0) return;
            var clip = brush.GetPaintBounds(bounds, placement);
            mesh = DrawingPathClipper.Clip(mesh, DrawingPathClipper.NormalizeContours(new[] { clip }, FillRule.NonZero));
            if (_state.Clip != null) mesh = DrawingPathClipper.Clip(mesh, _state.Clip);
            _renderer.DrawTextured(mesh, brush.Source, point =>
            {
                var samplePoint = brush.ToBrushSpace(point);
                var coordinate = brush.GetTextureCoordinate(samplePoint, placement);
                var color = brush.Tint * brush.Opacity;
                if (_state.OpacityMask != null) color *= _state.OpacityMask.Sample(point).A / 255f;
                color *= _state.Opacity;
                return (coordinate, _state.Effect?.Apply(color) ?? color);
            }, brush.SamplingMode, brush.TileMode);
        }

        private readonly struct DrawingState
        {
            public static DrawingState Default => new DrawingState(null, null, null, 1);

            private DrawingState(PathsD clip, DrawingOpacityMask opacityMask, DrawingColorMatrixEffect effect, float opacity)
            {
                Clip = clip;
                OpacityMask = opacityMask;
                Effect = effect;
                Opacity = opacity;
            }

            public PathsD Clip { get; }
            public DrawingOpacityMask OpacityMask { get; }
            public DrawingColorMatrixEffect Effect { get; }
            public float Opacity { get; }
            public DrawingState WithClip(PathsD clip) => new DrawingState(clip, OpacityMask, Effect, Opacity);
            public DrawingState WithOpacityMask(DrawingOpacityMask opacityMask) => new DrawingState(Clip, opacityMask, Effect, Opacity);
            public DrawingState WithEffect(DrawingColorMatrixEffect effect) => new DrawingState(Clip, OpacityMask, effect, Opacity);
            public DrawingState WithOpacity(float opacity) => new DrawingState(Clip, OpacityMask, Effect, opacity);
        }

        private sealed class DrawingOpacityMask
        {
            private readonly DrawingLinearGradient _gradient;
            private readonly Brush _brush;
            private readonly Rectangle _bounds;

            public DrawingOpacityMask(DrawingLinearGradient gradient) => _gradient = gradient;
            public DrawingOpacityMask(Brush brush, Rectangle bounds)
            {
                _brush = brush;
                _bounds = bounds;
            }

            public Color Sample(Vector2 point) => _brush?.Sample(point, _bounds) ?? _gradient.Sample(point);
        }
    }

    /// <summary>Hard limits enforced by the first backend-neutral drawing contract.</summary>
    public static class DrawingContextLimits
    {
        public const int MaximumStateDepth = 16;
        public const int MaximumPathCommands = 4_096;
        public const int MaximumSubdivisionDepth = 12;
        public const int MaximumClipVertices = 256;
        public const int MaximumMeshVertices = 16_384;
        public const int MaximumMeshIndices = 49_152;
        public const int MaximumEffectGroupLength = 4;
        public const int MaximumShadowCount = 4;
        public const float MaximumBlurRadius = 64f;
        public const float MaximumOffscreenExpansion = 128f;
        public const int MaximumOffscreenNestingDepth = 4;
        public const int MaximumRenderTargetDimension = 4_096;
        public const long MaximumRenderTargetArea = 16_777_216;
        public const long MaximumDeviceCacheBytes = 64L * 1024 * 1024;
        public const int MaximumRuntimeDiagnostics = 16;
    }

    /// <summary>A two-stop linear gradient in transformed drawing coordinates.</summary>
    public sealed class DrawingLinearGradient
    {
        public DrawingLinearGradient(Vector2 start, Vector2 end, Color startColor, Color endColor)
        {
            if (start == end) throw new ArgumentException("A linear gradient requires distinct start and end points.", nameof(end));
            Start = start;
            End = end;
            StartColor = startColor;
            EndColor = endColor;
        }

        public Vector2 Start { get; }
        public Vector2 End { get; }
        public Color StartColor { get; }
        public Color EndColor { get; }

        internal Color Sample(Vector2 point)
        {
            var axis = End - Start;
            var amount = MathHelper.Clamp(Vector2.Dot(point - Start, axis) / axis.LengthSquared(), 0, 1);
            return Color.Lerp(StartColor, EndColor, amount);
        }
    }

    /// <summary>A finite 4x5 color transform applied to one drawing state.</summary>
    public sealed class DrawingColorMatrixEffect
    {
        private readonly float[] _values;

        public DrawingColorMatrixEffect(IReadOnlyList<float> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Count != 20) throw new ArgumentException("A color matrix requires exactly 20 values.", nameof(values));
            _values = new float[20];
            for (var index = 0; index < values.Count; index++)
            {
                if (!float.IsFinite(values[index])) throw new ArgumentOutOfRangeException(nameof(values));
                _values[index] = values[index];
            }
        }

        internal Color Apply(Color color)
        {
            var red = color.R / 255f;
            var green = color.G / 255f;
            var blue = color.B / 255f;
            var alpha = color.A / 255f;
            return new Color(
                MathHelper.Clamp(red * _values[0] + green * _values[1] + blue * _values[2] + alpha * _values[3] + _values[4], 0, 1),
                MathHelper.Clamp(red * _values[5] + green * _values[6] + blue * _values[7] + alpha * _values[8] + _values[9], 0, 1),
                MathHelper.Clamp(red * _values[10] + green * _values[11] + blue * _values[12] + alpha * _values[13] + _values[14], 0, 1),
                MathHelper.Clamp(red * _values[15] + green * _values[16] + blue * _values[17] + alpha * _values[18] + _values[19], 0, 1));
        }
    }

    /// <summary>Backend-neutral path data consumed by Forma drawing and hit-test pipelines.</summary>
    public sealed class DrawingPath
    {
        private readonly List<DrawingPathCommand> _commands = new List<DrawingPathCommand>();
        private Vector2 _current;
        private Vector2 _contourStart;
        private Vector2 _lastCubicControl;
        private Vector2 _lastQuadraticControl;
        private bool _hasOpenContour;
        private bool _previousWasCubic;
        private bool _previousWasQuadratic;

        public bool IsFrozen { get; private set; }

        public void Freeze() => IsFrozen = true;

        /// <summary>The immutable-in-order command stream recorded for this path.</summary>
        public IReadOnlyList<DrawingPathCommand> Commands => _commands;

        /// <summary>Begins a contour at the supplied point.</summary>
        public DrawingPath MoveTo(Vector2 point)
        {
            AddCommand(new DrawingPathCommand(DrawingPathCommandKind.Move, point, default, default));
            _current = point;
            _contourStart = point;
            _hasOpenContour = true;
            ResetCurveState();
            return this;
        }

        public DrawingPath MoveToRelative(Vector2 offset) => MoveTo(_current + offset);

        /// <summary>Adds a straight segment to the supplied point.</summary>
        public DrawingPath LineTo(Vector2 point)
        {
            RequireOpenContour();
            AddCommand(new DrawingPathCommand(DrawingPathCommandKind.Line, point, default, default));
            _current = point;
            ResetCurveState();
            return this;
        }

        public DrawingPath LineToRelative(Vector2 offset) => LineTo(_current + offset);
        public DrawingPath HorizontalTo(float x) => LineTo(new Vector2(x, _current.Y));
        public DrawingPath HorizontalToRelative(float offset) => HorizontalTo(_current.X + offset);
        public DrawingPath VerticalTo(float y) => LineTo(new Vector2(_current.X, y));
        public DrawingPath VerticalToRelative(float offset) => VerticalTo(_current.Y + offset);

        /// <summary>Adds a cubic Bezier segment.</summary>
        public DrawingPath CubicTo(Vector2 control1, Vector2 control2, Vector2 end)
        {
            RequireOpenContour();
            AddCubic(control1, control2, end);
            _lastCubicControl = control2;
            _previousWasCubic = true;
            _previousWasQuadratic = false;
            return this;
        }

        public DrawingPath CubicToRelative(Vector2 control1, Vector2 control2, Vector2 end) =>
            CubicTo(_current + control1, _current + control2, _current + end);

        public DrawingPath SmoothCubicTo(Vector2 control2, Vector2 end)
        {
            var control1 = _previousWasCubic ? _current * 2 - _lastCubicControl : _current;
            return CubicTo(control1, control2, end);
        }

        public DrawingPath SmoothCubicToRelative(Vector2 control2, Vector2 end) =>
            SmoothCubicTo(_current + control2, _current + end);

        public DrawingPath QuadraticTo(Vector2 control, Vector2 end)
        {
            RequireOpenContour();
            var control1 = _current + (control - _current) * (2f / 3f);
            var control2 = end + (control - end) * (2f / 3f);
            AddCubic(control1, control2, end);
            _lastQuadraticControl = control;
            _previousWasQuadratic = true;
            _previousWasCubic = false;
            return this;
        }

        public DrawingPath QuadraticToRelative(Vector2 control, Vector2 end) =>
            QuadraticTo(_current + control, _current + end);

        public DrawingPath SmoothQuadraticTo(Vector2 end)
        {
            var control = _previousWasQuadratic ? _current * 2 - _lastQuadraticControl : _current;
            return QuadraticTo(control, end);
        }

        public DrawingPath SmoothQuadraticToRelative(Vector2 end) => SmoothQuadraticTo(_current + end);

        public DrawingPath ArcTo(Vector2 radius, float rotationDegrees, bool isLargeArc, bool sweepClockwise, Vector2 end)
        {
            RequireOpenContour();
            var start = _current;
            var radiusX = MathF.Abs(radius.X);
            var radiusY = MathF.Abs(radius.Y);
            if (start == end)
            {
                ResetCurveState();
                return this;
            }
            if (radiusX <= float.Epsilon || radiusY <= float.Epsilon) return LineTo(end);

            var rotation = rotationDegrees * MathF.PI / 180f;
            var cosine = MathF.Cos(rotation);
            var sine = MathF.Sin(rotation);
            var midpoint = (start - end) * .5f;
            var transformedX = cosine * midpoint.X + sine * midpoint.Y;
            var transformedY = -sine * midpoint.X + cosine * midpoint.Y;
            var scale = transformedX * transformedX / (radiusX * radiusX) + transformedY * transformedY / (radiusY * radiusY);
            if (scale > 1)
            {
                var factor = MathF.Sqrt(scale);
                radiusX *= factor;
                radiusY *= factor;
            }

            var radiusXSquared = radiusX * radiusX;
            var radiusYSquared = radiusY * radiusY;
            var transformedXSquared = transformedX * transformedX;
            var transformedYSquared = transformedY * transformedY;
            var denominator = radiusXSquared * transformedYSquared + radiusYSquared * transformedXSquared;
            var numerator = MathF.Max(0, radiusXSquared * radiusYSquared - denominator);
            var coefficient = denominator <= float.Epsilon ? 0 : MathF.Sqrt(numerator / denominator);
            if (isLargeArc == sweepClockwise) coefficient = -coefficient;
            var centerX = coefficient * radiusX * transformedY / radiusY;
            var centerY = coefficient * -radiusY * transformedX / radiusX;
            var center = new Vector2(
                cosine * centerX - sine * centerY + (start.X + end.X) * .5f,
                sine * centerX + cosine * centerY + (start.Y + end.Y) * .5f);
            var startUnit = new Vector2((transformedX - centerX) / radiusX, (transformedY - centerY) / radiusY);
            var endUnit = new Vector2((-transformedX - centerX) / radiusX, (-transformedY - centerY) / radiusY);
            var startAngle = MathF.Atan2(startUnit.Y, startUnit.X);
            var sweepAngle = MathF.Atan2(startUnit.X * endUnit.Y - startUnit.Y * endUnit.X, Vector2.Dot(startUnit, endUnit));
            if (!sweepClockwise && sweepAngle > 0) sweepAngle -= MathHelper.TwoPi;
            else if (sweepClockwise && sweepAngle < 0) sweepAngle += MathHelper.TwoPi;

            var segmentCount = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweepAngle) / (MathF.PI * .5f)));
            var segmentAngle = sweepAngle / segmentCount;
            for (var segment = 0; segment < segmentCount; segment++)
            {
                var angle = startAngle + segment * segmentAngle;
                var nextAngle = angle + segmentAngle;
                var alpha = 4f / 3f * MathF.Tan(segmentAngle * .25f);
                var segmentStart = ArcPoint(center, radiusX, radiusY, cosine, sine, angle);
                var segmentEnd = segment == segmentCount - 1 ? end : ArcPoint(center, radiusX, radiusY, cosine, sine, nextAngle);
                var startTangent = ArcTangent(radiusX, radiusY, cosine, sine, angle);
                var endTangent = ArcTangent(radiusX, radiusY, cosine, sine, nextAngle);
                AddCubic(segmentStart + startTangent * alpha, segmentEnd - endTangent * alpha, segmentEnd);
            }
            ResetCurveState();
            return this;
        }

        public DrawingPath ArcToRelative(Vector2 radius, float rotationDegrees, bool isLargeArc, bool sweepClockwise, Vector2 end) =>
            ArcTo(radius, rotationDegrees, isLargeArc, sweepClockwise, _current + end);

        /// <summary>Closes the current contour.</summary>
        public DrawingPath Close()
        {
            RequireOpenContour();
            AddCommand(new DrawingPathCommand(DrawingPathCommandKind.Close, default, default, default));
            _current = _contourStart;
            _hasOpenContour = false;
            ResetCurveState();
            return this;
        }

        /// <summary>Tests a point against the transformed nonzero-winding fill geometry.</summary>
        public bool ContainsPoint(Vector2 point, Matrix transform, float tolerance = .25f)
            => ContainsPoint(point, transform, FillRule.NonZero, tolerance);

        /// <summary>Tests a point against transformed geometry using the selected winding rule.</summary>
        public bool ContainsPoint(Vector2 point, Matrix transform, FillRule fillRule, float tolerance = .25f)
        {
            var winding = 0;
            var crossings = 0;
            foreach (var contour in DrawingPathFlattener.Flatten(this, transform, tolerance))
            {
                if (contour.Count < 3) continue;
                for (var index = 0; index < contour.Count; index++)
                {
                    var start = contour[index];
                    var end = contour[(index + 1) % contour.Count];
                    if (start.Y <= point.Y)
                    {
                        if (end.Y > point.Y && Cross(end - start, point - start) > 0) { winding++; crossings++; }
                    }
                    else if (end.Y <= point.Y && Cross(end - start, point - start) < 0)
                    {
                        winding--;
                        crossings++;
                    }
                }
            }
            return fillRule == FillRule.EvenOdd ? (crossings & 1) != 0 : winding != 0;
        }

        private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;

        private static Vector2 ArcPoint(Vector2 center, float radiusX, float radiusY, float cosine, float sine, float angle) =>
            center + new Vector2(
                cosine * radiusX * MathF.Cos(angle) - sine * radiusY * MathF.Sin(angle),
                sine * radiusX * MathF.Cos(angle) + cosine * radiusY * MathF.Sin(angle));

        private static Vector2 ArcTangent(float radiusX, float radiusY, float cosine, float sine, float angle) =>
            new Vector2(
                -cosine * radiusX * MathF.Sin(angle) - sine * radiusY * MathF.Cos(angle),
                -sine * radiusX * MathF.Sin(angle) + cosine * radiusY * MathF.Cos(angle));

        private void AddCubic(Vector2 control1, Vector2 control2, Vector2 end)
        {
            AddCommand(new DrawingPathCommand(DrawingPathCommandKind.Cubic, control1, control2, end));
            _current = end;
        }

        private void ResetCurveState()
        {
            _previousWasCubic = false;
            _previousWasQuadratic = false;
        }

        private void AddCommand(DrawingPathCommand command)
        {
            if (IsFrozen) throw new InvalidOperationException("A frozen drawing path cannot be changed.");
            if (_commands.Count >= DrawingContextLimits.MaximumPathCommands)
                throw new InvalidOperationException($"A drawing path cannot exceed {DrawingContextLimits.MaximumPathCommands} commands.");
            _commands.Add(command);
        }

        private void RequireOpenContour()
        {
            if (!_hasOpenContour)
                throw new InvalidOperationException("MoveTo must begin a contour before adding path segments.");
        }

        public static DrawingPath Parse(string data) => SvgPathDataParser.Parse(data);
    }

    internal ref struct SvgPathDataParser
    {
        private readonly ReadOnlySpan<char> _data;
        private int _position;
        private char _command;

        private SvgPathDataParser(string data)
        {
            _data = data.AsSpan();
            _position = 0;
            _command = '\0';
        }

        public static DrawingPath Parse(string data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var parser = new SvgPathDataParser(data);
            return parser.ReadPath();
        }

        private DrawingPath ReadPath()
        {
            var path = new DrawingPath();
            while (SkipSeparators())
            {
                if (IsCommand(_data[_position])) _command = _data[_position++];
                else if (_command == '\0' || char.ToUpperInvariant(_command) == 'Z') ThrowFormat("Expected a path command.");
                ReadCommand(path);
            }
            return path;
        }

        private void ReadCommand(DrawingPath path)
        {
            var relative = char.IsLower(_command);
            switch (char.ToUpperInvariant(_command))
            {
                case 'M':
                    ReadMove(path, relative);
                    break;
                case 'L':
                    ReadPairs(path, relative ? path.LineToRelative : path.LineTo);
                    break;
                case 'H':
                    ReadNumbers(path, relative ? path.HorizontalToRelative : path.HorizontalTo);
                    break;
                case 'V':
                    ReadNumbers(path, relative ? path.VerticalToRelative : path.VerticalTo);
                    break;
                case 'C':
                    ReadSix(path, relative ? path.CubicToRelative : path.CubicTo);
                    break;
                case 'S':
                    ReadFour(path, relative ? path.SmoothCubicToRelative : path.SmoothCubicTo);
                    break;
                case 'Q':
                    ReadFour(path, relative ? path.QuadraticToRelative : path.QuadraticTo);
                    break;
                case 'T':
                    ReadPairs(path, relative ? path.SmoothQuadraticToRelative : path.SmoothQuadraticTo);
                    break;
                case 'A':
                    ReadArcs(path, relative);
                    break;
                case 'Z':
                    path.Close();
                    break;
                default:
                    ThrowFormat($"Unsupported path command '{_command}'.");
                    break;
            }
        }

        private void ReadMove(DrawingPath path, bool relative)
        {
            var point = ReadPoint();
            if (relative) path.MoveToRelative(point); else path.MoveTo(point);
            while (HasNumber())
            {
                point = ReadPoint();
                if (relative) path.LineToRelative(point); else path.LineTo(point);
            }
            _command = relative ? 'l' : 'L';
        }

        private void ReadPairs(DrawingPath path, Func<Vector2, DrawingPath> add)
        {
            RequireNumber();
            do add(ReadPoint()); while (HasNumber());
        }

        private void ReadNumbers(DrawingPath path, Func<float, DrawingPath> add)
        {
            RequireNumber();
            do add(ReadNumber()); while (HasNumber());
        }

        private void ReadFour(DrawingPath path, Func<Vector2, Vector2, DrawingPath> add)
        {
            RequireNumber();
            do add(ReadPoint(), ReadPoint()); while (HasNumber());
        }

        private void ReadSix(DrawingPath path, Func<Vector2, Vector2, Vector2, DrawingPath> add)
        {
            RequireNumber();
            do add(ReadPoint(), ReadPoint(), ReadPoint()); while (HasNumber());
        }

        private void ReadArcs(DrawingPath path, bool relative)
        {
            RequireNumber();
            do
            {
                var radius = ReadPoint();
                var rotation = ReadNumber();
                var isLargeArc = ReadFlag();
                var sweepClockwise = ReadFlag();
                var end = ReadPoint();
                if (relative) path.ArcToRelative(radius, rotation, isLargeArc, sweepClockwise, end);
                else path.ArcTo(radius, rotation, isLargeArc, sweepClockwise, end);
            } while (HasNumber());
        }

        private Vector2 ReadPoint() => new Vector2(ReadNumber(), ReadNumber());

        private bool ReadFlag()
        {
            SkipSeparators();
            if (_position >= _data.Length || (_data[_position] != '0' && _data[_position] != '1')) ThrowFormat("Arc flags must be zero or one.");
            return _data[_position++] == '1';
        }

        private float ReadNumber()
        {
            SkipSeparators();
            var start = _position;
            if (_position < _data.Length && (_data[_position] == '+' || _data[_position] == '-')) _position++;
            var digits = 0;
            while (_position < _data.Length && char.IsDigit(_data[_position])) { _position++; digits++; }
            if (_position < _data.Length && _data[_position] == '.')
            {
                _position++;
                while (_position < _data.Length && char.IsDigit(_data[_position])) { _position++; digits++; }
            }
            if (digits == 0) ThrowFormat("Expected a number.");
            if (_position < _data.Length && (_data[_position] == 'e' || _data[_position] == 'E'))
            {
                _position++;
                if (_position < _data.Length && (_data[_position] == '+' || _data[_position] == '-')) _position++;
                var exponentStart = _position;
                while (_position < _data.Length && char.IsDigit(_data[_position])) _position++;
                if (_position == exponentStart) ThrowFormat("Expected exponent digits.");
            }
            if (!float.TryParse(_data.Slice(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                ThrowFormat("Path numbers must be finite invariant-culture values.");
            return value;
        }

        private bool HasNumber()
        {
            SkipSeparators();
            return _position < _data.Length && (_data[_position] == '+' || _data[_position] == '-' || _data[_position] == '.' || char.IsDigit(_data[_position]));
        }

        private void RequireNumber()
        {
            if (!HasNumber()) ThrowFormat($"Command '{_command}' requires parameters.");
        }

        private bool SkipSeparators()
        {
            while (_position < _data.Length && (char.IsWhiteSpace(_data[_position]) || _data[_position] == ',')) _position++;
            return _position < _data.Length;
        }

        private static bool IsCommand(char value) => value is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't' or 'A' or 'a' or 'Z' or 'z';

        private void ThrowFormat(string message) => throw new FormatException($"{message} Offset {_position}.");
    }

    /// <summary>A normalized drawing-path operation.</summary>
    public readonly struct DrawingPathCommand
    {
        internal DrawingPathCommand(DrawingPathCommandKind kind, Vector2 first, Vector2 second, Vector2 third)
        {
            Kind = kind;
            First = first;
            Second = second;
            Third = third;
        }

        public DrawingPathCommandKind Kind { get; }
        public Vector2 First { get; }
        public Vector2 Second { get; }
        public Vector2 Third { get; }
    }

    public enum DrawingPathCommandKind
    {
        Move,
        Line,
        Cubic,
        Close,
    }

    internal static class DrawingPathFlattener
    {
        internal const int MaximumSubdivisionDepth = DrawingContextLimits.MaximumSubdivisionDepth;

        public static IReadOnlyList<IReadOnlyList<Vector2>> Flatten(DrawingPath path, Matrix transform, float tolerance)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (!float.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));

            var contours = new List<IReadOnlyList<Vector2>>();
            List<Vector2> contour = null;
            var current = Vector2.Zero;
            var start = Vector2.Zero;

            foreach (var command in path.Commands)
            {
                switch (command.Kind)
                {
                    case DrawingPathCommandKind.Move:
                        FinishContour(contours, contour);
                        current = command.First;
                        start = current;
                        contour = new List<Vector2> { Transform(current, transform) };
                        break;
                    case DrawingPathCommandKind.Line:
                        current = command.First;
                        AddDistinct(contour, Transform(current, transform));
                        break;
                    case DrawingPathCommandKind.Cubic:
                        var control1 = command.First;
                        var control2 = command.Second;
                        var end = command.Third;
                        FlattenCubic(
                            contour,
                            Transform(current, transform),
                            Transform(control1, transform),
                            Transform(control2, transform),
                            Transform(end, transform),
                            tolerance * tolerance,
                            0);
                        current = end;
                        break;
                    case DrawingPathCommandKind.Close:
                        AddDistinct(contour, Transform(start, transform));
                        FinishContour(contours, contour);
                        contour = null;
                        current = start;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown path command {command.Kind}.");
                }
            }

            FinishContour(contours, contour);
            return contours;
        }

        private static void FlattenCubic(
            List<Vector2> points,
            Vector2 start,
            Vector2 control1,
            Vector2 control2,
            Vector2 end,
            float toleranceSquared,
            int depth)
        {
            if (depth >= MaximumSubdivisionDepth || IsFlatEnough(start, control1, control2, end, toleranceSquared))
            {
                AddDistinct(points, end);
                return;
            }

            var startControl = (start + control1) * .5f;
            var controlsMidpoint = (control1 + control2) * .5f;
            var controlEnd = (control2 + end) * .5f;
            var leftControl = (startControl + controlsMidpoint) * .5f;
            var rightControl = (controlsMidpoint + controlEnd) * .5f;
            var midpoint = (leftControl + rightControl) * .5f;

            FlattenCubic(points, start, startControl, leftControl, midpoint, toleranceSquared, depth + 1);
            FlattenCubic(points, midpoint, rightControl, controlEnd, end, toleranceSquared, depth + 1);
        }

        private static bool IsFlatEnough(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, float toleranceSquared)
        {
            var chord = end - start;
            var chordLengthSquared = chord.LengthSquared();
            if (chordLengthSquared <= float.Epsilon)
                return Vector2.DistanceSquared(start, control1) <= toleranceSquared &&
                    Vector2.DistanceSquared(start, control2) <= toleranceSquared;

            var firstCross = MathF.Abs(chord.X * (start.Y - control1.Y) - chord.Y * (start.X - control1.X));
            var secondCross = MathF.Abs(chord.X * (start.Y - control2.Y) - chord.Y * (start.X - control2.X));
            var maximumCross = MathF.Max(firstCross, secondCross);
            return maximumCross * maximumCross <= toleranceSquared * chordLengthSquared;
        }

        private static Vector2 Transform(Vector2 point, Matrix transform) => Vector2.Transform(point, transform);

        private static void AddDistinct(List<Vector2> points, Vector2 point)
        {
            if (points == null) throw new InvalidOperationException("MoveTo must begin a contour before adding path segments.");
            if (points.Count == 0 || points[points.Count - 1] != point) points.Add(point);
        }

        private static void FinishContour(List<IReadOnlyList<Vector2>> contours, List<Vector2> contour)
        {
            if (contour != null && contour.Count > 0) contours.Add(contour);
        }
    }

    internal readonly struct DrawingMesh
    {
        public DrawingMesh(Vector2[] vertices, int[] indices)
        {
            if (vertices.Length > DrawingContextLimits.MaximumMeshVertices)
                throw new InvalidOperationException($"A drawing mesh cannot exceed {DrawingContextLimits.MaximumMeshVertices} vertices.");
            if (indices.Length > DrawingContextLimits.MaximumMeshIndices)
                throw new InvalidOperationException($"A drawing mesh cannot exceed {DrawingContextLimits.MaximumMeshIndices} indices.");
            Vertices = vertices;
            Indices = indices;
        }

        public Vector2[] Vertices { get; }
        public int[] Indices { get; }
    }

    internal static class DrawingPathTessellator
    {
        public static DrawingMesh TessellateFill(DrawingPath path, Matrix transform, float tolerance, FillRule fillRule = FillRule.NonZero)
        {
            var contours = DrawingPathFlattener.Flatten(path, transform, tolerance);
            if (contours.Count != 1) return TessellateContours(contours, fillRule);

            var source = contours[0];
            var vertexCount = source.Count > 1 && source[0] == source[source.Count - 1]
                ? source.Count - 1
                : source.Count;
            if (vertexCount < 3) return new DrawingMesh(Array.Empty<Vector2>(), Array.Empty<int>());

            var vertices = new Vector2[vertexCount];
            for (var index = 0; index < vertexCount; index++) vertices[index] = source[index];

            var remaining = new List<int>(vertexCount);
            if (SignedArea(vertices) > 0)
                for (var index = 0; index < vertexCount; index++) remaining.Add(index);
            else
                for (var index = vertexCount - 1; index >= 0; index--) remaining.Add(index);

            var indices = new List<int>((vertexCount - 2) * 3);
            var attempts = 0;
            while (remaining.Count > 3)
            {
                var earFound = false;
                for (var index = 0; index < remaining.Count; index++)
                {
                    var previous = remaining[(index + remaining.Count - 1) % remaining.Count];
                    var current = remaining[index];
                    var next = remaining[(index + 1) % remaining.Count];
                    if (!IsEar(vertices, remaining, previous, current, next)) continue;

                    indices.Add(previous);
                    indices.Add(current);
                    indices.Add(next);
                    remaining.RemoveAt(index);
                    earFound = true;
                    break;
                }

                if (!earFound || ++attempts > vertexCount * vertexCount)
                    throw new InvalidOperationException("The path is self-intersecting or cannot be tessellated as a simple contour.");
            }

            indices.Add(remaining[0]);
            indices.Add(remaining[1]);
            indices.Add(remaining[2]);
            return new DrawingMesh(vertices, indices.ToArray());
        }

        private static DrawingMesh TessellateContours(IReadOnlyList<IReadOnlyList<Vector2>> contours, FillRule fillRule)
        {
            var paths = GeometryClipper.ToPaths(contours);
            if (paths.Count == 0) return new DrawingMesh(Array.Empty<Vector2>(), Array.Empty<int>());
            var normalized = Clipper.Union(paths, fillRule == FillRule.EvenOdd ? ClipperFillRule.EvenOdd : ClipperFillRule.NonZero);
            var result = Clipper.Triangulate(normalized, GeometryClipper.DecimalPrecision, out var triangles, false);
            if (result == TriangulateResult.noPolygons) return new DrawingMesh(Array.Empty<Vector2>(), Array.Empty<int>());
            if (result != TriangulateResult.success) throw new InvalidOperationException($"The path could not be triangulated ({result}).");
            var vertices = new Vector2[triangles.Count * 3];
            var indices = new int[vertices.Length];
            for (var triangle = 0; triangle < triangles.Count; triangle++)
            {
                if (triangles[triangle].Count != 3) throw new InvalidOperationException("Clipper returned a non-triangle element.");
                for (var point = 0; point < 3; point++)
                {
                    var index = triangle * 3 + point;
                    vertices[index] = new Vector2((float)triangles[triangle][point].x, (float)triangles[triangle][point].y);
                    indices[index] = index;
                }
            }
            return new DrawingMesh(vertices, indices);
        }

        public static DrawingMesh TessellateStroke(DrawingPath path, Matrix transform, float thickness, float tolerance, StrokeStyle style = null)
        {
            if (!float.IsFinite(thickness) || thickness <= 0) throw new ArgumentOutOfRangeException(nameof(thickness));
            style ??= StrokeStyle.Default;
            style.Validate();
            var contours = DrawingPathFlattener.Flatten(path, transform, tolerance);
            var vertices = new List<Vector2>();
            var indices = new List<int>();

            foreach (var contour in contours)
            {
                var closed = contour.Count > 2 && contour[0] == contour[contour.Count - 1];
                var alignmentSign = closed && style.Alignment != StrokeAlignment.Center
                    ? (SignedArea(contour) >= 0 ? 1f : -1f) * (style.Alignment == StrokeAlignment.Inside ? 1f : -1f)
                    : 0;
                var startOffset = alignmentSign == 0 ? -thickness * .5f : MathF.Min(0, alignmentSign * thickness);
                var endOffset = alignmentSign == 0 ? thickness * .5f : MathF.Max(0, alignmentSign * thickness);
                var segments = BuildStrokeSegments(contour, style);
                for (var index = 0; index < segments.Count; index++)
                {
                    var start = segments[index].Start;
                    var end = segments[index].End;
                    var direction = end - start;
                    if (direction.LengthSquared() <= float.Epsilon) continue;
                    direction.Normalize();
                    var normal = new Vector2(-direction.Y, direction.X);
                    var startCap = segments[index].StartsDash || index == 0 ? style.StartLineCap : StrokeLineCap.Butt;
                    var endCap = segments[index].EndsDash || index == segments.Count - 1 ? style.EndLineCap : StrokeLineCap.Butt;
                    var capStart = startCap == StrokeLineCap.Square ? -direction * thickness * .5f : Vector2.Zero;
                    var capEnd = endCap == StrokeLineCap.Square ? direction * thickness * .5f : Vector2.Zero;
                    var vertexOffset = vertices.Count;
                    vertices.Add(start + capStart + normal * endOffset);
                    vertices.Add(start + capStart + normal * startOffset);
                    vertices.Add(end + capEnd + normal * endOffset);
                    vertices.Add(end + capEnd + normal * startOffset);
                    indices.Add(vertexOffset);
                    indices.Add(vertexOffset + 1);
                    indices.Add(vertexOffset + 2);
                    indices.Add(vertexOffset + 2);
                    indices.Add(vertexOffset + 1);
                    indices.Add(vertexOffset + 3);
                    if (startCap == StrokeLineCap.Round) AddRoundCap(vertices, indices, start, -direction, thickness * .5f);
                    if (endCap == StrokeLineCap.Round) AddRoundCap(vertices, indices, end, direction, thickness * .5f);
                    if (style.Alignment == StrokeAlignment.Center && !segments[index].EndsDash && index + 1 < segments.Count)
                        AddJoin(vertices, indices, end, direction, Vector2.Normalize(segments[index + 1].End - segments[index + 1].Start), thickness, style);
                }
            }

            return new DrawingMesh(vertices.ToArray(), indices.ToArray());
        }

        private static List<StrokeSegment> BuildStrokeSegments(IReadOnlyList<Vector2> contour, StrokeStyle style)
        {
            var result = new List<StrokeSegment>();
            if (style.DashArray.Count == 0)
            {
                for (var index = 0; index + 1 < contour.Count; index++)
                    result.Add(new StrokeSegment(contour[index], contour[index + 1], false, false));
                return result;
            }

            var patternLength = 0f;
            foreach (var dash in style.DashArray) patternLength += dash;
            if ((style.DashArray.Count & 1) != 0) patternLength *= 2;
            var offset = style.DashOffset % patternLength;
            if (offset < 0) offset += patternLength;
            var patternIndex = 0;
            var patternRemaining = style.DashArray[0];
            while (offset >= patternRemaining)
            {
                offset -= patternRemaining;
                patternIndex = (patternIndex + 1) % (style.DashArray.Count * ((style.DashArray.Count & 1) == 0 ? 1 : 2));
                patternRemaining = style.DashArray[patternIndex % style.DashArray.Count];
            }
            patternRemaining -= offset;
            var drawing = (patternIndex & 1) == 0;

            for (var index = 0; index + 1 < contour.Count; index++)
            {
                var start = contour[index];
                var end = contour[index + 1];
                var delta = end - start;
                var length = delta.Length();
                if (length <= float.Epsilon) continue;
                var consumed = 0f;
                while (consumed < length)
                {
                    var amount = MathF.Min(patternRemaining, length - consumed);
                    if (drawing && amount > float.Epsilon)
                    {
                        var segmentStart = start + delta * (consumed / length);
                        var segmentEnd = start + delta * ((consumed + amount) / length);
                        result.Add(new StrokeSegment(segmentStart, segmentEnd, true, true));
                    }
                    consumed += amount;
                    patternRemaining -= amount;
                    if (patternRemaining <= float.Epsilon)
                    {
                        patternIndex = (patternIndex + 1) % (style.DashArray.Count * ((style.DashArray.Count & 1) == 0 ? 1 : 2));
                        patternRemaining = style.DashArray[patternIndex % style.DashArray.Count];
                        drawing = !drawing;
                    }
                }
            }
            return result;
        }

        private static void AddRoundCap(List<Vector2> vertices, List<int> indices, Vector2 center, Vector2 direction, float radius)
        {
            const int steps = 8;
            var baseAngle = MathF.Atan2(direction.Y, direction.X) - MathF.PI * .5f;
            var centerIndex = vertices.Count;
            vertices.Add(center);
            for (var step = 0; step <= steps; step++)
                vertices.Add(center + new Vector2(MathF.Cos(baseAngle + MathF.PI * step / steps), MathF.Sin(baseAngle + MathF.PI * step / steps)) * radius);
            for (var step = 0; step < steps; step++)
            {
                indices.Add(centerIndex);
                indices.Add(centerIndex + step + 1);
                indices.Add(centerIndex + step + 2);
            }
        }

        private static void AddJoin(List<Vector2> vertices, List<int> indices, Vector2 center, Vector2 incoming, Vector2 outgoing, float thickness, StrokeStyle style)
        {
            if (incoming.LengthSquared() <= float.Epsilon || outgoing.LengthSquared() <= float.Epsilon) return;
            var cross = Cross(incoming, outgoing);
            if (MathF.Abs(cross) <= .0001f) return;
            var side = cross > 0 ? -1f : 1f;
            var first = center + new Vector2(-incoming.Y, incoming.X) * side * thickness * .5f;
            var second = center + new Vector2(-outgoing.Y, outgoing.X) * side * thickness * .5f;
            if (style.LineJoin == StrokeLineJoin.Round)
            {
                var startAngle = MathF.Atan2(first.Y - center.Y, first.X - center.X);
                var endAngle = MathF.Atan2(second.Y - center.Y, second.X - center.X);
                var sweep = endAngle - startAngle;
                if (cross > 0 && sweep > 0) sweep -= MathHelper.TwoPi;
                else if (cross < 0 && sweep < 0) sweep += MathHelper.TwoPi;
                var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweep) / (MathF.PI / 8)));
                var centerIndex = vertices.Count;
                vertices.Add(center);
                for (var step = 0; step <= steps; step++)
                    vertices.Add(center + new Vector2(MathF.Cos(startAngle + sweep * step / steps), MathF.Sin(startAngle + sweep * step / steps)) * thickness * .5f);
                for (var step = 0; step < steps; step++)
                {
                    indices.Add(centerIndex);
                    indices.Add(centerIndex + step + 1);
                    indices.Add(centerIndex + step + 2);
                }
                return;
            }

            var join = center;
            if (style.LineJoin == StrokeLineJoin.Miter)
            {
                var bisector = Vector2.Normalize((first - center) + (second - center));
                var denominator = Vector2.Dot(bisector, Vector2.Normalize(first - center));
                var miterLength = MathF.Abs(denominator) <= float.Epsilon ? float.PositiveInfinity : thickness * .5f / denominator;
                if (MathF.Abs(miterLength) <= style.MiterLimit * thickness) join = center + bisector * miterLength;
            }
            var offset = vertices.Count;
            vertices.Add(first);
            vertices.Add(join);
            vertices.Add(second);
            indices.Add(offset);
            indices.Add(offset + 1);
            indices.Add(offset + 2);
        }

        private static float SignedArea(IReadOnlyList<Vector2> vertices)
        {
            var area = 0f;
            for (var index = 0; index + 1 < vertices.Count; index++) area += vertices[index].X * vertices[index + 1].Y - vertices[index + 1].X * vertices[index].Y;
            return area * .5f;
        }

        private readonly struct StrokeSegment
        {
            public StrokeSegment(Vector2 start, Vector2 end, bool startsDash, bool endsDash)
            {
                Start = start;
                End = end;
                StartsDash = startsDash;
                EndsDash = endsDash;
            }
            public Vector2 Start { get; }
            public Vector2 End { get; }
            public bool StartsDash { get; }
            public bool EndsDash { get; }
        }

        private static bool IsEar(Vector2[] vertices, List<int> remaining, int previous, int current, int next)
        {
            var first = vertices[previous];
            var second = vertices[current];
            var third = vertices[next];
            if (Cross(second - first, third - second) <= 0) return false;

            foreach (var candidate in remaining)
            {
                if (candidate == previous || candidate == current || candidate == next) continue;
                if (Contains(first, second, third, vertices[candidate])) return false;
            }
            return true;
        }

        private static bool Contains(Vector2 first, Vector2 second, Vector2 third, Vector2 point)
        {
            var firstSide = Cross(second - first, point - first);
            var secondSide = Cross(third - second, point - second);
            var thirdSide = Cross(first - third, point - third);
            return firstSide >= 0 && secondSide >= 0 && thirdSide >= 0;
        }

        private static float SignedArea(Vector2[] vertices)
        {
            var area = 0f;
            for (var index = 0; index < vertices.Length; index++)
            {
                var next = vertices[(index + 1) % vertices.Length];
                area += vertices[index].X * next.Y - next.X * vertices[index].Y;
            }
            return area * .5f;
        }

        private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;
    }

    internal static class DrawingPathClipper
    {
        public static PathsD NormalizeContours(IReadOnlyList<IReadOnlyList<Vector2>> contours, FillRule fillRule)
        {
            var vertexCount = 0;
            foreach (var contour in contours)
            {
                vertexCount += contour.Count > 1 && contour[0] == contour[contour.Count - 1] ? contour.Count - 1 : contour.Count;
                if (vertexCount > DrawingContextLimits.MaximumClipVertices)
                    throw new ArgumentException($"A clip cannot exceed {DrawingContextLimits.MaximumClipVertices} vertices.", nameof(contours));
            }
            var paths = GeometryClipper.ToPaths(contours);
            if (paths.Count == 0) throw new ArgumentException("A clip requires at least one contour with three distinct vertices.", nameof(contours));
            return Clipper.Union(paths, fillRule == FillRule.EvenOdd ? ClipperFillRule.EvenOdd : ClipperFillRule.NonZero);
        }

        public static DrawingMesh Clip(DrawingMesh mesh, PathsD clip)
        {
            var vertices = new List<Vector2>();
            var indices = new List<int>();
            for (var triangle = 0; triangle < mesh.Indices.Length; triangle += 3)
            {
                var subject = new PathsD
                {
                    new PathD
                    {
                        new PointD(mesh.Vertices[mesh.Indices[triangle]].X, mesh.Vertices[mesh.Indices[triangle]].Y),
                        new PointD(mesh.Vertices[mesh.Indices[triangle + 1]].X, mesh.Vertices[mesh.Indices[triangle + 1]].Y),
                        new PointD(mesh.Vertices[mesh.Indices[triangle + 2]].X, mesh.Vertices[mesh.Indices[triangle + 2]].Y),
                    },
                };
                var intersection = Clipper.Intersect(subject, clip, ClipperFillRule.NonZero, GeometryClipper.DecimalPrecision);
                var result = Clipper.Triangulate(intersection, GeometryClipper.DecimalPrecision, out var triangles, false);
                if (result == TriangulateResult.noPolygons) continue;
                if (result != TriangulateResult.success) throw new InvalidOperationException($"The clipped mesh could not be triangulated ({result}).");
                foreach (var clippedTriangle in triangles)
                {
                    if (vertices.Count + 3 > DrawingContextLimits.MaximumMeshVertices)
                        throw new InvalidOperationException($"A clipped mesh cannot exceed {DrawingContextLimits.MaximumMeshVertices} vertices.");
                    foreach (var point in clippedTriangle)
                    {
                        indices.Add(vertices.Count);
                        vertices.Add(new Vector2((float)point.x, (float)point.y));
                    }
                }
            }
            return new DrawingMesh(vertices.ToArray(), indices.ToArray());
        }
    }
}