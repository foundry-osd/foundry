// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;
using Foundry.Utilities.Networking;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheShareAccessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-ncsa-" + Guid.NewGuid().ToString("N"));

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task RunAsync_UsesTheSignedInUserWithoutOpeningOrClosingASession()
    {
        var connector = new BlockingConnector();
        NetworkCacheShare share = Share(accountId: null);
        Directory.CreateDirectory(share.Path);

        NetworkCacheShareAccessResult<string> result = await new NetworkCacheShareAccess(connector).RunAsync(
            Settings(share), share, _ => null, (contentRoot, _) => Task.FromResult(contentRoot), Cancellation);

        Assert.Equal(NetworkCacheShareAccessStatus.Completed, result.Status);
        Assert.Equal(NetworkCacheLayout.ContentRoot(share.Path), result.Value);
        Assert.Equal(0, connector.ConnectCount);
        Assert.Empty(connector.Disconnects);
    }

    [Fact]
    public async Task RunAsync_ReportsAShareWithoutAccountThatCannotBeFoundAsUnreachable()
    {
        NetworkCacheShare share = Share(accountId: null);

        NetworkCacheShareAccessResult<bool> result = await new NetworkCacheShareAccess(new BlockingConnector()).RunAsync(
            Settings(share), share, _ => null, (_, _) => Task.FromResult(true), Cancellation);

        Assert.Equal(NetworkCacheShareAccessStatus.Unreachable, result.Status);
    }

    [Fact]
    public async Task RunAsync_ClosesItsSessionWithoutForcingOpenFilesClosed()
    {
        var connector = new BlockingConnector();
        NetworkCacheShare share = Share(accountId: "a1");

        NetworkCacheShareAccessResult<bool> result = await new NetworkCacheShareAccess(connector).RunAsync(
            Settings(share), share, _ => "Secret1!".ToCharArray(), (_, _) => Task.FromResult(true), Cancellation);

        Assert.Equal(NetworkCacheShareAccessStatus.Completed, result.Status);
        Assert.Equal([(share.Path, false)], connector.Disconnects);
    }

    [Fact]
    public async Task RunAsync_StopsWaitingForAConnectionThatHangsAndClosesItWhenItCompletes()
    {
        var connector = new BlockingConnector { Block = true };
        NetworkCacheShare share = Share(accountId: "a1");
        bool ran = false;

        NetworkCacheShareAccessResult<bool> result = await new NetworkCacheShareAccess(connector, TimeSpan.FromMilliseconds(50)).RunAsync(
            Settings(share), share, _ => "Secret1!".ToCharArray(), (_, _) => Task.FromResult(ran = true), Cancellation);

        Assert.Equal(NetworkCacheShareAccessStatus.Unreachable, result.Status);
        Assert.IsType<TimeoutException>(result.Error);
        Assert.False(ran);
        connector.Release.Set();
        Assert.True(SpinWait.SpinUntil(() => connector.Disconnects.Count == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal([(share.Path, false)], connector.Disconnects);
    }

    private NetworkCacheShare Share(string? accountId) => new() { Id = "s1", Path = Path.Combine(root, "share"), AccountId = accountId };

    private static NetworkCacheSettings Settings(NetworkCacheShare share) => new()
    {
        IsEnabled = true,
        Shares = [share],
        Accounts = [new NetworkCacheAccount { Id = "a1", UserName = @"CONTOSO\svc" }]
    };

    private sealed class BlockingConnector : ISmbConnector
    {
        private int connectCount;

        public bool Block { get; init; }
        public ManualResetEventSlim Release { get; } = new();
        public int ConnectCount => Volatile.Read(ref connectCount);
        public List<(string Path, bool Force)> Disconnects { get; } = [];

        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password)
        {
            Interlocked.Increment(ref connectCount);
            if (Block) Release.Wait(TimeSpan.FromSeconds(10));
            return new SmbConnectResult(SmbConnectStatus.Connected, 0);
        }

        public void Disconnect(string uncPath) => Disconnect(uncPath, force: true);

        public void Disconnect(string uncPath, bool force)
        {
            lock (Disconnects) Disconnects.Add((uncPath, force));
        }
    }
}
