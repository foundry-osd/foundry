// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using Foundry.Connect.Configuration;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Security;
using Foundry.Connect.Startup;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Connect.Tests.Support;
using Foundry.Deploy.Tests;
using Foundry.Telemetry;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Shell;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundry.Connect.Tests;

[Collection(nameof(WpfApplicationHostCollection))]
public sealed class ConnectPhaseTests
{
    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Connect", TelemetryApps.FoundryConnect, "1.0.0.0");

    [Fact]
    public void CreateStatusPageModel_UsesTheHostTitleAndFallsBackToTheApplicationName()
    {
        ResourceLocalizationService localization = TestLocalization.Create();

        Assert.Equal("Foundry OSD", ConnectPhase.CreateStatusPageModel("Foundry OSD", localization).Title);
        Assert.Equal(localization.GetString("App.Name"), ConnectPhase.CreateStatusPageModel(null, localization).Title);
        Assert.Equal(localization.GetString("App.Name"), ConnectPhase.CreateStatusPageModel(" ", localization).Title);
        Assert.Equal(localization.GetString("Status.LookingForNetwork"), ConnectPhase.CreateStatusPageModel("x", localization).Status);
    }

    [Fact]
    public void LoadServices_ReportsConfigurationLoadedOnceTheServicesAreBuilt()
    {
        var events = new List<string>();
        RuntimeStartupDiagnostics startup = RuntimeStartupDiagnostics.CreateForHostedPhase(
            Identity, static () => { }, () => events.Add(StartupStage.ConfigurationLoaded));
        var configuration = new FoundryConnectConfigurationDocument();

        using IHost? host = ConnectPhase.LoadServices(
            startup,
            new StubConfigurationService(ConnectConfigurationLoadResult.Success(configuration, ConnectRuntimeSecrets.None(), BootMediaUpdateReason.None)),
            new FakeShellHost(),
            stage => events.Add(stage.ToString()));

        Assert.NotNull(host);
        Assert.Equal(
            [nameof(ConnectStartupStage.Configuration), nameof(ConnectStartupStage.Services), StartupStage.ConfigurationLoaded],
            events);
    }

    [Fact]
    public void LoadServices_ConfigurationFailure_DoesNotReportConfigurationLoaded()
    {
        var events = new List<string>();
        RuntimeStartupDiagnostics startup = RuntimeStartupDiagnostics.CreateForHostedPhase(
            Identity, static () => { }, () => events.Add(StartupStage.ConfigurationLoaded));

        using IHost? host = ConnectPhase.LoadServices(
            startup,
            new StubConfigurationService(ConnectConfigurationLoadResult.Failure("The configuration is invalid.", new InvalidDataException())),
            new FakeShellHost(),
            stage => events.Add(stage.ToString()));

        Assert.Null(host);
        Assert.Equal([nameof(ConnectStartupStage.Configuration)], events);
        Assert.Equal(ChildRuntimeExitCode.ConfigurationFailure, ConnectStartupFailurePolicy.Map(ConnectStartupStage.Configuration));
    }

    [Fact]
    public void AttachCloseHandler_ACloseRequestEndsConnectAndKeepsTheWindowOpen()
    {
        var shell = new FakeShellHost();
        int aborted = 0;
        using IDisposable attachment = ConnectPhase.AttachCloseHandler(shell, () => aborted++);

        var args = new CancelEventArgs();
        shell.RaiseCloseRequested(args);

        Assert.Equal(1, aborted);
        Assert.True(args.Cancel);
    }

    [Fact]
    public void AttachCloseHandler_DisposingTheAttachmentLeavesTheCloseDecisionToTheNextPhase()
    {
        var shell = new FakeShellHost();
        int aborted = 0;
        ConnectPhase.AttachCloseHandler(shell, () => aborted++).Dispose();

        var args = new CancelEventArgs();
        shell.RaiseCloseRequested(args);

        Assert.Equal(0, aborted);
        Assert.False(args.Cancel);
    }

    [Fact]
    public Task PresentContent_MakesTheShellWindowTheMainWindowAndShowsTheContentInIt() => WpfApplicationTestHost.RunAsync(() =>
    {
        Application application = Application.Current;
        Window? previous = application.MainWindow;
        // Another main window first: the application adopts a new window as its main window only when it has none.
        var other = new Window();
        application.MainWindow = other;
        var window = new Window();
        var content = new System.Windows.Controls.UserControl();
        var shell = new FakeShellHost(window);
        try
        {
            ConnectPhase.PresentContent(application, shell, content);

            Assert.Same(window, application.MainWindow);
            Assert.Same(content, shell.ShownContent);
            Assert.Equal(1, shell.ShowCount);
        }
        finally
        {
            application.MainWindow = previous;
            window.Close();
            other.Close();
        }

        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateSuccessResult_CarriesTheProvisioningAndHandsTheKeyOverOnlyOnTheFirstRun(bool isRevisit)
    {
        var provisioning = new ProvisioningOutcome(Dot1xState.NotConfigured, ProvisionedWifiState.NotConfigured, null, []);
        using DeploymentKeyTransfer key = DeploymentKeyTransfer.FromKey(Enumerable.Repeat((byte)7, DeploymentKeyTransfer.KeyLength).ToArray());
        ResourceLocalizationService localization = TestLocalization.Create();
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IThemeService>(new ThemeService())
            .AddSingleton<ILocalizationService>(localization)
            .AddSingleton<IProvisioningStep>(new StubProvisioningStep(provisioning))
            .AddSingleton<IConnectUnlockService>(new FakeUnlockService { DeploymentKey = key })
            .BuildServiceProvider();

        ConnectPhaseResult result = ConnectPhase.CreateSuccessResult(services, isRevisit);

        Assert.Equal((int)ChildRuntimeExitCode.Success, result.ExitCode);
        Assert.Same(provisioning, result.Provisioning);
        Assert.Equal(nameof(Foundry.Wpf.Theme.ThemeMode.System), result.ThemeMode);
        Assert.Equal(localization.CurrentCulture.Name, result.CultureName);
        if (isRevisit)
        {
            // The deployment keeps its own key: a key Connect obtained while reopened is zeroed.
            Assert.Null(result.DeploymentKey);
            Assert.False(key.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
        }
        else
        {
            Assert.Same(key, result.DeploymentKey);
        }
    }

    [Fact]
    public void CreateEndedResult_ReleasesTheProfileAndClearsTheSecretsBeforeHandingTheProvisioningOver()
    {
        var document = new WifiProfileDocument("Lab", WifiSsid.FromText("Lab"), false, "<WLANProfile><name>Lab</name></WLANProfile>");
        var provisioning = new ProvisioningOutcome(
            Dot1xState.NotConfigured, new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Lab")), document, []);
        var secrets = new ConnectRuntimeSecrets(ConnectSecret.Available("lab-passphrase".ToCharArray()), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());
        var coordinator = new FakeConnectionCoordinator();
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IThemeService>(new ThemeService())
            .AddSingleton<ILocalizationService>(TestLocalization.Create())
            .AddSingleton<IProvisioningStep>(new StubProvisioningStep(provisioning))
            .AddSingleton<IConnectUnlockService>(new FakeUnlockService())
            .AddSingleton<IConnectionCoordinator>(coordinator)
            .AddSingleton(secrets)
            .BuildServiceProvider();

        ConnectPhaseResult result = ConnectPhase.CreateEndedResult((int)ChildRuntimeExitCode.UserAborted, services);

        Assert.Equal((int)ChildRuntimeExitCode.UserAborted, result.ExitCode);
        Assert.Same(provisioning, result.Provisioning);
        Assert.False(document.TryGetXml(out _));
        Assert.Equal(1, coordinator.ReleaseCalls);
        Assert.Equal(ConnectSecretStatus.Cleared, secrets.WifiPassphrase.Status);
        Assert.Null(result.DeploymentKey);
    }

    [Fact]
    public Task RunAsync_RevisitCancelledBeforeItStarts_ZeroesTheDeploymentKeyCopy() => WpfApplicationTestHost.RunAsync(async () =>
    {
        using DeploymentKeyTransfer key = DeploymentKeyTransfer.FromKey(Enumerable.Repeat((byte)7, DeploymentKeyTransfer.KeyLength).ToArray());
        var options = new ConnectPhaseOptions
        {
            Shell = new FakeShellHost(),
            OnFirstContentRendered = static () => { },
            Revisit = new ConnectRevisit(PreviousProvisioning: null, key)
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConnectPhase.RunAsync(options, new CancellationToken(canceled: true)));

        Assert.False(key.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    });

    private sealed class StubProvisioningStep(ProvisioningOutcome outcome) : IProvisioningStep
    {
        public ProvisioningOutcome? LastOutcome => outcome;

        public Task<ProvisioningOutcome> RunAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProvisioningOutcome> RefreshWifiAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Restore(ProvisioningOutcome previous) => throw new NotSupportedException();
    }

    private sealed class FakeShellHost(Window? window = null) : IShellHost
    {
        public FrameworkElement? ShownContent { get; private set; }

        public int ShowCount { get; private set; }

        public Window Window => window ?? throw new NotSupportedException("Connect must not touch the window while handling a close request.");

        public event EventHandler<CancelEventArgs>? CloseRequested;

        public event EventHandler<ShellContentRenderedEventArgs>? ShellContentRendered
        {
            add { }
            remove { }
        }

        public void ShowContent(FrameworkElement content)
        {
            ShownContent = content;
            ShowCount++;
        }

        public void RaiseCloseRequested(CancelEventArgs args) => CloseRequested?.Invoke(this, args);
    }

    private sealed class StubConfigurationService(ConnectConfigurationLoadResult result) : IConnectConfigurationService
    {
        public string? ConfigurationPath => null;

        public bool IsLoadedFromDisk => false;

        public bool IsBootMediaUpdateRecommended => false;

        public BootMediaUpdateReason BootMediaUpdateReason => BootMediaUpdateReason.None;

        public ConnectConfigurationLoadResult Load() => result;
    }
}
