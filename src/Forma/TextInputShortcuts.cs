// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework.Input;

namespace Forma;

internal static class TextInputShortcuts
{
    internal static bool HasCommandModifier(KeyboardState keyboard) =>
        !keyboard.IsKeyDown(Keys.LeftAlt) && !keyboard.IsKeyDown(Keys.RightAlt) &&
        (TextNavigation.HasControl(keyboard) || TextNavigation.HasCommand(keyboard));
}
