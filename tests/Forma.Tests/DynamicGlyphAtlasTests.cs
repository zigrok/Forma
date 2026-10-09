// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Microsoft.Xna.Framework;

namespace Forma.Tests
{
    [TestFixture]
    public sealed class DynamicGlyphAtlasTests
    {
        [Test]
        public void AllocatorPlacesPaddedRectanglesDeterministicallyAtEdges()
        {
            var allocator = new GlyphRectangleAllocator(16, 16);

            Assert.That(allocator.TryAllocate(6, 6, 1, out var first), Is.True);
            Assert.That(allocator.TryAllocate(6, 6, 1, out var second), Is.True);
            Assert.That(allocator.TryAllocate(14, 6, 1, out var third), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(new Rectangle(1, 1, 6, 6)));
                Assert.That(second, Is.EqualTo(new Rectangle(9, 1, 6, 6)));
                Assert.That(third, Is.EqualTo(new Rectangle(1, 9, 14, 6)));
                Assert.That(allocator.UsedArea, Is.EqualTo(256));
                Assert.That(allocator.TryAllocate(1, 1, 0, out _), Is.False);
            });
        }

        [Test]
        public void AllocatorRejectsOversizedAndFragmentedRequests()
        {
            var allocator = new GlyphRectangleAllocator(10, 10);
            Assert.That(allocator.TryAllocate(6, 6, 0, out _), Is.True);
            Assert.That(allocator.TryAllocate(4, 4, 0, out _), Is.True);
            Assert.That(allocator.TryAllocate(7, 5, 0, out _), Is.False);
            Assert.That(allocator.TryAllocate(11, 1, 0, out _), Is.False);
        }

        [Test]
        public void ResetRestoresInitialPlacementAndZeroArea()
        {
            var allocator = new GlyphRectangleAllocator(16, 16);
            Assert.That(allocator.TryAllocate(4, 5, 2, out var first), Is.True);
            allocator.Reset();
            Assert.That(allocator.TryAllocate(4, 5, 2, out var afterReset), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(afterReset, Is.EqualTo(first));
                Assert.That(allocator.UsedArea, Is.EqualTo(72));
            });
        }

        [Test]
        public void ZeroAreaGlyphDoesNotConsumeAtlasSpace()
        {
            var allocator = new GlyphRectangleAllocator(4, 4);
            Assert.That(allocator.TryAllocate(0, 0, 1, out var empty), Is.True);
            Assert.That(empty, Is.EqualTo(Rectangle.Empty));
            Assert.That(allocator.UsedArea, Is.Zero);
        }

        [TestCase(1f, 14f, 1f)]
        [TestCase(1.854f, 14f, 26f / 14f)]
        [TestCase(2f, 14f, 2f)]
        public void DynamicGlyphsRasterizeAtWholePhysicalPixelSizeAndDrawOneToOne(float displayScale, float fontSize, float expectedRasterScale)
        {
            var baseline = new Vector2(10.25f, 20.75f);
            var rasterScale = UIRenderContext.GetDynamicGlyphRasterScale(displayScale, fontSize);
            var position = UIRenderContext.GetDynamicGlyphPosition(baseline, 2, 9, displayScale);

            Assert.Multiple(() =>
            {
                Assert.That(rasterScale, Is.EqualTo(expectedRasterScale).Within(0.0001f));
                Assert.That(fontSize * rasterScale, Is.EqualTo(MathF.Round(fontSize * rasterScale)).Within(0.0001f));
                Assert.That((position.X - 2 / displayScale) * displayScale, Is.EqualTo(MathF.Round(baseline.X * displayScale)).Within(0.001f));
                Assert.That((position.Y + 9 / displayScale) * displayScale, Is.EqualTo(MathF.Round(baseline.Y * displayScale)).Within(0.001f));
            });
        }

        [Test]
        public void ClearReleasesPagesBetweenFramesAndAllowsDeterministicReuse()
        {
            var store = new DynamicGlyphAtlasStore(new DynamicGlyphCacheOptions(8, 8, 2, 1));
            store.BeginFrame();
            store.GetOrAdd(Key(1), () => Bitmap(1, 2, 2, 42));
            Assert.That(() => store.Clear(), Throws.InvalidOperationException);
            store.EndFrame();
            var missesBeforeClear = store.GetDiagnostics().Misses;

            store.Clear();
            var cleared = store.GetDiagnostics();
            store.BeginFrame();
            store.GetOrAdd(Key(1), () => Bitmap(1, 2, 2, 42));
            store.EndFrame();

            Assert.Multiple(() =>
            {
                Assert.That(cleared.PageCount, Is.Zero);
                Assert.That(cleared.GlyphCount, Is.Zero);
                Assert.That(cleared.Bytes, Is.Zero);
                Assert.That(cleared.Misses, Is.EqualTo(missesBeforeClear));
                Assert.That(store.GetDiagnostics().PageCount, Is.EqualTo(1));
                Assert.That(store.GetDiagnostics().Misses, Is.EqualTo(missesBeforeClear + 1));
            });
        }

        [Test]
        public void StoreBatchesDirtyPagesAndReusesWarmGlyphs()
        {
            var store = new DynamicGlyphAtlasStore(new DynamicGlyphCacheOptions(8, 8, 2, 1));
            var firstKey = Key(1);
            var secondKey = Key(2);
            var rasterizations = 0;
            store.BeginFrame();
            var first = store.GetOrAdd(firstKey, () => { rasterizations++; return Bitmap(1, 2, 2, 10); });
            var second = store.GetOrAdd(secondKey, () => { rasterizations++; return Bitmap(2, 2, 2, 20); });
            var warm = store.GetOrAdd(firstKey, () => { rasterizations++; return Bitmap(1, 2, 2, 30); });
            store.EndFrame();

            var diagnostics = store.GetDiagnostics();
            Assert.Multiple(() =>
            {
                Assert.That(warm, Is.SameAs(first));
                Assert.That(second.PageIndex, Is.EqualTo(first.PageIndex));
                Assert.That(rasterizations, Is.EqualTo(2));
                Assert.That(diagnostics.Misses, Is.EqualTo(2));
                Assert.That(diagnostics.Hits, Is.EqualTo(1));
                Assert.That(diagnostics.PendingUploads, Is.EqualTo(1));
                Assert.That(diagnostics.QueuedGlyphs, Is.EqualTo(2));
                Assert.That(diagnostics.RasterTime, Is.GreaterThan(TimeSpan.Zero));
            });

            store.MarkPageUploaded(first.PageIndex);
            Assert.That(store.GetDiagnostics().Uploads, Is.EqualTo(1));
            Assert.That(store.GetDiagnostics().PendingUploads, Is.Zero);
            Assert.That(store.GetDiagnostics().QueuedGlyphs, Is.Zero);
            Assert.That(store.GetDiagnostics().UploadBytes, Is.EqualTo(64));
        }

        [Test]
        public void StoreResetMarksUploadedPagesPendingWithoutRerasterizingGlyphs()
        {
            var store = new DynamicGlyphAtlasStore(new DynamicGlyphCacheOptions(8, 8, 1, 1));
            var key = Key(1);
            var rasterizations = 0;
            store.BeginFrame();
            var original = store.GetOrAdd(key, () => { rasterizations++; return Bitmap(1, 2, 2, 42); });
            store.EndFrame();
            store.MarkPageUploaded(original.PageIndex);

            store.MarkAllPagesDirty();
            store.BeginFrame();
            var recovered = store.GetOrAdd(key, () => { rasterizations++; return Bitmap(1, 2, 2, 99); });
            store.EndFrame();

            Assert.Multiple(() =>
            {
                Assert.That(recovered, Is.SameAs(original));
                Assert.That(recovered.Uploaded, Is.False);
                Assert.That(rasterizations, Is.EqualTo(1));
                Assert.That(store.GetDiagnostics().PendingUploads, Is.EqualTo(1));
                Assert.That(store.GetPagePixels(original.PageIndex).Span[original.Bounds.Y * 8 + original.Bounds.X], Is.EqualTo(42));
            });
        }

        [Test]
        public void StoreEvictsOldestInactivePageButNeverCurrentFramePage()
        {
            var store = new DynamicGlyphAtlasStore(new DynamicGlyphCacheOptions(8, 8, 2, 1));
            var firstKey = Key(1);
            var secondKey = Key(2);
            var thirdKey = Key(3);
            store.BeginFrame();
            store.GetOrAdd(firstKey, () => Bitmap(1, 6, 6, 10));
            store.GetOrAdd(secondKey, () => Bitmap(2, 6, 6, 20));
            var deferred = store.GetOrAdd(thirdKey, () => Bitmap(3, 6, 6, 30));
            Assert.That(deferred.PageIndex, Is.EqualTo(-1));
            store.EndFrame();

            store.BeginFrame();
            store.GetOrAdd(secondKey, () => throw new AssertionException("Warm glyph must not rasterize."));
            store.GetOrAdd(thirdKey, () => Bitmap(3, 6, 6, 30));
            store.EndFrame();

            Assert.Multiple(() =>
            {
                Assert.That(store.Contains(firstKey), Is.False);
                Assert.That(store.Contains(secondKey), Is.True);
                Assert.That(store.Contains(thirdKey), Is.True);
                Assert.That(store.GetDiagnostics().Evictions, Is.EqualTo(1));
                Assert.That(store.GetDiagnostics().Failures, Is.EqualTo(1));
                Assert.That(store.GetDiagnostics().LastFailure, Does.Contain("every page is active"));
                Assert.That(store.GetDiagnostics().LastEviction, Does.Contain("least-recently-used"));
                Assert.That(store.GetDiagnostics().Bytes, Is.EqualTo(128));
            });
        }

        [Test]
        public void RandomizedGlyphChurnNeverExceedsConfiguredMemoryBudget()
        {
            const int pageSize = 16;
            const int maximumPages = 3;
            var store = new DynamicGlyphAtlasStore(new DynamicGlyphCacheOptions(pageSize, pageSize, maximumPages, 1));
            var random = new Random(1729);

            for (var frame = 0; frame < 2000; frame++)
            {
                var glyphId = (uint)random.Next(1, 257);
                var width = random.Next(1, 11);
                var height = random.Next(1, 11);
                store.BeginFrame();
                store.GetOrAdd(Key(glyphId), () => Bitmap(glyphId, width, height, (byte)glyphId));
                store.EndFrame();

                var diagnostics = store.GetDiagnostics();
                Assert.Multiple(() =>
                {
                    Assert.That(diagnostics.PageCount, Is.LessThanOrEqualTo(maximumPages));
                    Assert.That(diagnostics.Bytes, Is.LessThanOrEqualTo(pageSize * pageSize * maximumPages));
                    Assert.That(diagnostics.UsedArea, Is.LessThanOrEqualTo(diagnostics.Capacity));
                });
            }

            Assert.That(store.GetDiagnostics().Evictions, Is.GreaterThan(0));
        }

        [Test]
        public void DebugPagesAreCopiesAndOptionsEnforceMemoryBudget()
        {
            var store = new DynamicGlyphAtlasStore(new DynamicGlyphCacheOptions(8, 8, 1, 1));
            store.BeginFrame();
            var entry = store.GetOrAdd(Key(1), () => Bitmap(1, 2, 2, 77));
            store.EndFrame();
            var snapshot = store.GetDebugPages()[0];
            var original = snapshot.Pixels.Span[entry.Bounds.Y * snapshot.Width + entry.Bounds.X];
            var secondSnapshot = store.GetDebugPages()[0];

            Assert.Multiple(() =>
            {
                Assert.That(original, Is.EqualTo(77));
                Assert.That(secondSnapshot.Pixels.Span[entry.Bounds.Y * snapshot.Width + entry.Bounds.X], Is.EqualTo(77));
                Assert.That(() => new DynamicGlyphCacheOptions(2048, 2048, 9), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new DynamicGlyphCacheOptions(2049, 64, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        private static DynamicGlyphKey Key(uint glyphId) => new DynamicGlyphKey(new UIFontIdentity("test", "face"), glyphId, 1024, UIFontHinting.Default);

        private static UIFontGlyphBitmap Bitmap(uint glyphId, int width, int height, byte value)
        {
            var pixels = new byte[width * height];
            Array.Fill(pixels, value);
            return new UIFontGlyphBitmap(glyphId, width, height, 0, height, width, pixels);
        }
    }
}