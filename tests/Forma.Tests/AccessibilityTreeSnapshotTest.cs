// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Linq;
using Microsoft.Xna.Framework;

namespace Forma.Tests;

/// <summary>
/// The snapshot is what every later consumer reads — locators, the OS bridge, golden files — so
/// what it includes and in what order is the contract. These pin the rules that are easy to get
/// wrong and expensive to discover late: nothing is listed twice, order matches what a person sees,
/// and reachability agrees with input.
/// </summary>
public sealed class AccessibilityTreeSnapshotTest
{
    private static UIContext CreateContext()
    {
        var context = new UIContext { ViewportSize = new Vector2(800, 600) };
        return context;
    }

    [Test]
    public void EveryControlAppearsExactlyOnce()
    {
        using var context = CreateContext();
        var root = new Panel { Size = new Vector2(400, 300) };
        var inner = new StackPanel();
        var first = new Button { Text = "First" };
        var second = new Button { Text = "Second" };
        inner.AddChild(first);
        inner.AddChild(second);
        root.AddChild(inner);
        context.Add(root);

        var snapshot = AccessibilityTree.Capture(context);
        var ids = snapshot.Nodes.Select(node => node.Id).ToList();

        // GetAccessibilityChildren returns peers of visual children, and the walk also recurses into
        // them; counting both is the obvious way to get this wrong.
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), "no control may be listed twice");
        Assert.That(ids, Does.Contain(first.AccessibilityId));
        Assert.That(ids, Does.Contain(second.AccessibilityId));
    }

    [Test]
    public void ParentLinksReconstructTheTree()
    {
        using var context = CreateContext();
        var root = new Panel { Size = new Vector2(400, 300) };
        var child = new Button { Text = "Child" };
        root.AddChild(child);
        context.Add(root);

        var snapshot = AccessibilityTree.Capture(context);

        Assert.That(snapshot.TryGetNode(root.AccessibilityId, out var rootNode), Is.True);
        Assert.That(snapshot.TryGetNode(child.AccessibilityId, out var childNode), Is.True);
        Assert.That(rootNode.IsRoot, Is.True, "a context root has no parent");
        Assert.That(childNode.ParentId, Is.EqualTo(root.AccessibilityId));
    }

    [Test]
    public void OrderIsDepthFirstInDrawOrder()
    {
        using var context = CreateContext();
        var root = new StackPanel { Size = new Vector2(400, 300) };
        var first = new Button { Name = "first" };
        var second = new Button { Name = "second" };
        root.AddChild(first);
        root.AddChild(second);
        context.Add(root);

        var snapshot = AccessibilityTree.Capture(context);
        var order = snapshot.Nodes.Select(node => node.Id).ToList();

        // Reading the list top to bottom should match what a person sees top to bottom.
        Assert.That(order.IndexOf(root.AccessibilityId), Is.LessThan(order.IndexOf(first.AccessibilityId)));
        Assert.That(order.IndexOf(first.AccessibilityId), Is.LessThan(order.IndexOf(second.AccessibilityId)));
    }

    [Test]
    public void HiddenSubtreesAreExcludedEntirely()
    {
        using var context = CreateContext();
        var root = new Panel { Size = new Vector2(400, 300) };
        var hidden = new StackPanel { Visible = false };
        var buried = new Button { Text = "Buried" };
        hidden.AddChild(buried);
        root.AddChild(hidden);
        context.Add(root);

        var snapshot = AccessibilityTree.Capture(context);
        var ids = snapshot.Nodes.Select(node => node.Id).ToList();

        Assert.That(ids, Does.Not.Contain(hidden.AccessibilityId));
        Assert.That(ids, Does.Not.Contain(buried.AccessibilityId),
            "a hidden panel's children are no more reachable than the panel");
    }

    [Test]
    public void AModalPopupHidesEverythingBehindIt()
    {
        using var context = CreateContext();
        var background = new Panel { Size = new Vector2(400, 300) };
        var behind = new Button { Text = "Behind" };
        background.AddChild(behind);
        context.Add(background);

        var popup = new PopupPanel { Size = new Vector2(200, 100) };
        var inside = new Button { Text = "Inside" };
        popup.AddChild(inside);
        context.Add(popup);
        popup.PopupAt(new Vector2(50, 50));

        var snapshot = AccessibilityTree.Capture(context);
        var ids = snapshot.Nodes.Select(node => node.Id).ToList();

        // Reporting what a modal covers would invite a consumer to act on something unreachable.
        Assert.That(snapshot.IsModalActive, Is.True);
        Assert.That(ids, Does.Contain(inside.AccessibilityId));
        Assert.That(ids, Does.Not.Contain(behind.AccessibilityId));
    }

    [Test]
    public void FocusIsReported()
    {
        using var context = CreateContext();
        var root = new Panel { Size = new Vector2(400, 300) };
        var button = new Button { Text = "Focus me" };
        root.AddChild(button);
        context.Add(root);
        context.Layout();
        button.GrabFocus();

        var snapshot = AccessibilityTree.Capture(context);

        Assert.That(snapshot.FocusedId, Is.EqualTo(button.AccessibilityId));
        Assert.That(snapshot.ToText(), Does.Contain("<focus>"));
    }

    [Test]
    public void NothingFocusedReportsZero()
    {
        using var context = CreateContext();
        context.Add(new Panel { Size = new Vector2(100, 100) });

        Assert.That(AccessibilityTree.Capture(context).FocusedId, Is.Zero);
    }

    [Test]
    public void VirtualizedItemsAppearEvenThoughTheyAreNotVisualChildren()
    {
        using var context = CreateContext();
        var list = new ListBox
        {
            ItemsSource = new[] { "alpha", "beta", "gamma" },
            Size = new Vector2(200, 300),
        };
        context.Add(list);

        var snapshot = AccessibilityTree.Capture(context);
        var items = snapshot.Nodes.Where(node => node.Role == AccessibilityRole.ListItem).ToList();

        Assert.That(items, Is.Not.Empty, "a list whose rows are absent cannot be navigated");
        Assert.That(items.Select(node => node.Id).Distinct().Count(), Is.EqualTo(items.Count));
    }

    [Test]
    public void AutomationIdAndNameBothTravel()
    {
        using var context = CreateContext();
        var button = new Button { Text = "Save", Name = "Save", AutomationId = "cmd.save" };
        context.Add(button);

        var snapshot = AccessibilityTree.Capture(context);
        Assert.That(snapshot.TryGetNode(button.AccessibilityId, out var node), Is.True);

        Assert.That(node.Name, Is.EqualTo("Save"));
        Assert.That(node.AutomationId, Is.EqualTo("cmd.save"));
        Assert.That(node.Role, Is.EqualTo(AccessibilityRole.Button));
    }

    [Test]
    public void TextDumpIsIndentedAndOmitsChurningDetail()
    {
        using var context = CreateContext();
        var root = new Panel { Size = new Vector2(400, 300), Name = "Root" };
        root.AddChild(new Button { Name = "Ok", AutomationId = "cmd.ok" });
        context.Add(root);

        var text = AccessibilityTree.Capture(context).ToText();

        Assert.That(text, Does.Contain("Generic \"Root\""));
        Assert.That(text, Does.Contain("  Button \"Ok\" #cmd.ok"), "children indent by two spaces");

        // Ids and bounds are deliberately absent: both churn with allocation order and window size,
        // and would make every unrelated change look like a golden-file regression.
        Assert.That(text, Does.Not.Contain(root.AccessibilityId.ToString()));
    }

    /// <summary>
    /// A menu's entries reach the tree. A menu draws them rather than building a control for each,
    /// so before this the whole command surface of an application was one empty node: a screen
    /// reader announced "menu" and stopped, and an automation client saw a container with nothing
    /// in it.
    /// </summary>
    [Test]
    public void AMenusEntriesAreInTheTree()
    {
        using var context = new UIContext { ViewportSize = new Vector2(400, 300) };
        var menu = new PopupMenu();
        menu.AddItem("Open");
        menu.AddItem("Save");
        menu.AddSeparator();
        menu.AddItem("Quit");
        context.Add(menu);
        menu.PopupAt(new Vector2(10, 10), null);
        context.WaitForSettled();

        var names = AccessibilityTree.Capture(context).Nodes.Select(node => node.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("Open"));
            Assert.That(names, Does.Contain("Save"));
            Assert.That(names, Does.Contain("Quit"));
        });
    }

    /// <summary>
    /// Separators and filtered-out entries stay out. Announcing something a person cannot see or
    /// act on is worse than announcing nothing.
    /// </summary>
    [Test]
    public void SeparatorsAndHiddenEntriesAreNotAnnounced()
    {
        using var context = new UIContext { ViewportSize = new Vector2(400, 300) };
        var menu = new PopupMenu();
        menu.AddItem("Open");
        menu.AddSeparator();
        menu.AddItem("Save");
        context.Add(menu);
        menu.PopupAt(new Vector2(10, 10), null);
        context.WaitForSettled();

        var entries = AccessibilityTree.Capture(context).Nodes
            .Count(node => node.Role == AccessibilityRole.MenuItem);

        Assert.That(entries, Is.EqualTo(2), "a separator is decoration, not an entry");
    }

    /// <summary>
    /// Pressing an entry through its peer runs the same activation a click does, so an assistive
    /// technology cannot reach an entry a person cannot.
    /// </summary>
    [Test]
    public void PressingAnEntryActivatesIt()
    {
        using var context = new UIContext { ViewportSize = new Vector2(400, 300) };
        var pressed = -1;
        var menu = new PopupMenu();
        menu.AddItem("Open");
        menu.AddItem("Save");
        menu.IndexPressed += (_, index) => pressed = index;
        context.Add(menu);
        menu.PopupAt(new Vector2(10, 10), null);
        context.WaitForSettled();

        var save = AccessibilityTree.Capture(context).Nodes.First(node => node.Name == "Save");

        Assert.That(AccessibilityTree.TryPerformAction(context, save.Id, AccessibilityActions.Press), Is.True);
        Assert.That(pressed, Is.EqualTo(1));
    }

    [Test]
    public void ADisabledEntryRefusesToBePressed()
    {
        using var context = new UIContext { ViewportSize = new Vector2(400, 300) };
        var pressed = false;
        var menu = new PopupMenu();
        menu.AddItem("Open");
        menu.SetItemDisabled(0, true);
        menu.IndexPressed += (_, _) => pressed = true;
        context.Add(menu);
        menu.PopupAt(new Vector2(10, 10), null);
        context.WaitForSettled();

        var open = AccessibilityTree.Capture(context).Nodes.First(node => node.Name == "Open");

        Assert.Multiple(() =>
        {
            Assert.That(open.States.HasFlag(AccessibilityStates.Disabled), Is.True);
            Assert.That(AccessibilityTree.TryPerformAction(context, open.Id, AccessibilityActions.Press), Is.False);
            Assert.That(pressed, Is.False);
        });
    }

}

/// <summary>
/// Incremental updates are what make an OS bridge viable: pushing every node because one label
/// changed is the difference between usable and unusable. These pin that a diff reports only real
/// movement, and that the watcher does no work when nothing happened.
/// </summary>
public sealed class AccessibilityTreeDeltaTest
{
    private static UIContext CreateContext() => new() { ViewportSize = new Vector2(800, 600) };

    [Test]
    public void FirstDiffAgainstNothingIsEntirelyAdditions()
    {
        using var context = CreateContext();
        context.Add(new Button { Text = "Only" });

        var snapshot = AccessibilityTree.Capture(context);
        var delta = AccessibilityTree.Diff(null, snapshot);

        Assert.That(delta.Added.Count, Is.EqualTo(snapshot.Nodes.Count));
        Assert.That(delta.Removed, Is.Empty);
        Assert.That(delta.IsEmpty, Is.False);
    }

    [Test]
    public void AnUnchangedTreeProducesAnEmptyDelta()
    {
        using var context = CreateContext();
        context.Add(new Button { Text = "Stable" });

        var first = AccessibilityTree.Capture(context);
        var second = AccessibilityTree.Capture(context);

        // Capturing twice with nothing happening in between must not look like change, or every
        // frame would push the whole tree.
        Assert.That(AccessibilityTree.Diff(first, second).IsEmpty, Is.True);
    }

    [Test]
    public void ARenamedControlIsReportedUpdatedNotReadded()
    {
        using var context = CreateContext();
        var button = new Button { Name = "Before" };
        context.Add(button);

        var first = AccessibilityTree.Capture(context);
        button.Name = "After";
        var second = AccessibilityTree.Capture(context);

        var delta = AccessibilityTree.Diff(first, second);

        Assert.That(delta.Added, Is.Empty, "identity is stable, so this is an update");
        Assert.That(delta.Removed, Is.Empty);
        Assert.That(delta.Updated.Select(node => node.Id), Does.Contain(button.AccessibilityId));
    }

    [Test]
    public void RemovingAControlReportsItById()
    {
        using var context = CreateContext();
        var root = new StackPanel { Size = new Vector2(200, 200) };
        var doomed = new Button { Text = "Doomed" };
        root.AddChild(doomed);
        context.Add(root);

        var first = AccessibilityTree.Capture(context);
        root.RemoveChild(doomed);
        var second = AccessibilityTree.Capture(context);

        var delta = AccessibilityTree.Diff(first, second);

        // By id, so a consumer can drop it without searching its own copy of the tree.
        Assert.That(delta.Removed, Does.Contain(doomed.AccessibilityId));
    }

    [Test]
    public void FocusMovementIsReportedOnItsOwn()
    {
        using var context = CreateContext();
        var first = new Button { Text = "First" };
        var second = new Button { Text = "Second" };
        var root = new StackPanel { Size = new Vector2(200, 200) };
        root.AddChild(first);
        root.AddChild(second);
        context.Add(root);
        context.Layout();

        first.GrabFocus();
        var before = AccessibilityTree.Capture(context);
        second.GrabFocus();
        var after = AccessibilityTree.Capture(context);

        var delta = AccessibilityTree.Diff(before, after);

        Assert.That(delta.FocusChanged, Is.True);
        Assert.That(delta.FocusedId, Is.EqualTo(second.AccessibilityId));
    }

    [Test]
    public void WatcherDoesNoWorkWhenNothingChanged()
    {
        using var context = CreateContext();
        context.Add(new Button { Text = "Idle" });
        using var watcher = new AccessibilityTreeWatcher(context);

        var first = watcher.Update();
        Assert.That(first.Added, Is.Not.Empty, "the first update establishes the tree");

        var second = watcher.Update();

        Assert.That(watcher.IsDirty, Is.False);
        Assert.That(second.IsEmpty, Is.True, "an idle frame must cost nothing");
    }

    [Test]
    public void WatcherNoticesAChangedControl()
    {
        using var context = CreateContext();
        var button = new Button { Name = "Before" };
        context.Add(button);
        using var watcher = new AccessibilityTreeWatcher(context);
        watcher.Update();

        button.Name = "After";

        Assert.That(watcher.IsDirty, Is.True, "AccessibilityChanged should mark the tree dirty");
        Assert.That(watcher.Update().Updated.Select(node => node.Id), Does.Contain(button.AccessibilityId));
    }

    [Test]
    public void WatcherNoticesFocusMoving()
    {
        using var context = CreateContext();
        var button = new Button { Text = "Focus" };
        context.Add(button);
        context.Layout();
        using var watcher = new AccessibilityTreeWatcher(context);
        watcher.Update();

        button.GrabFocus();

        Assert.That(watcher.IsDirty, Is.True);
        Assert.That(watcher.Update().FocusChanged, Is.True);
    }
    /// <summary>
    /// A label announces its text. Most of what an application says to the user - status lines,
    /// panel messages, field captions - is a label, and an unnamed one is silent to a screen reader
    /// and unfindable by <c>ByText</c>. Labels used to report <see cref="Control.Name"/> instead,
    /// which leaked internal part names such as "PART_ContentPresenter" as the accessible name.
    /// </summary>
    [Test]
    public void ALabelAnnouncesItsText()
    {
        using var context = new UIContext { ViewportSize = new Vector2(400, 300) };
        var label = new Label { Name = "statusText", Text = "No problems found." };
        context.Add(label);
        context.WaitForSettled();

        var nodes = AccessibilityTree.Capture(context).Nodes;
        Assert.That(nodes.Any(candidate => candidate.Name == "No problems found."), Is.True,
            "the label's text should be its accessible name");
    }

    /// <summary>An explicit label still wins, so a caption can differ from what is announced.</summary>
    [Test]
    public void AnExplicitAccessibilityLabelOverridesALabelsText()
    {
        using var context = new UIContext { ViewportSize = new Vector2(400, 300) };
        context.Add(new Label { Text = "3", AccessibilityLabel = "three unread messages" });
        context.WaitForSettled();

        var nodes = AccessibilityTree.Capture(context).Nodes;
        Assert.That(nodes.Any(candidate => candidate.Name == "three unread messages"), Is.True);
        Assert.That(nodes.Any(candidate => candidate.Name == "3"), Is.False);
    }

}
