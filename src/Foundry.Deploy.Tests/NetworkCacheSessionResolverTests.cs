// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.NetworkCache;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Security;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class NetworkCacheSessionResolverTests
{
    private const string FirstShare = @"\\fs01\cache";
    private const string SecondShare = @"\\fs02\cache";
    private static readonly byte[] Key = MediaSecretEnvelopeProtector.GenerateMediaKey();

    [Fact]
    public async Task Resolve_Disabled_ReturnsDisabled()
    {
        var harness = new Harness(Settings() with { IsEnabled = false });

        using NetworkCacheSession session = await harness.ResolveAsync();

        Assert.Equal(NetworkCacheOutcome.Disabled, session.Outcome);
        Assert.Empty(harness.Connector.Connected);
    }

    [Fact]
    public async Task Resolve_NoMatchingScope_ReturnsNoMatchingScope()
    {
        var harness = new Harness(Settings(), addresses: ["192.168.50.1"]);

        using NetworkCacheSession session = await harness.ResolveAsync();

        Assert.Equal(NetworkCacheOutcome.NoMatchingScope, session.Outcome);
        Assert.Null(session.SharePath);
    }

    [Fact]
    public async Task Resolve_FirstShareAccessDenied_UsesSecond()
    {
        var harness = new Harness(Settings());
        harness.Connector.Results[FirstShare] = new(SmbConnectStatus.AccessDenied, 5);

        using NetworkCacheSession session = await harness.ResolveAsync();

        Assert.Equal(NetworkCacheOutcome.Connected, session.Outcome);
        Assert.Equal(SecondShare, session.SharePath);
        Assert.Equal(Path.Combine(SecondShare, "Foundry"), session.ContentRoot);
        Assert.Equal([(FirstShare, "CONTOSO\\svc"), (SecondShare, "CONTOSO\\svc")], harness.Connector.Connected);
        Assert.Equal("secret", harness.Connector.LastPassword);
    }

    [Fact]
    public async Task Resolve_WhenProbeTimesOut_TriesNextShare()
    {
        var harness = new Harness(Settings());
        harness.Probe.HangingReads.Add(Path.Combine(FirstShare, "Foundry"));

        Task<NetworkCacheSession> resolving = harness.ResolveAsync();
        await harness.WaitForHangAsync();
        using NetworkCacheSession session = await resolving;

        Assert.Equal(SecondShare, session.SharePath);
        Assert.Contains(FirstShare, harness.Connector.Disconnected);
    }

    [Fact]
    public async Task Resolve_AllFail_ReturnsUnavailable()
    {
        var harness = new Harness(Settings());
        harness.Connector.Results[FirstShare] = new(SmbConnectStatus.NotFound, 67);
        harness.Connector.Results[SecondShare] = new(SmbConnectStatus.Failed, 1231);

        using NetworkCacheSession session = await harness.ResolveAsync();

        Assert.Equal(NetworkCacheOutcome.Unavailable, session.Outcome);
        Assert.False(session.CanWrite);
    }

    [Fact]
    public async Task Resolve_ShareWithoutAccount_IsReadOnly()
    {
        var harness = new Harness(Settings(withAccount: false));

        using NetworkCacheSession session = await harness.ResolveAsync();

        Assert.Equal(NetworkCacheOutcome.Connected, session.Outcome);
        Assert.False(session.CanWrite);
        Assert.Equal([(FirstShare, (string?)null)], harness.Connector.Connected);
        Assert.Empty(harness.Probe.WriteProbes);
    }

    [Fact]
    public async Task Resolve_WriteProbeFails_IsReadOnly()
    {
        var harness = new Harness(Settings());
        harness.Probe.CanWrite = false;

        using NetworkCacheSession session = await harness.ResolveAsync();

        Assert.Equal(FirstShare, session.SharePath);
        Assert.False(session.CanWrite);
    }

    [Fact]
    public async Task Resolve_WithAccount_IsWritable()
    {
        var harness = new Harness(Settings());

        using NetworkCacheSession session = await harness.ResolveAsync();

        Assert.True(session.CanWrite);
        Assert.Single(harness.Probe.WriteProbes);
    }

    [Fact]
    public async Task Resolve_MalformedEnvelope_SkipsShare()
    {
        DeployNetworkCacheSettings settings = Settings();
        DeployNetworkCacheShare broken = settings.Shares[0] with { EncryptedCredentials = settings.Shares[0].EncryptedCredentials! with { Ciphertext = "%%%" } };
        var harness = new Harness(settings with { Shares = [broken, settings.Shares[1]] });

        using NetworkCacheSession session = await harness.ResolveAsync();

        Assert.Equal(SecondShare, session.SharePath);
    }

    [Fact]
    public async Task Resolve_UnexpectedError_ReturnsUnavailable()
    {
        var harness = new Harness(Settings() with { Scopes = null! });

        using NetworkCacheSession session = await harness.ResolveAsync();

        Assert.Equal(NetworkCacheOutcome.Unavailable, session.Outcome);
    }

    [Fact]
    public async Task Dispose_DisconnectsShare()
    {
        var harness = new Harness(Settings());

        NetworkCacheSession session = await harness.ResolveAsync();
        session.Dispose();

        Assert.Equal([FirstShare], harness.Connector.Disconnected);
    }

    private static DeployNetworkCacheSettings Settings(bool withAccount = true) => new()
    {
        IsEnabled = true,
        Shares = [Share("s1", FirstShare, withAccount), Share("s2", SecondShare, withAccount)],
        Scopes = [new() { Kind = NetworkCacheScopeKind.Subnet, Value = "10.10.0.0/16", ShareIds = ["s1", "s2"] }]
    };

    private static DeployNetworkCacheShare Share(string id, string path, bool withAccount)
    {
        if (!withAccount) return new() { Id = id, Path = path };
        byte[] payload = NetworkShareCredentialPayloadCodec.Encode(new(path, "CONTOSO\\svc"), "secret");
        return new()
        {
            Id = id,
            Path = path,
            AccountName = "CONTOSO\\svc",
            EncryptedCredentials = MediaSecretEnvelopeProtector.EncryptBytes(payload, Key, MediaSecretEnvelopeProtector.DeploymentKeyId)
        };
    }

    private sealed class Harness
    {
        private readonly DeployNetworkCacheSettings settings;
        private readonly string[] addresses;

        public Harness(DeployNetworkCacheSettings settings, string[]? addresses = null)
        {
            this.settings = settings;
            this.addresses = addresses ?? ["10.10.4.2"];
        }

        public FakeConnector Connector { get; } = new();
        public FakeProbe Probe { get; } = new();

        public Task<NetworkCacheSession> ResolveAsync()
        {
            var resolver = new NetworkCacheSessionResolver(
                new StaticConfiguration(settings),
                Connector,
                new StaticAdapters(addresses),
                new StaticKeys(),
                Probe,
                connectTimeout: TimeSpan.FromSeconds(10),
                probeTimeout: TimeSpan.FromMilliseconds(100),
                NullLogger<NetworkCacheSessionResolver>.Instance);
            return resolver.ResolveAsync(@"X:\Foundry", "session01", TestContext.Current.CancellationToken);
        }

        public Task WaitForHangAsync() => Probe.HangStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private sealed class FakeConnector : ISmbConnector
    {
        public Dictionary<string, SmbConnectResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Path, string? User)> Connected { get; } = [];
        public List<string> Disconnected { get; } = [];
        public string? LastPassword { get; private set; }

        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password)
        {
            Connected.Add((uncPath, userName));
            if (userName is not null) LastPassword = new string(password);
            return Results.TryGetValue(uncPath, out SmbConnectResult? result) ? result : new(SmbConnectStatus.Connected, 0);
        }

        public void Disconnect(string uncPath) => Disconnected.Add(uncPath);
    }

    private sealed class FakeProbe : INetworkCacheProbe
    {
        public HashSet<string> HangingReads { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> WriteProbes { get; } = [];
        public bool CanWrite { get; set; } = true;
        public TaskCompletionSource HangStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> CanReadAsync(string contentRoot, CancellationToken cancellationToken)
        {
            if (!HangingReads.Contains(contentRoot)) return true;
            HangStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        }

        public Task<bool> CanWriteAsync(string contentRoot, string sessionId, CancellationToken cancellationToken)
        {
            WriteProbes.Add(contentRoot);
            return Task.FromResult(CanWrite);
        }
    }

    private sealed class StaticConfiguration(DeployNetworkCacheSettings settings) : IDeployConfigurationService
    {
        public DeployConfigurationLoadResult LoadOptional() => new()
        {
            Exists = true,
            Document = new Foundry.Deploy.Models.Configuration.FoundryDeployConfigurationDocument { NetworkCache = settings }
        };
    }

    private sealed class StaticAdapters(string[] addresses) : INetworkAdapterSnapshotProvider
    {
        public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters() =>
        [
            new("id", "Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up, string.Empty, [], [], [], true)
            {
                UnicastAddresses = addresses
            }
        ];
    }

    private sealed class StaticKeys : IDeploymentSecretKeyProvider
    {
        public Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default) => Task.FromResult(Key.ToArray());
    }
}
