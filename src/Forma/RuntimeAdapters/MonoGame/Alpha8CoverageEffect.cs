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
        public static bool RequiresColorGlyphAtlas =>
#if FORMA_BROWSER
            true;
#else
            PlatformInfo.GraphicsBackend is GraphicsBackend.OpenGL or GraphicsBackend.Vulkan or GraphicsBackend.Metal;
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
                GraphicsBackend.Vulkan or GraphicsBackend.Metal => "Forma.Alpha8Coverage.Vulkan.mgfxo.b64",
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