// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using Foundry.Connect.Configuration;
using Foundry.Connect.Startup;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Foundry.Wpf.Shell;
using Microsoft.Extensions.Hosting;

namespace Foundry.Connect.Tests;

public sealed class ConnectPhaseTests
{
    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Connect", TelemetryApps.FoundryConnect, "1.0.0.0");

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

    private sealed class FakeShellHost : IShellHost
    {
        public Window Window => throw new NotSupportedException("Connect must not touch the window while handling a close request.");

        public event EventHandler<CancelEventArgs>? CloseRequested;

        public event EventHandler? FirstContentRendered
        {
            add { }
            remove { }
        }

        public void ShowContent(FrameworkElement content) => throw new NotSupportedException();

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
