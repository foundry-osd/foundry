// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Operations;
using Foundry.Utilities.Progress;
using Foundry.Utilities.Diagnostics;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Utilities.Storage;
using Serilog;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Provides shared state, logging, workspace paths, and progress helpers to deployment steps.
/// </summary>
public sealed class DeploymentStepExecutionContext : IDisposable
{
    /// <summary>Holds captured network data only for the active deployment, outside persisted diagnostics.</summary>
    internal PreOobe.PreOobeNetworkProfileRoamingPayload? NetworkProfileRoamingPayload { get; set; }

    /// <summary>Distinguishes a resolved empty network payload from an unresolved preparation.</summary>
    internal bool NetworkProfileRoamingResolved { get; set; }
    /// <summary>Holds credential-bearing bytes only in memory between validation and staging.</summary>
    internal Unattend.UnattendSnapshot? UnattendSnapshot { get; set; }

    /// <summary>Holds readiness evidence only for this execution and protects an externally prepared image.</summary>
    internal DeploymentPreflightState? Preflight { get; set; }

    /// <summary>Proves a cache path is on a known physical disk other than the target; WinPE RAM is never external staging.</summary>
    internal async Task<bool> IsExternalStorageAsync(string path, CancellationToken cancellationToken)
    {
        string? root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.Equals(root, @"X:\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int? number = await _targetDiskService.GetDiskNumberForPathAsync(path, cancellationToken).ConfigureAwait(false);
        if (number == Request.TargetDiskNumber)
        {
            throw Steps.PreflightDeploymentStep.Guard("Preflight.CacheUnavailable", "cache_on_target_disk");
        }
        return number is >= 0 && number.Value != Request.TargetDiskNumber;
    }

    /// <summary>Proves source separation for physical media or a currently attached read-only optical volume.</summary>
    internal async Task<bool> IsCustomSourceSeparateAsync(Models.CustomImageAsset asset, string path, CancellationToken cancellationToken)
    {
        Services.Images.CustomImageSourceLease.EnsureRegularPath(path);
        if (!asset.IsOptical) return await IsExternalStorageAsync(path, cancellationToken).ConfigureAwait(false);
        string? root = Path.GetPathRoot(Path.GetFullPath(path));
        if (!string.Equals(root, Path.GetPathRoot(Path.GetFullPath(asset.VolumeRoot)), StringComparison.OrdinalIgnoreCase)) return false;
        var drive = new DriveInfo(root!);
        return drive.IsReady && drive.DriveType == DriveType.CDRom;
    }

    /// <summary>Releases sensitive answer-file content on every terminal deployment outcome.</summary>
    public void Dispose()
    {
        UnattendSnapshot?.Dispose();
        UnattendSnapshot = null;
        Preflight?.Dispose();
        Preflight = null;
        NetworkProfileRoamingPayload = null;
    }

    private const string WinPeRoot = @"X:\Foundry";
    private static readonly string WinPeDriveRoot = Path.GetPathRoot(WinPeRoot) ?? @"X:\";
    private const string LogsFolderName = "Logs";
    private const string TempFolderName = "Temp";
    private const string StateFolderName = "State";
    private const string RuntimeFolderName = "Runtime";
    private const string CacheFolderName = "Cache";
    private const string OperatingSystemsFolderName = "OperatingSystems";
    private const string DriverPacksFolderName = "DriverPacks";
    private const string MicrosoftUpdateCatalogFolderName = "MicrosoftUpdateCatalog";
    private const string DriversFolderName = "Drivers";
    private const string FirmwareFolderName = "Firmware";
    private const string DryRunWorkspaceFolderName = "DryRun";
    private const string RuntimeWorkspaceFolderName = "Runtime";
    private const long UnknownTotalDownloadProgressIncrementBytes = 16L * 1024 * 1024;

    private readonly IOperationProgressService _operationProgressService;
    private readonly IDeploymentLogService _deploymentLogService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly IDeploymentStorageService _storageService;
    private readonly Action<DeploymentStepProgress> _emitStepProgress;
    private readonly object _runtimeStatePersistenceLock = new();
    private readonly SemaphoreSlim _sessionPersistenceGate = new(1, 1);
    private Task _pendingRuntimeStatePersistence = Task.CompletedTask;

    /// <summary>
    /// Initializes a deployment step execution context and creates the initial log session.
    /// </summary>
    public DeploymentStepExecutionContext(
        DeploymentContext request,
        DeploymentRuntimeState runtimeState,
        IReadOnlyList<string> plannedSteps,
        IOperationProgressService operationProgressService,
        IDeploymentLogService deploymentLogService,
        ITargetDiskService targetDiskService,
        Action<DeploymentStepProgress> emitStepProgress,
        IDeploymentStorageService? storageService = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        RuntimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        PlannedSteps = plannedSteps ?? throw new ArgumentNullException(nameof(plannedSteps));
        _operationProgressService = operationProgressService ?? throw new ArgumentNullException(nameof(operationProgressService));
        _deploymentLogService = deploymentLogService ?? throw new ArgumentNullException(nameof(deploymentLogService));
        _targetDiskService = targetDiskService ?? throw new ArgumentNullException(nameof(targetDiskService));
        _storageService = storageService ?? new DeploymentStorageService();
        _emitStepProgress = emitStepProgress ?? throw new ArgumentNullException(nameof(emitStepProgress));

        EnsureWorkspaceFolders();
        LogSession = _deploymentLogService.Initialize(RuntimeState.WorkspaceRoot);
    }

    /// <summary>
    /// Gets the immutable deployment request.
    /// </summary>
    public DeploymentContext Request { get; }

    /// <summary>
    /// Gets mutable runtime state persisted between deployment steps.
    /// </summary>
    public DeploymentRuntimeState RuntimeState { get; }

    /// <summary>
    /// Gets the planned step names in execution order.
    /// </summary>
    public IReadOnlyList<string> PlannedSteps { get; private set; }

    private IReadOnlyList<DeploymentPlanEntry>? _plan;
    private int _reportedProgress;
    private readonly object _stepProgressSync = new();
    private int _stepGeneration;
    private bool _stepIsTerminal;

    /// <summary>Updates future work while retaining an immutable snapshot for queued UI progress events.</summary>
    internal void UpdatePlan(IReadOnlyList<DeploymentPlanEntry> plan)
    {
        _plan = plan;
        PlannedSteps = plan.Select(entry => entry.Name).ToArray();
        StepCount = plan.Count;
    }

    /// <summary>
    /// Gets the active deployment log session.
    /// </summary>
    public DeploymentLogSession LogSession { get; private set; }

    /// <summary>
    /// Gets the one-based index of the currently executing step.
    /// </summary>
    public int StepIndex { get; private set; }

    /// <summary>
    /// Gets the total number of planned deployment steps.
    /// </summary>
    public int StepCount { get; private set; }

    /// <summary>
    /// Gets the name of the currently executing step.
    /// </summary>
    public string StepName { get; private set; } = string.Empty;

    /// <summary>
    /// Resolves the workspace root for WinPE, dry-run, or local runtime execution.
    /// </summary>
    /// <param name="context">The deployment request.</param>
    /// <returns>The workspace root path.</returns>
    public static string ResolveWorkspaceRoot(DeploymentContext context)
    {
        bool hasWinPeDrive = Directory.Exists(WinPeDriveRoot);
        if (hasWinPeDrive)
        {
            // Real WinPE runs use X:\Foundry so logs and transient files stay with the boot environment.
            return WinPeRoot;
        }

        string modeFolder = context.IsDryRun ? DryRunWorkspaceFolderName : RuntimeWorkspaceFolderName;
        return Path.Combine(Path.GetTempPath(), "Foundry", modeFolder);
    }

    /// <summary>
    /// Updates the current step metadata before a deployment step runs.
    /// </summary>
    /// <param name="step">Step that is about to execute.</param>
    /// <param name="stepIndex">One-based step index.</param>
    public void SetCurrentStep(IDeploymentStep step, int stepIndex)
    {
        ArgumentNullException.ThrowIfNull(step);

        lock (_stepProgressSync)
        {
            _stepGeneration++;
            _stepIsTerminal = false;
            StepName = step.Name;
            StepIndex = stepIndex;
            StepCount = PlannedSteps.Count;
            RuntimeState.CurrentStep = step.Name;
            RuntimeState.CurrentOperation = DeploymentOperationNames.ForStep(step.Name);
        }
    }

    /// <summary>
    /// Updates the active logical operation used for failure telemetry.
    /// </summary>
    /// <param name="operationName">Stable logical operation name.</param>
    public void SetCurrentOperation(string operationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        RuntimeState.CurrentOperation = operationName;
        QueueRuntimeStatePersistence();
    }

    /// <summary>
    /// Emits the current step state and optional nested progress to subscribers.
    /// </summary>
    /// <param name="state">Current state of the deployment step.</param>
    /// <param name="message">Optional primary progress message.</param>
    /// <param name="stepSubProgressPercent">Optional nested step progress percentage.</param>
    /// <param name="stepSubProgressIndeterminate">Whether nested progress should be shown as indeterminate.</param>
    /// <param name="stepSubProgressLabel">Optional nested progress label.</param>
    public void EmitCurrentStep(
        DeploymentStepState state,
        string? message,
        double? stepSubProgressPercent = null,
        bool stepSubProgressIndeterminate = true,
        string? stepSubProgressLabel = null)
    {
        lock (_stepProgressSync)
        {
            if (_stepIsTerminal && state == DeploymentStepState.Running) return;
            _stepIsTerminal = state is DeploymentStepState.Succeeded or DeploymentStepState.Skipped or DeploymentStepState.Failed or DeploymentStepState.Cancelled;
            int finishedCount = Math.Max(0, StepIndex - (state is DeploymentStepState.Succeeded or DeploymentStepState.Skipped ? 0 : 1));
            int progressPercent = CalculateStepProgressPercent(finishedCount, StepCount);
            _reportedProgress = Math.Max(_reportedProgress, Math.Min(progressPercent, 99));
            _emitStepProgress(new DeploymentStepProgress
            {
                StepName = StepName,
                StepLabel = _plan?.FirstOrDefault(entry => entry.Name == StepName)?.Label,
                Plan = _plan,
                State = state,
                StepIndex = StepIndex,
                StepCount = StepCount,
                ProgressPercent = _reportedProgress,
                Message = message,
                StepSubProgressPercent = stepSubProgressPercent,
                StepSubProgressIndeterminate = stepSubProgressIndeterminate,
                StepSubProgressLabel = stepSubProgressLabel
            });
        }
    }

    /// <summary>
    /// Emits the current step as running with an indeterminate nested progress label.
    /// </summary>
    /// <param name="stepMessage">Primary progress message.</param>
    /// <param name="stepSubProgressLabel">Nested indeterminate progress label.</param>
    /// <param name="operationName">Stable logical operation name.</param>
    public void EmitCurrentStepIndeterminate(
        string stepMessage,
        string stepSubProgressLabel,
        string operationName)
    {
        SetCurrentOperation(operationName);
        EmitCurrentStep(
            DeploymentStepState.Running,
            stepMessage,
            stepSubProgressPercent: null,
            stepSubProgressIndeterminate: true,
            stepSubProgressLabel: stepSubProgressLabel);
    }

    /// <summary>
    /// Reports shell-level progress for the current step.
    /// </summary>
    /// <param name="message">Progress message shown by the shell.</param>
    public void ReportCurrentStepProgress(string message)
    {
        _operationProgressService.Report(_reportedProgress, message);
    }

    /// <summary>
    /// Appends an entry to the active deployment log.
    /// </summary>
    /// <param name="level">Log level for the entry.</param>
    /// <param name="message">Log message.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    /// <returns>A task that completes after the log entry is persisted.</returns>
    public Task AppendLogAsync(
        DeploymentLogLevel level,
        string message,
        CancellationToken cancellationToken = default)
    {
        return _deploymentLogService.AppendAsync(LogSession, level, message, cancellationToken);
    }

    /// <summary>
    /// Persists the current runtime state into the active log session.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    /// <returns>A task that completes after state is persisted.</returns>
    public async Task SaveRuntimeStateAsync(CancellationToken cancellationToken = default)
    {
        await _sessionPersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _deploymentLogService.SaveStateAsync(LogSession, RuntimeState, cancellationToken).ConfigureAwait(false);
        }
        finally { _sessionPersistenceGate.Release(); }
    }

    /// <summary>
    /// Attempts to persist runtime state without replacing the primary deployment outcome on failure.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    /// <returns>A task that completes after the best-effort persistence attempt.</returns>
    public async Task TrySaveRuntimeStateAsync(CancellationToken cancellationToken = default)
    {
        Task persistence;
        lock (_runtimeStatePersistenceLock)
        {
            persistence = PersistRuntimeStateAfterAsync(_pendingRuntimeStatePersistence, cancellationToken);
            _pendingRuntimeStatePersistence = ObserveRuntimeStatePersistenceAsync(persistence);
        }

        await persistence.ConfigureAwait(false);
    }

    private void QueueRuntimeStatePersistence()
    {
        lock (_runtimeStatePersistenceLock)
        {
            Task persistence = PersistRuntimeStateAfterAsync(
                _pendingRuntimeStatePersistence,
                CancellationToken.None);
            _pendingRuntimeStatePersistence = ObserveRuntimeStatePersistenceAsync(persistence);
        }
    }

    private async Task PersistRuntimeStateAfterAsync(
        Task previousPersistence,
        CancellationToken cancellationToken)
    {
        await previousPersistence.ConfigureAwait(false);
        try
        {
            await SaveRuntimeStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogRuntimeStatePersistenceFailure(ex);
        }
    }

    private static async Task ObserveRuntimeStatePersistenceAsync(Task persistence)
    {
        try
        {
            await persistence.ConfigureAwait(false);
        }
        catch
        {
            // Preserve the caller-visible outcome on the original task while keeping the serialized queue recoverable.
        }
    }

    private void LogRuntimeStatePersistenceFailure(Exception exception)
    {
        Log.ForContext<DeploymentStepExecutionContext>().Warning(
            exception,
            "Deployment runtime state could not be persisted. Workflow={Workflow}, Stage={Stage}, StepName={StepName}, OperationId={OperationId}, FailedOperationName={FailedOperationName}",
            "deployment",
            "runtime_state_persistence",
            RuntimeState.CurrentStep,
            RuntimeState.OperationId,
            RuntimeState.CurrentOperation);
    }

    /// <summary>
    /// Moves the active log session to the target Windows Foundry root after the target partition is available.
    /// </summary>
    /// <param name="targetFoundryRoot">Foundry root on the applied Windows partition.</param>
    /// <param name="cancellationToken">Token that cancels the transfer log write.</param>
    /// <returns>A task that completes after the log session is rebound.</returns>
    public async Task<DeploymentArtifactHandoffResult> RebindLogSessionToTargetAsync(
        string targetFoundryRoot,
        CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (_runtimeStatePersistenceLock) pending = _pendingRuntimeStatePersistence;
        await pending.ConfigureAwait(false);
        await _sessionPersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        DeploymentLogSession previousSession = LogSession;
        List<string> persisted = [];
        try
        {
            DeploymentLogSession rebound = _deploymentLogService.Initialize(targetFoundryRoot);
            string? startupDirectory = Path.GetDirectoryName(FoundryDeployLogging.CurrentLogFilePath);
            await CopyDiagnosticTreeAsync(Path.Combine(previousSession.RootPath, "State"), Path.Combine(rebound.RootPath, "State"), persisted, cancellationToken).ConfigureAwait(false);
            await CopyLogTreeAsync(Path.Combine(previousSession.RootPath, "Logs"), rebound, persisted, cancellationToken).ConfigureAwait(false);
            await CopyBootstrapDiagnosticsAsync(previousSession, rebound, startupDirectory, persisted, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(startupDirectory) && Directory.Exists(startupDirectory) &&
                !Path.GetFullPath(startupDirectory).Equals(Path.GetFullPath(previousSession.LogsDirectoryPath), StringComparison.OrdinalIgnoreCase))
            {
                await CopyLogTreeAsync(startupDirectory, rebound, persisted, cancellationToken, startup: true).ConfigureAwait(false);
            }
            await _deploymentLogService.SaveStateAsync(rebound, RuntimeState, cancellationToken).ConfigureAwait(false);
            persisted.Add(rebound.StateFilePath);
            bool canRetire = FoundryDeployLogging.CanRetireRoot(previousSession.RootPath);
            FoundryDeployLogging.SwitchPersistenceDirectory(previousSession.RootPath, rebound.LogsDirectoryPath);
            LogSession = rebound;
            return new(rebound.RootPath, persisted, [], canRetire);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log.ForContext<DeploymentStepExecutionContext>().Warning(ex,
                "Required diagnostic handoff failed; source evidence retained. SourceRootPath={SourceRootPath}, TargetRootPath={TargetRootPath}",
                previousSession.RootPath, targetFoundryRoot);
            return new(previousSession.RootPath, persisted, [ex.Message], false);
        }
        finally { _sessionPersistenceGate.Release(); }
    }

    /// <summary>
    /// Revalidates that the selected target disk is still present and selectable.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels disk enumeration.</param>
    /// <returns>The selected disk, or a failed step result when validation fails.</returns>
    public async Task<(TargetDiskInfo? SelectedDisk, DeploymentStepResult? Failure)> TryGetValidatedTargetDiskAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TargetDiskInfo> disks = await _targetDiskService
            .GetDisksAsync(cancellationToken, includeExcludedDisks: true).ConfigureAwait(false);
        DiskIdentity? expected = Request.TargetDiskIdentity;
        DiskIdentity? resolved = expected?.Resolve(disks.Select(static disk => disk.Identity).OfType<DiskIdentity>());
        if (expected is null || expected.Number != Request.TargetDiskNumber || resolved is null)
        {
            return (null, DeploymentStepResult.Failed(
                LocalizationText.GetString("Disk.IdentityCannotBeConfirmed"),
                DeploymentFailure.Guard(
                    DeploymentOperationNames.ValidateTargetDisk,
                    DeploymentFailureReasons.InvalidState,
                    "target_disk_identity_mismatch")));
        }

        TargetDiskInfo selectedDisk = disks.Single(disk => ReferenceEquals(disk.Identity, resolved));
        if (!selectedDisk.IsSelectable || selectedDisk.IsSystem || selectedDisk.IsBoot ||
            selectedDisk.IsReadOnly || selectedDisk.IsOffline ||
            string.Equals(selectedDisk.BusType, "USB", StringComparison.OrdinalIgnoreCase))
        {
            return (null, DeploymentStepResult.Failed(
                LocalizationText.GetString("Disk.IdentityCannotBeConfirmed"),
                DeploymentFailure.Guard(
                    DeploymentOperationNames.ValidateTargetDisk,
                    DeploymentFailureReasons.InvalidState,
                    "target_disk_not_selectable")));
        }

        await AppendLogAsync(DeploymentLogLevel.Info, $"Target disk revalidated: {selectedDisk.DisplayLabel}", cancellationToken).ConfigureAwait(false);
        return (selectedDisk, null);
    }

    /// <summary>
    /// Creates required workspace folders under the current runtime workspace root.
    /// </summary>
    public void EnsureWorkspaceFolders()
    {
        EnsureWorkspaceFolders(RuntimeState.WorkspaceRoot);
    }

    /// <summary>
    /// Resolves the logs folder path for the active workspace.
    /// </summary>
    /// <returns>The workspace logs path.</returns>
    public string ResolveWorkspaceLogsPath()
    {
        return Path.Combine(ResolveWorkspaceRoot(RuntimeState), LogsFolderName);
    }

    /// <summary>
    /// Resolves a path under the active workspace temporary folder.
    /// </summary>
    /// <param name="relativeSegments">Optional path segments below the temporary folder.</param>
    /// <returns>The resolved temporary path.</returns>
    public string ResolveWorkspaceTempPath(params string[] relativeSegments)
    {
        string currentPath = Path.Combine(ResolveWorkspaceRoot(RuntimeState), TempFolderName);
        foreach (string segment in relativeSegments)
        {
            currentPath = Path.Combine(currentPath, segment);
        }

        return currentPath;
    }

    /// <summary>
    /// Resolves the operating system cache root for the current deployment mode.
    /// </summary>
    /// <returns>The operating system cache root.</returns>
    public string ResolveOperatingSystemCacheRoot()
    {
        return ResolvePayloadCacheRoot(OperatingSystemsFolderName, requiredBytes: 0);
    }

    /// <summary>
    /// Resolves the driver pack cache root and falls back to the target workspace when the USB cache is too small.
    /// </summary>
    /// <param name="requiredBytes">Expected payload size in bytes.</param>
    /// <param name="relativePath">Selected package path below the driver cache, used only to account for its existing allocation.</param>
    /// <returns>The driver pack cache root.</returns>
    public string ResolveDriverPackCacheRoot(long requiredBytes, string? relativePath = null)
    {
        return ResolvePayloadCacheRoot(DriverPacksFolderName, requiredBytes, relativePath);
    }

    /// <summary>Chooses storage for a selected catalog CAB while retaining its existing allocation for subsequent content validation.</summary>
    public string ResolveMicrosoftUpdateCatalogDriverCacheRoot(long requiredBytes, string relativePath)
    {
        string cacheRoot = requiredBytes > 0
            ? ResolvePayloadCacheRoot(MicrosoftUpdateCatalogFolderName, requiredBytes, Path.Combine(DriversFolderName, relativePath))
            : Path.Combine(EnsureTargetFoundryRoot(), CacheFolderName, MicrosoftUpdateCatalogFolderName);
        return Path.Combine(cacheRoot, DriversFolderName);
    }

    /// <summary>Chooses storage for the selected firmware CAB using its expected size and reusable cache allocation.</summary>
    public string ResolveMicrosoftUpdateCatalogFirmwareCacheRoot(long requiredBytes, string relativePath)
    {
        string cacheRoot = requiredBytes > 0
            ? ResolvePayloadCacheRoot(MicrosoftUpdateCatalogFolderName, requiredBytes, Path.Combine(FirmwareFolderName, relativePath))
            : Path.Combine(EnsureTargetFoundryRoot(), CacheFolderName, MicrosoftUpdateCatalogFolderName);
        return Path.Combine(cacheRoot, FirmwareFolderName);
    }

    /// <summary>
    /// Gets the target Foundry root or throws when the target partition has not been prepared.
    /// </summary>
    /// <returns>The target Foundry root path.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the target Foundry root is unavailable.</exception>
    public string EnsureTargetFoundryRoot()
    {
        return RuntimeState.TargetFoundryRoot
            ?? throw new InvalidOperationException("Target Foundry root is unavailable.");
    }

    /// <summary>
    /// Maps artifact verification and download progress to the shell, resetting counts between phases.
    /// </summary>
    /// <param name="artifactLabel">User-facing artifact label.</param>
    /// <param name="operationName">Stable logical operation name.</param>
    /// <returns>A download progress reporter.</returns>
    public IProgress<DownloadProgress> CreateDownloadProgressReporter(
        string artifactLabel,
        string operationName)
    {
        SetCurrentOperation(operationName);
        DownloadPhase? lastPhase = null;
        double? lastReportedPercent = null;
        long nextUnknownTotalReportThreshold = 0;
        int generation = _stepGeneration;

        return new DelegateProgress<DownloadProgress>(progress =>
        {
            lock (_stepProgressSync)
            {
                if (generation != _stepGeneration || _stepIsTerminal) return;
                if (progress.Phase != lastPhase)
                {
                    lastPhase = progress.Phase;
                    lastReportedPercent = null;
                    nextUnknownTotalReportThreshold = 0;
                }

                bool isVerifyingCache = progress.Phase == DownloadPhase.VerifyingCache;
                string details;
                double? stepSubProgressPercent = null;
                bool stepSubProgressIndeterminate = true;

                if (progress.TotalBytes is long totalBytes && totalBytes > 0)
                {
                    double percent = TransferProgress.CalculatePercentage(progress.BytesProcessed, totalBytes) ?? 0d;
                    bool isFinal = progress.BytesProcessed >= totalBytes;
                    if (!isFinal &&
                        lastReportedPercent.HasValue &&
                        percent <= lastReportedPercent.Value)
                    {
                        return;
                    }

                    lastReportedPercent = percent;
                    details = $"{percent:0.#}% ({FormatByteSize(progress.BytesProcessed)} / {FormatByteSize(totalBytes)})";
                    if (isVerifyingCache)
                    {
                        details = $"Checking cache: {details}";
                    }

                    stepSubProgressPercent = percent;
                    stepSubProgressIndeterminate = false;
                }
                else
                {
                    bool shouldReport = progress.BytesProcessed == 0 ||
                                        progress.BytesProcessed >= nextUnknownTotalReportThreshold;
                    if (!shouldReport)
                    {
                        return;
                    }

                    nextUnknownTotalReportThreshold = progress.BytesProcessed + UnknownTotalDownloadProgressIncrementBytes;
                    details = isVerifyingCache ? "Checking cache..." : $"{FormatByteSize(progress.BytesProcessed)} downloaded";
                }

                string stepMessage = isVerifyingCache ? "Checking cache..." : $"Downloading {artifactLabel}...";
                ReportCurrentStepProgress(stepMessage);
                EmitCurrentStep(
                    DeploymentStepState.Running,
                    stepMessage,
                    stepSubProgressPercent,
                    stepSubProgressIndeterminate,
                    details);
            }
        });
    }

    /// <summary>
    /// Creates a progress adapter that emits monotonic nested step percentages.
    /// </summary>
    /// <param name="stepMessage">Primary step message.</param>
    /// <param name="stepLabelPrefix">Nested label prefix.</param>
    /// <returns>A percentage progress reporter.</returns>
    public IProgress<double> CreateStepPercentProgressReporter(string stepMessage, string stepLabelPrefix)
    {
        int generation = _stepGeneration;
        double lastReportedPercent = double.NaN;

        return new DelegateProgress<double>(percent =>
        {
            double normalized = Math.Clamp(percent, 0d, 100d);
            lock (_stepProgressSync)
            {
                if (generation != _stepGeneration || _stepIsTerminal) return;
                if (!double.IsNaN(lastReportedPercent) && normalized <= lastReportedPercent)
                {
                    return;
                }

                lastReportedPercent = normalized;
                EmitCurrentStep(
                    DeploymentStepState.Running,
                    stepMessage,
                    stepSubProgressPercent: normalized,
                    stepSubProgressIndeterminate: false,
                    stepSubProgressLabel: $"{stepLabelPrefix}: {normalized:0.#}%");
            }
        });
    }

    /// <summary>
    /// Chooses the primary hash when available, otherwise falls back to a secondary hash.
    /// </summary>
    /// <param name="primaryHash">Preferred hash value.</param>
    /// <param name="secondaryHash">Fallback hash value.</param>
    /// <returns>The trimmed hash value, or an empty string.</returns>
    public static string ResolvePreferredHash(string? primaryHash, string? secondaryHash)
    {
        if (!string.IsNullOrWhiteSpace(primaryHash))
        {
            return primaryHash.Trim();
        }

        return secondaryHash?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Resolves a safe artifact file name from a preferred name or source URL.
    /// </summary>
    /// <param name="preferredFileName">Preferred catalog file name.</param>
    /// <param name="sourceUrl">Source URL used when no preferred file name is available.</param>
    /// <returns>A safe file name for local storage.</returns>
    public static string ResolveFileName(string preferredFileName, string sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(preferredFileName))
        {
            return SanitizePathSegment(preferredFileName);
        }

        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri))
        {
            string fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return SanitizePathSegment(fileName);
            }
        }

        return $"artifact-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.bin";
    }

    /// <summary>
    /// Replaces invalid file-name characters in a path segment.
    /// </summary>
    /// <param name="value">Path segment to sanitize.</param>
    /// <returns>A non-empty path segment.</returns>
    public static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "item";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }

    private string EnsureResolvedCache()
    {
        return RuntimeState.ResolvedCache?.RootPath
            ?? throw new InvalidOperationException("Cache strategy has not been resolved.");
    }

    private string EnsureCacheBaseRoot()
    {
        return ResolveCacheBaseRoot(EnsureResolvedCache());
    }

    private string ResolvePayloadCacheRoot(string payloadFolderName, long requiredBytes, string? relativePath = null)
    {
        if (RuntimeState.Mode == DeploymentMode.Iso &&
            !string.IsNullOrWhiteSpace(RuntimeState.TargetFoundryRoot))
        {
            return Path.Combine(RuntimeState.TargetFoundryRoot, CacheFolderName, payloadFolderName);
        }

        string cacheRoot = Path.Combine(EnsureCacheBaseRoot(), CacheFolderName, payloadFolderName);
        if (RuntimeState.Mode == DeploymentMode.Usb &&
            !string.IsNullOrWhiteSpace(RuntimeState.TargetFoundryRoot) &&
            (!HasAvailableSpace(cacheRoot, requiredBytes, relativePath) ||
             !_storageService.CanWriteDirectory(cacheRoot, relativePath is null ? null : Path.Combine(cacheRoot, relativePath))))
        {
            return Path.Combine(RuntimeState.TargetFoundryRoot, CacheFolderName, payloadFolderName);
        }

        return cacheRoot;
    }

    private bool HasAvailableSpace(string path, long requiredBytes, string? relativePath)
    {
        try
        {
            string rootPath = Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return true;
            }

            long existingBytes = 0;
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                string candidatePath = Path.Combine(path, relativePath);
                if (File.Exists(candidatePath)) existingBytes = new FileInfo(candidatePath).Length;
            }

            // Replacement truncates this same file. Its allocation is reusable, but the downloader still validates all bytes.
            long allocationBytes = Math.Max(0, requiredBytes - existingBytes);
            long? availableBytes = _storageService.GetAvailableBytes(path);
            // Preserve best-effort routing when capacity cannot be queried; payload validation remains mandatory.
            return !availableBytes.HasValue || availableBytes.Value >= allocationBytes;
        }
        catch
        {
            return true;
        }
    }

    private static string ResolveWorkspaceRoot(DeploymentRuntimeState runtimeState)
    {
        return string.IsNullOrWhiteSpace(runtimeState.WorkspaceRoot)
            ? WinPeRoot
            : runtimeState.WorkspaceRoot;
    }

    private static void EnsureWorkspaceFolders(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new InvalidOperationException("Workspace root is required.");
        }

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(workspaceRoot, LogsFolderName));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, TempFolderName));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, StateFolderName));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, RuntimeFolderName));
    }

    private static string ResolveCacheBaseRoot(string runtimeRoot)
    {
        string normalized = runtimeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string leaf = Path.GetFileName(normalized);
        if (!leaf.Equals("Runtime", StringComparison.OrdinalIgnoreCase))
        {
            return runtimeRoot;
        }

        string? parent = Path.GetDirectoryName(normalized);
        return string.IsNullOrWhiteSpace(parent)
            ? runtimeRoot
            : parent;
    }

    private static int CalculateStepProgressPercent(int stepIndex, int stepCount)
    {
        if (stepCount <= 0)
        {
            return 0;
        }

        return (int)Math.Round((double)stepIndex / stepCount * 100d);
    }

    private static string FormatByteSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(0, bytes);
        int unit = 0;
        while (size >= 1024d && unit < units.Length - 1)
        {
            size /= 1024d;
            unit++;
        }

        return unit == 0
            ? $"{size:F0} {units[unit]}"
            : $"{size:F1} {units[unit]}";
    }

    private static async Task CopyBootstrapDiagnosticsAsync(
        DeploymentLogSession previousSession,
        DeploymentLogSession destination,
        string? startupDirectory,
        List<string> persisted,
        CancellationToken cancellationToken)
    {
        string? inherited = Environment.GetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(inherited) || !Directory.Exists(inherited)) return;
        string source = Path.GetFullPath(inherited);
        string?[] coveredDirectories = [previousSession.LogsDirectoryPath, Path.Combine(previousSession.RootPath, "Logs"),
            destination.LogsDirectoryPath, Path.Combine(destination.RootPath, "Logs"), startupDirectory];
        if (coveredDirectories.Any(path => !string.IsNullOrWhiteSpace(path) &&
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Equals(Path.TrimEndingDirectorySeparator(source), StringComparison.OrdinalIgnoreCase))) return;

        // Bootstrap's USB session owns top-level application snapshots and Startup records, not arbitrary adjacent payloads.
        await DiagnosticLogSnapshot.CopyAsync(source, destination.LogsDirectoryPath, "*.log", cancellationToken).ConfigureAwait(false);
        persisted.AddRange(Directory.EnumerateFiles(source, "*.log").Select(path => Path.Combine(destination.LogsDirectoryPath, Path.GetFileName(path))));
        await CopyDiagnosticTreeAsync(Path.Combine(source, "Startup"),
            Path.Combine(destination.RootPath, "Logs", "Bootstrap", "Startup"), persisted, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyLogTreeAsync(string source, DeploymentLogSession destination, List<string> persisted, CancellationToken cancellationToken, bool startup = false)
    {
        if (!Directory.Exists(source)) return;
        await DiagnosticLogSnapshot.CopyAsync(source, destination.LogsDirectoryPath, startup ? "*.log" : "*", cancellationToken).ConfigureAwait(false);
        persisted.AddRange(Directory.EnumerateFiles(destination.LogsDirectoryPath).Where(static file =>
            !Path.GetFileName(file).StartsWith(".snapshot", StringComparison.OrdinalIgnoreCase)));
        if (startup && !Path.GetFileName(source).Equals("Logs", StringComparison.OrdinalIgnoreCase)) return;
        foreach (string child in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(child);
            if (name.Equals("PendingLogs", StringComparison.OrdinalIgnoreCase)) continue;
            await CopyDiagnosticTreeAsync(child, Path.Combine(destination.RootPath, "Logs", name), persisted, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CopyDiagnosticTreeAsync(string source, string destination, List<string> persisted, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source) || Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return;
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Diagnostic handoff cannot follow a directory link.");
        // Autopilot diagnostics can contain hardware identifiers; keep their restricted ACL after publication.
        if (Path.GetFileName(source).Equals("AutopilotHash", StringComparison.OrdinalIgnoreCase))
            AutopilotDiagnosticsDirectory.CreateRestricted(destination);
        await DiagnosticLogSnapshot.CopyAsync(source, destination, "*", cancellationToken).ConfigureAwait(false);
        persisted.AddRange(Directory.EnumerateFiles(destination).Where(static file =>
            !Path.GetFileName(file).StartsWith(".snapshot", StringComparison.OrdinalIgnoreCase)));
        foreach (string child in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(child);
            if (name.Equals("PendingLogs", StringComparison.OrdinalIgnoreCase)) continue;
            await CopyDiagnosticTreeAsync(child, Path.Combine(destination, name), persisted, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class DelegateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public DelegateProgress(Action<T> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Report(T value)
        {
            _callback(value);
        }
    }
}
