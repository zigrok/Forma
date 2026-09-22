// SPDX-License-Identifier: MIT
// Copyright (c) Igor Hipólito Vieira

using System;
using System.Collections.Generic;
using System.Text;

namespace Forma
{
    /// <summary>
    /// Explains why <see cref="UIContext.IsSettled"/> is false.
    /// <para>
    /// A wait that times out with "the UI never settled" tells you nothing you can act on — the
    /// whole tree is suspect. Naming the controls that are still dirty turns that into a lead,
    /// which matters because the usual cause is one control re-queuing layout from inside its own
    /// layout pass, and it is invisible from the outside.
    /// </para>
    /// </summary>
    public static class Quiescence
    {
        /// <summary>
        /// Controls that have not settled, deepest-first, as `Type#AutomationId` paths from their
        /// root. Empty when the context is settled.
        /// </summary>
        public static IReadOnlyList<string> UnsettledControls(UIContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var paths = new List<string>();
            foreach (var root in context.GetRootsInDrawOrder())
            {
                if (!root.IsRendered) continue;
                Collect(root, string.Empty, paths);
            }

            return paths;
        }

        /// <summary>
        /// A one-line summary naming up to <paramref name="limit"/> unsettled controls, suitable
        /// for a timeout message.
        /// </summary>
        public static string Describe(UIContext context, int limit = 8)
        {
            var unsettled = UnsettledControls(context);
            if (unsettled.Count == 0) return "the UI is settled";

            var builder = new StringBuilder();
            builder.Append(unsettled.Count).Append(unsettled.Count == 1 ? " control is" : " controls are")
                .Append(" still dirty: ");

            for (var index = 0; index < unsettled.Count && index < limit; index++)
            {
                if (index > 0) builder.Append(", ");
                builder.Append(unsettled[index]);
            }

            if (unsettled.Count > limit) builder.Append(", ...");
            return builder.ToString();
        }

        /// <summary>
        /// Whether any outstanding frame-boundary callbacks are keeping the context unsettled, as
        /// opposed to layout. The two have different causes and different fixes, so they are worth
        /// telling apart.
        /// </summary>
        public static bool HasPendingFrameWork(UIContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.PendingFrameBoundaryCallbackCount > 0;
        }

        private static void Collect(Control control, string prefix, List<string> paths)
        {
            var name = control.GetType().Name;
            if (!string.IsNullOrEmpty(control.AutomationId)) name += "#" + control.AutomationId;
            var path = prefix.Length == 0 ? name : prefix + " > " + name;

            // Depth-first so the innermost cause is listed before the ancestors QueueLayout marked
            // on its way up, which are effects rather than causes.
            foreach (var child in control.VisualChildren) Collect(child, path, paths);

            // A dirty control whose children are all clean is the one that queued the pass.
            if (control.IsLayoutDirty) paths.Add(path);
        }
    }
}
