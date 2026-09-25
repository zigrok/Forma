// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;

namespace Forma
{
    internal static class Alpha8CoverageEffect
    {
        // GraphicsBackend.Headless (6) exists only in the zigrok MonoGame fork. Forma builds against
        // stock MonoGame by default, where naming the member outright is a compile error, so it is
        // matched by value instead. Kept as a const so it still works in a pattern.
        private const GraphicsBackend Headless = (GraphicsBackend)6;

        public static bool RequiresColorGlyphAtlas =>
#if FORMA_BROWSER
            true;
#else
            PlatformInfo.GraphicsBackend is GraphicsBackend.OpenGL or GraphicsBackend.Vulkan
                or GraphicsBackend.Metal or Headless;
#endif

        public static Effect Create(GraphicsDevice graphicsDevice)
        {
#if FORMA_BROWSER
            // Native GLES3 has its own MGFX profile; DesktopGL bytecode is not compatible.
            var resourceName = PlatformInfo.GraphicsBackend switch
            {
                GraphicsBackend.WebGL => "Forma.Alpha8Coverage.Browser.mgfxo.b64",
                _ => throw new NotSupportedException($"The browser Forma build requires the native WebGL backend, not {PlatformInfo.GraphicsBackend}."),
            };
#else
            var resourceName = PlatformInfo.GraphicsBackend switch
            {
                GraphicsBackend.OpenGL => "Forma.Alpha8Coverage.OpenGL.mgfxo.b64",
                GraphicsBackend.DirectX => "Forma.Alpha8Coverage.DirectX11.mgfxo.b64",
                GraphicsBackend.DirectX12 => "Forma.Alpha8Coverage.DirectX12.mgfxo.b64",
                // Headless reports the Vulkan shader profile (80) and never executes a shader, so it
                // loads the same blob. Taking the real path rather than a null one keeps a headless
                // run exercising the same effect-loading code the desktop backends do.
                GraphicsBackend.Vulkan or GraphicsBackend.Metal or Headless
                    => "Forma.Alpha8Coverage.Vulkan.mgfxo.b64",
                _ => throw new NotSupportedException($"The {PlatformInfo.GraphicsBackend} graphics backend does not have an embedded Alpha8 coverage effect."),
            };
#endif
            using var stream = typeof(Alpha8CoverageEffect).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"The embedded MonoGame Alpha8 coverage effect '{resourceName}' is missing. Browser builds require FormaBrowserAlpha8EffectPath pointing to bytecode compiled for the native browser shader profile.");
            using var reader = new StreamReader(stream);
            return new Effect(graphicsDevice, Convert.FromBase64String(reader.ReadToEnd()));
        }
    }
}