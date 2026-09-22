// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma.Tests;

public sealed class PlatformInputCancellationTest
{
    private sealed class VirtualFileSystem : IFileDialogFileSystem
    {
        internal string Root { get; } = Path.Combine(Environment.CurrentDirectory, "b03-virtual-files");
        internal string Child => Path.Combine(Root, "child");
        public bool IsAvailable => true;
        public string GetCurrentDirectory() => Root;
        public bool FileExists(string path) => false;
        public bool DirectoryExists(string path) => path == Root || path == Child;
        public IEnumerable<string> EnumerateEntries(string path) => path == Root ? new[] { Child } : Array.Empty<string>();
        public string GetParentDirectory(string path) => path == Child ? Root : null;
        public void CreateDirectory(string path) => throw new NotSupportedException();
        public DateTime GetLastWriteTimeUtc(string path) => DateTime.UnixEpoch;
    }
    private static readonly GameTime Frame = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    private static UIContext Context(Control root)
    {
        var context = new UIContext { ViewportSize = new Vector2(640, 480) };
        context.Add(root);
        context.Layout();
        return context;
    }
    private static void Tick(UIContext context, Point point, bool pressed = false) =>
        context.Update(Frame, new MouseState(point.X, point.Y, 0,
            pressed ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released), default);

    [TestCase(Orientation.Horizontal)]
    [TestCase(Orientation.Vertical)]
    public void SliderCancellationStopsUnpressedMotionWithoutCommittingAndAllowsNewGesture(Orientation orientation)
    {
        var slider = new Slider(orientation) { Size = new Vector2(120, 120), Value = 50 };
        using var context = Context(slider);
        var completed = 0;
        slider.DragEnded += (_, _) => completed++;
        Tick(context, new Point(25, 25), true);
        Tick(context, new Point(40, 40), true);
        var value = slider.Value;
        context.ResetPlatformInput();
        Tick(context, new Point(85, 85));
        Assert.Multiple(() =>
        {
            Assert.That(slider.Value, Is.EqualTo(value));
            Assert.That(completed, Is.Zero);
            Assert.That(context.FocusedControl, Is.SameAs(slider));
            Assert.That(context.HasPinnedInteraction(slider), Is.False);
        });
        Tick(context, new Point(60, 60), true);
        Tick(context, new Point(90, 90), true);
        Tick(context, new Point(90, 90));
        Assert.That(slider.Value, Is.Not.EqualTo(value));
        Assert.That(completed, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SpinBoxCancellationStopsDraggingAndHeldArrowRepeat(bool drag)
    {
        var spin = new SpinBox { Size = new Vector2(100, 24), Value = 50 };
        using var context = Context(spin);
        spin.PointerPressed(new Point(94, 4));
        if (drag) { spin.PointerMoved(new Point(94, 12)); spin.PointerMoved(new Point(94, 18)); }
        var value = spin.Value;
        context.ResetPlatformInput();
        spin.PointerMoved(new Point(94, 20));
        spin.Process(Frame);
        Assert.That(spin.Value, Is.EqualTo(value));
        Assert.That(spin.IsDraggingValue, Is.False);
    }

    [TestCase(Orientation.Horizontal)]
    [TestCase(Orientation.Vertical)]
    public void ScrollbarCancellationStopsGrabberAndSmoothMotion(Orientation orientation)
    {
        var scroll = new ScrollBar(orientation) { Size = new Vector2(150, 150), MaxValue = 100, Page = 20, Value = 40 };
        using var context = Context(scroll);
        var point = scroll.GetGrabberRectangle().Center;
        scroll.PointerPressed(point);
        Assert.That(scroll.IsDraggingGrabber, Is.True);
        context.ResetPlatformInput();
        scroll.PointerMoved(point + new Point(25, 25));
        scroll.Process(Frame);
        Assert.That(scroll.Value, Is.EqualTo(40));
        Assert.That(scroll.IsDraggingGrabber, Is.False);
        scroll.SmoothScrollEnabled = true;
        scroll.PointerPressed(new Point(orientation == Orientation.Horizontal ? 120 : 5,
            orientation == Orientation.Vertical ? 120 : 5));
        var value = scroll.Value;
        context.ResetPlatformInput();
        scroll.Process(Frame);
        Assert.That(scroll.Value, Is.EqualTo(value));
        Assert.That(scroll.IsSmoothScrolling, Is.False);
    }

    [Test]
    public void NoncapturedScrollbarDragNodeObserverIsCancelled()
    {
        var root = new Control { Size = new Vector2(200, 120) };
        var content = new Control { Name = "Content", Size = new Vector2(100, 100) };
        var scroll = new VScrollBar { Position = new Vector2(120, 0), Size = new Vector2(14, 100), Value = 40, MaxValue = 100, Page = 20 };
        root.AddChild(content);
        root.AddChild(scroll);
        scroll.SetDragNode("../Content");
        using var context = Context(root);
        context.TouchscreenAvailable = true;
        Tick(context, new Point(20, 60), true);
        Tick(context, new Point(20, 40), true);
        Assert.That(scroll.IsDragNodeTouching, Is.True);
        var value = scroll.Value;
        context.ResetPlatformInput();
        Tick(context, new Point(20, 20));
        scroll.Process(Frame);
        Assert.That(scroll.IsDragNodeTouching, Is.False);
        Assert.That(scroll.IsDragNodeDecelerating, Is.False);
        Assert.That(scroll.Value, Is.EqualTo(value));
    }

    [Test]
    public void ScrollContainerCancellationStopsObservedTouchInertia()
    {
        var scroll = new ScrollContainer { Size = new Vector2(100, 100) };
        var content = new Control { CustomMinimumSize = new Vector2(300, 500), Size = new Vector2(300, 500) };
        scroll.AddChild(content);
        using var context = Context(scroll);
        context.TouchscreenAvailable = true;
        Tick(context, new Point(20, 60), true);
        Tick(context, new Point(20, 30), true);
        Assert.That(scroll.IsTouchDragging, Is.True);
        var offset = scroll.ScrollOffset;
        context.ResetPlatformInput();
        Tick(context, new Point(20, 10));
        Assert.That(scroll.IsTouchDragging, Is.False);
        Assert.That(scroll.IsTouchDragDecelerating, Is.False);
        Assert.That(scroll.ScrollOffset, Is.EqualTo(offset));
    }

    [Test]
    public void NestedSplitAndHelperDraggersStopWithoutChangingOffsets()
    {
        var outer = new HSplitContainer { Size = new Vector2(200, 120), DragAreaSize = 6 };
        var nested = new VSplitContainer { DragAreaSize = 6, DraggingNestedIntersections = true };
        nested.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
        nested.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
        outer.AddChild(nested);
        outer.AddChild(new Control { CustomMinimumSize = new Vector2(10, 10) });
        using var context = Context(outer);
        outer.PointerPressed(new Point(99, 59));
        outer.PointerMoved(new Point(119, 79));
        var offsets = new Vector2(outer.SplitOffset, nested.SplitOffset);
        context.ResetPlatformInput();
        outer.PointerMoved(new Point(130, 90));
        nested.PointerMoved(new Point(130, 90));
        Assert.That(new Vector2(outer.SplitOffset, nested.SplitOffset), Is.EqualTo(offsets));

        var single = new SplitContainerDragger { Target = outer };
        var multiple = new SplitContainerMultiDragger();
        multiple.Targets.Add(nested);
        context.Add(single);
        context.Add(multiple);
        single.PointerPressed(Point.Zero);
        multiple.PointerPressed(Point.Zero);
        context.ResetPlatformInput();
        single.PointerMoved(new Point(50, 60));
        multiple.PointerMoved(new Point(50, 60));
        Assert.That(new Vector2(outer.SplitOffset, nested.SplitOffset), Is.EqualTo(offsets));
    }

    [Test]
    public void DeferredColorPickerCancellationDoesNotFlushOrAddRecentPreset()
    {
        var picker = new ColorPicker { DeferredMode = true, Size = new Vector2(180, 140) };
        using var context = Context(picker);
        var changes = 0;
        picker.ColorChanged += (_, _) => changes++;
        picker.PointerPressed(new Point(175, 30));
        picker.PointerMoved(new Point(175, 90));
        var color = picker.Color;
        context.ResetPlatformInput();
        picker.PointerMoved(new Point(175, 120));
        Assert.That(picker.Color, Is.EqualTo(color));
        Assert.That(changes, Is.Zero);
        Assert.That(picker.RecentPresets, Is.Empty);
    }

    [Test]
    public void TabBarAndContainerCancellationStopsReordering()
    {
        var bar = new TabBar { Size = new Vector2(300, 28), TabSizing = TabBarSizingMode.Justify, DragToRearrangeEnabled = true };
        bar.AddTab("A"); bar.AddTab("B"); bar.AddTab("C");
        using var context = Context(bar);
        bar.PointerPressed(new Point(150, 10));
        context.ResetPlatformInput();
        bar.PointerMoved(new Point(250, 10));
        Assert.That(bar.Tabs, Is.EqualTo(new[] { "A", "B", "C" }));

        var container = new TabContainer { Size = new Vector2(300, 120), DragToRearrangeEnabled = true };
        foreach (var name in new[] { "A", "B", "C" }) container.AddChild(new Control { Name = name });
        using var other = Context(container);
        container.PointerPressed(new Point(150, 10));
        other.ResetPlatformInput();
        container.PointerMoved(new Point(250, 10));
        Assert.That(container.Children.Select(child => child.Name), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void VirtualJoystickCancellationNeutralizesInputWithoutReleaseEvent()
    {
        var joystick = new VirtualJoystick { Size = new Vector2(100, 100) };
        using var context = Context(joystick);
        var releases = 0;
        var values = new List<Vector2>();
        joystick.Released += (_, _) => releases++;
        joystick.ValueChanged += (_, value) => values.Add(value);
        joystick.PointerPressed(new Point(80, 50));
        Assert.That(joystick.Value, Is.Not.EqualTo(Vector2.Zero));
        context.ResetPlatformInput();
        joystick.PointerMoved(new Point(10, 50));
        joystick.PointerReleased(new Point(10, 50), true);
        context.ResetPlatformInput();
        Assert.That(joystick.IsPressed, Is.False);
        Assert.That(joystick.Value, Is.EqualTo(Vector2.Zero));
        Assert.That(values.Count, Is.EqualTo(2));
        Assert.That(values.Last(), Is.EqualTo(Vector2.Zero));
        Assert.That(releases, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TreeCancellationStopsTouchDraggingAndInertia(bool decelerating)
    {
        var tree = new Tree { Size = new Vector2(120, 80), ItemHeight = 20 };
        for (var index = 0; index < 20; index++) tree.CreateItem().Text = $"Row {index}";
        using var context = Context(tree);
        tree.BeginTouchDragScroll();
        tree.TouchDragScrollBy(relativeMotion: -30, velocity: -200);
        if (decelerating) tree.EndTouchDragScroll();
        Assert.That(tree.IsTouchDragging, Is.True);
        Assert.That(tree.IsTouchDragDecelerating, Is.EqualTo(decelerating));
        var offset = tree.GetScroll();
        context.ResetPlatformInput();
        tree.Process(new GameTime(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)));
        Assert.That(tree.IsTouchDragging, Is.False);
        Assert.That(tree.IsTouchDragDecelerating, Is.False);
        Assert.That(tree.TouchDragSpeed, Is.Zero);
        Assert.That(tree.GetScroll(), Is.EqualTo(offset));
    }

    [Test]
    public void HostedViewportCancelsItsChildCaptureAndDoesNotInventRelease()
    {
        var host = new SubViewportContainer { Size = new Vector2(100, 100) };
        var slider = new HSlider { Size = new Vector2(100, 50) };
        host.ViewportContext.Add(slider);
        using var context = Context(host);
        Tick(context, new Point(20, 20), true);
        Tick(context, new Point(40, 20), true);
        var value = slider.Value;
        var completed = 0;
        slider.DragEnded += (_, _) => completed++;
        context.ResetPlatformInput();
        Tick(context, new Point(80, 20));
        Assert.That(slider.Value, Is.EqualTo(value));
        Assert.That(completed, Is.Zero);
        Assert.That(host.ViewportContext.HasPinnedInteraction(slider), Is.False);
        Assert.That(host.ViewportContext.FocusedControl, Is.SameAs(slider));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GraphElementCancellationStopsMoveOrResizeWithoutCompletion(bool resize)
    {
        var graph = new GraphEdit { Size = new Vector2(500, 300), MinimapEnabled = false, SnappingEnabled = false };
        var node = new GraphNode { Name = "node", Position = new Vector2(40, 40), Size = new Vector2(100, 80), Resizable = true };
        graph.AddChild(node);
        using var context = Context(graph);
        var completions = 0;
        node.Dragged += (_, _, _) => completions++;
        node.ResizeEnd += (_, _) => completions++;
        var point = resize ? node.GetResizeHandleBounds().Center : node.Bounds.Center;
        node.PointerPressed(point);
        node.PointerMoved(point + new Point(10, 10));
        var position = node.Position;
        var size = node.Size;
        context.ResetPlatformInput();
        node.PointerMoved(point + new Point(30, 30));
        Assert.That(node.Position, Is.EqualTo(position));
        Assert.That(node.Size, Is.EqualTo(size));
        Assert.That(node.IsResizing, Is.False);
        Assert.That(completions, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GraphBackgroundCancellationStopsPanOrBoxSelection(bool boxSelection)
    {
        var graph = new GraphEdit { Size = new Vector2(500, 300), MinimapEnabled = false, BoxSelectionEnabled = boxSelection };
        var node = new GraphNode { Name = "node", Position = new Vector2(300, 150), Size = new Vector2(100, 80) };
        graph.AddChild(node);
        using var context = Context(graph);
        graph.PointerPressed(new Point(10, 10));
        graph.PointerMoved(new Point(30, 30));
        var offset = graph.ScrollOffset;
        var selected = node.Selected;
        context.ResetPlatformInput();
        graph.PointerMoved(new Point(450, 250));
        Assert.That(graph.ScrollOffset, Is.EqualTo(offset));
        Assert.That(node.Selected, Is.EqualTo(selected));
        Assert.That(graph.Panner.IsPanning, Is.False);
    }

    [Test]
    public void GraphConnectionCancellationNeverConnectsOrDropsOnEmpty()
    {
        var graph = new GraphEdit { Size = new Vector2(500, 300), MinimapEnabled = false };
        var source = new GraphNode { Name = "source", Size = new Vector2(100, 80) };
        source.AddOutputPort("out", 1);
        graph.AddChild(source);
        using var context = Context(graph);
        var emptyDrops = 0;
        graph.ConnectionToEmpty += (_, _, _, _) => emptyDrops++;
        graph.StartKeyboardConnecting(source, -1, 0);
        Assert.That(graph.IsConnectionDragging, Is.True);
        context.ResetPlatformInput();
        Assert.That(graph.IsConnectionDragging, Is.False);
        Assert.That(graph.IsKeyboardConnecting, Is.False);
        Assert.That(graph.Connections, Is.Empty);
        Assert.That(emptyDrops, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GraphMinimapCancellationStopsPanOrResize(bool resize)
    {
        var graph = new GraphEdit { Size = new Vector2(400, 300) };
        graph.AddChild(new GraphNode { Name = "far", Position = new Vector2(1000, 400), Size = new Vector2(100, 60) });
        using var context = Context(graph);
        var minimap = graph.Minimap;
        var point = resize ? minimap.GetResizeHandleBounds().Center : minimap.Bounds.Center;
        minimap.PointerPressed(point);
        var offset = graph.ScrollOffset;
        var size = graph.MinimapSize;
        context.ResetPlatformInput();
        minimap.PointerMoved(point + new Point(20, 20));
        Assert.That(graph.ScrollOffset, Is.EqualTo(offset));
        Assert.That(graph.MinimapSize, Is.EqualTo(size));
    }

    [Test]
    public void CodeMinimapCancellationStopsScrolling()
    {
        var code = new CodeEdit { Text = string.Join("\n", Enumerable.Range(0, 30).Select(i => $"line {i}")), Size = new Vector2(160, 48), DrawMinimap = true };
        using var context = Context(code);
        var minimap = code.GetMinimapBounds();
        code.PointerPressed(new Point(minimap.Center.X, minimap.Bottom - 1));
        var line = code.FirstVisibleLine;
        context.ResetPlatformInput();
        code.PointerMoved(new Point(minimap.Center.X, minimap.Top));
        Assert.That(code.FirstVisibleLine, Is.EqualTo(line));
    }

    [Test]
    public void RichTextCancellationPreservesSelectionAndStopsAutoScroll()
    {
        var text = new RichTextLabel { Text = "alpha beta gamma delta", SelectionEnabled = true, Size = new Vector2(220, 40) };
        using var context = Context(text);
        text.PointerPressed(new Point(5, 10));
        text.PointerMoved(new Point(80, 10));
        var selected = text.GetSelectedText();
        Assert.That(selected, Is.Not.Empty);
        context.ResetPlatformInput();
        text.PointerMoved(new Point(170, 30));
        text.Process(Frame);
        Assert.That(text.GetSelectedText(), Is.EqualTo(selected));
    }

    [Test]
    public void TreeCancellationStopsColumnResizeAndNumericRangeDragAndRepeat()
    {
        var tree = new Tree { Size = new Vector2(240, 120), Columns = 2, ColumnTitlesVisible = true };
        using var context = Context(tree);
        var divider = tree.Bounds.X + 1 + tree.GetColumnWidth(0);
        tree.PointerPressed(new Point(divider, 8));
        tree.PointerMoved(new Point(divider + 20, 8));
        var width = tree.GetColumnWidth(0);
        context.ResetPlatformInput();
        tree.PointerMoved(new Point(divider + 40, 8));
        Assert.That(tree.GetColumnWidth(0), Is.EqualTo(width));

        tree.ColumnTitlesVisible = false;
        var item = tree.CreateItem();
        item.SetRangeConfig(0, 0, 100, 1); item.SetRange(0, 50); item.SetEditable(0, true);
        context.Layout();
        var cell = tree.GetItemAreaRectangle(item, 0);
        var point = new Point(cell.X + 20, cell.Center.Y);
        tree.PointerPressed(point);
        tree.PointerMoved(point + new Point(0, 8));
        tree.PointerMoved(point + new Point(0, -2));
        var value = item.GetRange(0);
        context.ResetPlatformInput();
        tree.PointerMoved(point + new Point(0, -12));
        Assert.That(item.GetRange(0), Is.EqualTo(value));
        tree.PointerPressed(new Point(cell.Right - 2, cell.Y + 2));
        value = item.GetRange(0);
        context.ResetPlatformInput();
        tree.Process(Frame);
        Assert.That(item.GetRange(0), Is.EqualTo(value));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ListCancellationDoesNotCarryDoubleClickAcrossDeactivation(bool itemList)
    {
        var activated = 0;
        Control list;
        if (itemList)
        {
            var items = new ItemList { Size = new Vector2(120, 100), ItemHeight = 20 };
            items.AddItem("one");
            items.ItemActivated += (_, _) => activated++;
            list = items;
        }
        else
        {
            var items = new ListBox
            {
                Size = new Vector2(120, 100),
                ItemsSource = new[] { "one" },
                ItemTemplate = DataTemplate.Create<string>((_, _) => new Control { CustomMinimumSize = new Vector2(80, 20) })
            };
            items.ItemActivated += (_, _) => activated++;
            list = items;
        }
        using var context = Context(list);
        var point = list is ListBox listBox ? listBox.GetRealizedContainer(0).VisualBounds.Center : new Point(5, 5);
        list.PointerPressed(point);
        context.ResetPlatformInput();
        list.PointerPressed(point);
        list.PointerReleased(point, true);
        Assert.That(activated, Is.Zero);
        list.PointerPressed(point);
        list.PointerReleased(point, true);
        Assert.That(activated, Is.EqualTo(1));
    }

    [Test]
    public void FileDialogCancellationDoesNotTurnNextClickIntoDirectoryActivation()
    {
        var fileSystem = new VirtualFileSystem();
        var dialog = new FileDialog
        {
            FileSystem = fileSystem, FileMode = FileDialogMode.OpenFile,
            DisplayMode = FileDialogDisplayMode.List, Visible = true, Size = new Vector2(560, 360)
        };
        dialog.SetCurrentDir(fileSystem.Root);
        using var context = Context(dialog);
        var point = new Point(196, 118);
        dialog.PointerPressed(point);
        Assert.That(dialog.CurrentFile, Is.Not.Empty);
        context.ResetPlatformInput();
        dialog.PointerPressed(point);
        dialog.PointerReleased(point, true);
        Assert.That(dialog.CurrentPath, Is.EqualTo(fileSystem.Root));
        dialog.PointerPressed(point);
        dialog.PointerReleased(point, true);
        Assert.That(dialog.CurrentPath, Is.EqualTo(fileSystem.Child));
    }
}
