// SPDX-License-Identifier: MIT

using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Forma
{
    /// <summary>
    /// Saves what was drawn to a PNG.
    /// </summary>
    /// <remarks>
    /// Here rather than in each application because every one of them ends up writing it: a
    /// sample's <c>--screenshot</c> flag, a test harness capturing a failure, a tool exporting a
    /// preview. They were all the same ten lines, and each copy re-learned the timing rule below
    /// the hard way.
    /// </remarks>
    public static class ScreenCapture
    {
        /// <summary>
        /// Writes the current back buffer to <paramref name="path"/> as a PNG.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Call this at the end of a draw, not from outside the loop.</b> A back buffer that
        /// has not been presented reads back cleared, so a capture taken from a test between
        /// frames produces a solid black image — one that looks like a real screenshot of a
        /// broken UI rather than like a mistake in the capture.
        /// </para>
        /// <para>
        /// A headless backend discards every draw call, so there is nothing to capture there
        /// either. Callers that can be run both ways should say which they are rather than
        /// writing a black rectangle.
        /// </para>
        /// </remarks>
        public static void SaveBackBuffer(GraphicsDevice device, string path)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required.", nameof(path));

            var width = device.PresentationParameters.BackBufferWidth;
            var height = device.PresentationParameters.BackBufferHeight;
            if (width <= 0 || height <= 0) throw new InvalidOperationException("The back buffer has no size yet.");

            var pixels = new Color[width * height];
            device.GetBackBufferData(pixels);

            using var texture = new Texture2D(device, width, height);
            texture.SetData(pixels);

            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using var stream = File.Create(full);
            texture.SaveAsPng(stream, width, height);
        }
    }
}
