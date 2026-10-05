// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT

using Forma.Xaml.Build;

namespace Forma.Xaml.Compiler.Tests;

public class FormaXamlAssemblyWriterTest
{
    [Test]
    public void ExclusiveFileLockIsRetriedAndRecoversAfterRelease()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var blockingStream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var attempts = 0;

            FormaXamlAssemblyWriter.WriteWithRetry(
                () =>
                {
                    attempts++;
                    using var output = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                },
                _ => blockingStream.Dispose());

            Assert.That(attempts, Is.EqualTo(2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void TransientIoFailureRetriesUntilWriteSucceeds()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        FormaXamlAssemblyWriter.WriteWithRetry(
            () =>
            {
                attempts++;
                if (attempts < 3) throw new IOException("locked");
            },
            delays.Add);

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100) }));
        });
    }

    [Test]
    public void PersistentIoFailureStopsAtMaximumAttempts()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        var exception = Assert.Throws<IOException>(() => FormaXamlAssemblyWriter.WriteWithRetry(
            () =>
            {
                attempts++;
                throw new IOException("still locked");
            },
            delays.Add));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("still locked"));
            Assert.That(attempts, Is.EqualTo(FormaXamlAssemblyWriter.MaxAttempts));
            Assert.That(delays, Has.Count.EqualTo(FormaXamlAssemblyWriter.MaxAttempts - 1));
            Assert.That(delays.Sum(delay => delay.TotalMilliseconds), Is.EqualTo(750));
        });
    }

    [Test]
    public void NonIoFailureIsNotRetried()
    {
        var attempts = 0;

        Assert.Throws<InvalidOperationException>(() => FormaXamlAssemblyWriter.WriteWithRetry(
            () =>
            {
                attempts++;
                throw new InvalidOperationException("compiler failure");
            },
            _ => Assert.Fail("Non-I/O failures must not delay.")));

        Assert.That(attempts, Is.EqualTo(1));
    }
}