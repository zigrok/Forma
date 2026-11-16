// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;

namespace Forma
{
    /// <summary>
    /// Records where a pointer press, release or wheel event travelled: the hit target, every control it
    /// bubbled through, whether each one filtered or accepted it, and which ListBox received it when a row
    /// stopped the bubble. Off by default; set <c>FORMA_UI_EVENTS=1</c> in the process environment, or turn
    /// <see cref="TracePointerRoutes"/> on for one dispatch, to collect it.
    /// </summary>
    public sealed partial class UIContext
    {
        private const int MaximumPointerRouteEntries = 512;
        private static readonly bool s_PointerRoutesFromEnvironment =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FORMA_UI_EVENTS"));
        private readonly List<string> _pointerRouteTrace = new List<string>();

        /// <summary>
        /// Whether pointer dispatch appends to <see cref="TakePointerRouteTrace"/>. Defaults to on when
        /// <c>FORMA_UI_EVENTS</c> is set in the environment, which also copies the lines to standard error.
        /// </summary>
        public bool TracePointerRoutes { get; set; } = s_PointerRoutesFromEnvironment;

        /// <summary>
        /// Returns the pointer-route lines recorded since the previous call and clears them. Empty when the
        /// trace is off or no pointer event was dispatched.
        /// </summary>
        public IReadOnlyList<string> TakePointerRouteTrace()
        {
            if (_pointerRouteTrace.Count == 0) return Array.Empty<string>();
            var lines = _pointerRouteTrace.ToArray();
            _pointerRouteTrace.Clear();
            return lines;
        }

        private void RecordPointerRoute(string line)
        {
            if (!TracePointerRoutes) return;
            if (_pointerRouteTrace.Count >= MaximumPointerRouteEntries) _pointerRouteTrace.RemoveAt(0);
            _pointerRouteTrace.Add(line);
            if (s_PointerRoutesFromEnvironment) Console.Error.WriteLine(line);
        }

        private void TracePointerTarget(string phase, Control target, Point point)
        {
            if (!TracePointerRoutes) return;
            RecordPointerRoute($"[pointer] {phase} at ({point.X}, {point.Y}) target={DescribePointerControl(target)}");
        }

        private void TracePointerStop(Control control, bool accepted)
        {
            if (!TracePointerRoutes) return;
            var consumes = control.ConsumesPointerPress ? "yes" : "no";
            RecordPointerRoute($"  {DescribePointerControl(control)} filter={control.MouseFilter} consumesPress={consumes} accepted={(accepted ? "yes" : "no")}");
        }

        private void TracePointerRowOwner(Control stoppedAt, ListBox owner)
        {
            if (!TracePointerRoutes) return;
            if (stoppedAt.ConsumesPointerPress)
            {
                RecordPointerRoute($"  -> stopped at {DescribePointerControl(stoppedAt)}; it consumes the press, so the row owner is not consulted");
                return;
            }

            RecordPointerRoute(owner is null
                ? $"  -> stopped at {DescribePointerControl(stoppedAt)}; no row owner"
                : $"  -> stopped at {DescribePointerControl(stoppedAt)}; row owner {DescribePointerControl(owner)} also receives the event");
        }

        private static string DescribePointerControl(Control control)
        {
            if (control is null) return "<none>";
            var text = new StringBuilder(control.GetType().Name);
            if (!string.IsNullOrEmpty(control.AutomationId)) text.Append('#').Append(control.AutomationId);
            var name = control.AccessibilityName;
            if (!string.IsNullOrEmpty(name))
            {
                name = name.ReplaceLineEndings(" ");
                if (name.Length > 40) name = name[..37] + "...";
                text.Append(" \"").Append(name).Append('"');
            }
            return text.ToString();
        }
    }
}
