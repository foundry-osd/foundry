// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Adk;
using Foundry.Core.Services.Media;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Serilog;

namespace Foundry.Services.Adk;

/// <summary>Adapts authenticated ADK installation to application progress and shared machine ownership.</summary>
internal sealed class AdkService(
    IAdkInstallationProbe installationProbe,
    AdkInstallationService installationService,
    IOperationProgressService operationProgressService,
    IApplicationLocalizationService localizationService,
    ILogger logger) : IAdkService
{
    private readonly ILogger logger = logger.ForContext<AdkService>();
    private readonly object sync = new();
    private Task<AdkInstallResult>? activeOperation;
    private CancellationTokenSource? operationCancellation;
    private bool closeRequested;

    /// <inheritdoc />
    public event EventHandler<AdkStatusChangedEventArgs>? StatusChanged;

    /// <inheritdoc />
    public AdkInstallationStatus CurrentStatus { get; private set; } = new(false, false, false, null,
        AdkVersionRelation.Unknown, null, "Windows ADK 24H2 / 10.1.26100");

    /// <inheritdoc />
    public AdkInstallResult? LastResult { get; private set; }

    /// <inheritdoc />
    public bool HasUncertainOwnership => installationService.HasUncertainOwnership;

    /// <inheritdoc />
    public Task? ActiveOperation
    {
        get
        {
            lock (sync)
                return activeOperation is { IsCompleted: false } ? activeOperation : installationService.ActiveNativeOperation;
        }
    }

    /// <inheritdoc />
    public Task<AdkInstallationStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyStatus(new AdkInstallationDetector(installationProbe).Detect());
        return Task.FromResult(CurrentStatus);
    }

    /// <inheritdoc />
    public Task<AdkInstallResult> InstallAsync(CancellationToken cancellationToken = default)
        => StartOperation(false, cancellationToken);

    /// <inheritdoc />
    public Task<AdkInstallResult> UpgradeAsync(CancellationToken cancellationToken = default)
        => StartOperation(true, cancellationToken);

    /// <inheritdoc />
    public async Task RequestCancellationAndWaitAsync(CancellationToken waitToken)
    {
        Task? operation;
        Task cancellation;
        lock (sync)
        {
            closeRequested = true;
            cancellation = operationCancellation?.CancelAsync() ?? Task.CompletedTask;
            operation = activeOperation;
        }
        await cancellation.WaitAsync(waitToken);
        if (operation is not null)
        {
            try { await operation.WaitAsync(waitToken); }
            catch when (operation.IsCompleted && !waitToken.IsCancellationRequested) { }
        }
        Task? native = installationService.ActiveNativeOperation;
        if (native is not null)
        {
            try { await native.WaitAsync(waitToken); }
            catch when (native.IsCompleted && !waitToken.IsCancellationRequested) { }
        }
        waitToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public void ResumeAfterCancelledClose()
    {
        lock (sync) closeRequested = false;
    }

    private Task<AdkInstallResult> StartOperation(bool upgrade, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (closeRequested || HasUncertainOwnership || LastResult?.RebootRequired == true || activeOperation is { IsCompleted: false })
                throw new InvalidOperationException("An ADK operation is active or requires recovery.");
            operationCancellation?.Dispose();
            operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeOperation = RunInstallOperationAsync(upgrade, operationCancellation.Token);
            return activeOperation;
        }
    }

    private async Task<AdkInstallResult> RunInstallOperationAsync(bool upgrade, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using MediaOperationLease lease = MediaOperationLease.Acquire(Constants.WinPeWorkspaceDirectoryPath, "Adk");
        OperationKind kind = upgrade ? OperationKind.AdkUpgrade : OperationKind.AdkInstall;
        string terminalStatus = localizationService.GetString("Adk.Operation.Failed");
        bool progressStarted = false;
        try
        {
            LastResult = null;
            operationProgressService.Start(kind, localizationService.GetString(upgrade ? "Adk.Operation.UpgradeStarted" : "Adk.Operation.InstallStarted"));
            progressStarted = true;
            logger.Information("ADK operation started. OperationKind={OperationKind}, OperationId={OperationId}", kind, lease.OperationId);
            var progress = new InlineProgress(value => operationProgressService.Report(value.Percent,
                localizationService.GetString($"Adk.Operation.{value.Stage}")));
            AdkInstallResult result = await installationService.InstallAsync(Constants.InstallerCacheDirectoryPath, upgrade, progress, cancellationToken);
            LastResult = result;
            if (result.Outcome == AdkInstallOutcome.OwnershipUncertain)
                lease.RetainForRecovery(new string[] { Constants.InstallerCacheDirectoryPath });
            ApplyStatus(result.Status);
            terminalStatus = localizationService.GetString(GetOutcomeResourceKey(result.Outcome));
            if (result.Outcome == AdkInstallOutcome.Ready) operationProgressService.Complete(terminalStatus);
            else operationProgressService.Report(100, terminalStatus);
            logger.Information("ADK operation ended. OperationKind={OperationKind}, Outcome={Outcome}, RebootRequired={RebootRequired}, ProcessId={ProcessId}, ProcessStartUtc={ProcessStartUtc}",
                kind, result.Outcome, result.RebootRequired, installationService.LastExecution?.ProcessId, installationService.LastExecution?.StartTimeUtc);
            return result;
        }
        catch (Exception error)
        {
            if (installationService.HasUncertainOwnership) lease.RetainForRecovery(new string[] { Constants.InstallerCacheDirectoryPath });
            bool reboot = error.Data["AdkRebootRequired"] is true;
            LastResult = new(error.Data["AdkFinalStatus"] as AdkInstallationStatus ?? CurrentStatus,
                reboot ? AdkInstallOutcome.RebootRequired : AdkInstallOutcome.NotReady, reboot);
            if (reboot) terminalStatus = localizationService.GetString("Adk.Operation.RebootRequired");
            ApplyStatus(LastResult.Status);
            logger.Error(error, "ADK operation failed. OperationKind={OperationKind}, OperationId={OperationId}", kind, lease.OperationId);
            throw;
        }
        finally
        {
            if (progressStarted) operationProgressService.Reset(terminalStatus);
            if (!lease.RecoveryRequired)
            {
                try { lease.DeleteOwnedWorkspace(); }
                catch (Exception error) { logger.Warning(error, "Unable to remove completed ADK operation workspace."); }
            }
        }
    }

    internal static string GetOutcomeResourceKey(AdkInstallOutcome outcome) => outcome switch
    {
        AdkInstallOutcome.Ready => "Adk.Operation.Completed",
        AdkInstallOutcome.RebootRequired => "Adk.Operation.RebootRequired",
        AdkInstallOutcome.Cancelled => "Adk.Operation.Cancelled",
        AdkInstallOutcome.OwnershipUncertain => "Adk.Operation.OwnershipUncertain",
        _ => "Adk.Operation.NotReady"
    };

    private void ApplyStatus(AdkInstallationStatus status)
    {
        CurrentStatus = LastResult is { Outcome: not AdkInstallOutcome.Ready }
            ? status with { IsCompatible = false } : status;
        StatusChanged?.Invoke(this, new(CurrentStatus));
    }

    private sealed class InlineProgress(Action<AdkInstallationProgress> report) : IProgress<AdkInstallationProgress>
    {
        public void Report(AdkInstallationProgress value) => report(value);
    }
}
