// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Threading;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.DomainJoin;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Network;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.Security;
using Foundry.Deploy.Services.Startup;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Wizard;
using Foundry.Deploy.ViewModels;
using Foundry.Telemetry;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Processes;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Progress;
using Foundry.Wpf.Shell;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

[Collection(nameof(WpfApplicationHostCollection))]
public sealed class ProgressPageContractTests
{
    [Fact]
    public void Session_ImplementsTheSharedProgressPageContract()
    {
        Assert.True(typeof(IProgressPageViewModel).IsAssignableFrom(typeof(DeploymentSessionViewModel)));
    }

    [Fact]
    public void ResourceKeys_ResolveInDeployResources()
    {
        Assert.All(ProgressPageResourceKeys.All, key => Assert.NotEqual(key, LocalizationText.GetString(key)));
    }

    [Fact]
    public Task CompletionAction_IsTheRebootLabel() => WpfApplicationTestHost.RunAsync(() =>
    {
        using DeploymentSessionViewModel session = DeploymentPreviewSession.Create(Dispatcher.CurrentDispatcher);

        Assert.Equal(LocalizationText.GetString("Success.RebootNow"), session.CompletionActionText);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Cancellation_MirrorsTheMainWindowDeploymentIntoTheSession() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var execution = new PendingExecution();
        using MainWindowViewModel viewModel = CreateMainWindowViewModel(execution);
        IProgressPageViewModel session = viewModel.Session;
        Assert.False(session.CanCancel);

        Task run = viewModel.StartDeploymentCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsDeploymentRunning);
        Assert.True(session.IsProgressPage);
        Assert.True(session.CanCancel);
        Assert.Same(viewModel.CancelDeploymentCommand, session.CancelCommand);
        Assert.False(session.IsCancellationPending);

        session.CancelCommand!.Execute(null);

        Assert.True(viewModel.IsCancellationPending);
        Assert.True(session.IsCancellationPending);
        Assert.False(session.CancelCommand.CanExecute(null));

        execution.Complete(new DeploymentExecutionRunResult { IsSuccess = false, IsCancelled = true, Message = "Cancelled." });
        await run;

        Assert.False(session.CanCancel);
        Assert.False(session.IsCancellationPending);
        Assert.True(session.IsCancelled);
        Assert.Same(viewModel.CancelDeploymentCommand, session.CancelCommand);
        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
    });

    /// <summary>Creates the Deploy main window view model around test doubles; the services a test does not reach throw.</summary>
    internal static MainWindowViewModel CreateMainWindowViewModel(IDeploymentExecutionService execution, IThemeService? theme = null,
        IDeploymentAccessGate? accessGate = null, IDeploymentStartupCoordinator? coordinator = null, IApplicationShellService? shell = null,
        ICatalogStartupDialogService? catalogDialog = null, INetworkPageNavigator? networkPageNavigator = null,
        INetworkCacheStartupDialogService? networkCacheDialog = null, LatestReleaseVersionReader? readLatestReleaseVersion = null)
    {
        var localization = new ResourceLocalizationService(LocalizationText.ResourceManager);
        return new MainWindowViewModel(
            localization,
            theme ?? new AutopilotInteractiveTestRuntime.FixedTheme(Foundry.Wpf.Theme.ThemeMode.Light),
            new OperationProgressService(),
            accessGate ?? new Unused(),
            coordinator ?? new Unused(),
            new IsoRuntime(),
            new ReadyLaunch(),
            execution,
            new StartableWizard(),
            new IdleOrchestrator(),
            shell ?? new Unused(),
            new WizardFactory(localization),
            new Unused(),
            new AutopilotInteractiveTestRuntime.FixedKeyboardLayout("00000409"),
            new Unused(),
            new Unused(),
            new StartingCredentialCheck(),
            networkCacheDialog ?? new Unused(),
            catalogDialog ?? new Unused(),
            new NullTelemetryService(),
            readLatestReleaseVersion ?? (_ => Task.FromResult<string?>(null)),
            NullLogger<MainWindowViewModel>.Instance,
            new NoNetwork(),
            networkPageNavigator);
    }

    /// <summary>Execution that stays running until the test completes it, so cancellation can be observed mid-deployment.</summary>
    private sealed class PendingExecution : IDeploymentExecutionService
    {
        private readonly TaskCompletionSource<DeploymentExecutionRunResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(DeploymentExecutionRunResult result) => _result.SetResult(result);

        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, CancellationToken cancellationToken = default) =>
            _result.Task;

        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, DomainJoinPreparedInput? domainJoinInput,
            CancellationToken cancellationToken = default) => _result.Task;
    }

    private sealed class ReadyLaunch : IDeploymentLaunchPreparationService
    {
        public DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request) => Prepare(request, null);

        public DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request,
            Foundry.Core.Models.Configuration.Deploy.DeployDomainJoinSettings? domainJoin, DomainJoinSubmission? domainJoinSubmission = null)
        {
            DeploymentContext context = DeploymentPreviewSession.Request();
            return DeploymentLaunchPreparationResult.Success(context.TargetComputerName, new TargetDiskInfo { DiskNumber = 1 }, context);
        }
    }

    private sealed class StartableWizard : IDeploymentWizardStateService
    {
        public bool CanGoPrevious(DeploymentWizardStateSnapshot snapshot) => false;
        public bool CanNavigate(DeploymentWizardStateSnapshot snapshot) => false;
        public bool CanGoNext(DeploymentWizardStateSnapshot snapshot) => false;
        public bool CanStartDeployment(DeploymentWizardStateSnapshot snapshot) => !snapshot.IsDeploymentRunning;
    }

    private sealed class WizardFactory(ResourceLocalizationService localization) : IDeploymentWizardContextFactory
    {
        public DeploymentWizardContext Create(bool isDebugSafeMode) => new(
            new DeploymentPreparationViewModel(localization, isDebugSafeMode),
            new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64"),
            new DriverPackSelectionViewModel(new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance), localization, "x64"));
    }

    private sealed class StartingCredentialCheck : IZeroTouchDomainCredentialCheck
    {
        public Task<ZeroTouchDomainCredentialCheckDecision> CheckAsync(DeploymentContext context, DomainJoinPreparedInput? input,
            CancellationToken token) => Task.FromResult(ZeroTouchDomainCredentialCheckDecision.Start);
    }

    private sealed class IsoRuntime : IDeploymentRuntimeContextService
    {
        public DeploymentRuntimeContext Resolve() => new(DeploymentMode.Iso, null);
    }

    private sealed class IdleOrchestrator : IDeploymentOrchestrator
    {
        public event EventHandler<DeploymentStepProgress>? StepProgressChanged { add { } remove { } }
        public event EventHandler? CompletionStarting { add { } remove { } }
        public Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<DeploymentResult> RunAsync(DeploymentContext context, DomainJoinPreparedInput? input,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoNetwork : INetworkAdapterSnapshotProvider
    {
        public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters() => [];
    }

    /// <summary>Services the cancellation flow never reaches; any call fails the test.</summary>
    private sealed class Unused : IDeploymentAccessGate, IDeploymentStartupCoordinator, IApplicationShellService, IProcessRunner,
        IDomainCredentialVerifier, IZeroTouchMediaCredentialCheck, INetworkCacheStartupDialogService, ICatalogStartupDialogService
    {
        public Task<bool> AuthorizeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DeploymentStartupSnapshot> InitializeAsync(DeploymentStartupRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeploymentCatalogSnapshot> ReloadCatalogsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AutopilotGroupTagRefresh> RefreshAutopilotGroupTagsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool ShouldResolveNetworkCacheAgain() => throw new NotSupportedException();

        public Task<NetworkCacheStartupResult?> ResolveNetworkCacheAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public CatalogStartupChoice ShowFailure(Exception failure, bool canOpenNetwork) => throw new NotSupportedException();

        public void ShowAbout() => throw new NotSupportedException();
        public bool ConfirmWarning(string title, string message) => throw new NotSupportedException();
        public void Shutdown() => throw new NotSupportedException();

        public Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, string workingDirectory,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory,
            Action<string>? onOutputData, Action<string>? onErrorData, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DomainCredentialCheckResult> VerifyAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password,
            CancellationToken token) => throw new NotSupportedException();

        public Task<DomainCredentialCheckResult?> VerifyMediaCredentialsAsync(
            Foundry.Core.Models.Configuration.Deploy.DeployDomainJoinSettings settings, string? selectedDomainId,
            IDomainCredentialVerifier verifier, CancellationToken token) => throw new NotSupportedException();

        public NetworkCacheStartupChoice ShowFailure(NetworkCacheStartupFailure failure, bool canOpenNetwork) => throw new NotSupportedException();
    }
}
