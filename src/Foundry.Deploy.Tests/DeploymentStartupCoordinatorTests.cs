// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using BootMediaUpdateReason = Foundry.Core.Models.Configuration.BootMediaUpdateReason;
using System.Net.Http;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Sharing;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.Startup;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecretEnvelope = Foundry.Core.Models.Configuration.SecretEnvelope;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentStartupCoordinatorTests
{
    [Theory]
    [InlineData(BootMediaUpdateReason.NewerRelease)]
    [InlineData(BootMediaUpdateReason.UnknownAuthoringVersion)]
    [InlineData(BootMediaUpdateReason.None)]
    public async Task InitializeAsync_PropagatesBootMediaUpdateReason(BootMediaUpdateReason reason)
    {
        var dependencies = new StartupDependencies(enabled: false, reason);
        var discovery = new StubDiscovery(_ => throw new InvalidOperationException("Discovery must not start."));
        var coordinator = new DeploymentStartupCoordinator(dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, discovery, new RecordingLogger());

        DeploymentStartupSnapshot snapshot = await coordinator.InitializeAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(reason, snapshot.BootMediaUpdateReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializeAsync_WhenOptionalDiscoveryFails_PreservesTagsAndCompletesStartup(bool timeout)
    {
        Exception failure = timeout ? new TaskCanceledException("HTTP timeout") : new HttpRequestException("Unavailable");
        var discovery = new StubDiscovery(_ => Task.FromException<IReadOnlyList<string>>(failure));
        var logger = new RecordingLogger();
        DeploymentStartupCoordinator coordinator = CreateCoordinator(discovery, logger);

        DeploymentStartupSnapshot snapshot = await coordinator.InitializeAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("Sales", snapshot.DeployConfigurationDocument!.Autopilot.HardwareHashUpload.DefaultGroupTag);
        Assert.Equal(["Kiosk"], snapshot.DeployConfigurationDocument.Autopilot.HardwareHashUpload.KnownGroupTags);
        Assert.NotNull(snapshot.DetectedHardware);
        Assert.NotNull(snapshot.CatalogSnapshot);
        Assert.Single(logger.Levels, level => level == LogLevel.Warning);
        Assert.DoesNotContain(LogLevel.Error, logger.Levels);
    }

    [Fact]
    public async Task InitializeAsync_WhenDiscoverySucceeds_UsesFreshTagsWithoutChangingDefault()
    {
        var discovery = new StubDiscovery(_ => Task.FromResult<IReadOnlyList<string>>(["New tag", "Sales"]));
        DeploymentStartupCoordinator coordinator = CreateCoordinator(discovery, new RecordingLogger());

        DeploymentStartupSnapshot snapshot = await coordinator.InitializeAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(["New tag", "Sales"], snapshot.DeployConfigurationDocument!.Autopilot.HardwareHashUpload.KnownGroupTags);
        Assert.Equal("Sales", snapshot.DeployConfigurationDocument.Autopilot.HardwareHashUpload.DefaultGroupTag);
    }

    [Fact]
    public async Task InitializeAsync_WhenDiscoveryStalls_CancelsLookupAndReturnsFallback()
    {
        var clock = new DeadlineTimeProvider();
        var pending = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken discoveryToken = default;
        bool released = false;
        var discovery = new StubDiscovery(async token =>
        {
            discoveryToken = token;
            using CancellationTokenRegistration registration = token.Register(() => pending.TrySetCanceled(token));
            try { return await pending.Task; }
            finally { released = true; }
        });
        var logger = new RecordingLogger();
        Task<DeploymentStartupSnapshot> startup = CreateCoordinator(discovery, logger, clock).InitializeAsync(CreateRequest(), TestContext.Current.CancellationToken);
        try
        {
            clock.Advance(TimeSpan.FromSeconds(29));
            Assert.False(startup.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
            DeploymentStartupSnapshot snapshot = await startup.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(discoveryToken.IsCancellationRequested);
            Assert.True(released);
            Assert.Equal(["Kiosk"], snapshot.DeployConfigurationDocument!.Autopilot.HardwareHashUpload.KnownGroupTags);
            Assert.Single(logger.Levels, level => level == LogLevel.Warning);
        }
        finally
        {
            pending.TrySetResult([]);
            await startup;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializeAsync_WhenGraphPageStalls_CancelsHttpWithoutRetryOrPartialTags(bool hasFirstPage)
    {
        var clock = new DeadlineTimeProvider();
        using var handler = new StalledGraphHandler(clock, hasFirstPage);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") };
        var graph = new AutopilotGraphImportClient(http, NullLogger<AutopilotGraphImportClient>.Instance);
        var discovery = new StubDiscovery(token => graph.ListGroupTagsAsync("test-token", token));
        var logger = new RecordingLogger();

        DeploymentStartupSnapshot snapshot = await CreateCoordinator(discovery, logger, clock)
            .InitializeAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(hasFirstPage ? 2 : 1, handler.RequestCount);
        Assert.Equal(["Kiosk"], snapshot.DeployConfigurationDocument!.Autopilot.HardwareHashUpload.KnownGroupTags);
        Assert.Equal("Sales", snapshot.DeployConfigurationDocument.Autopilot.HardwareHashUpload.DefaultGroupTag);
        Assert.Single(logger.Levels, level => level == LogLevel.Warning);
    }

    [Fact]
    public async Task InitializeAsync_WhenAutopilotIsDisabled_PreservesConfigurationWithoutDiscovery()
    {
        var dependencies = new StartupDependencies(enabled: false);
        var logger = new RecordingLogger();
        var discovery = new StubDiscovery(_ => throw new InvalidOperationException("Discovery must not start."));
        var coordinator = new DeploymentStartupCoordinator(dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, discovery, logger);

        DeploymentStartupSnapshot snapshot = await coordinator.InitializeAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.False(snapshot.DeployConfigurationDocument!.Autopilot.IsEnabled);
        Assert.Equal(["Kiosk"], snapshot.DeployConfigurationDocument.Autopilot.HardwareHashUpload.KnownGroupTags);
        Assert.DoesNotContain(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public async Task InitializeAsync_WhenDiscoveryHasUnexpectedFailure_DoesNotHideIt()
    {
        var discovery = new StubDiscovery(_ => throw new NotSupportedException("Unexpected implementation failure"));

        await Assert.ThrowsAsync<NotSupportedException>(() => CreateCoordinator(discovery, new RecordingLogger())
            .InitializeAsync(CreateRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeAsync_WhenCallerCancelsDuringDiscovery_PropagatesWithoutWarning()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var logger = new RecordingLogger();
        var discovery = new StubDiscovery(token =>
        {
            caller.Cancel();
            Assert.True(token.IsCancellationRequested);
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>([]);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateCoordinator(discovery, logger).InitializeAsync(CreateRequest(), caller.Token));

        Assert.Empty(logger.Levels);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializeAsync_WhenCancellationRacesWithDiscoveryCompletion_PropagatesWithoutWarning(bool fails)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var logger = new RecordingLogger();
        var discovery = new StubDiscovery(_ =>
        {
            caller.Cancel();
            return fails
                ? Task.FromException<IReadOnlyList<string>>(new HttpRequestException("Unavailable"))
                : Task.FromResult<IReadOnlyList<string>>(["Sales"]);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateCoordinator(discovery, logger).InitializeAsync(CreateRequest(), caller.Token));

        Assert.Empty(logger.Levels);
    }

    [Fact]
    public async Task InitializeAsync_WhenAlreadyCancelled_DoesNotStartDiscovery()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var discovery = new StubDiscovery(_ => throw new NotSupportedException("Discovery must not start."));
        var logger = new RecordingLogger();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateCoordinator(discovery, logger).InitializeAsync(CreateRequest(), caller.Token));

        Assert.Empty(logger.Levels);
    }

    [Fact]
    public async Task InitializeAsync_ResolvesNetworkCacheSessionAndStoresItForTheWizard()
    {
        var dependencies = new StartupDependencies(enabled: false);
        var discovery = new StubDiscovery(_ => throw new InvalidOperationException("Discovery must not start."));
        using NetworkCacheSession session = NetworkCacheSession.Unavailable(NetworkCacheOutcome.NoMatchingScope);
        var resolver = new RecordingNetworkCacheResolver(session);
        var accessor = new NetworkCacheSessionAccessor();
        var coordinator = new DeploymentStartupCoordinator(dependencies, dependencies, dependencies, dependencies, dependencies, dependencies,
            discovery, new RecordingLogger(), networkCacheResolver: resolver, networkCacheAccessor: accessor);

        await coordinator.InitializeAsync(CreateLiveRequest(), TestContext.Current.CancellationToken);

        Assert.Same(session, accessor.Current);
        string workspaceRoot = Assert.Single(resolver.WorkspaceRoots);
        Assert.False(string.IsNullOrWhiteSpace(workspaceRoot));
        Assert.Equal([DiagnosticSessionContext.CurrentSessionId], resolver.SessionIds);
    }

    [Fact]
    public async Task InitializeAsync_AgainAfterAFailure_ClosesThePreviousShareBeforeResolving()
    {
        var dependencies = new StartupDependencies(enabled: false);
        var connector = new RecordingConnector();
        var accessor = new NetworkCacheSessionAccessor { Current = NetworkCacheSession.Connected(@"\\srv\cache", canWrite: false, connector) };
        using NetworkCacheSession next = NetworkCacheSession.Unavailable(NetworkCacheOutcome.NoMatchingScope);
        var resolver = new RecordingNetworkCacheResolver(next, onResolve: () => Assert.Equal([@"\\srv\cache"], connector.Disconnected));
        var coordinator = new DeploymentStartupCoordinator(dependencies, dependencies, dependencies, dependencies, dependencies, dependencies,
            new StubDiscovery(_ => throw new InvalidOperationException("Discovery must not start.")), new RecordingLogger(),
            networkCacheResolver: resolver, networkCacheAccessor: accessor);

        await coordinator.InitializeAsync(CreateLiveRequest(), TestContext.Current.CancellationToken);

        Assert.Single(resolver.SessionIds);
        Assert.Same(next, accessor.Current);
    }

    [Fact]
    public async Task InitializeAsync_WithAPublisherKey_PutsTheIndexSectionsInTheSnapshot()
    {
        string share = Path.Combine(Path.GetTempPath(), "Fstartupindex", Guid.NewGuid().ToString("N"));
        try
        {
            using PublicationSigningKey signer = PublicationSigningKey.Create();
            Foundry.Core.Models.Configuration.Deploy.DeployPreOobeSettings preOobe = new()
            {
                IsEnabled = true,
                Actions = [new() { Name = "Restart", Kind = Foundry.Core.Models.Configuration.PreOobeActionKind.Restart }]
            };
            byte[] index = NetworkCacheContentIndexCodec.Write(new() { KeyId = signer.KeyId, Sequence = 3, PreOobe = preOobe }, signer);
            await NetworkCacheIndexStore.WriteAsync(Path.Combine(share, "Foundry"), signer.KeyId, index, TestContext.Current.CancellationToken);
            var dependencies = new StartupDependencies(enabled: false, networkCache: new()
            {
                IsEnabled = true,
                PublisherPublicKey = Convert.ToBase64String(signer.PublicKey),
                PublisherKeyId = signer.KeyId
            });
            using NetworkCacheSession session = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());

            DeploymentStartupSnapshot snapshot = await CreateNetworkCacheCoordinator(dependencies, session).InitializeAsync(CreateLiveRequest(), TestContext.Current.CancellationToken);

            Assert.Null(snapshot.NetworkCacheFailure);
            Assert.Equal("Restart", Assert.Single(snapshot.DeployConfigurationDocument!.PreOobe.Actions).Name);
            Assert.Equal(["Kiosk"], snapshot.DeployConfigurationDocument.Autopilot.HardwareHashUpload.KnownGroupTags);
        }
        finally
        {
            if (Directory.Exists(share)) Directory.Delete(share, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithAPublisherKey_ReportsTheSessionFailure()
    {
        var dependencies = new StartupDependencies(enabled: false, networkCache: new() { IsEnabled = true, PublisherPublicKey = "AAAA" });
        var failure = new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.AccessDenied, @"\\srv\cache", @"CONTOSO\svc-cache");
        using NetworkCacheSession session = NetworkCacheSession.Unavailable(NetworkCacheOutcome.Unavailable, failure);

        DeploymentStartupSnapshot snapshot = await CreateNetworkCacheCoordinator(dependencies, session).InitializeAsync(CreateLiveRequest(), TestContext.Current.CancellationToken);

        Assert.Same(failure, snapshot.NetworkCacheFailure);
    }

    [Fact]
    public async Task InitializeAsync_InDebugSafeMode_NeverResolvesTheShare()
    {
        // A developer machine must not map a real share or write its probe file, even with a publisher key configured.
        var dependencies = new StartupDependencies(enabled: false, networkCache: new() { IsEnabled = true, PublisherPublicKey = "AAAA" });
        using NetworkCacheSession session = NetworkCacheSession.Unavailable(NetworkCacheOutcome.Unavailable,
            new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.Unreachable, @"\\srv\cache"));
        var resolver = new RecordingNetworkCacheResolver(session);
        var accessor = new NetworkCacheSessionAccessor();
        var coordinator = new DeploymentStartupCoordinator(dependencies, dependencies, dependencies, dependencies, dependencies, dependencies,
            new StubDiscovery(_ => throw new InvalidOperationException("Discovery must not start.")), new RecordingLogger(),
            networkCacheResolver: resolver, networkCacheAccessor: accessor);

        DeploymentStartupSnapshot snapshot = await coordinator.InitializeAsync(CreateRequest(), TestContext.Current.CancellationToken);

        Assert.Empty(resolver.SessionIds);
        Assert.Null(accessor.Current);
        Assert.Null(snapshot.NetworkCacheFailure);
    }

    private static DeploymentStartupCoordinator CreateNetworkCacheCoordinator(StartupDependencies dependencies, NetworkCacheSession session) =>
        new(dependencies, dependencies, dependencies, dependencies, dependencies, dependencies,
            new StubDiscovery(_ => throw new InvalidOperationException("Discovery must not start.")), new RecordingLogger(),
            networkCacheResolver: new RecordingNetworkCacheResolver(session), networkCacheAccessor: new NetworkCacheSessionAccessor());

    private sealed class NoopConnector : ISmbConnector
    {
        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password) => new(SmbConnectStatus.Connected, 0);
        public void Disconnect(string uncPath) { }
    }

    private static DeploymentStartupCoordinator CreateCoordinator(StubDiscovery discovery, RecordingLogger logger, TimeProvider? clock = null)
    {
        var dependencies = new StartupDependencies();
        return new DeploymentStartupCoordinator(dependencies, dependencies, dependencies, dependencies, dependencies, dependencies, discovery, logger, clock);
    }

    private static DeploymentStartupRequest CreateRequest() => new()
    {
        RuntimeContext = new DeploymentRuntimeContext(DeploymentMode.Iso, null),
        IsDebugSafeMode = true,
        FallbackComputerName = "TEST-PC"
    };

    /// <summary>A WinPE start, the only one that resolves the network cache; the share itself is replaced by a test resolver.</summary>
    private static DeploymentStartupRequest CreateLiveRequest() => CreateRequest() with { IsDebugSafeMode = false };

    private sealed class RecordingConnector : ISmbConnector
    {
        public List<string> Disconnected { get; } = [];
        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password) => new(SmbConnectStatus.Connected, 0);
        public void Disconnect(string uncPath) => Disconnected.Add(uncPath);
    }

    private sealed class RecordingNetworkCacheResolver(NetworkCacheSession session, Action? onResolve = null) : INetworkCacheSessionResolver
    {
        public List<string> WorkspaceRoots { get; } = [];
        public List<string> SessionIds { get; } = [];

        public Task<NetworkCacheSession> ResolveAsync(string workspaceRootPath, string sessionId, CancellationToken cancellationToken)
        {
            onResolve?.Invoke();
            WorkspaceRoots.Add(workspaceRootPath);
            SessionIds.Add(sessionId);
            return Task.FromResult(session);
        }
    }

    private sealed class StubDiscovery(Func<CancellationToken, Task<IReadOnlyList<string>>> discover) : IAutopilotGroupTagDiscoveryService
    {
        public Task<IReadOnlyList<string>> DiscoverAsync(DeployAutopilotHardwareHashUploadSettings settings, string workspaceRootPath, CancellationToken cancellationToken = default)
            => discover(cancellationToken);
    }

    // Replace machine/tenant I/O; exercise the real startup coordinator and its returned configuration.
    private sealed class StartupDependencies(bool enabled = true, BootMediaUpdateReason reason = BootMediaUpdateReason.None,
        Foundry.Core.Models.Configuration.Deploy.DeployNetworkCacheSettings? networkCache = null) : IDeployConfigurationService, IAutopilotProfileCatalogService,
        IHardwareProfileService, IOfflineWindowsComputerNameService, ITargetDiskService, IDeploymentCatalogLoadService
    {
        public DeployConfigurationLoadResult LoadOptional() => new()
        {
            BootMediaUpdateReason = reason,
            Document = new FoundryDeployConfigurationDocument
            {
                NetworkCache = networkCache ?? new(),
                Autopilot = new DeployAutopilotSettings
                {
                    IsEnabled = enabled,
                    ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                    HardwareHashUpload = new DeployAutopilotHardwareHashUploadSettings
                    {
                        TenantId = "test-tenant",
                        ClientId = "test-client",
                        ActiveCertificateThumbprint = "test-thumbprint",
                        ActiveCertificateExpiresOnUtc = DateTimeOffset.MaxValue,
                        CertificatePfxSecret = new SecretEnvelope(),
                        CertificatePfxPasswordSecret = new SecretEnvelope(),
                        DefaultGroupTag = "Sales",
                        KnownGroupTags = ["Kiosk"]
                    }
                }
            }
        };

        public IReadOnlyList<AutopilotProfileCatalogItem> LoadAvailableProfiles() => [];
        public Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HardwareProfile());
        public Task<string?> TryGetOfflineComputerNameAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false)
            => Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeploymentCatalogSnapshot> LoadAsync() => Task.FromResult(new DeploymentCatalogSnapshot([], []));
    }

    private sealed class RecordingLogger : ILogger<DeploymentStartupCoordinator>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }

    private sealed class DeadlineTimeProvider : TimeProvider
    {
        private Action? _fire;
        private TimeSpan _remaining;
        public void Advance(TimeSpan elapsed)
        {
            if (_fire is null || _remaining == Timeout.InfiniteTimeSpan) { return; }
            _remaining -= elapsed;
            if (_remaining <= TimeSpan.Zero) { _fire(); }
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _remaining = dueTime;
            _fire = () => callback(state);
            return new DeadlineTimer(() => _fire = null);
        }

        private sealed class DeadlineTimer(Action dispose) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => dispose();
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class StalledGraphHandler(DeadlineTimeProvider clock, bool hasFirstPage) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (hasFirstPage && RequestCount == 1)
            {
                clock.Advance(TimeSpan.FromSeconds(20));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {"value":[{"groupTag":"Partial tag"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?$skiptoken=next"}
                        """)
                };
            }

            clock.Advance(TimeSpan.FromSeconds(hasFirstPage ? 10 : 30));
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The stalled Graph request must be cancelled.");
        }
    }
}
