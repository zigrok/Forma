// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

namespace Forma.Xaml.Compiler.Html;

/// <summary>One mapped element or construct of the dialect, with a small view that demonstrates it and the Forma type it lowers to.</summary>
public sealed record FormaHtmlCatalogEntry(string Name, string Html, string FormaType);

/// <summary>The dialect's mapping catalog. Every entry is converted, compiled and checked by a test, so the table cannot drift from the converter.</summary>
public static class FormaHtmlDialect
{
    public static IReadOnlyList<FormaHtmlCatalogEntry> Catalog { get; } =
    [
        new("div (plain)", "<div><span>x</span></div>", "Container"),
        new("div (display: flex, row)", "<div style=\"display: flex; gap: 4px\"><span>a</span><span>b</span></div>", "HBoxContainer"),
        new("div (display: flex, column)", "<div style=\"display: flex; flex-direction: column; gap: 4px\"><span>a</span></div>", "VBoxContainer"),
        new("div (padding, border, background)", "<div style=\"padding: 4px; border-width: 1px; background-color: #112233\"><span>a</span></div>", "Border"),
        new("div (display: grid)", "<div style=\"display: grid; grid-template-columns: 1fr 2fr\"><span>a</span><span>b</span></div>", "GridPanel"),
        new("section, nav, main, header, footer", "<section><span>a</span></section>", "Container"),
        new("span, p, label, h1-h6", "<p>Text</p>", "Label"),
        new("button", "<button>Go</button>", "Button"),
        new("input type=text", "<input type=\"text\" value=\"a\">", "LineEdit"),
        new("input type=checkbox", "<input type=\"checkbox\" checked>", "CheckBox"),
        new("input type=range", "<input type=\"range\" min=\"0\" max=\"10\" value=\"2\">", "HSlider"),
        new("textarea", "<textarea></textarea>", "TextEdit"),
        new("select", "<select></select>", "OptionButton"),
        new("ul, ol, li", "<ul><li><span>a</span></li></ul>", "VBoxContainer"),
        new("table", "<table><tr><td>a</td><td>b</td></tr></table>", "GridPanel"),
        new("f-border", "<f-border><span>a</span></f-border>", "Border"),
        new("f-scroll", "<f-scroll data-vertical=\"Auto\"><span>a</span></f-scroll>", "ScrollContainer"),
        new("f-group-box", "<f-group-box><span>a</span></f-group-box>", "GroupBox"),
        new("f-control", "<f-control type=\"Control\"></f-control>", "Control"),
        new("<style> tokens (:root, var())", "<style>:root { --accent: #FF8800; --dim: 0.5; } span.a { color: var(--accent); opacity: var(--dim); }</style><div><span class=\"a\">x</span></div>", "Container"),
        new("<style> @media and transition", "<style>@media (input-modality: pointer) { button.b:hover { opacity: 0.5; } } button.b { transition: opacity 150ms; }</style><div><button class=\"b\">x</button></div>", "Container"),
        new("::part() and part attribute", "<style>button.k::part(chrome) { opacity: 0.5; }</style><div><span part=\"chrome\">x</span></div>", "Container"),
        new("typed and attribute selectors", "<style>input[type=checkbox] { opacity: 1; } table.t { opacity: 1; } [role=tablist] { opacity: 1; } select { opacity: 1; }</style><div></div>", "Control"),
        new("<template for> control template with <slot> and part", "<style>button.t { -f-Template: template(tb); }</style><template for=\"button\" id=\"tb\"><f-border part=\"chrome\"><slot></slot></f-border></template><div><button class=\"t\">x</button></div>", "Container"),
        new("ul/ol bind:items with <template>", "<meta name=\"f-namespace\" content=\"t=clr-namespace:Forma.Xaml.Compiler.Tests;assembly=Forma.Xaml.Compiler.Tests\"><div data-type=\"t:ListModel\"><ul bind:items=\"Rows\"><template data-type=\"t:RowModel\"><div><span bind:text=\"Name\"></span></div></template></ul></div>", "Container"),
        new("ul selectable bind:items", "<meta name=\"f-namespace\" content=\"t=clr-namespace:Forma.Xaml.Compiler.Tests;assembly=Forma.Xaml.Compiler.Tests\"><div data-type=\"t:ListModel\"><ul selectable bind:items=\"Rows\"><template data-type=\"t:RowModel\"><div><span bind:text=\"Name\"></span></div></template></ul></div>", "Container"),
        new("table bind:items data grid", "<meta name=\"f-namespace\" content=\"t=clr-namespace:Forma.Xaml.Compiler.Tests;assembly=Forma.Xaml.Compiler.Tests\"><div data-type=\"t:ListModel\"><table bind:items=\"Rows\"><thead><tr><th>Name</th></tr></thead><tbody><template data-type=\"t:RowModel\"><tr><td><span bind:text=\"Name\"></span></td></tr></template></tbody></table></div>", "Container"),
        new("ARIA tablist and tabpanel", "<div role=\"tablist\"><div role=\"tabpanel\" id=\"One\"><span>1</span></div></div>", "TabContainer"),
        new("dialog", "<dialog backdrop=\"scrim\"><span>x</span></dialog>", "Container"),
        new("@keyframes and animation", "<style>@keyframes f { from { opacity: 0.5; } to { opacity: 1; } } #a { animation: f 200ms ease-out; }</style><div><span id=\"a\">x</span></div>", "Container"),
        new("@media prefers-reduced-motion and prefers-color-scheme", "<style>@media (prefers-reduced-motion: reduce) { span.m { opacity: 0.5; } } @media (prefers-color-scheme: dark) { span.m { font-weight: bold; } }</style><div><span class=\"m\">x</span></div>", "Container"),
        new("progress", "<div><progress value=\"1\" max=\"2\"></progress></div>", "Container"),
        new("details and summary", "<div><details><summary>t</summary><span>x</span></details></div>", "Container"),
        new("hr and empty div as ColorRect", "<div><hr style=\"background-color: #112233\"></div>", "Container"),
        new("registered custom element", "<meta name=\"f-element\" content=\"my-label=Label\"><div><my-label></my-label></div>", "Container"),
        new("box model: padding on flex wraps in a Border; margin", "<div style=\"display: flex; padding: 4px; margin: 2px\"><span>x</span></div>", "Border"),
        new("flex-wrap and overflow", "<div style=\"display: flex; flex-wrap: wrap; overflow: hidden\"><span>x</span></div>", "HFlowContainer"),
        new("align-items and justify-content", "<div style=\"display: flex; align-items: center; justify-content: flex-end\"><span>x</span></div>", "HBoxContainer"),
        new("width, height and max sizes", "<div style=\"width: 10px; height: 20px; max-width: 30px\"><span>x</span></div>", "Container"),
        new("data-* state and [data-*] selectors", "<style>span[data-state=open] { opacity: 0.5; }</style><div><span data-state=\"open\">x</span></div>", "Container"),
        new(":is(), :first-child, :nth-child(), [disabled]", "<style>span:is(.a, .b):first-child { opacity: 1; } span:nth-child(2n+1) { opacity: 1; } button[disabled] { opacity: 1; }</style><div><span class=\"a\">x</span></div>", "Container"),
        new(":focus-visible", "<style>button.k:focus-visible { opacity: 0.5; }</style><div><button class=\"k\">a</button></div>", "Container"),
        new("theme overrides :root[data-theme]", "<style>:root { --a: #112233; } :root[data-theme=\"dark\"] { --a: #000000; } f-border.c { background-color: var(--a); }</style><div><f-border class=\"c\"><span>x</span></f-border></div>", "Container"),
        new("dir and lang", "<div dir=\"rtl\" lang=\"ar\"><span>x</span></div>", "Container"),
        new("data-i18n", "<div><span data-i18n=\"menu.play\"></span></div>", "Container"),
        new("-f-Property and resource()", "<style>span.c { -f-Opacity: 0.5; color: resource(External.Color); }</style><div><span class=\"c\">x</span></div>", "Container"),
    ];
}
