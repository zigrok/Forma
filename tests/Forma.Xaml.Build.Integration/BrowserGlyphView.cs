// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml;

namespace Forma.Xaml.Build.Integration;

public sealed class BrowserGlyphView : Label
{
    public BrowserGlyphView() => FormaXamlLoader.Load(this);
    public BrowserGlyphView(string text) : this() => DataContext = new BrowserGlyphViewModel { Text = text };
}

public sealed class BrowserGlyphViewModel
{
    public string Text { get; init; } = "";
}
