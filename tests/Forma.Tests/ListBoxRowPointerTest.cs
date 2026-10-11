// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using Forma.Testing;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

/// <summary>
/// A press on content inside a row must reach the owning ListBox, whatever the content's MouseFilter,
/// so rows select and activate on click without per-template tweaks.
/// </summary>
public sealed class ListBoxRowPointerTest
{
    private static (HeadlessInput Input, ListBox List) Build(Func<Control> row, bool singleClick = false)
    {
        var input = HeadlessInput.Create();
        var list = new ListBox
        {
            Size = new Vector2(300, 300),
            ActivateOnSingleClick = singleClick,
            ItemTemplate = DataTemplate.Create<string>((_, item) => row()),
            ItemsSource = new[] { "a", "b", "c" },
        };
        input.Context.Add(list);
        input.Context.Layout();
        return (input, list);
    }

    private static Control Row(Control child)
    {
        var box = new HBoxContainer { CustomMinimumSize = new Vector2(280, 30) };
        box.AddChild(child);
        return box;
    }

    private static Control Stopper() => new Border { CustomMinimumSize = new Vector2(280, 30), MouseFilter = MouseFilter.Stop };
    private static Control Plain() => new Label { Text = "row", CustomMinimumSize = new Vector2(280, 30) };

    [TestCase(false), TestCase(true)]
    public void ClickOnRowContentSelectsTheRow(bool stop)
    {
        var (input, list) = Build(() => Row(stop ? Stopper() : Plain()));
        using var context = input.Context;
        var container = list.GetRealizedContainer(1);
        input.Click(HeadlessInput.CenterOf(container));
        Assert.That(list.SelectedIndex, Is.EqualTo(1));
    }

    [Test]
    public void SingleClickActivatesWhenEnabled()
    {
        var (input, list) = Build(() => Row(Stopper()), singleClick: true);
        using var context = input.Context;
        var activated = -1;
        list.ItemActivated += (_, e) => activated = e.Index;
        input.Click(HeadlessInput.CenterOf(list.GetRealizedContainer(2)));
        Assert.That(activated, Is.EqualTo(2));
    }

    [Test]
    public void DoubleClickActivatesAndSingleClickDoesNot()
    {
        var (input, list) = Build(() => Row(Stopper()));
        using var context = input.Context;
        var activated = 0;
        list.ItemActivated += (_, _) => activated++;
        var at = HeadlessInput.CenterOf(list.GetRealizedContainer(0));
        input.Click(at);
        Assert.That(activated, Is.EqualTo(0));
        input.Click(at);
        Assert.That(activated, Is.EqualTo(1));
    }

    [Test]
    public void ButtonInsideARowStillGetsItsOwnClick()
    {
        var clicked = 0;
        var (input, list) = Build(() =>
        {
            var button = new Button { Text = "x", CustomMinimumSize = new Vector2(280, 30) };
            button.Pressed += (_, _) => clicked++;
            return Row(button);
        });
        using var context = input.Context;
        input.Click(HeadlessInput.CenterOf(list.GetRealizedContainer(0)));
        Assert.That(clicked, Is.EqualTo(1));
    }
}
