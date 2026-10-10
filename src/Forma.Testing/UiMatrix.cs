// SPDX-License-Identifier: MIT
// Copyright (c) Igor Hipólito Vieira

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>One cell of a <see cref="UiMatrix"/> run: the view, locale, UI scale and viewport it was laid out with.</summary>
    public sealed class UiMatrixCase
    {
        /// <summary>Creates a case.</summary>
        public UiMatrixCase(string view, string locale, int scalePercent, Vector2 viewport, float displayScale, Vector2 logicalViewport)
        {
            View = view;
            Locale = locale;
            ScalePercent = scalePercent;
            Viewport = viewport;
            DisplayScale = displayScale;
            LogicalViewport = logicalViewport;
        }

        /// <summary>The name of the view.</summary>
        public string View { get; }
        /// <summary>The locale the view was built for.</summary>
        public string Locale { get; }
        /// <summary>The user's UI scale percent (100 is the default).</summary>
        public int ScalePercent { get; }
        /// <summary>The back buffer size in pixels.</summary>
        public Vector2 Viewport { get; }
        /// <summary>The physical-to-logical scale the case lays out with: back buffer height over the design height, times the scale percent.</summary>
        public float DisplayScale { get; }
        /// <summary>The viewport in logical UI units, which is what the controls are laid out in.</summary>
        public Vector2 LogicalViewport { get; }
        /// <summary>View, locale, scale and viewport as one line.</summary>
        public override string ToString() => $"{View} | {Locale} | {ScalePercent}% | {Viewport.X:0}x{Viewport.Y:0}";
    }

    /// <summary>A named view a <see cref="UiMatrix"/> run lays out; the factory receives the case so it can build the view for that locale.</summary>
    public sealed class UiMatrixView
    {
        /// <summary>Creates a view entry.</summary>
        public UiMatrixView(string name, Func<UiMatrixCase, Control> create)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Create = create ?? throw new ArgumentNullException(nameof(create));
        }

        /// <summary>The name reported in failures.</summary>
        public string Name { get; }
        /// <summary>Builds the view for a case.</summary>
        public Func<UiMatrixCase, Control> Create { get; }
    }

    /// <summary>A failure found in one cell of the matrix.</summary>
    public sealed class UiMatrixFailure
    {
        /// <summary>Creates a failure.</summary>
        public UiMatrixFailure(UiMatrixCase matrixCase, string message)
        {
            Case = matrixCase;
            Message = message;
        }

        /// <summary>The cell that failed.</summary>
        public UiMatrixCase Case { get; }
        /// <summary>What the check reported.</summary>
        public string Message { get; }
        /// <summary>The case and the message as one line.</summary>
        public override string ToString() => $"{Case}: {Message}";
    }

    /// <summary>
    /// Lays every view out for every locale, UI scale and viewport and runs a check on each combination, so a layout that only breaks in one
    /// language at one scale on one window size is found, and named with all four coordinates. The helper owns the host and disposes it.
    /// </summary>
    public static class UiMatrix
    {
        /// <summary>The height the UI is authored for; the display scale is the back buffer height over this, times the user's scale.</summary>
        public const float DefaultDesignHeight = 720f;

        /// <summary>Lays out each combination and returns the check's messages as failures. The check receives the laid-out context, the view's root and the case.</summary>
        public static IReadOnlyList<UiMatrixFailure> Run(
            IEnumerable<UiMatrixView> views,
            IEnumerable<string> locales,
            IEnumerable<int> scalePercents,
            IEnumerable<Vector2> viewports,
            Func<UIContext, Control, UiMatrixCase, IEnumerable<string>> check,
            float designHeight = DefaultDesignHeight,
            Action<UIContext> configureContext = null)
        {
            if (views == null) throw new ArgumentNullException(nameof(views));
            if (locales == null) throw new ArgumentNullException(nameof(locales));
            if (scalePercents == null) throw new ArgumentNullException(nameof(scalePercents));
            if (viewports == null) throw new ArgumentNullException(nameof(viewports));
            if (check == null) throw new ArgumentNullException(nameof(check));
            var failures = new List<UiMatrixFailure>();
            var localeList = locales.ToList();
            var scaleList = scalePercents.ToList();
            var viewportList = viewports.ToList();
            foreach (var view in views)
            foreach (var locale in localeList)
            foreach (var scale in scaleList)
            foreach (var viewport in viewportList)
            {
                var displayScale = viewport.Y / designHeight * scale / 100f;
                var logical = new Vector2(viewport.X / displayScale, viewport.Y / displayScale);
                var matrixCase = new UiMatrixCase(view.Name, locale, scale, viewport, displayScale, logical);
                using var context = new UIContext { DisplayScale = displayScale, ViewportSize = logical };
                configureContext?.Invoke(context);
                var root = new Control { Size = logical };
                var control = view.Create(matrixCase);
                control.Size = logical;
                root.AddChild(control);
                context.Add(root);
                context.Layout();
                foreach (var message in check(context, root, matrixCase)) failures.Add(new UiMatrixFailure(matrixCase, message));
            }

            return failures;
        }

        /// <summary>A check that reports every <see cref="UIContext.FindLayoutProblems(Control, LayoutProblemOptions)"/> finding.</summary>
        public static Func<UIContext, Control, UiMatrixCase, IEnumerable<string>> LayoutProblems(LayoutProblemOptions options = null) =>
            (context, root, _) => context.FindLayoutProblems(root, options).Select(problem => $"{problem.Kind}: {problem.Message}");
    }
}
