using System.IO;
using System.Text.Json;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;

namespace Foundry.Deploy.Services.Deployment;

public sealed class DeploymentOrchestrator : IDeploymentOrchestrator
{
    private static readonly string[] Steps =
    [
        "Initialize deployment workspace",
        "Validate target configuration",
        "Resolve cache strategy",
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
    private readonly IDriverPackCatalogService _driverPackCatalogService;
    private readonly IDriverPackSelectionService _driverPackSelectionService;
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
        IDriverPackCatalogService driverPackCatalogService,
        IDriverPackSelectionService driverPackSelectionService,
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
        _driverPackCatalogService = driverPackCatalogService;
        _driverPackSelectionService = driverPackSelectionService;
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
                Message = "Another operation is already running."
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
            OperatingSystemUrl = context.OperatingSystem.Url
        };

        try
        {
            EmitLog($"[INFO] Deployment mode: {context.Mode}");
            EmitLog($"[INFO] Cache root: {context.CacheRootPath}");
            EmitLog($"[INFO] Target disk number: {context.TargetDiskNumber}");
            EmitLog($"[INFO] OS: {context.OperatingSystem.DisplayLabel}");
            EmitLog($"[INFO] Driver pack: {(context.DriverPack?.DisplayLabel ?? "None")}");
            EmitLog($"[INFO] Autopilot mode: {(context.UseFullAutopilot ? "Full" : "Disabled")}");
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
            return new DeploymentResult
            {
                IsSuccess = true,
                Message = "Deployment orchestration completed."
            };
        }
        catch (OperationCanceledException)
        {
            _operationProgressService.Fail("Deployment cancelled.");
            await AppendLogAsync(logSession, DeploymentLogLevel.Warning, "[WARN] Deployment cancelled by user.", cancellationToken).ConfigureAwait(false);
            return new DeploymentResult
            {
                IsSuccess = false,
                Message = "Deployment cancelled."
            };
        }
        catch (Exception ex)
        {
            _operationProgressService.Fail("Deployment failed.");
            await AppendLogAsync(logSession, DeploymentLogLevel.Error, $"[ERROR] {ex.Message}", cancellationToken).ConfigureAwait(false);
            return new DeploymentResult
            {
                IsSuccess = false,
                Message = ex.Message
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
                    DeploymentLogSession session = InitializeLogSessionWithFallback(context.CacheRootPath);
                    await _deploymentLogService
                        .AppendAsync(session, DeploymentLogLevel.Info, $"Log session initialized at '{session.RootPath}'.", cancellationToken)
                        .ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Workspace initialized.", session);
                }

            case "Validate target configuration":
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
                    EnsureCacheFolders(cache.RootPath);

                    DeploymentLogSession? rebound = logSession;
                    if (logSession is null || !cache.RootPath.Equals(logSession.RootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        rebound = _deploymentLogService.Initialize(cache.RootPath);
                        await _deploymentLogService.AppendAsync(rebound, DeploymentLogLevel.Info, "Log session rebound to resolved cache root.", cancellationToken).ConfigureAwait(false);
                    }

                    return StepExecutionOutcome.Succeeded("Cache strategy resolved.", rebound);
                }

            case "Download operating system image":
                {
                    string cacheRoot = EnsureResolvedCache(runtimeState);
                    string osDirectory = Path.Combine(cacheRoot, "OS");
                    Directory.CreateDirectory(osDirectory);

                    string fileName = ResolveFileName(context.OperatingSystem.FileName, context.OperatingSystem.Url);
                    string destinationPath = Path.Combine(osDirectory, fileName);
                    ArtifactDownloadResult result = await _artifactDownloadService
                        .DownloadAsync(context.OperatingSystem.Url, destinationPath, expectedSha256: null, preferBits: true, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.DownloadedOperatingSystemPath = result.DestinationPath;
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"OS image {(result.Downloaded ? "downloaded" : "reused")} via {result.Method}: {result.DestinationPath}", cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Operating system image ready.");
                }

            case "Download and prepare driver pack":
                {
                    string cacheRoot = EnsureResolvedCache(runtimeState);
                    DriverPackCatalogItem? driverPack = context.DriverPack;

                    if (driverPack is null && context.AutoSelectDriverPackWhenEmpty)
                    {
                        IReadOnlyList<DriverPackCatalogItem> catalog = await _driverPackCatalogService.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
                        HardwareProfile hardware = runtimeState.HardwareProfile ?? await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                        DriverPackSelectionResult selection = _driverPackSelectionService.SelectBest(catalog, hardware, context.OperatingSystem);
                        driverPack = selection.DriverPack;
                        await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"Driver auto-selection: {selection.SelectionReason}", cancellationToken).ConfigureAwait(false);
                    }

                    if (driverPack is null)
                    {
                        return StepExecutionOutcome.Skipped("No driver pack selected.");
                    }

                    runtimeState.DriverPackName = driverPack.Name;
                    runtimeState.DriverPackUrl = driverPack.DownloadUrl;

                    string driverPackDirectory = Path.Combine(cacheRoot, "DriverPacks", SanitizePathSegment(driverPack.Manufacturer));
                    Directory.CreateDirectory(driverPackDirectory);
                    string archiveName = ResolveFileName(driverPack.FileName, driverPack.DownloadUrl);
                    string archivePath = Path.Combine(driverPackDirectory, archiveName);

                    ArtifactDownloadResult download = await _artifactDownloadService
                        .DownloadAsync(driverPack.DownloadUrl, archivePath, driverPack.Sha256, preferBits: true, cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.DownloadedDriverPackPath = download.DestinationPath;

                    string extractionRoot = Path.Combine(cacheRoot, "Extracted", "Drivers");
                    DriverPackPreparationResult preparation = await _driverPackPreparationService
                        .PrepareAsync(driverPack, download.DestinationPath, extractionRoot, cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.PreparedDriverPath = preparation.ExtractedDirectoryPath;
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, preparation.Message, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Driver pack prepared.");
                }

            case "Apply operating system image":
                {
                    string cacheRoot = EnsureResolvedCache(runtimeState);
                    string imagePath = runtimeState.DownloadedOperatingSystemPath ?? string.Empty;
                    if (!File.Exists(imagePath))
                    {
                        return StepExecutionOutcome.Failed("Operating system image was not downloaded.");
                    }

                    string workingDirectory = Path.Combine(cacheRoot, "Temp", "Deployment");
                    Directory.CreateDirectory(workingDirectory);

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

                    DeploymentTargetLayout layout = await _windowsDeploymentService
                        .PrepareTargetDiskAsync(context.TargetDiskNumber, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.TargetSystemPartitionRoot = layout.SystemPartitionRoot;
                    runtimeState.TargetWindowsPartitionRoot = layout.WindowsPartitionRoot;

                    int imageIndex = await _windowsDeploymentService
                        .ResolveImageIndexAsync(imagePath, context.OperatingSystem.Edition, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.AppliedImageIndex = imageIndex;

                    string scratchDirectory = Path.Combine(cacheRoot, "Temp", "Dism");
                    await _windowsDeploymentService
                        .ApplyImageAsync(imagePath, imageIndex, layout.WindowsPartitionRoot, scratchDirectory, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);

                    await _windowsDeploymentService
                        .ConfigureBootAsync(layout.WindowsPartitionRoot, layout.SystemPartitionRoot, workingDirectory, cancellationToken)
                        .ConfigureAwait(false);

                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"OS image applied to {layout.WindowsPartitionRoot} (index {imageIndex}); boot configured on {layout.SystemPartitionRoot}.", cancellationToken).ConfigureAwait(false);
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

                    string cacheRoot = EnsureResolvedCache(runtimeState);
                    string workingDirectory = Path.Combine(cacheRoot, "Temp", "Deployment");
                    string scratchDirectory = Path.Combine(cacheRoot, "Temp", "Dism");

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

                    string cacheRoot = EnsureResolvedCache(runtimeState);
                    if (string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot))
                    {
                        return StepExecutionOutcome.Failed("Target Windows partition is unavailable for Autopilot artifacts.");
                    }

                    HardwareProfile hardware = runtimeState.HardwareProfile ?? await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
                    AutopilotExecutionResult result = await _autopilotService
                        .ExecuteFullWorkflowAsync(
                            cacheRoot,
                            runtimeState.TargetWindowsPartitionRoot,
                            hardware,
                            context.OperatingSystem,
                            cancellationToken)
                        .ConfigureAwait(false);

                    runtimeState.AutopilotWorkflowPath = result.WorkflowManifestPath;
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"Autopilot transcript: {result.TranscriptPath}", cancellationToken).ConfigureAwait(false);

                    if (!result.IsSuccess)
                    {
                        return StepExecutionOutcome.Failed(result.Message);
                    }

                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, result.Message, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Autopilot workflow completed.");
                }

            case "Finalize deployment and write logs":
                {
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, "Finalize step completed.", cancellationToken).ConfigureAwait(false);
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
                    DeploymentLogSession session = InitializeLogSessionWithFallback(context.CacheRootPath);
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
                    EnsureCacheFolders(cache.RootPath);
                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Cache resolved: {cache.RootPath} ({cache.Source})", cancellationToken).ConfigureAwait(false);

                    DeploymentLogSession? rebound = logSession;
                    if (logSession is null || !cache.RootPath.Equals(logSession.RootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        rebound = _deploymentLogService.Initialize(cache.RootPath);
                        await _deploymentLogService.AppendAsync(rebound, DeploymentLogLevel.Info, "Debug safe mode log session rebound.", cancellationToken).ConfigureAwait(false);
                    }

                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Cache strategy resolved (simulation).", rebound);
                }

            case "Download operating system image":
                {
                    string cacheRoot = EnsureResolvedCache(runtimeState);
                    string osDirectory = Path.Combine(cacheRoot, "OS");
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
                    string cacheRoot = EnsureResolvedCache(runtimeState);
                    DriverPackCatalogItem? driverPack = context.DriverPack;
                    if (driverPack is null)
                    {
                        await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                        return StepExecutionOutcome.Skipped("No driver pack selected.");
                    }

                    runtimeState.DriverPackName = driverPack.Name;
                    runtimeState.DriverPackUrl = driverPack.DownloadUrl;

                    string driverRoot = Path.Combine(cacheRoot, "Extracted", "Drivers", "dry-run");
                    Directory.CreateDirectory(driverRoot);
                    string infPath = Path.Combine(driverRoot, "dryrun.inf");
                    await File.WriteAllTextAsync(infPath, "; dry-run only", cancellationToken).ConfigureAwait(false);
                    runtimeState.PreparedDriverPath = driverRoot;

                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Driver payload simulated: {driverRoot}", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    return StepExecutionOutcome.Succeeded("Driver pack prepared (simulation).");
                }

            case "Apply operating system image":
                {
                    string cacheRoot = EnsureResolvedCache(runtimeState);
                    string targetRoot = Path.Combine(cacheRoot, "DryRunTarget");
                    string systemRoot = Path.Combine(targetRoot, "System");
                    string windowsRoot = Path.Combine(targetRoot, "Windows");
                    Directory.CreateDirectory(systemRoot);
                    Directory.CreateDirectory(windowsRoot);

                    runtimeState.TargetSystemPartitionRoot = systemRoot;
                    runtimeState.TargetWindowsPartitionRoot = windowsRoot;
                    runtimeState.AppliedImageIndex = 1;

                    await File.WriteAllTextAsync(
                        Path.Combine(targetRoot, "apply-image.log"),
                        $"Dry-run image apply at {DateTimeOffset.UtcNow:O}{Environment.NewLine}OS={context.OperatingSystem.DisplayLabel}",
                        cancellationToken).ConfigureAwait(false);

                    await AppendLogAsync(logSession, DeploymentLogLevel.Info, $"[DRY-RUN] Simulated OS apply to {windowsRoot}.", cancellationToken).ConfigureAwait(false);
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

                    string cacheRoot = EnsureResolvedCache(runtimeState);
                    string autopilotRoot = Path.Combine(cacheRoot, "Autopilot");
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

    private static void EnsureCacheFolders(string cacheRoot)
    {
        string[] folders =
        [
            "OS",
            "DriverPacks",
            Path.Combine("Extracted", "Drivers"),
            "Logs",
            "State",
            Path.Combine("Temp", "Deployment"),
            Path.Combine("Temp", "Dism"),
            "Autopilot"
        ];

        foreach (string folder in folders)
        {
            Directory.CreateDirectory(Path.Combine(cacheRoot, folder));
        }
    }

    private DeploymentLogSession InitializeLogSessionWithFallback(string preferredRootPath)
    {
        try
        {
            return _deploymentLogService.Initialize(preferredRootPath);
        }
        catch
        {
            string fallbackRoot = ResolveTransientCacheRoot("LogFallback");
            return _deploymentLogService.Initialize(fallbackRoot);
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

        string safeRoot = ResolveTransientCacheRoot("IsoConflict");
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

    private static string ResolveTransientCacheRoot(string suffix)
    {
        string candidate = Path.Combine(@"X:\Windows\Temp\Foundry\Deploy", suffix);
        try
        {
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        catch
        {
            string fallback = Path.Combine(Path.GetTempPath(), "Foundry", "Deploy", suffix);
            Directory.CreateDirectory(fallback);
            return fallback;
        }
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
}
