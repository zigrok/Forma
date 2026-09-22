// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;

namespace Forma.Tests;

/// <summary>
/// Quiescence is what removes fixed frame counts from tests. Advancing "enough" frames and hoping
/// is the commonest source of UI-test flakiness: too few and the assertion races layout, too many
/// and every test pays for the slowest case.
/// </summary>
public sealed class QuiescenceTest
{
    private static UIContext CreateContext() => new() { ViewportSize = new Vector2(800, 600) };

    [Test]
    public void AFreshlyAddedControlIsNotSettledUntilLaidOut()
    {
        using var context = CreateContext();
        context.Add(new Panel { CustomMinimumSize = new Vector2(100, 100) });

        Assert.That(context.IsSettled, Is.False, "a control that has never been laid out owes a pass");

        Assert.That(context.WaitForSettled(), Is.True);
        Assert.That(context.IsSettled, Is.True);
    }

    [Test]
    public void QueueingLayoutMakesItUnsettledAgain()
    {
        using var context = CreateContext();
        var panel = new Panel { CustomMinimumSize = new Vector2(100, 100) };
        context.Add(panel);
        context.WaitForSettled();

        panel.QueueLayout();

        Assert.That(context.IsSettled, Is.False);
        Assert.That(context.WaitForSettled(), Is.True);
    }

    [Test]
    public void SettlingIsDeepNotJustTheRoot()
    {
        using var context = CreateContext();
        var root = new StackPanel { CustomMinimumSize = new Vector2(200, 200) };
        var child = new Button { Text = "Deep", CustomMinimumSize = new Vector2(80, 30) };
        root.AddChild(child);
        context.Add(root);
        context.WaitForSettled();

        // Only the child is dirtied. A check that looked at roots alone would call this settled and
        // let a test read bounds that are about to change.
        child.QueueLayout();

        Assert.That(context.IsSettled, Is.False);
    }

    [Test]
    public void SettlingIsBoundedByPassesNotTime()
    {
        using var context = CreateContext();
        context.Add(new Panel { CustomMinimumSize = new Vector2(100, 100) });

        // A pass budget behaves the same on a loaded machine as on an idle one, which is the whole
        // point — a wall-clock budget would reintroduce the flakiness this removes.
        Assert.That(context.WaitForSettled(maxPasses: 1), Is.True, "layout converges immediately here");
    }

    [Test]
    public void LocatorsSettleBeforeResolving()
    {
        using var context = CreateContext();
        var root = new StackPanel { CustomMinimumSize = new Vector2(200, 200) };
        context.Add(root);
        context.WaitForSettled();

        // Added without any explicit layout afterwards: the locator is responsible for settling, so
        // the test never advances frames by hand.
        root.AddChild(new Button { Text = "Fresh", Name = "Fresh", AutomationId = "fresh", CustomMinimumSize = new Vector2(100, 40) });

        var node = context.ByAutomationId("fresh").Resolve();

        Assert.That(node.Bounds.Width, Is.GreaterThan(0), "bounds must be real, not pre-layout zeros");
        Assert.That(context.IsSettled, Is.True);
    }

    [Test]
    public void ActionsAlsoSettleFirst()
    {
        using var context = CreateContext();
        var root = new StackPanel { CustomMinimumSize = new Vector2(200, 200) };
        context.Add(root);
        context.WaitForSettled();

        var button = new Button { Text = "Go", AutomationId = "go", CustomMinimumSize = new Vector2(100, 40) };
        var pressed = 0;
        button.Pressed += (_, _) => pressed++;
        root.AddChild(button);

        // No manual layout: acting has to settle, or actionability would hit-test stale bounds.
        context.ByAutomationId("go").Click();

        Assert.That(pressed, Is.EqualTo(1));
    }
}
