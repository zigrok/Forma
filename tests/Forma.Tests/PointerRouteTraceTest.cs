// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using Forma.Testing;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

/// <summary>
/// The pointer-route trace is the diagnostic for "the wrong handler ran": it names the control a press
/// stopped at and the row owner that received it instead, without a debugger attached to the game.
/// </summary>
public sealed class PointerRouteTraceTest
{
    private static (HeadlessInput Input, ListBox List) Build(Func<Control> row, bool singleClick = false)
    {
        var input = HeadlessInput.Create();
        var list = new ListBox
        {
            Name = "Rows",
            Size = new Vector2(300, 300),
            ActivateOnSingleClick = singleClick,
            ItemTemplate = DataTemplate.Create<string>((_, _) => row()),
            ItemsSource = new[] { "a", "b", "c" },
        };
        input.Context.Add(list);
        input.Context.TracePointerRoutes = true;
        input.Context.Layout();
        return (input, list);
    }

    private static Control Row(Control child)
    {
        var box = new HBoxContainer { CustomMinimumSize = new Vector2(280, 30) };
        box.AddChild(child);
        return box;
    }

    [Test]
    public void PressOnAWidgetInsideARowSaysTheRowOwnerWasNotConsulted()
    {
        var (input, list) = Build(() => Row(new Button
        {
            Text = "Delete",
            AutomationId = "row.delete",
            CustomMinimumSize = new Vector2(280, 30),
            FocusMode = FocusMode.None,
        }), singleClick: true);
        using var context = input.Context;

        input.Click(HeadlessInput.CenterOf(list.GetRealizedContainer(1)));
        var trace = context.TakePointerRouteTrace();

        Assert.That(trace, Has.Some.Contains("press at"));
        Assert.That(trace, Has.Some.Matches<string>(line =>
            line.Contains("Button#row.delete \"Delete\"") && line.Contains("consumesPress=yes")));
        Assert.That(trace, Has.Some.Contains("it consumes the press, so the row owner is not consulted"));
        Assert.That(trace, Has.None.Contains("row owner ListBox"));
    }

    [Test]
    public void PressOnRowContentNamesTheRowOwnerThatReceivesIt()
    {
        var (input, list) = Build(() => Row(new Label { Text = "miguel", CustomMinimumSize = new Vector2(280, 30) }));
        using var context = input.Context;

        input.Click(HeadlessInput.CenterOf(list.GetRealizedContainer(1)));
        var trace = context.TakePointerRouteTrace();

        Assert.That(trace, Has.Some.Matches<string>(line =>
            line.Contains("Label \"miguel\"") && line.Contains("consumesPress=no")));
        Assert.That(trace, Has.Some.Contains("row owner ListBox \"Rows\" also receives the event"));
    }

    [Test]
    public void TakingTheTraceClearsIt()
    {
        var (input, list) = Build(() => Row(new Label { Text = "miguel", CustomMinimumSize = new Vector2(280, 30) }));
        using var context = input.Context;

        input.Click(HeadlessInput.CenterOf(list.GetRealizedContainer(0)));
        Assert.That(context.TakePointerRouteTrace(), Is.Not.Empty);
        Assert.That(context.TakePointerRouteTrace(), Is.Empty);
    }

    [Test]
    public void TraceStaysOffWhenItWasNotEnabled()
    {
        Assume.That(Environment.GetEnvironmentVariable("FORMA_UI_EVENTS"), Is.Null.Or.Empty);
        var input = HeadlessInput.Create();
        using var context = input.Context;
        var button = new Button { Text = "x", Size = new Vector2(80, 30) };
        context.Add(button);
        context.Layout();

        input.Click(button);

        Assert.That(context.TakePointerRouteTrace(), Is.Empty);
    }
}
