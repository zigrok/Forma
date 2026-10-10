// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>What kind of layout problem <see cref="UIContext.FindLayoutProblems(Control, LayoutProblemOptions)"/> found.</summary>
    [Flags]
    public enum LayoutProblemKind
    {
        None = 0,
        /// <summary>A label or button's text needs more room than its box has and is cut off or drawn past it.</summary>
        TextClipped = 1,
        /// <summary>An autowrapping label's wrapped lines are taller than its box.</summary>
        TextWrappedPastBox = 2,
        /// <summary>A control extends past the bounds of its parent.</summary>
        ChildOutsideParent = 4,
        /// <summary>A control extends past the viewport.</summary>
        OutsideViewport = 8,
        /// <summary>A control extends past an ancestor that clips its contents.</summary>
        ClippedByAncestor = 16,
        All = TextClipped | TextWrappedPastBox | ChildOutsideParent | OutsideViewport | ClippedByAncestor,
    }

    /// <summary>Selects what <see cref="UIContext.FindLayoutProblems(Control, LayoutProblemOptions)"/> reports.</summary>
    public sealed class LayoutProblemOptions
    {
        /// <summary>The kinds to report. Default: all.</summary>
        public LayoutProblemKind Kinds { get; set; } = LayoutProblemKind.All;
        /// <summary>How many pixels a control may extend past its box before it counts. Default 1.</summary>
        public float Tolerance { get; set; } = 1f;
        /// <summary>Whether to ignore controls that scroll or clip their content by design (scroll containers, lists, data grids, tabs, text fields). Default true.</summary>
        public bool IgnoreScrollingContainers { get; set; } = true;
    }

    /// <summary>One clipped or overflowing control.</summary>
    public sealed class LayoutProblem
    {
        public LayoutProblem(LayoutProblemKind kind, Control control, Vector2 overflow, string message)
        {
            Kind = kind;
            Control = control ?? throw new ArgumentNullException(nameof(control));
            Overflow = overflow;
            Message = message;
        }

        public LayoutProblemKind Kind { get; }
        public Control Control { get; }
        /// <summary>The control's name (<c>x:Name</c> or <c>id</c>), or an empty string.</summary>
        public string Name => Control.Name ?? string.Empty;
        /// <summary>The control's style classes, space separated.</summary>
        public string Classes => string.Join(" ", Control.Classes);
        /// <summary>Where the control was authored (<c>file:line</c>) when the view carries it in the <c>origin</c> data attribute; otherwise empty.</summary>
        public string Origin => Control.GetData("origin") ?? string.Empty;
        /// <summary>How far the control extends past its box, per axis, in layout pixels.</summary>
        public Vector2 Overflow { get; }
        public string Message { get; }
        public override string ToString() => Message;
    }

    public partial class UIContext
    {
        /// <summary>Finds text that does not fit its box and controls that extend past their parent, an ancestor's clip or the viewport.</summary>
        /// <param name="root">The control to inspect, with its descendants.</param>
        /// <param name="options">What to report; null reports everything with a one pixel tolerance.</param>
        public IReadOnlyList<LayoutProblem> FindLayoutProblems(Control root, LayoutProblemOptions options = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            options ??= new LayoutProblemOptions();
            Layout();
            var problems = new List<LayoutProblem>();
            var viewport = new Rectangle(0, 0, (int)MathF.Round(ViewportSize.X), (int)MathF.Round(ViewportSize.Y));
            Walk(root, root, viewport, options, problems);
            return problems;
        }

        private static void Walk(Control control, Control root, Rectangle viewport, LayoutProblemOptions options, List<LayoutProblem> problems)
        {
            if (!control.Visible) return;
            var box = control.Bounds;
            if (box.Width > 0 && box.Height > 0)
            {
                CheckText(control, options, problems);
                var visual = control.VisualBounds;
                if (!ReferenceEquals(control, root))
                {
                    var scrolled = options.IgnoreScrollingContainers && AnyAncestor(control, IsScrolling);
                    if (!scrolled)
                    {
                        if ((options.Kinds & LayoutProblemKind.OutsideViewport) != 0 && Past(visual, viewport, options.Tolerance) is { } outside)
                            problems.Add(new LayoutProblem(LayoutProblemKind.OutsideViewport, control, outside, Describe(control, $"extends {Format(outside)} past the {viewport.Width}x{viewport.Height} viewport")));
                        if ((options.Kinds & LayoutProblemKind.ClippedByAncestor) != 0 && FirstClippingAncestor(control, root) is { } clip && Past(visual, clip.VisualBounds, options.Tolerance) is { } clipped)
                            problems.Add(new LayoutProblem(LayoutProblemKind.ClippedByAncestor, control, clipped, Describe(control, $"extends {Format(clipped)} past {clip.GetType().Name} '{clip.Name}' that clips it")));
                        else if ((options.Kinds & LayoutProblemKind.ChildOutsideParent) != 0 && control.VisualParent is { } parent && !parent.ClipContents && Past(visual, parent.VisualBounds, options.Tolerance) is { } beyond)
                            problems.Add(new LayoutProblem(LayoutProblemKind.ChildOutsideParent, control, beyond, Describe(control, $"extends {Format(beyond)} past its parent {parent.GetType().Name} '{parent.Name}'")));
                    }
                }
            }

            foreach (var child in control.Children) Walk(child, root, viewport, options, problems);
        }

        private static void CheckText(Control control, LayoutProblemOptions options, List<LayoutProblem> problems)
        {
            if (control is Label label)
            {
                var info = label.TextFit;
                if (info.Fit != LabelTextFit.Clipped) return;
                var wrapped = label.AutowrapMode != LabelAutowrapMode.Off;
                var kind = wrapped ? LayoutProblemKind.TextWrappedPastBox : LayoutProblemKind.TextClipped;
                if ((options.Kinds & kind) == 0 || (info.Overflow.X <= options.Tolerance && info.Overflow.Y <= options.Tolerance)) return;
                problems.Add(new LayoutProblem(kind, control, info.Overflow,
                    Describe(control, $"text \"{Shorten(label.Text)}\" needs {info.Measured.X:0}x{info.Measured.Y:0} but has {info.Available.X:0}x{info.Available.Y:0}")));
            }
            else if (control is BaseButton button && (options.Kinds & LayoutProblemKind.TextClipped) != 0)
            {
                var minimum = button.GetMinimumSize();
                var overflow = new Vector2(Math.Max(0, minimum.X - control.Size.X), Math.Max(0, minimum.Y - control.Size.Y));
                if (overflow.X > options.Tolerance || overflow.Y > options.Tolerance)
                    problems.Add(new LayoutProblem(LayoutProblemKind.TextClipped, control, overflow,
                        Describe(control, $"button \"{Shorten(button.Text)}\" needs {minimum.X:0}x{minimum.Y:0} but has {control.Size.X:0}x{control.Size.Y:0}")));
            }
        }

        private static Vector2? Past(Rectangle box, Rectangle limit, float tolerance)
        {
            var x = Math.Max(Math.Max(0, limit.Left - box.Left), Math.Max(0, box.Right - limit.Right));
            var y = Math.Max(Math.Max(0, limit.Top - box.Top), Math.Max(0, box.Bottom - limit.Bottom));
            return x > tolerance || y > tolerance ? new Vector2(x, y) : (Vector2?)null;
        }

        private static Control FirstClippingAncestor(Control control, Control root)
        {
            for (var ancestor = control.VisualParent; ancestor != null; ancestor = ancestor.VisualParent)
            {
                if (ancestor.ClipContents && !IsScrolling(ancestor)) return ancestor;
                if (ReferenceEquals(ancestor, root)) break;
            }

            return null;
        }

        private static bool AnyAncestor(Control control, Func<Control, bool> predicate)
        {
            for (var parent = control.VisualParent; parent != null; parent = parent.VisualParent)
                if (predicate(parent)) return true;
            return false;
        }

        private static bool IsScrolling(Control control) =>
            control is ScrollContainer or ListBox or DataGrid or ItemsControl or TabContainer or LineEdit or TextEdit;

        private static string Describe(Control control, string what) =>
            $"{control.GetType().Name}{(string.IsNullOrEmpty(control.Name) ? string.Empty : " '" + control.Name + "'")}{(control.Classes.Count > 0 ? " (" + string.Join(" ", control.Classes) + ")" : string.Empty)} {what}.";

        private static string Format(Vector2 overflow) => $"{overflow.X:0}x{overflow.Y:0}px";

        private static string Shorten(string text) => text == null ? string.Empty : text.Length <= 40 ? text : text.Substring(0, 37) + "...";
    }
}
