// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

namespace Forma.Tests;

/// <summary>
/// Governance for accessibility roles, mirroring how <c>control-families.json</c> governs
/// documentation: a control reporting <see cref="AccessibilityRole.Generic"/> must be a decision
/// somebody made, not an omission nobody noticed.
///
/// Generic is frequently the <em>right</em> answer — pure layout and presentational elements should
/// be transparent to assistive technology rather than announced as something. So this does not
/// demand a non-Generic role everywhere; it demands that each Generic control appear below, which
/// forces the question to be asked once when a control is added.
/// </summary>
public sealed class AccessibilityRoleCoverageTest
{
    /// <summary>
    /// Controls that report Generic deliberately. Grouped by why, because the reason is the point —
    /// an entry with no applicable reason is a bug waiting to be found.
    /// </summary>
    private static readonly HashSet<string> IntentionallyGeneric = new(StringComparer.Ordinal)
    {
        // Layout only. These position their children and contribute no semantics of their own;
        // announcing each as a group would bury the content in structure.
        "AspectRatioContainer", "BoxContainer", "CanvasPanel", "CenterContainer", "Container",
        "FlexPanel", "GridContainer", "GridPanel", "HBoxContainer", "HFlowContainer",
        "MarginContainer", "OverlayPanel", "PanelContainer", "StackPanel", "VBoxContainer",
        "VFlowContainer", "Viewbox", "VirtualizingGridPanel", "VirtualizingStackPanel", "WrapPanel",

        // Decoration. Purely visual, with no interaction and nothing to announce.
        "Border", "ColorRect", "EllipseShape", "LineShape", "NinePatchRect", "NineSliceImage",
        "PathShape", "PolygonShape", "PolylineShape", "RectangleShape", "ReferenceRect",
        "HSeparator", "VSeparator",

        // Template plumbing. Internal presenters a template composes; the templated control that
        // owns them is what carries the role.
        "ContentControl", "ContentPresenter", "ItemsPresenter", "LineEditPresenter",
        "ScrollPresenter", "TemplatedControl", "TreePresenter",

        // Bases and diagnostics, never a leaf a user reaches.
        "Control", "Panel", "DynamicGlyphAtlasView", "GraphEditFilter", "GraphEditMinimap",

        // Images. There is no Image role in AccessibilityRole; until one exists these stay Generic
        // rather than borrow a role that means something else.
        "Image", "TextureRect", "ThemeIconRect", "ThemeIconView",

        // Text. Likewise there is no Label or Text role; a static string is announced through the
        // accessible name of whatever contains it.
        "Label", "TextBlock",

        // Split-container drag handles. Interactive, but they resize a region rather than
        // representing a value, and none of the available roles describes that.
        "SplitContainerDragger", "SplitContainerMultiDragger",

        // Docking. A dock pane is a tab host and a workspace is a layout root; both are candidates
        // for TabList/Group once the docking accessibility story is designed properly.
        "DockPane", "DockWorkspace",

        // ARIA has columnheader; AccessibilityRole does not, and Cell would misreport a header as
        // data. Revisit if the enum gains one.
        "DataGridColumnHeader",
    };

    [Test]
    public void EveryPublicControlDeclaresARoleOrIsAnAcknowledgedException()
    {
        var generic = typeof(Control).Assembly.GetTypes()
            .Where(type => type.IsPublic && !type.IsAbstract && typeof(Control).IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) != null)
            .Select(type => new { type.Name, Control = (Control)Activator.CreateInstance(type) })
            .Where(entry => entry.Control.AccessibilityRole == AccessibilityRole.Generic)
            .Select(entry => entry.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var unacknowledged = generic.Where(name => !IntentionallyGeneric.Contains(name)).ToArray();
        Assert.That(unacknowledged, Is.Empty,
            "These controls report Generic without being listed as intentional. Give each a role, or "
            + "add it to IntentionallyGeneric under the reason that applies.");

        // The list is a record of decisions, so an entry that no longer applies has to go too —
        // otherwise it silently keeps a future regression acknowledged in advance.
        var stale = IntentionallyGeneric.Where(name => !generic.Contains(name)).ToArray();
        Assert.That(stale, Is.Empty,
            "These are listed as intentionally Generic but no longer report Generic. Remove them.");
    }

    [Test]
    public void ItemContainersReportTheRoleTheirOwnerImplies()
    {
        // A container that announces itself as a list, grid or tree, whose children are anonymous,
        // cannot be navigated item by item — the exact failure this pins.
        Assert.Multiple(() =>
        {
            Assert.That(new ListBox().AccessibilityRole, Is.EqualTo(AccessibilityRole.List));
            Assert.That(new ListBoxItem().AccessibilityRole, Is.EqualTo(AccessibilityRole.ListItem));

            Assert.That(new DataGrid().AccessibilityRole, Is.EqualTo(AccessibilityRole.Grid));
            Assert.That(new DataGridRow().AccessibilityRole, Is.EqualTo(AccessibilityRole.Row));
            Assert.That(new DataGridCell().AccessibilityRole, Is.EqualTo(AccessibilityRole.Cell));

            Assert.That(new Tree().AccessibilityRole, Is.EqualTo(AccessibilityRole.Tree));
        });
    }
}
