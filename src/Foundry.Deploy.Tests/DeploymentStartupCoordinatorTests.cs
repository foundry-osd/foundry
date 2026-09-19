// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.Startup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentStartupCoordinatorTests
{
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

    private sealed class StubDiscovery(Func<CancellationToken, Task<IReadOnlyList<string>>> discover) : IAutopilotGroupTagDiscoveryService
    {
        public Task<IReadOnlyList<string>> DiscoverAsync(DeployAutopilotHardwareHashUploadSettings settings, string workspaceRootPath, CancellationToken cancellationToken = default)
            => discover(cancellationToken);
    }

    // Replace machine/tenant I/O; exercise the real startup coordinator and its returned configuration.
    private sealed class StartupDependencies(bool enabled = true) : IDeployConfigurationService, IAutopilotProfileCatalogService,
        IHardwareProfileService, IOfflineWindowsComputerNameService, ITargetDiskService, IDeploymentCatalogLoadService
    {
        public DeployConfigurationLoadResult LoadOptional() => new()
        {
            Document = new FoundryDeployConfigurationDocument
            {
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
