using System.IO;
using System.Text.Json;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;

namespace Foundry.Deploy.Services.Deployment;

public sealed class DeploymentOrchestrator : IDeploymentOrchestrator
{
    private const string WinPeRoot = @"X:\Foundry";

    private static readonly string[] Steps =
    [
        "Initialize deployment workspace",
        "Validate target configuration",
        "Resolve cache strategy",
        "Prepare target disk layout",
        "Download operating system image",
        "Download and prepare driver pack",
        "Apply operating system image",
        "Apply offline drivers",
        "Execute full Autopilot workflow",
        "Finalize deployment and write logs"
    ];

    private readonly IOperationProgressService _operationProgressService;
    private readonly ICacheLocatorService _cacheLocatorService;
    private readonly IDeploymentLogService _deploymentLogService;
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly IMicrosoftUpdateCatalogDriverService _microsoftUpdateCatalogDriverService;
    private readonly IArtifactDownloadService _artifactDownloadService;
    private readonly IDriverPackPreparationService _driverPackPreparationService;
    private readonly IWindowsDeploymentService _windowsDeploymentService;
    private readonly IAutopilotService _autopilotService;

    public DeploymentOrchestrator(
        IOperationProgressService operationProgressService,
        ICacheLocatorService cacheLocatorService,
        IDeploymentLogService deploymentLogService,
        IHardwareProfileService hardwareProfileService,
        ITargetDiskService targetDiskService,
        IMicrosoftUpdateCatalogDriverService microsoftUpdateCatalogDriverService,
        IArtifactDownloadService artifactDownloadService,
        IDriverPackPreparationService driverPackPreparationService,
        IWindowsDeploymentService windowsDeploymentService,
        IAutopilotService autopilotService)
    {
        _operationProgressService = operationProgressService;
        _cacheLocatorService = cacheLocatorService;
        _deploymentLogService = deploymentLogService;
        _hardwareProfileService = hardwareProfileService;
        _targetDiskService = targetDiskService;
        _microsoftUpdateCatalogDriverService = microsoftUpdateCatalogDriverService;
        _artifactDownloadService = artifactDownloadService;
        _driverPackPreparationService = driverPackPreparationService;
        _windowsDeploymentService = windowsDeploymentService;
        _autopilotService = autopilotService;
    }

    public IReadOnlyList<string> PlannedSteps => Steps;

    public event EventHandler<DeploymentStepProgress>? StepProgressChanged;
    public event EventHandler<string>? LogEmitted;

    public async Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default)
    {
        if (!_operationProgressService.TryStart(OperationKind.Deploy, "Starting Foundry.Deploy orchestration.", 0))
        {
            return new DeploymentResult
            {
                IsSuccess = false,
                Message = "Another operation is already running.",
                LogsDirectoryPath = string.Empty
            };
        }

        DeploymentLogSession? logSession = null;
        var runtimeState = new DeploymentRuntimeState
        {
            Mode = context.Mode,
            IsDryRun = context.IsDryRun,
            RequestedCacheRootPath = context.CacheRootPath,
            TargetDiskNumber = context.TargetDiskNumber,
            OperatingSystemFileName = context.OperatingSystem.FileName,
            OperatingSystemUrl = context.OperatingSystem.Url,
            DriverPackSelectionKind = context.DriverPackSelectionKind
        };

        try
        {
            EmitLog($"[INFO] Deployment mode: {context.Mode}");
            EmitLog($"[INFO] Cache root: {context.CacheRootPath}");
            EmitLog($"[INFO] Target disk number: {context.TargetDiskNumber}");
            EmitLog($"[INFO] OS: {context.OperatingSystem.DisplayLabel}");
            EmitLog($"[INFO] Driver pack mode: {context.DriverPackSelectionKind}");
            EmitLog($"[INFO] Driver pack: {(context.DriverPack?.DisplayLabel ?? "None")}");
            EmitLog($"[INFO] Autopilot mode: {(context.UseFullAutopilot ? "Full" : "Disabled")}");
            EmitLog($"[INFO] Autopilot deferred completion: {(context.AllowAutopilotDeferredCompletion ? "Enabled" : "Disabled")}");
            EmitLog("[INFO] Telemetry mode: disabled (zero telemetry).");
            EmitLog($"[INFO] Execution mode: {(context.IsDryRun ? "Debug Safe Mode (dry-run)" : "Live")}");

            for (int i = 0; i < Steps.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string stepName = Steps[i];
                runtimeState.CurrentStep = stepName;
                EmitStep(stepName, DeploymentStepState.Running, i + 1, Steps.Length, $"Starting {stepName}.");
                await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[STEP] {stepName}", cancellationToken).ConfigureAwait(false);

                StepExecutionOutcome outcome = await ExecuteStepAsync(
                    stepName,
                    context,
                    runtimeState,
                    logSession,
                    cancellationToken).ConfigureAwait(false);

                if (outcome.RebindLogSession is not null)
                {
                    logSession = outcome.RebindLogSession;
                }

                int progressPercent = (int)Math.Round((double)(i + 1) / Steps.Length * 100d);
                _operationProgressService.Report(progressPercent, outcome.Message);
                EmitStep(stepName, outcome.State, i + 1, Steps.Length, outcome.Message);

                if (outcome.State == DeploymentStepState.Failed)
                {
                    throw new InvalidOperationException(outcome.Message);
                }

                if (outcome.State == DeploymentStepState.Succeeded)
                {
                    runtimeState.CompletedSteps.Add(stepName);
                }

                if (logSession is not null)
                {
                    await _deploymentLogService.SaveStateAsync(logSession, runtimeState, cancellationToken).ConfigureAwait(false);
                }
            }

            _operationProgressService.Complete("Deployment orchestration completed.");
            await AppendLogAsync(logSession, DeploymentLogLevel.Info, "[SUCCESS] Deployment orchestration completed.", cancellationToken).ConfigureAwait(false);

            string summaryPath = await PersistFinalArtifactsAsync(runtimeState, logSession, cancellationToken).ConfigureAwait(false);
            runtimeState.DeploymentSummaryPath = summaryPath;

            CleanupTargetFoundryRoot(runtimeState, logSession);
            return new DeploymentResult
            {
                IsSuccess = true,
                Message = "Deployment orchestration completed.",
                LogsDirectoryPath = ResolveFinalLogsDirectory(runtimeState, logSession)
            };
        }
        catch (OperationCanceledException)
        {
            _operationProgressService.Fail("Deployment cancelled.");
            await AppendLogAsync(logSession, DeploymentLogLevel.Warning, "[WARN] Deployment cancelled by user.", cancellationToken).ConfigureAwait(false);
            return new DeploymentResult
            {
                IsSuccess = false,
                Message = "Deployment cancelled.",
                LogsDirectoryPath = ResolveCurrentLogsDirectory(runtimeState, logSession)
            };
        }
        catch (Exception ex)
        {
            _operationProgressService.Fail("Deployment failed.");
            await AppendLogAsync(logSession, DeploymentLogLevel.Error, $"[ERROR] {ex.Message}", cancellationToken).ConfigureAwait(false);
            return new DeploymentResult
            {
                IsSuccess = false,
                Message = ex.Message,
                LogsDirectoryPath = ResolveCurrentLogsDirectory(runtimeState, logSession)
            };
        }
    }

    private async Task<StepExecutionOutcome> ExecuteStepAsync(
        string stepName,
        DeploymentContext context,
        DeploymentRuntimeState runtimeState,
        DeploymentLogSession? logSession,
        CancellationToken cancellationToken)
    {
        if (context.IsDryRun)
        {
            return await ExecuteDryRunStepAsync(stepName, context, runtimeState, logSession, cancellationToken).ConfigureAwait(false);
        }

        switch (stepName)
        {
            case "Initialize deployment workspace":
                {
                    EnsureWinPeWorkspaceFolders();
                    DeploymentLogSession session = _deploymentLogService.Initialize(WinPeRoot);
                    await _deploymentLogService
                        .AppendAsync(session, DeploymentLogLevel.Info, $"Log session initialized at '{session.RootPath}'.", cancellationToken)
                        .ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Workspace initialized.", session);
                }

            case "Validate target configuration":
                {
                    StepExecutionOutcome? validationFailure = await ValidateTargetDiskSelectionAsync(context, logSession, cancellationToken).ConfigureAwait(false);
                    if (validationFailure is not null)
                    {
                        return validationFailure;
                    }

                    if (string.IsNullOrWhiteSpace(context.OperatingSystem.Url))
                    {
                        return StepExecutionOutcome.Failed("Operating system URL is missing.");
                    }

                    if (context.TargetDiskNumber < 0)
                    {
                        return StepExecutionOutcome.Failed("Target disk number is required.");
                    }

                    HardwareProfile hardware = await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                    runtimeState.HardwareProfile = hardware;
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"Detected hardware: {hardware.DisplayLabel}", cancellationToken).ConfigureAwait(false);
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"Autopilot capable: {hardware.IsAutopilotCapable}", cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Target configuration validated.");
                }

            case "Resolve cache strategy":
                {
                    CacheResolution cache = await _cacheLocatorService
                        .ResolveAsync(context.Mode, context.CacheRootPath, cancellationToken)
                        .ConfigureAwait(false);

                    cache = await AdjustCacheForTargetDiskConflictAsync(cache, context, logSession, cancellationToken).ConfigureAwait(false);
                    runtimeState.ResolvedCache = cache;
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"Cache resolved: {cache.RootPath} ({cache.Source})", cancellationToken).ConfigureAwait(false);
                    EnsureWinPeWorkspaceFolders();
                    return StepExecutionOutcome.Succeeded("Cache strategy resolved.");
                }

            case "Prepare target disk layout":
                {
                    string workingDirectory = Path.Combine(WinPeRoot, "Temp", "Deployment");
                    Directory.CreateDirectory(workingDirectory);

                    StepExecutionOutcome? validationFailure = await ValidateTargetDiskSelectionAsync(context, logSession, cancellationToken).ConfigureAwait(false);
                    if (validationFailure is not null)
                    {
                        return validationFailure;
                    }

                    DeploymentTargetLayout layout = await _windowsDeploymentService
                        .PrepareTargetDiskAsync(context.TargetDiskNumber, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.TargetSystemPartitionRoot = layout.SystemPartitionRoot;
                    runtimeState.TargetWindowsPartitionRoot = layout.WindowsPartitionRoot;
                    runtimeState.TargetFoundryRoot = Path.Combine(layout.WindowsPartitionRoot, "Foundry");
                    if (runtimeState.Mode == DeploymentMode.Iso)
                    {
                        Directory.CreateDirectory(Path.Combine(runtimeState.TargetFoundryRoot, "OperatingSystem"));
                        Directory.CreateDirectory(Path.Combine(runtimeState.TargetFoundryRoot, "DriverPack"));
                    }

                    DeploymentLogSession? rebound = await RebindLogSessionIfNeededAsync(logSession, runtimeState.TargetFoundryRoot, cancellationToken).ConfigureAwait(false);

                    await AppendLogAsync(rebound, DeploymentLogLevel.Info, $"Target disk prepared: system='{layout.SystemPartitionRoot}', windows='{layout.WindowsPartitionRoot}'.", cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Target disk layout prepared.", rebound);
                }

            case "Download operating system image":
                {
                    string osDirectory = ResolveOperatingSystemCacheRoot(runtimeState);
                    Directory.CreateDirectory(osDirectory);

                    string fileName = ResolveFileName(context.OperatingSystem.FileName, context.OperatingSystem.Url);
                    string destinationPath = Path.Combine(osDirectory, fileName);
                    string? expectedOsHash = ResolvePreferredHash(context.OperatingSystem.Sha256, context.OperatingSystem.Sha1);
                    ArtifactDownloadResult result = await _artifactDownloadService
                        .DownloadAsync(context.OperatingSystem.Url, destinationPath, expectedHash: expectedOsHash, preferBits: true, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.DownloadedOperatingSystemPath = result.DestinationPath;
                    await UpdateCacheIndexAsync(
                        runtimeState,
                        artifactType: "OperatingSystem",
                        sourceUrl: context.OperatingSystem.Url,
                        destinationPath: result.DestinationPath,
                        sizeBytes: result.SizeBytes,
                        expectedHash: expectedOsHash,
                        cancellationToken).ConfigureAwait(false);
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"OS image {(result.Downloaded ? "downloaded" : "reused")} via {result.Method}: {result.DestinationPath}", cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Operating system image ready.");
                }

            case "Download and prepare driver pack":
                {
                    string targetFoundryRoot = EnsureTargetFoundryRoot(runtimeState);
                    runtimeState.DriverPackSelectionKind = context.DriverPackSelectionKind;

                    switch (context.DriverPackSelectionKind)
                    {
                        case DriverPackSelectionKind.None:
                            return StepExecutionOutcome.Skipped("Driver pack disabled (None selected).");

                        case DriverPackSelectionKind.MicrosoftUpdateCatalog:
                            {
                                string destination = Path.Combine(targetFoundryRoot, "Extracted", "Drivers", "MicrosoftUpdateCatalog");
                                MicrosoftUpdateCatalogDriverResult microsoftResult = await _microsoftUpdateCatalogDriverService
                                    .DownloadAsync(destination, cancellationToken)
                                    .ConfigureAwait(false);

                                runtimeState.DriverPackName = "Microsoft Update Catalog";
                                runtimeState.PreparedDriverPath = microsoftResult.DestinationDirectory;
                                await AppendLogAsync(logSession, DeploymentLogLevel.Info, microsoftResult.Message, cancellationToken).ConfigureAwait(false);
                                return StepExecutionOutcome.Succeeded("Microsoft Update Catalog driver payload prepared.");
                            }

                        case DriverPackSelectionKind.OemCatalog:
                            {
                                DriverPackCatalogItem? driverPack = context.DriverPack;

                                if (driverPack is null)
                                {
                                    return StepExecutionOutcome.Skipped("OEM driver pack mode selected but no driver pack was provided.");
                                }

                                runtimeState.DriverPackName = driverPack.Name;
                                runtimeState.DriverPackUrl = driverPack.DownloadUrl;

                                string driverPackDirectory = Path.Combine(ResolveDriverPackCacheRoot(runtimeState), SanitizePathSegment(driverPack.Manufacturer));
                                Directory.CreateDirectory(driverPackDirectory);
                                string archiveName = ResolveFileName(driverPack.FileName, driverPack.DownloadUrl);
                                string archivePath = Path.Combine(driverPackDirectory, archiveName);

                                ArtifactDownloadResult download = await _artifactDownloadService
                                    .DownloadAsync(driverPack.DownloadUrl, archivePath, expectedHash: driverPack.Sha256, preferBits: true, cancellationToken)
                                    .ConfigureAwait(false);

                                runtimeState.DownloadedDriverPackPath = download.DestinationPath;
                                await UpdateCacheIndexAsync(
                                    runtimeState,
                                    artifactType: "DriverPack",
                                    sourceUrl: driverPack.DownloadUrl,
                                    destinationPath: download.DestinationPath,
                                    sizeBytes: download.SizeBytes,
                                    expectedHash: driverPack.Sha256,
                                    cancellationToken).ConfigureAwait(false);

                                string extractionRoot = Path.Combine(targetFoundryRoot, "Extracted", "Drivers");
                                DriverPackPreparationResult preparation = await _driverPackPreparationService
                                    .PrepareAsync(driverPack, download.DestinationPath, extractionRoot, cancellationToken)
                                    .ConfigureAwait(false);

                                runtimeState.PreparedDriverPath = preparation.ExtractedDirectoryPath;
                                await AppendLogAsync(logSession, DeploymentLogLevel.Info, preparation.Message, cancellationToken).ConfigureAwait(false);
                                return StepExecutionOutcome.Succeeded("OEM driver pack prepared.");
                            }
                    }

                    return StepExecutionOutcome.Skipped("No driver pack operation for selected mode.");
                }

            case "Apply operating system image":
                {
                    if (string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot) ||
                        string.IsNullOrWhiteSpace(runtimeState.TargetSystemPartitionRoot))
                    {
                        return StepExecutionOutcome.Failed("Target disk layout was not prepared.");
                    }

                    string targetFoundryRoot = EnsureTargetFoundryRoot(runtimeState);
                    string imagePath = runtimeState.DownloadedOperatingSystemPath ?? string.Empty;
                    if (!File.Exists(imagePath))
                    {
                        return StepExecutionOutcome.Failed("Operating system image was not downloaded.");
                    }

                    string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
                    Directory.CreateDirectory(workingDirectory);

                    int imageIndex = await _windowsDeploymentService
                        .ResolveImageIndexAsync(imagePath, context.OperatingSystem.Edition, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.AppliedImageIndex = imageIndex;

                    string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");
                    await _windowsDeploymentService
                        .ApplyImageAsync(imagePath, imageIndex, runtimeState.TargetWindowsPartitionRoot, scratchDirectory, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);

                    await _windowsDeploymentService
                        .ConfigureBootAsync(runtimeState.TargetWindowsPartitionRoot, runtimeState.TargetSystemPartitionRoot, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);

                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"OS image applied to {runtimeState.TargetWindowsPartitionRoot} (index {imageIndex}); boot configured on {runtimeState.TargetSystemPartitionRoot}.", cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Operating system image applied.");
                }

            case "Apply offline drivers":
                {
                    if (string.IsNullOrWhiteSpace(runtimeState.PreparedDriverPath))
                    {
                        return StepExecutionOutcome.Skipped("No extracted INF driver payload available.");
                    }

                    if (string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot))
                    {
                        return StepExecutionOutcome.Failed("Target Windows partition is unavailable.");
                    }

                    string targetFoundryRoot = EnsureTargetFoundryRoot(runtimeState);
                    string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
                    string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");

                    await _windowsDeploymentService
                        .ApplyOfflineDriversAsync(
                            runtimeState.TargetWindowsPartitionRoot,
                            runtimeState.PreparedDriverPath,
                            scratchDirectory,
                            workingDirectory,
                            cancellationToken)
                        .ConfigureAwait(false);

                    int infCount = Directory.EnumerateFiles(runtimeState.PreparedDriverPath, "*.inf", SearchOption.AllDirectories).Count();
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"Offline drivers injected: {infCount} INF files from '{runtimeState.PreparedDriverPath}'.", cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Offline drivers applied.");
                }

            case "Execute full Autopilot workflow":
                {
                    if (!context.UseFullAutopilot)
                    {
                        return StepExecutionOutcome.Skipped("Autopilot is disabled.");
                    }

                    string targetFoundryRoot = EnsureTargetFoundryRoot(runtimeState);
                    if (string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot))
                    {
                        return StepExecutionOutcome.Failed("Target Windows partition is unavailable for Autopilot artifacts.");
                    }

                    HardwareProfile hardware = runtimeState.HardwareProfile ?? await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                    AutopilotExecutionResult result = await _autopilotService
                        .ExecuteFullWorkflowAsync(
                            targetFoundryRoot,
                            runtimeState.TargetWindowsPartitionRoot,
                            hardware,
                            context.OperatingSystem,
                            context.AllowAutopilotDeferredCompletion,
                            cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.AutopilotWorkflowPath = result.WorkflowManifestPath;
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"Autopilot transcript: {result.TranscriptPath}", cancellationToken).ConfigureAwait(false);

                    if (!result.IsSuccess)
                    {
                        return StepExecutionOutcome.Failed(result.Message);
                    }

                    if (result.DeferredCompletionPrepared)
                    {
                        await AppendLogAsync(logSession, DeploymentLogLevel.Warning, result.Message, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(result.DeferredScriptPath))
                        {
                            await AppendLogAsync(logSession, DeploymentLogLevel.Warning, $"Deferred script: {result.DeferredScriptPath}", cancellationToken).ConfigureAwait(false);
                        }

                        if (!string.IsNullOrWhiteSpace(result.SetupCompleteHookPath))
                        {
                            await AppendLogAsync(logSession, DeploymentLogLevel.Warning, $"SetupComplete hook: {result.SetupCompleteHookPath}", cancellationToken).ConfigureAwait(false);
                        }

                        return StepExecutionOutcome.Succeeded("Autopilot deferred completion prepared.");
                    }

                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, result.Message, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Autopilot workflow completed.");
                }

            case "Finalize deployment and write logs":
                {
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, "Finalizing deployment artifacts.", cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Deployment finalized.");
                }
        }

        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        return StepExecutionOutcome.Skipped("No operation for step.");
    }

    private async Task<StepExecutionOutcome> ExecuteDryRunStepAsync(
        string stepName,
        DeploymentContext context,
        DeploymentRuntimeState runtimeState,
        DeploymentLogSession? logSession,
        CancellationToken cancellationToken)
    {
        switch (stepName)
        {
            case "Initialize deployment workspace":
                {
                    EnsureWinPeWorkspaceFolders();
                    DeploymentLogSession session = _deploymentLogService.Initialize(WinPeRoot);
                    await _deploymentLogService.AppendAsync(session, DeploymentLogLevel.Info, "Debug safe mode log session initialized.", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Workspace initialized (simulation).", session);
                }

            case "Validate target configuration":
                {
                    HardwareProfile hardware = await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                    runtimeState.HardwareProfile = hardware;
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Hardware detected: {hardware.DisplayLabel}", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Target configuration validated (simulation).");
                }

            case "Resolve cache strategy":
                {
                    CacheResolution cache = await _cacheLocatorService
                        .ResolveAsync(context.Mode, context.CacheRootPath, cancellationToken)
                        .ConfigureAwait(false);

                    cache = await AdjustCacheForTargetDiskConflictAsync(cache, context, logSession, cancellationToken).ConfigureAwait(false);
                    runtimeState.ResolvedCache = cache;
                    EnsureWinPeWorkspaceFolders();
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Cache resolved: {cache.RootPath} ({cache.Source})", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Cache strategy resolved (simulation).");
                }

            case "Prepare target disk layout":
                {
                    string targetRoot = Path.Combine(WinPeRoot, "Temp", "DryRunTarget");
                    string systemRoot = Path.Combine(targetRoot, "System");
                    string windowsRoot = Path.Combine(targetRoot, "Windows");
                    Directory.CreateDirectory(systemRoot);
                    Directory.CreateDirectory(windowsRoot);

                    runtimeState.TargetSystemPartitionRoot = systemRoot;
                    runtimeState.TargetWindowsPartitionRoot = windowsRoot;
                    runtimeState.TargetFoundryRoot = Path.Combine(windowsRoot, "Foundry");

                    DeploymentLogSession? rebound = await RebindLogSessionIfNeededAsync(logSession, runtimeState.TargetFoundryRoot, cancellationToken).ConfigureAwait(false);

                    await AppendLogAsync(rebound, DeploymentLogLevel.Info, $"[DRY-RUN] Simulated target disk layout: system='{systemRoot}', windows='{windowsRoot}'.", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Target disk layout prepared (simulation).", rebound);
                }

            case "Download operating system image":
                {
                    string osDirectory = ResolveOperatingSystemCacheRoot(runtimeState);
                    Directory.CreateDirectory(osDirectory);

                    string fileName = ResolveFileName(context.OperatingSystem.FileName, context.OperatingSystem.Url);
                    string simulatedPath = Path.Combine(osDirectory, $"{fileName}.dryrun.txt");
                    await File.WriteAllTextAsync(
                        simulatedPath,
                        $"Dry-run artifact created at {DateTimeOffset.UtcNow:O}{Environment.NewLine}SourceUrl={context.OperatingSystem.Url}",
                        cancellationToken).ConfigureAwait(false);

                    runtimeState.DownloadedOperatingSystemPath = simulatedPath;
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Simulated OS artifact: {simulatedPath}", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Operating system image ready (simulation).");
                }

            case "Download and prepare driver pack":
                {
                    string targetFoundryRoot = EnsureTargetFoundryRoot(runtimeState);
                    runtimeState.DriverPackSelectionKind = context.DriverPackSelectionKind;

                    if (context.DriverPackSelectionKind == DriverPackSelectionKind.None)
                    {
                        await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                        return StepExecutionOutcome.Skipped("Driver pack disabled (None selected).");
                    }

                    string simulationSegment = context.DriverPackSelectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog
                        ? "ms-update-catalog"
                        : "oem";

                    if (context.DriverPackSelectionKind == DriverPackSelectionKind.OemCatalog)
                    {
                        DriverPackCatalogItem? driverPack = context.DriverPack;
                        if (driverPack is null)
                        {
                            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                            return StepExecutionOutcome.Skipped("OEM driver pack mode selected but no driver pack was provided.");
                        }

                        runtimeState.DriverPackName = driverPack.Name;
                        runtimeState.DriverPackUrl = driverPack.DownloadUrl;
                    }
                    else
                    {
                        runtimeState.DriverPackName = "Microsoft Update Catalog";
                    }

                    string driverRoot = Path.Combine(targetFoundryRoot, "Extracted", "Drivers", "dry-run", simulationSegment);
                    Directory.CreateDirectory(driverRoot);
                    string infPath = Path.Combine(driverRoot, "dryrun.inf");
                    await File.WriteAllTextAsync(infPath, "; dry-run only", cancellationToken).ConfigureAwait(false);
                    runtimeState.PreparedDriverPath = driverRoot;

                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Driver payload simulated ({context.DriverPackSelectionKind}): {driverRoot}", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Driver pack prepared (simulation).");
                }

            case "Apply operating system image":
                {
                    if (string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot) ||
                        string.IsNullOrWhiteSpace(runtimeState.TargetSystemPartitionRoot))
                    {
                        return StepExecutionOutcome.Failed("Target disk layout was not prepared.");
                    }

                    string targetFoundryRoot = EnsureTargetFoundryRoot(runtimeState);
                    string targetRoot = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
                    Directory.CreateDirectory(targetRoot);
                    runtimeState.AppliedImageIndex = 1;

                    await File.WriteAllTextAsync(
                        Path.Combine(targetRoot, "apply-image.log"),
                        $"Dry-run image apply at {DateTimeOffset.UtcNow:O}{Environment.NewLine}OS={context.OperatingSystem.DisplayLabel}",
                        cancellationToken).ConfigureAwait(false);

                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Simulated OS apply to {runtimeState.TargetWindowsPartitionRoot}.", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(180, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Operating system image applied (simulation).");
                }

            case "Apply offline drivers":
                {
                    if (string.IsNullOrWhiteSpace(runtimeState.PreparedDriverPath) || !Directory.Exists(runtimeState.PreparedDriverPath))
                    {
                        await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                        return StepExecutionOutcome.Skipped("No extracted INF driver payload available.");
                    }

                    int infCount = Directory.EnumerateFiles(runtimeState.PreparedDriverPath, "*.inf", SearchOption.AllDirectories).Count();
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Simulated offline driver injection: {infCount} INF files.", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Offline drivers applied (simulation).");
                }

            case "Execute full Autopilot workflow":
                {
                    if (!context.UseFullAutopilot)
                    {
                        await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                        return StepExecutionOutcome.Skipped("Autopilot is disabled.");
                    }

                    string targetFoundryRoot = EnsureTargetFoundryRoot(runtimeState);
                    string autopilotRoot = Path.Combine(targetFoundryRoot, "Autopilot");
                    Directory.CreateDirectory(autopilotRoot);
                    string manifestPath = Path.Combine(autopilotRoot, "autopilot-workflow.dryrun.json");

                    string manifest = JsonSerializer.Serialize(new
                    {
                        createdAtUtc = DateTimeOffset.UtcNow,
                        mode = "dry-run",
                        note = "Debug safe mode simulation. No online registration executed."
                    }, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    await File.WriteAllTextAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
                    runtimeState.AutopilotWorkflowPath = manifestPath;

                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Autopilot workflow simulated: {manifestPath}", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Autopilot workflow completed (simulation).");
                }

            case "Finalize deployment and write logs":
                {
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, "[DRY-RUN] Finalize step completed.", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Deployment finalized (simulation).");
                }
        }

        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        return StepExecutionOutcome.Skipped("No operation for step.");
    }

    private void EmitStep(string stepName, DeploymentStepState state, int stepIndex, int stepCount, string? message)
    {
        int progressPercent = (int)Math.Round((double)stepIndex / stepCount * 100d);
        StepProgressChanged?.Invoke(this, new DeploymentStepProgress
        {
            StepName = stepName,
            State = state,
            StepIndex = stepIndex,
            StepCount = stepCount,
            ProgressPercent = progressPercent,
            Message = message
        });
    }

    private void EmitLog(string message)
    {
        string line = $"[{DateTimeOffset.Now:O}] {message}";
        LogEmitted?.Invoke(this, line);
    }

    private async Task AppendLogAsync(
        DeploymentLogSession? session,
        DeploymentLogLevel level,
        string message,
        CancellationToken cancellationToken)
    {
        EmitLog($"[{level}] {message}");

        if (session is not null)
        {
            await _deploymentLogService.AppendAsync(session, level, message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string EnsureResolvedCache(DeploymentRuntimeState runtimeState)
    {
        return runtimeState.ResolvedCache?.RootPath
            ?? throw new InvalidOperationException("Cache strategy has not been resolved.");
    }

    private static string EnsureCacheBaseRoot(DeploymentRuntimeState runtimeState)
    {
        return ResolveCacheBaseRoot(EnsureResolvedCache(runtimeState));
    }

    private static string EnsureTargetFoundryRoot(DeploymentRuntimeState runtimeState)
    {
        return runtimeState.TargetFoundryRoot
            ?? throw new InvalidOperationException("Target Foundry root is unavailable.");
    }

    private static void EnsureWinPeWorkspaceFolders()
    {
        string[] folders =
        [
            WinPeRoot,
            Path.Combine(WinPeRoot, "Logs"),
            Path.Combine(WinPeRoot, "Temp"),
            Path.Combine(WinPeRoot, "State"),
            Path.Combine(WinPeRoot, "Runtime")
        ];

        foreach (string folder in folders)
        {
            Directory.CreateDirectory(folder);
        }
    }

    private async Task<CacheResolution> AdjustCacheForTargetDiskConflictAsync(
        CacheResolution resolvedCache,
        DeploymentContext context,
        DeploymentLogSession? logSession,
        CancellationToken cancellationToken)
    {
        int? cacheDiskNumber = await _targetDiskService
            .GetDiskNumberForPathAsync(resolvedCache.RootPath, cancellationToken)
            .ConfigureAwait(false);

        if (!cacheDiskNumber.HasValue || cacheDiskNumber.Value != context.TargetDiskNumber)
        {
            return resolvedCache;
        }

        if (resolvedCache.Mode == DeploymentMode.Usb)
        {
            string message =
                $"USB cache conflict: cache path '{resolvedCache.RootPath}' is on target disk {context.TargetDiskNumber}. " +
                "Deployment is blocked to preserve USB cache consistency.";
            await AppendLogAsync(logSession, DeploymentLogLevel.Error, message, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(message);
        }

        string safeRoot = ResolveWinPeConflictCacheRoot("IsoConflict");
        var adjusted = new CacheResolution
        {
            Mode = resolvedCache.Mode,
            RootPath = safeRoot,
            Source = $"{resolvedCache.Source} (conflict fallback)",
            IsPersistent = false
        };

        await AppendLogAsync(
                logSession,
                DeploymentLogLevel.Warning,
                $"Cache disk conflict detected (cache disk {cacheDiskNumber.Value} equals target disk {context.TargetDiskNumber}). Switched cache to '{safeRoot}'.",
                cancellationToken)
            .ConfigureAwait(false);

        return adjusted;
    }

    private async Task<StepExecutionOutcome?> ValidateTargetDiskSelectionAsync(
        DeploymentContext context,
        DeploymentLogSession? logSession,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TargetDiskInfo> disks = await _targetDiskService.GetDisksAsync(cancellationToken).ConfigureAwait(false);
        TargetDiskInfo? selectedDisk = disks.FirstOrDefault(disk => disk.DiskNumber == context.TargetDiskNumber);
        if (selectedDisk is null)
        {
            return StepExecutionOutcome.Failed($"Target disk {context.TargetDiskNumber} is no longer present.");
        }

        if (!selectedDisk.IsSelectable)
        {
            return StepExecutionOutcome.Failed(
                $"Target disk {context.TargetDiskNumber} is blocked: {selectedDisk.SelectionWarning}");
        }

        await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"Target disk revalidated: {selectedDisk.DisplayLabel}", cancellationToken).ConfigureAwait(false);
        return null;
    }

    private async Task<DeploymentLogSession?> RebindLogSessionIfNeededAsync(
        DeploymentLogSession? currentSession,
        string targetFoundryRoot,
        CancellationToken cancellationToken)
    {
        if (currentSession is null)
        {
            return null;
        }

        return await RebindLogSessionToTargetAsync(currentSession, targetFoundryRoot, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DeploymentLogSession> RebindLogSessionToTargetAsync(
        DeploymentLogSession currentSession,
        string targetFoundryRoot,
        CancellationToken cancellationToken)
    {
        DeploymentLogSession rebound = _deploymentLogService.Initialize(targetFoundryRoot);
        CopyDirectoryContents(currentSession.LogsDirectoryPath, rebound.LogsDirectoryPath);
        CopyDirectoryContents(currentSession.StateDirectoryPath, rebound.StateDirectoryPath);

        await _deploymentLogService
            .AppendAsync(
                rebound,
                DeploymentLogLevel.Info,
                $"Log session transferred from '{currentSession.RootPath}' to '{targetFoundryRoot}'.",
                cancellationToken)
            .ConfigureAwait(false);

        return rebound;
    }

    private async Task<string> PersistFinalArtifactsAsync(
        DeploymentRuntimeState runtimeState,
        DeploymentLogSession? logSession,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot))
        {
            string transientRoot = EnsureResolvedCache(runtimeState);
            string summaryPath = Path.Combine(transientRoot, "State", "deployment-summary.json");
            await WriteDeploymentSummaryAsync(summaryPath, runtimeState, cancellationToken).ConfigureAwait(false);
            return summaryPath;
        }

        string targetWindowsTempRoot = Path.Combine(runtimeState.TargetWindowsPartitionRoot, "Windows", "Temp", "Foundry");
        Directory.CreateDirectory(targetWindowsTempRoot);

        if (logSession is not null)
        {
            CopyDirectoryContents(logSession.LogsDirectoryPath, Path.Combine(targetWindowsTempRoot, "Logs"));
            CopyDirectoryContents(logSession.StateDirectoryPath, Path.Combine(targetWindowsTempRoot, "State"));
        }

        string finalSummaryPath = Path.Combine(targetWindowsTempRoot, "deployment-summary.json");
        await WriteDeploymentSummaryAsync(finalSummaryPath, runtimeState, cancellationToken).ConfigureAwait(false);
        return finalSummaryPath;
    }

    private static async Task WriteDeploymentSummaryAsync(
        string path,
        DeploymentRuntimeState runtimeState,
        CancellationToken cancellationToken)
    {
        string directoryPath = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Invalid deployment summary path '{path}'.");
        Directory.CreateDirectory(directoryPath);

        string json = JsonSerializer.Serialize(new
        {
            completedAtUtc = DateTimeOffset.UtcNow,
            mode = runtimeState.Mode.ToString(),
            isDryRun = runtimeState.IsDryRun,
            targetDiskNumber = runtimeState.TargetDiskNumber,
            operatingSystemFileName = runtimeState.OperatingSystemFileName,
            operatingSystemUrl = runtimeState.OperatingSystemUrl,
            downloadedOperatingSystemPath = runtimeState.DownloadedOperatingSystemPath,
            downloadedDriverPackPath = runtimeState.DownloadedDriverPackPath,
            preparedDriverPath = runtimeState.PreparedDriverPath,
            targetSystemPartitionRoot = runtimeState.TargetSystemPartitionRoot,
            targetWindowsPartitionRoot = runtimeState.TargetWindowsPartitionRoot,
            autopilotWorkflowPath = runtimeState.AutopilotWorkflowPath,
            completedSteps = runtimeState.CompletedSteps
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
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

    private static string ResolveOperatingSystemCacheRoot(DeploymentRuntimeState runtimeState)
    {
        if (runtimeState.Mode == DeploymentMode.Iso &&
            !string.IsNullOrWhiteSpace(runtimeState.TargetFoundryRoot))
        {
            return Path.Combine(runtimeState.TargetFoundryRoot, "OperatingSystem");
        }

        return Path.Combine(EnsureCacheBaseRoot(runtimeState), "OperatingSystem");
    }

    private static string ResolveDriverPackCacheRoot(DeploymentRuntimeState runtimeState)
    {
        if (runtimeState.Mode == DeploymentMode.Iso &&
            !string.IsNullOrWhiteSpace(runtimeState.TargetFoundryRoot))
        {
            return Path.Combine(runtimeState.TargetFoundryRoot, "DriverPack");
        }

        return Path.Combine(EnsureCacheBaseRoot(runtimeState), "DriverPack");
    }

    private static string ResolvePreferredHash(string? primaryHash, string? secondaryHash)
    {
        if (!string.IsNullOrWhiteSpace(primaryHash))
        {
            return primaryHash.Trim();
        }

        return secondaryHash?.Trim() ?? string.Empty;
    }

    private static string ResolveWinPeConflictCacheRoot(string suffix)
    {
        string path = Path.Combine(WinPeRoot, "Runtime", suffix);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task UpdateCacheIndexAsync(
        DeploymentRuntimeState runtimeState,
        string artifactType,
        string sourceUrl,
        string destinationPath,
        long sizeBytes,
        string? expectedHash,
        CancellationToken cancellationToken)
    {
        if (!ShouldMaintainUsbCacheIndex(runtimeState))
        {
            return;
        }

        string runtimeRoot = EnsureResolvedCache(runtimeState);
        Directory.CreateDirectory(runtimeRoot);
        string indexPath = Path.Combine(runtimeRoot, "cache-index.json");

        CacheIndexDocument document = await ReadCacheIndexAsync(indexPath, cancellationToken).ConfigureAwait(false);
        string normalizedSourceUrl = sourceUrl.Trim();
        CacheIndexEntry? entry = document.Items.FirstOrDefault(item =>
            item.SourceUrl.Equals(normalizedSourceUrl, StringComparison.OrdinalIgnoreCase));

        string normalizedHash = string.IsNullOrWhiteSpace(expectedHash)
            ? string.Empty
            : expectedHash.Trim();
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        if (entry is null)
        {
            document.Items.Add(new CacheIndexEntry
            {
                ArtifactType = artifactType,
                SourceUrl = normalizedSourceUrl,
                DestinationPath = destinationPath,
                SizeBytes = sizeBytes,
                ExpectedHash = normalizedHash,
                LastUpdatedAtUtc = nowUtc
            });
        }
        else
        {
            entry.ArtifactType = artifactType;
            entry.DestinationPath = destinationPath;
            entry.SizeBytes = sizeBytes;
            entry.ExpectedHash = normalizedHash;
            entry.LastUpdatedAtUtc = nowUtc;
        }

        document.UpdatedAtUtc = nowUtc;
        string json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(indexPath, json, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CacheIndexDocument> ReadCacheIndexAsync(string indexPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(indexPath))
        {
            return new CacheIndexDocument();
        }

        try
        {
            string json = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
            CacheIndexDocument? parsed = JsonSerializer.Deserialize<CacheIndexDocument>(json);
            if (parsed is null)
            {
                return new CacheIndexDocument();
            }

            parsed.Items ??= [];
            return parsed;
        }
        catch
        {
            return new CacheIndexDocument();
        }
    }

    private static bool ShouldMaintainUsbCacheIndex(DeploymentRuntimeState runtimeState)
    {
        return runtimeState.Mode == DeploymentMode.Usb &&
               runtimeState.ResolvedCache is not null &&
               runtimeState.ResolvedCache.IsPersistent;
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (string sourceFilePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            string destinationPath = Path.Combine(destinationDirectory, relativePath);
            string? destinationFolder = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            File.Copy(sourceFilePath, destinationPath, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void CleanupTargetFoundryRoot(DeploymentRuntimeState runtimeState, DeploymentLogSession? logSession)
    {
        if (string.IsNullOrWhiteSpace(runtimeState.TargetFoundryRoot) ||
            string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot))
        {
            return;
        }

        string finalRoot = Path.Combine(runtimeState.TargetWindowsPartitionRoot, "Windows", "Temp", "Foundry");
        if (runtimeState.TargetFoundryRoot.Equals(finalRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (logSession is not null &&
            logSession.RootPath.Equals(runtimeState.TargetFoundryRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Cleanup is done after final logging. At this stage it is safe.
            TryDeleteDirectory(runtimeState.TargetFoundryRoot);
            return;
        }

        TryDeleteDirectory(runtimeState.TargetFoundryRoot);
    }

    private static string ResolveFinalLogsDirectory(DeploymentRuntimeState runtimeState, DeploymentLogSession? logSession)
    {
        if (!string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot))
        {
            return Path.Combine(runtimeState.TargetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "Logs");
        }

        return ResolveCurrentLogsDirectory(runtimeState, logSession);
    }

    private static string ResolveCurrentLogsDirectory(DeploymentRuntimeState runtimeState, DeploymentLogSession? logSession)
    {
        if (logSession is not null && !string.IsNullOrWhiteSpace(logSession.LogsDirectoryPath))
        {
            return logSession.LogsDirectoryPath;
        }

        return Path.Combine(WinPeRoot, "Logs");
    }

    private static string ResolveFileName(string preferredFileName, string sourceUrl)
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

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "item";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }

    private sealed record StepExecutionOutcome
    {
        public required DeploymentStepState State { get; init; }
        public required string Message { get; init; }
        public DeploymentLogSession? RebindLogSession { get; init; }

        public static StepExecutionOutcome Succeeded(string message, DeploymentLogSession? rebindLogSession = null)
            => new() { State = DeploymentStepState.Succeeded, Message = message, RebindLogSession = rebindLogSession };

        public static StepExecutionOutcome Skipped(string message, DeploymentLogSession? rebindLogSession = null)
            => new() { State = DeploymentStepState.Skipped, Message = message, RebindLogSession = rebindLogSession };

        public static StepExecutionOutcome Failed(string message, DeploymentLogSession? rebindLogSession = null)
            => new() { State = DeploymentStepState.Failed, Message = message, RebindLogSession = rebindLogSession };
    }

    private sealed class CacheIndexDocument
    {
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<CacheIndexEntry> Items { get; set; } = [];
    }

    private sealed class CacheIndexEntry
    {
        public string ArtifactType { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string ExpectedHash { get; set; } = string.Empty;
        public DateTimeOffset LastUpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
