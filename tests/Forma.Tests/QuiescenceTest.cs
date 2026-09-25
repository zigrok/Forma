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
    /// <summary>
    /// A scroll container used to re-queue layout at the end of every arrange, which meant its
    /// whole ancestor chain was dirty again the moment a pass finished. Since most real trees put
    /// a ScrollContainer somewhere, quiescence silently became unreachable for entire applications
    /// and every auto-wait degraded into a fixed timeout.
    /// </summary>
    [Test]
    public void AScrollContainerSettles()
    {
        using var context = CreateContext();
        var body = new StackPanel { Orientation = Orientation.Vertical };
        for (var index = 0; index < 20; index++) body.AddChild(new Label { Text = $"row {index}" });
        context.Add(new ScrollContainer { Content = body, CustomMinimumSize = new Vector2(200, 120) });
        Assert.That(context.WaitForSettled(), Is.True, Quiescence.Describe(context));
    }

    /// <summary>
    /// Scrolling has to unsettle it again, or the fix above would have bought quiescence by making
    /// it meaningless.
    /// </summary>
    [Test]
    public void ScrollingUnsettlesAScrollContainerAgain()
    {
        using var context = CreateContext();
        var body = new StackPanel { Orientation = Orientation.Vertical };
        for (var index = 0; index < 20; index++) body.AddChild(new Label { Text = $"row {index}" });
        var scroll = new ScrollContainer { Content = body, CustomMinimumSize = new Vector2(200, 120) };
        context.Add(scroll);
        context.WaitForSettled();

        scroll.ScrollOffset = new Vector2(0, 40);

        Assert.That(context.IsSettled, Is.False);
        Assert.That(context.WaitForSettled(), Is.True);
    }

    /// <summary>
    /// Names the controls keeping a context unsettled, deepest first. A timeout that says only
    /// "the UI never settled" gives a caller nothing to act on.
    /// </summary>
    [Test]
    public void DiagnosticsNameTheUnsettledControl()
    {
        using var context = CreateContext();
        var root = new StackPanel { CustomMinimumSize = new Vector2(200, 200) };
        var child = new Button { Text = "Deep", AutomationId = "deep", CustomMinimumSize = new Vector2(80, 30) };
        root.AddChild(child);
        context.Add(root);
        context.WaitForSettled();

        child.QueueLayout();

        var unsettled = Quiescence.UnsettledControls(context);
        Assert.That(unsettled, Is.Not.Empty);
        // Deepest first: the control that queued the pass, not the ancestors it marked on the way up.
        Assert.That(unsettled[0], Does.Contain("Button#deep"));
        Assert.That(Quiescence.Describe(context), Does.Contain("Button#deep"));
    }

    [Test]
    public void DiagnosticsSayNothingIsWrongWhenSettled()
    {
        using var context = CreateContext();
        context.Add(new Panel { CustomMinimumSize = new Vector2(100, 100) });
        context.WaitForSettled();

        Assert.That(Quiescence.UnsettledControls(context), Is.Empty);
        Assert.That(Quiescence.Describe(context), Is.EqualTo("the UI is settled"));
    }

    /// <summary>
    /// Same shape of defect as the scroll container's: a split container whose dragger geometry was
    /// written twice per pass, by the container and again by the presenter hosting it.
    /// </summary>
    [Test]
    public void ASplitContainerSettles()
    {
        using var context = CreateContext();
        var split = new SplitContainer(Orientation.Horizontal) { CustomMinimumSize = new Vector2(400, 300) };
        split.AddChild(new Panel { CustomMinimumSize = new Vector2(100, 100) });
        split.AddChild(new Panel { CustomMinimumSize = new Vector2(100, 100) });
        context.Add(split);

        Assert.That(context.WaitForSettled(), Is.True, Quiescence.Describe(context));
    }

    [Test]
    public void AMenuBarSettles()
    {
        using var context = CreateContext();
        var bar = new MenuBar();
        var file = new MenuButton { Text = "File" };
        file.Menu.AddItem("Open");
        file.Menu.AddItem("Save");
        bar.AddChild(file);
        context.Add(bar);

        Assert.That(context.WaitForSettled(), Is.True, Quiescence.Describe(context));
    }

    /// <summary>
    /// The shape a real application has: nested splits, dock panes, scrolling panel bodies. Each
    /// piece settles on its own; this is the assembly that did not.
    /// </summary>
    [Test]
    public void ADockWorkspaceSettles()
    {
        using var context = CreateContext();
        var workspace = new DockWorkspace();

        var centre = workspace.CreatePane();
        centre.Add(new DockSection("viewport", "Viewport", new Panel { CustomMinimumSize = new Vector2(200, 200) }));

        var left = workspace.CreatePane();
        left.Add(new DockSection("parts", "Parts", ScrollingList("part")));
        var leftSplit = workspace.Dock(centre, left, DockZone.Right);

        var bottom = workspace.CreatePane();
        bottom.Add(new DockSection("problems", "Problems", ScrollingList("problem")));
        var bottomSplit = workspace.Dock(bottom, centre, DockZone.Bottom);

        // The third split is what made this fail: one split recovered on the next pass, nested ones
        // did not, and every real dock layout is nested.
        var right = workspace.CreatePane();
        right.Add(new DockSection("inspector", "Inspector", ScrollingList("field")));
        var rightSplit = workspace.Dock(right, centre, DockZone.Right);

        leftSplit?.SetSplitOffset(-200);
        bottomSplit?.SetSplitOffset(170);
        rightSplit?.SetSplitOffset(150);
        left.Activate(0);
        bottom.Activate(0);
        right.Activate(0);

        context.Add(workspace);

        Assert.That(context.WaitForSettled(), Is.True, Quiescence.Describe(context));
    }

    private static ScrollContainer ScrollingList(string prefix)
    {
        var body = new StackPanel { Orientation = Orientation.Vertical, Gap = 2 };
        for (var index = 0; index < 8; index++)
            body.AddChild(new Button { Text = $"{prefix} {index}", HorizontalSizeFlags = SizeFlags.Fill });
        return new ScrollContainer { Content = body };
    }

}
