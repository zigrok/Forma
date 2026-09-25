// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
// FoldableContainer and FoldableGroup behavior is adapted from Godot Engine's
// scene/gui/foldable_container.cpp; see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    /// <summary>Enforces single-expansion accordion behavior across a set of <see cref="FoldableContainer"/> instances, matching Godot's FoldableGroup resource.</summary>
    public sealed class FoldableGroup
    {
        private readonly List<FoldableContainer> _containers = new List<FoldableContainer>();
        private bool _allowFoldingAll;
        internal bool UpdatingGroup;
        /// <summary>When false, folding the group's only expanded container is refused, matching Godot's allow_folding_all.</summary>
        public bool AllowFoldingAll
        {
            get => _allowFoldingAll;
            set
            {
                _allowFoldingAll = value;
                if (!_allowFoldingAll && GetExpandedContainer() == null && _containers.Count > 0)
                {
                    UpdatingGroup = true;
                    _containers[0].Folded = false;
                    UpdatingGroup = false;
                }
            }
        }
        public void SetAllowFoldingAll(bool enabled) => AllowFoldingAll = enabled;
        public bool IsAllowFoldingAll() => AllowFoldingAll;
        /// <summary>Raised after a member container expands and its siblings are folded, matching Godot's FoldableGroup.expanded signal.</summary>
        public event Action<FoldableGroup, FoldableContainer> Expanded;
        /// <summary>Returns the group's single expanded container, or null when all members are folded.</summary>
        public FoldableContainer GetExpandedContainer()
        {
            foreach (var container in _containers) if (!container.Folded) return container;
            return null;
        }
        public IReadOnlyList<FoldableContainer> GetContainers() => _containers;
        internal void Add(FoldableContainer container) { if (!_containers.Contains(container)) _containers.Add(container); }
        internal void Remove(FoldableContainer container) => _containers.Remove(container);
        internal void HandleContainerExpanded(FoldableContainer expanded)
        {
            UpdatingGroup = true;
            foreach (var container in _containers) if (container != expanded) container.Folded = true;
            UpdatingGroup = false;
            Expanded?.Invoke(this, expanded);
        }
    }

    /// <summary>Collapsible vertically arranged section used by inspector-style interfaces.</summary>
    public sealed class FoldableContainer : TemplatedControl
    {
        public override AccessibilityRole AccessibilityRole => AccessibilityRole.Group;
        public override string AccessibilityName => string.IsNullOrEmpty(base.AccessibilityName) ? Title ?? string.Empty : base.AccessibilityName;
        public override AccessibilityActions AccessibilityActions => base.AccessibilityActions |
            (Folded ? AccessibilityActions.Expand : AccessibilityActions.Collapse);
        public override AccessibilityStates AccessibilityStates => base.AccessibilityStates |
            (Folded ? AccessibilityStates.Collapsed : AccessibilityStates.Expanded);
        private bool _folded;
        private FoldableGroup _foldableGroup;
        private bool _changingGroup;
        public FoldableContainer()
        {
            FocusMode = FocusMode.All;
            HeaderHeight = 28;
            ChildAdded += OnLogicalChildrenChanged;
            ChildRemoved += OnLogicalChildrenChanged;
        }
        public string Title { get; set; } = string.Empty;
        public float HeaderHeight { get; set; }
        public bool Folded
        {
            get => _folded;
            set
            {
                if (_folded == value) return;
                if (!_changingGroup && _foldableGroup != null)
                {
                    if (!value) _foldableGroup.HandleContainerExpanded(this);
                    else if (!_foldableGroup.UpdatingGroup && _foldableGroup.GetExpandedContainer() == this && !_foldableGroup.AllowFoldingAll) return;
                }
                _folded = value;
                foreach (var child in Children) child.Visible = !value;
                QueueLayout();
            }
        }
        /// <summary>Assigns the accordion group this container participates in, matching Godot's set_foldable_group.</summary>
        public FoldableGroup FoldableGroup
        {
            get => _foldableGroup;
            set
            {
                _foldableGroup?.Remove(this);
                _foldableGroup = value;
                if (_foldableGroup != null)
                {
                    _changingGroup = true;
                    if (_folded && _foldableGroup.GetExpandedContainer() == null && !_foldableGroup.AllowFoldingAll) Folded = false;
                    else if (!_folded && _foldableGroup.GetExpandedContainer() != null) Folded = true;
                    _foldableGroup.Add(this);
                    _changingGroup = false;
                }
            }
        }
        public void SetFoldableGroup(FoldableGroup group) => FoldableGroup = group;
        public FoldableGroup GetFoldableGroup() => FoldableGroup;
        // Godot's FoldableContainer::fold()/expand() call set_folded() and then unconditionally emit
        // folding_changed - unlike set_folded's own early-return guard, which only gates the state
        // mutation, not signal emission at these explicit call sites (or at the gui_input toggle paths
        // below). The bare `Folded` property setter matches Godot's property binding straight to
        // set_folded, which never emits the signal itself - including for the sibling folds a
        // FoldableGroup cascades through that same bare setter.
        public void Fold() { Folded = true; FoldedChanged?.Invoke(this, Folded); }
        public void Expand() { Folded = false; FoldedChanged?.Invoke(this, Folded); }
        public event Action<FoldableContainer, bool> FoldedChanged;
        public override Vector2 GetMinimumSize()
        {
            var size = new Vector2(CustomMinimumSize.X, Math.Max(CustomMinimumSize.Y, HeaderHeight));
            if (Folded) return size;
            foreach (var child in Children) if (child.Visible)
            {
                var min = child.GetMinimumSize();
                size.X = Math.Max(size.X, min.X); size.Y += min.Y;
            }
            return size;
        }
        protected override void ArrangeChildren()
        {
            base.ArrangeChildren();
            TemplateRoot?.QueueLayout();
        }
        internal void ArrangePresentedChildren(IReadOnlyList<ContentPresenter> presenters, Vector2 availableSize)
        {
            var y = HeaderHeight;
            foreach (var presenter in presenters)
            {
                if (presenter.Content is not Control child || !child.Visible) continue;
                var height = child.GetMinimumSize().Y;
                presenter.Position = new Vector2(0, y);
                presenter.Size = new Vector2(availableSize.X, height);
                y += height;
            }
        }
        protected internal override void PointerPressed(Point point)
        {
            if (point.Y >= Bounds.Top && point.Y < Bounds.Top + HeaderHeight) { GrabFocus(); Folded = !Folded; FoldedChanged?.Invoke(this, Folded); }
            else base.PointerPressed(point);
        }
        /// <summary>Toggles folding on the accept action while focused, matching Godot's FoldableContainer::gui_input ui_accept handling.</summary>
        internal override void KeyPressed(Keys key)
        {
            if (key == Keys.Enter || key == Keys.Space) { Folded = !Folded; FoldedChanged?.Invoke(this, Folded); return; }
            base.KeyPressed(key);
        }
        internal override bool HitTestBeforeChildren(Point point) =>
            point.Y >= Bounds.Top && point.Y < Bounds.Top + HeaderHeight;
        private void OnLogicalChildrenChanged(Control owner, Control child)
        {
            if (TemplateRoot is FoldableContainerPresenter presenter) presenter.SyncChildren();
            else if (child.VisualParent == this) RemoveVisualChild(child);
            QueueLayout();
        }
        internal string GetArrowIconName() => Folded ? IsLayoutRtl() ? "folded_arrow_mirrored" : "folded_arrow" : IsLayoutRtl() ? "expanded_arrow_mirrored" : "expanded_arrow";
    }

    /// <summary>Button carrying a color preset for color-palette workflows.</summary>
    public sealed class ColorPresetButton : BaseButton
    {
        public Color Color { get; set; } = Color.White;
    }
}
