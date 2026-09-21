// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Forma
{
    internal sealed class RuntimeTextInputAdapter : IDisposable
    {
        private readonly Action<char> _handler;

        public RuntimeTextInputAdapter(Game game, UIContext context)
        {
            _handler = context.TextInput;
            TextInputEXT.StartTextInput();
            TextInputEXT.TextInput += _handler;
        }

        internal bool SupportsTextComposition => false;
        internal void Update(bool active) { }

        public void Dispose()
        {
            TextInputEXT.TextInput -= _handler;
            TextInputEXT.StopTextInput();
        }
    }
}