// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System.Text;
using Forma.Xaml.Compiler.Html;

namespace Forma.Xaml.Tool;

/// <summary>
/// What an AI coding agent needs to author Forma UI in the HTML and CSS dialect: a skill in the common SKILL.md format with reference
/// files loaded on demand, and a short marked block for AGENTS.md or CLAUDE.md. Generated from the same catalog as the docs, so it
/// cannot describe something the converter does not do.
/// </summary>
internal static class AgentKit
{
    internal const string Version = "1";
    internal const string BlockBegin = "<!-- forma-agent:begin";
    internal const string BlockEnd = "<!-- forma-agent:end -->";

    internal static IReadOnlyDictionary<string, string> SkillFiles() => new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["SKILL.md"] = Skill(),
        ["references/authoring.md"] = Authoring(),
        ["references/tooling.md"] = Tooling(),
        ["references/troubleshooting.md"] = Troubleshooting(),
        ["references/support-matrix.md"] = FormaHtmlReference.SupportMatrix(),
        ["references/cookbook.md"] = FormaHtmlReference.CookbookPage(),
        ["references/reference.md"] = FormaHtmlReference.Reference(),
    };

    private static string Skill() => """
        ---
        name: forma-ui
        description: Author, restyle, debug and verify Forma UI written as .fhtml views and .fcss stylesheets (HTML and CSS dialect for the Forma game UI framework). Use when creating or changing a screen, a theme token, a control template, an animation or a list, grid or dialog, or when a UI diagnostic (FHTML…) appears.
        ---

        # Forma UI in HTML and CSS

        Forma views are `.fhtml` (HTML) and `.fcss` (CSS). They convert to Forma's XAML at build time; you never write XAML. Anything
        outside the dialect fails the build with a code, a location and a `Help:` line that names the nearest supported alternative.

        ## The loop (mandatory for every visible change)

        Run the tool as `forma-xaml` when it is on PATH; otherwise as `dotnet run --project <Forma>/tools/Forma.Xaml.Tool --` (the project's
        AGENTS.md says where). Do not report a UI change as done until step 4 has been run and you have looked at the picture.

        1. Edit the `.fhtml` or `.fcss` file. Reuse a class from the shared theme before writing a rule; values come from tokens
           (`var(--token)`), never raw literals.
        2. `forma-xaml validate <file>`: fix every diagnostic using its Help line (see `references/support-matrix.md`). Validation checks
           markup only, not that bound properties and handlers exist in C#; check those by reading the view model and code-behind.
        3. `forma-xaml format <file>`: the formatter is canonical.
        4. **Look at it.** Run `forma-xaml preview <file> -o <out>.png` (add `--state hover` for a state) and open the PNG with your
           image reader. In the running app use the game's MCP tools: `ui_query` with a CSS selector to confirm the control exists and
           where, `ui_inspect` to see why a rule did or did not apply (with `file:line`), `take_screenshot` with a `selector`.
           Always do both of these before reporting done: a preview (or element screenshot) you have looked at, and a query
           (`ui_query`) that confirms every control you added or restyled exists and shows its bounds and classes; for a style change
           also `ui_inspect` it. A change with no query result is unverified.
        5. Run the project's gallery or snapshot check; a visual difference is a defect to fix, not a baseline to update.

        ## Rules

        - Layout is web layout: `display: flex|grid`, `gap`, `margin`, `padding`, `min-*`/`max-*`. Padding on a flex container wraps it
          in a bordered box, as in a browser.
        - State is `:hover`, `:focus-visible`, `:disabled`, `:checked` and `data-*` attributes; animate with `@keyframes` and
          `animation`, and honor `@media (prefers-reduced-motion: reduce)`.
        - Template parts are marked with `part="name"` and styled with `::part(name)`; there is no `>>` in `.fcss`.
        - Bind with `bind:text`, `bind:visible`, `bind:items`, `onclick="Handler"` (a code-behind method name, never an expression).
        - Text is localized with `data-i18n="key"`; the key must exist in the locale files.
        - Do not invent properties, units or elements. If it is not in `references/support-matrix.md`, it is rejected on purpose.

        ## References (load only what you need)

        - `references/authoring.md`: structure of a view, tokens, classes, bindings, templates, lists, tabs, dialogs, animation.
        - `references/tooling.md`: every command and MCP tool with an example.
        - `references/troubleshooting.md`: diagnostics, "why is my style not applied", common mistakes.
        - `references/support-matrix.md`, `references/reference.md`, `references/cookbook.md`: generated, authoritative.
        """;

    internal static string AuthoringText() => Authoring();
    internal static string ToolingText() => Tooling();
    internal static string TroubleshootingText() => Troubleshooting();
    internal static IReadOnlyDictionary<string, string> DocFiles() => FormaHtmlReference.Files(Authoring(), Tooling(), Troubleshooting());

    private static string Authoring() => """
        # Authoring Forma views

        A view is one root element with `data-class` (the code-behind type) and `data-type` (the view-model type), preceded by
        `<meta name="f-namespace" content="local=clr-namespace:My.ViewModels">` lines and an optional `<link rel="stylesheet" href="theme.fcss">`.

        ```html
        <meta name="f-namespace" content="local=clr-namespace:Game.ViewModels">
        <div data-class="Game.Views.PauseView" data-type="local:PauseViewModel">
          <div id="MenuPanel" class="surface border" style="border-width: 1px; padding: 16px 18px; border-radius: 3px; min-width: 360px">
            <div style="display: flex; flex-direction: column; gap: 10px">
              <span class="title" bind:text="TitleText" style="align-self: center"></span>
              <button id="ResumeButton" class="button" bind:text="ResumeText" onclick="OnResumePressed"></button>
            </div>
          </div>
        </div>
        ```

        ## Tokens and classes

        `:root { --color-surface: #070D18DA; --space-4: 5px; }` in `.fcss` declares tokens; the property name is the resource key.
        Use them with `var(--color-surface)`. Colors are `#RRGGBB` or `#RRGGBBAA`. Theme overrides: `:root[data-theme="dark"] { … }`.

        ## Controls

        Application controls are registered with `<meta name="f-element" content="my-spin=views:SpinBox">` or placed with
        `<f-control type="views:SpinBox" id="…">`. Prefer the HTML and CSS spelling over `f:Property="value"`: `bind:<property-in-kebab-case>` binds any
        Forma property (`bind:font-color`, `bind:accessibility-label`, `bind:selected-index="Path;mode=TwoWay"`), and `aria-label`, `pointer-events`,
        `z-index`, `margin`, `min-width`, `text-align`, `align-self`, `flex-grow`, `data-expand`, `data-activate`, `data-flat` and the other `data-*` attributes
        in the support matrix ("Replacing f: properties") cover most cases. Use `f:Property` only for properties of an application control and values
        with no HTML or CSS spelling; `forma-xaml validate` hints when one has a spelling (`FHTML3001`).

        ## Templates, lists, grids, tabs

        - Control template: `<template for="button" id="MyButton"><f-border part="chrome"><slot></slot></f-border></template>`, applied with
          `-f-Template: template(MyButton)` in a rule.
        - List: `<ul bind:items="Rows" selectable><template data-type="local:RowVm" src="Row.fhtml"></template></ul>`.
        - Grid: `<table bind:items="Rows"><thead><tr><th width="2*"></th></tr></thead><tbody><template data-type="local:RowVm"><tr><td>…</td></tr></template></tbody></table>`.
        - Tabs: `<div role="tablist"><div role="tabpanel" id="General">…</div></div>`.

        ## Animation

        `@keyframes fade { from { opacity: 0.88; } to { opacity: 1; } }` and `#Root { animation: fade 140ms ease-out forwards; }`. An
        animation targets one `#id`. In a shared `.fcss` the id must belong to the root view that links it (otherwise the build fails with FXAML6001 "target not found in the local namescope"); `animation: none` inside `@media (prefers-reduced-motion: reduce)` turns it off. Start it from code with the storyboard named after the animation.
        """;

    private static string Tooling() => """
        # Tools

        | Task | Command or MCP tool |
        | --- | --- |
        | Check a view or stylesheet | `forma-xaml validate [--format human|json|sarif] <file|dir>` |
        | Format | `forma-xaml format [--check] <file|dir>` |
        | Render one view | `forma-xaml preview View.fhtml -o out.png [--state hover] [--lang ja] [--scale 130] [--size 1280x720]` |
        | Find clipped or overflowing text | `forma-xaml validate --layout View.fhtml [--lang ja] [--scale 130] [--size 1280x720]`; in a test `UIContext.FindLayoutProblems` and `UiMatrix.Run`; in the live app a game MCP `ui_layout_problems` when the game provides it |
        | Reference docs | `forma-xaml docs --out docs` (regenerates); `forma-xaml docs --out docs --check` (drift) |
        | Find controls in the live app | game MCP `game_ui` `ui-query button=<css selector>` |
        | Why is a rule (not) applied | game MCP `game_ui` `ui-inspect button=<id, name or selector>` |
        | Element or region screenshot | game MCP `take_screenshot selector=<css>` or `clip=x,y,w,h` |
        | Preferences and devices | game MCP `game_ui` `ui-emulate key=reduced-motion|dark|keyboard|gamepad|…` |
        | Install or check this skill | `forma-xaml agent install|update|doctor` |

        Preview renders with the application, so give it a long timeout (3 minutes) and read the PNG afterwards. A view with no scene
        in the project's preview host cannot be previewed; preview the nearest screen and say so.

        After any layout change, run the layout check for every language and UI scale the game ships, plus the pseudo-locale
        (`PseudoLocalizer`), and fix the layout; do not shorten text to pass.

        The `forma-xaml mcp` server exposes `validate`, `format`, `preview` and the generated docs to any MCP client.
        """;

    private static string Troubleshooting() => """
        # Troubleshooting

        - **A diagnostic** (`FHTMLnnnn`): read its `Help:` line; it names the supported alternative. `references/support-matrix.md` lists
          every property, selector and at-rule with its status.
        - **My style is not applied**: `ui-inspect button=<selector>` lists every rule with MATCH or NO and the reason, and the winning
          value with `file:line`. Typical causes: a type selector for the wrong control, a missing class, specificity, a rule in a
          view that does not link the stylesheet.
        - **A value looks wrong**: colors are `#RRGGBBAA` in `.fcss` (alpha last), not `#AARRGGBB`.
        - **Padding changed the layout**: on a flex or grid element it wraps the container in a Border; use `margin` for outside space.
        - **A class style on a ColorRect moved text**: set the color as an element value instead.
        """;

    internal static string Block() => $"""
        {BlockBegin} v{Version} -->
        ## Forma UI (generated; update with `forma-xaml agent update`)

        UI is written as `.fhtml` (HTML) and `.fcss` (CSS) and converted to Forma XAML at build time; never write XAML. Load the `forma-ui`
        skill before authoring or changing a screen. Loop: edit, `forma-xaml validate`, `forma-xaml format`, look at it
        (`forma-xaml preview` or the app's MCP `ui_query`/`ui_inspect`/`take_screenshot`), run the gallery check. Every rejection has a
        `Help:` line naming the supported alternative; do not invent properties or approximate with unsupported ones.
        {BlockEnd}
        """;
}
