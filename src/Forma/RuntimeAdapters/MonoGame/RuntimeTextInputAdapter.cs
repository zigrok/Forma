// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;

namespace Forma
{
    internal sealed class RuntimeTextInputAdapter : IDisposable
    {
        private readonly GameWindow _window;
        private readonly Action<char> _handler;
        private readonly RuntimeTextCompositionBridge _bridge;
        private readonly RuntimeTextCompositionSession _session;

        public RuntimeTextInputAdapter(Game game, UIContext context)
        {
            _window = game.Window;
            _handler = context.TextInput;
            _bridge = RuntimeTextCompositionBridge.TryCreate(_window, typeof(GameWindow));
            if (_bridge != null) _session = new RuntimeTextCompositionSession(context, _bridge);
            _window.TextInput += OnTextInput;
        }

        internal bool SupportsTextComposition => _session != null;
        internal void Update(bool active) => _session?.Update(active);

        private void OnTextInput(object sender, TextInputEventArgs e)
        {
            if (_session == null) _handler(e.Character);
        }

        public void Dispose()
        {
            _session?.Dispose();
            _bridge?.Dispose();
            _window.TextInput -= OnTextInput;
        }
    }
}