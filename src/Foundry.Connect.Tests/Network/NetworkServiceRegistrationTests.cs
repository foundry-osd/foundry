// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Configuration;
using Foundry.Connect.DependencyInjection;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Network;
using Foundry.Connect.Security;
using Foundry.Connect.ViewModels;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Foundry.Wpf.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Connect.Tests.Network;

public sealed class NetworkServiceRegistrationTests
{
    [Fact]
    public void AddFoundryConnectApplicationServices_SharesOneCoordinatorWithTheRoamingHistory()
    {
        using ServiceProvider provider = BuildProvider();

        IConnectionCoordinator coordinator = provider.GetRequiredService<IConnectionCoordinator>();

        Assert.Same(coordinator, provider.GetRequiredService<IWifiConnectionHistory>());
        Assert.Same(coordinator, provider.GetRequiredService<ConnectionCoordinator>());
        Assert.NotNull(provider.GetRequiredService<ISuccessfulExitService>());
        Assert.NotNull(provider.GetRequiredService<INetworkMonitor>());
        Assert.NotNull(provider.GetRequiredService<IConnectUnlockService>());
        Assert.IsType<DeploymentPasswordPrompt>(provider.GetRequiredService<IDeploymentPasswordPrompt>());
    }

    [Fact]
    public void AddFoundryConnectApplicationServices_ShellShutdownEndsThePhaseAsAUserAbort()
    {
        using ServiceProvider provider = BuildProvider();
        ApplicationLifetimeService lifetime = provider.GetRequiredService<ApplicationLifetimeService>();

        provider.GetRequiredService<IApplicationShellService>().Shutdown();

        Assert.Same(lifetime, provider.GetRequiredService<IApplicationLifetimeService>());
        Assert.Equal(ChildRuntimeExitCode.UserAborted, lifetime.RequestedExitCode);
        Assert.True(lifetime.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public void AddFoundryConnectApplicationServices_DoesNotLookUpTheLatestReleaseOnline()
    {
        // Connect often runs before the network is ready; Deploy owns the online newer-release notice.
        using ServiceProvider provider = BuildProvider();

        Assert.Null(provider.GetService<LatestReleaseVersionReader>());
    }

    [Fact]
    public void AddFoundryConnectApplicationServices_HoldsTheCountdownUntilThePageIsShown()
    {
        using ServiceProvider provider = BuildProvider();
        AutoContinueViewModel autoContinue = provider.GetRequiredService<AutoContinueViewModel>();

        autoContinue.OnInternetStatusChanged(InternetStatus.Online);

        Assert.False(autoContinue.IsCounting);
    }

    [Fact]
    public async Task AddFoundryConnectApplicationServices_ComposesThePageAroundOneNetworkActions()
    {
        using ServiceProvider provider = BuildProvider();
        ConnectNetworkActions actions = provider.GetRequiredService<ConnectNetworkActions>();
        int interactions = 0;
        actions.InteractionOccurred += (_, _) => interactions++;

        // A simulated scenario keeps every action away from the real coordinator.
        ConnectPageViewModel page = provider.GetRequiredService<ConnectPageViewModel>();
        page.ApplyDebugScenario(ConnectDebugScenario.WifiReady);
        page.WifiNetworks.SetPassphrase("password1".AsSpan());
        await page.ProvisionedWifi.DisconnectCommand.ExecuteAsync(null);

        Assert.True(actions.IsSimulation);
        Assert.Equal(2, interactions);
        Assert.Same(provider.GetRequiredService<WifiNetworksViewModel>(), page.WifiNetworks);
        Assert.Same(provider.GetRequiredService<ProvisionedWifiViewModel>(), page.ProvisionedWifi);
        Assert.Same(provider.GetRequiredService<AutoContinueViewModel>(), page.AutoContinue);
        Assert.Same(provider.GetRequiredService<DiagnosticsViewModel>(), page.Diagnostics);
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new FoundryConnectConfigurationDocument();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectConfigurationService>(new StubConfigurationService(configuration));
        services.AddSingleton(configuration);
        services.AddSingleton(ConnectRuntimeSecrets.None());
        services.AddSingleton(RuntimeStartupDiagnostics.CreateForHostedPhase(
            new RuntimeApplicationIdentity("Foundry.Connect", TelemetryApps.FoundryConnect, "1.0.0.0"), static () => { }));
        services.AddFoundryConnectApplicationServices();
        return services.BuildServiceProvider();
    }

    private sealed class StubConfigurationService(FoundryConnectConfigurationDocument configuration) : IConnectConfigurationService
    {
        public string? ConfigurationPath => null;

        public bool IsLoadedFromDisk => false;

        public bool IsBootMediaUpdateRecommended => false;

        public BootMediaUpdateReason BootMediaUpdateReason => BootMediaUpdateReason.None;

        public ConnectConfigurationLoadResult Load() => ConnectConfigurationLoadResult.Success(configuration, ConnectRuntimeSecrets.None(), BootMediaUpdateReason.None);
    }
}
