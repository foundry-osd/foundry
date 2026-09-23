// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Services.Storage;

namespace Foundry.Core.Tests.Storage;

public sealed class AuthoringArtifactCacheTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryCacheTests", Guid.NewGuid().ToString("N"));
    private readonly byte[] payload = Encoding.UTF8.GetBytes("validated package");
    private int downloads;
    private CachedArtifactRequest Request => new(AuthoringArtifactKind.WinPeDriver, "vendor/package/1.0/x64", "drivers.cab", Convert.ToHexString(SHA256.HashData(payload)), payload.Length, false);

    [Fact]
    public async Task AcquireAsync_VersionedInstallerPreservesLegacyAndReusesOnlyCompletedTransfer()
    {
        Directory.CreateDirectory(root);
        string legacy = Path.Combine(root, "adksetup.exe");
        await File.WriteAllTextAsync(legacy, "unproven legacy executable", TestContext.Current.CancellationToken);
        var request = new CachedArtifactRequest(AuthoringArtifactKind.Installer,
            "ADK/10.1.26100.2454/adksetup.exe/version-specific-url", "adksetup.exe", null, null, true);

        await using (var first = await Acquire(request)) Assert.False(first.CacheHit);
        await using (var reused = await Acquire(request)) Assert.True(reused.CacheHit);
        Assert.Equal(1, downloads);
        await using (var changed = await Acquire(request with { SourceIdentity = "ADK/new-version/new-url" })) Assert.False(changed.CacheHit);
        Assert.Equal(2, downloads);
        Assert.Equal("unproven legacy executable", await File.ReadAllTextAsync(legacy, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcquireAsync_AcrossServiceInstances_DownloadsValidPayloadOnce()
    {
        await using (var first = await Acquire())
        {
            Assert.False(first.CacheHit);
            Assert.Equal(payload, await File.ReadAllBytesAsync(first.Path, TestContext.Current.CancellationToken));
        }
        await using (var second = await Acquire())
        {
            Assert.True(second.CacheHit);
            Assert.Equal(payload, await File.ReadAllBytesAsync(second.Path, TestContext.Current.CancellationToken));
        }
        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task AcquireAsync_TrustedBytesNeedNoReceipt()
    {
        await using (var first = await Acquire()) { }
        foreach (string receipt in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)) File.Delete(receipt);
        await using var second = await Acquire();
        Assert.True(second.CacheHit);
        Assert.Equal(1, downloads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(17)]
    public async Task AcquireAsync_DamagedBytesAreReplaced(int length)
    {
        string path;
        await using (var first = await Acquire()) path = first.Path;
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        await File.WriteAllBytesAsync(path, new byte[length], TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(path, timestamp);
        await using var replacement = await Acquire();
        Assert.False(replacement.CacheHit);
        Assert.Equal(payload, await File.ReadAllBytesAsync(replacement.Path, TestContext.Current.CancellationToken));
        Assert.Equal(2, downloads);
    }

    [Fact]
    public async Task AcquireAsync_IdentityAndDigestSeparateSameFilename()
    {
        string original;
        await using (var first = await Acquire()) original = first.Path;
        await using (var other = await Acquire(Request with { SourceIdentity = "vendor/package/2.0/arm64" })) Assert.NotEqual(original, other.Path);
        byte[] changed = Encoding.UTF8.GetBytes("different package");
        await using var digest = await new AuthoringArtifactCache(root).AcquireAsync(Request with { ExpectedSha256 = Convert.ToHexString(SHA256.HashData(changed)) }, (path, token) => File.WriteAllBytesAsync(path, changed, token), TestContext.Current.CancellationToken);
        Assert.NotEqual(original, digest.Path);
        Assert.Equal(payload, await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("                                                                ")]
    public async Task AcquireAsync_MalformedDigestFailsBeforeDownload(string digest)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Acquire(Request with { ExpectedSha256 = digest }));
        Assert.Equal(0, downloads);
    }

    [Fact]
    public async Task AcquireAsync_HashlessPinnedTransferRequiresLocalReceipt()
    {
        var request = Request with { ExpectedSha256 = null, AllowCompletedTransferReuse = true };
        await using (var first = await Acquire(request)) { }
        await using (var second = await Acquire(request)) Assert.True(second.CacheHit);
        foreach (string receipt in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)) File.Delete(receipt);
        await using var third = await Acquire(request);
        Assert.False(third.CacheHit);
        Assert.Equal(2, downloads);
    }

    [Fact]
    public async Task AcquireAsync_MutableHashlessSourceAlwaysDownloads()
    {
        var request = Request with { ExpectedSha256 = null };
        await using (var first = await Acquire(request)) { }
        await using var second = await Acquire(request);
        Assert.False(second.CacheHit);
        Assert.Equal(2, downloads);
    }

    [Fact]
    public async Task AcquireAsync_LeaseSerializesAcquisitionUntilConsumerDisposes()
    {
        var first = await Acquire();
        Task<CachedArtifactLease> pending = Acquire();
        Assert.False(pending.IsCompleted);
        await first.DisposeAsync();
        await using var second = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(second.CacheHit);
        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task AcquireAsync_CanceledWaitDoesNotAffectActiveLease()
    {
        await using var first = await Acquire();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<CachedArtifactLease> pending = new AuthoringArtifactCache(root).AcquireAsync(Request, Download, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(payload, await File.ReadAllBytesAsync(first.Path, TestContext.Current.CancellationToken));
        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task AcquireAsync_AnotherProcessLeasePreventsAcquisition()
    {
        await using (var first = await Acquire()) { }
        string leasePath = Assert.Single(Directory.GetFiles(root, ".lease", SearchOption.AllDirectories));
        string ready = Path.Combine(root, "ready");
        string release = Path.Combine(root, "release");
        string script = $"$lease = [IO.File]::Open('{leasePath.Replace("'", "''")}', 'Open', 'ReadWrite', 'None'); try {{ [IO.File]::WriteAllText('{ready.Replace("'", "''")}', 'ready'); $deadline = [DateTime]::UtcNow.AddSeconds(15); while (-not [IO.File]::Exists('{release.Replace("'", "''")}') -and [DateTime]::UtcNow -lt $deadline) {{ Start-Sleep -Milliseconds 20 }} }} finally {{ $lease.Dispose() }}";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using Process process = Process.Start(start)!;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!File.Exists(ready)) await Task.Delay(20, timeout.Token);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            Task<CachedArtifactLease> pending = new AuthoringArtifactCache(root).AcquireAsync(Request, Download, cancellation.Token);
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(1, downloads);
        }
        finally
        {
            File.WriteAllText(release, "release");
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        await using var afterRelease = await Acquire();
        Assert.True(afterRelease.CacheHit);
    }

    [Fact]
    public async Task AcquireAsync_FailedRefreshPreservesPublishedGenerationAndRemovesOnlyOwnTemporary()
    {
        var request = Request with { ExpectedSha256 = null };
        string original;
        await using (var first = await Acquire(request)) original = first.Path;
        string? temporary = null;
        await Assert.ThrowsAsync<IOException>(() => new AuthoringArtifactCache(root).AcquireAsync(request, async (path, token) =>
        {
            temporary = path;
            await File.WriteAllBytesAsync(path, [1, 2], token);
            throw new IOException("Interrupted transfer");
        }, TestContext.Current.CancellationToken));
        Assert.Equal(payload, await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public async Task AcquireAsync_SizeMismatchRejectsBeforeHashing()
    {
        await using (var first = await Acquire()) { }
        var reports = new List<CachedArtifactVerificationProgress>();
        await Assert.ThrowsAsync<InvalidDataException>(() => new AuthoringArtifactCache(root).AcquireAsync(Request with { ExpectedLength = 99 }, Download, cancellationToken: TestContext.Current.CancellationToken, verificationProgress: new InlineProgress(reports.Add)));
        Assert.Empty(reports);
    }

    [Fact]
    public async Task AcquireAsync_CancellationDuringVerificationDoesNotDownload()
    {
        byte[] largePayload = new byte[1024 * 1024];
        var request = Request with { ExpectedLength = largePayload.Length, ExpectedSha256 = Convert.ToHexString(SHA256.HashData(largePayload)) };
        Task DownloadLarge(string path, CancellationToken token)
        {
            downloads++;
            return File.WriteAllBytesAsync(path, largePayload, token);
        }
        await using (var first = await new AuthoringArtifactCache(root).AcquireAsync(request, DownloadLarge, TestContext.Current.CancellationToken)) { }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var reports = new List<CachedArtifactVerificationProgress>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AuthoringArtifactCache(root).AcquireAsync(request, DownloadLarge, cancellation.Token, new InlineProgress(value =>
        {
            reports.Add(value);
            if (value.BytesVerified > 0) cancellation.Cancel();
        })));
        Assert.Contains(reports, value => value.BytesVerified > 0 && value.BytesVerified < largePayload.Length);
        Assert.DoesNotContain(reports, value => value.IsComplete);
        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task AcquireAsync_VerificationCompletionRequiresMatchingDigest()
    {
        var reports = new List<CachedArtifactVerificationProgress>();
        await Assert.ThrowsAsync<InvalidDataException>(() => new AuthoringArtifactCache(root).AcquireAsync(Request, (path, token) => File.WriteAllBytesAsync(path, new byte[payload.Length], token), cancellationToken: TestContext.Current.CancellationToken, verificationProgress: new InlineProgress(reports.Add)));
        Assert.DoesNotContain(reports, value => value.IsComplete);
        await using var good = await new AuthoringArtifactCache(root).AcquireAsync(Request, Download, cancellationToken: TestContext.Current.CancellationToken, verificationProgress: new InlineProgress(reports.Add));
        Assert.Contains(reports, value => value.IsComplete && value.BytesVerified == payload.Length && value.TotalBytes == payload.Length);
    }

    [Fact]
    public async Task AcquireAsync_IncompleteGenerationIsNeverAdopted()
    {
        string published;
        await using (var first = await Acquire()) published = first.Path;
        string generation = Path.GetDirectoryName(published)!;
        string pending = Path.Combine(Path.GetDirectoryName(generation)!, ".pending-interrupted");
        Directory.Move(generation, pending);
        await using var second = await Acquire();
        Assert.False(second.CacheHit);
        Assert.True(Directory.Exists(pending));
        Assert.Equal(2, downloads);
    }

    [Fact]
    public async Task AcquireAsync_HashlessReceiptFailurePreservesPreviousGeneration()
    {
        var request = Request with { ExpectedSha256 = null };
        string original;
        await using (var first = await Acquire(request)) original = first.Path;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new AuthoringArtifactCache(root).AcquireAsync(request, async (path, token) =>
        {
            await File.WriteAllBytesAsync(path, payload, token);
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "receipt.json"));
        }, TestContext.Current.CancellationToken));
        Assert.Equal(payload, await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcquireAsync_ForgedReceiptCannotAuthenticateCorruptedTrustedBytes()
    {
        string published;
        await using (var first = await Acquire()) published = first.Path;
        byte[] corrupt = new byte[payload.Length];
        await File.WriteAllBytesAsync(published, corrupt, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(published)!, "receipt.json"), "{\"Sha256\":\"" + Convert.ToHexString(SHA256.HashData(corrupt)) + "\",\"Length\":" + corrupt.Length + "}", TestContext.Current.CancellationToken);
        await using var second = await Acquire();
        Assert.False(second.CacheHit);
        Assert.Equal(payload, await File.ReadAllBytesAsync(second.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcquireAsync_FinalDirectoryPublicationFailurePreservesEarlierBytes()
    {
        var request = Request with { ExpectedSha256 = null };
        string original;
        await using (var first = await Acquire(request)) original = first.Path;
        await Assert.ThrowsAsync<IOException>(() => new AuthoringArtifactCache(root).AcquireAsync(request, async (path, token) =>
        {
            await File.WriteAllBytesAsync(path, payload, token);
            string pending = Path.GetDirectoryName(path)!;
            string target = Path.Combine(Path.GetDirectoryName(pending)!, Path.GetFileName(pending).Replace(".pending-", "generation-", StringComparison.Ordinal));
            await File.WriteAllTextAsync(target, "Blocks atomic publication", token);
        }, TestContext.Current.CancellationToken));
        Assert.Equal(payload, await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetDirectories(root, ".pending-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AcquireAsync_CanceledDownloadRetainsPublishedOriginalAndUsesUniqueTemporaryPaths()
    {
        var request = Request with { ExpectedSha256 = null };
        string original;
        await using (var first = await Acquire(request)) original = first.Path;
        var temporaryPaths = new HashSet<string>();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AuthoringArtifactCache(root).AcquireAsync(request, async (path, token) =>
            {
                Assert.True(temporaryPaths.Add(path));
                await File.WriteAllBytesAsync(path, payload, token);
                cancellation.Cancel();
            }, cancellation.Token));
        }
        Assert.Equal(payload, await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken));
        Assert.All(temporaryPaths, path => Assert.False(File.Exists(path)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcquireAsync_HashlessLocalConsistencyRejectsDamagedBytesOrReceipt(bool damageReceipt)
    {
        var request = Request with { ExpectedSha256 = null, AllowCompletedTransferReuse = true };
        string original;
        await using (var first = await Acquire(request)) original = first.Path;
        if (damageReceipt)
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(original)!, "receipt.json"), "not json", TestContext.Current.CancellationToken);
        else
            await File.WriteAllBytesAsync(original, new byte[payload.Length], TestContext.Current.CancellationToken);
        await using var replacement = await Acquire(request);
        Assert.False(replacement.CacheHit);
        Assert.Equal(payload, await File.ReadAllBytesAsync(replacement.Path, TestContext.Current.CancellationToken));
    }

    private Task<CachedArtifactLease> Acquire(CachedArtifactRequest? request = null) => new AuthoringArtifactCache(root).AcquireAsync(request ?? Request, Download, TestContext.Current.CancellationToken);
    private Task Download(string path, CancellationToken token)
    {
        Interlocked.Increment(ref downloads);
        return File.WriteAllBytesAsync(path, payload, token);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class InlineProgress(Action<CachedArtifactVerificationProgress> report) : IProgress<CachedArtifactVerificationProgress>
    {
        public void Report(CachedArtifactVerificationProgress value) => report(value);
    }
}
