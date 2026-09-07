// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class CustomDriverSourceInspectorTests
{
    [Theory]
    [InlineData(true, CustomDriverSourceState.Ready)]
    [InlineData(false, CustomDriverSourceState.NoDrivers)]
    public async Task Inspect_LocalTree(bool includeInf, CustomDriverSourceState expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryDriverTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        try
        {
            File.WriteAllText(Path.Combine(root, "nested", includeInf ? "driver.INF" : "readme.txt"), "fixture");
            Assert.Equal(expected, (await new CustomDriverSourceInspector().InspectAsync(root, default)).State);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Inspect_EmptyDoesNotAccessFilesystem()
    {
        var inspector = new CustomDriverSourceInspector(_ => throw new Exception("unexpected"), _ => throw new Exception("unexpected"));
        Assert.Equal(CustomDriverSourceState.Empty, (await inspector.InspectAsync(" ", default)).State);
    }

    [Fact]
    public async Task Inspect_MissingRoot()
    {
        var inspector = new CustomDriverSourceInspector(_ => throw new DirectoryNotFoundException(), _ => []);
        Assert.Equal(CustomDriverSourceState.Missing, (await inspector.InspectAsync("root", default)).State);
    }

    [Fact]
    public async Task Inspect_DeniedDescendantFailsClosed()
    {
        var inspector = new CustomDriverSourceInspector(p => p == "root" ? FileAttributes.Directory : throw new UnauthorizedAccessException(), _ => ["child"]);
        Assert.Equal(CustomDriverSourceState.Inaccessible, (await inspector.InspectAsync("root", default)).State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inspect_SkipsReparseWithoutFollowing(bool rootReparse)
    {
        int enumerations = 0;
        var inspector = new CustomDriverSourceInspector(p => FileAttributes.Directory | ((rootReparse || p != "root") ? FileAttributes.ReparsePoint : 0), p => { enumerations++; return ["loop"]; });
        Assert.NotEqual(CustomDriverSourceState.Ready, (await inspector.InspectAsync("root", default)).State);
        Assert.Equal(rootReparse ? 0 : 1, enumerations);
    }

    [Fact]
    public async Task Inspect_PreCancelledDoesNotAccessFilesystem()
    {
        var inspector = new CustomDriverSourceInspector(_ => throw new Exception("unexpected"), _ => []);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.InspectAsync("root", new CancellationToken(true)));
    }

    [Fact]
    public async Task Inspect_CancelledBlockedScanRetainsExclusiveOwnership()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        int scans = 0;
        var inspector = new CustomDriverSourceInspector(_ => FileAttributes.Directory, _ =>
        {
            Interlocked.Increment(ref scans); entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return [];
        });
        Task<CustomDriverSourceInspection> first = inspector.InspectAsync("first", cancellation.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            cancellation.Cancel();
            using var queuedCancellation = new CancellationTokenSource();
            Task<CustomDriverSourceInspection> second = inspector.InspectAsync("second", queuedCancellation.Token);
            queuedCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            Assert.Equal(1, Volatile.Read(ref scans));
            Assert.False(first.IsCompleted);
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }
}
