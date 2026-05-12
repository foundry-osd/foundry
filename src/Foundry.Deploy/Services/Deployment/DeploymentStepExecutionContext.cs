using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Provides shared state, logging, workspace paths, and progress helpers to deployment steps.
/// </summary>
public sealed class DeploymentStepExecutionContext
{
    private const string WinPeRoot = @"X:\Foundry";
    private static readonly string WinPeDriveRoot = Path.GetPathRoot(WinPeRoot) ?? @"X:\";
    private const string LogsFolderName = "Logs";
    private const string TempFolderName = "Temp";
    private const string StateFolderName = "State";
    private const string RuntimeFolderName = "Runtime";
    private const string CacheFolderName = "Cache";
    private const string OperatingSystemsFolderName = "OperatingSystems";
    private const string DriverPacksFolderName = "DriverPacks";
    private const string DryRunWorkspaceFolderName = "DryRun";
    private const string RuntimeWorkspaceFolderName = "Runtime";
    private const long UnknownTotalDownloadProgressIncrementBytes = 16L * 1024 * 1024;

    private readonly IOperationProgressService _operationProgressService;
    private readonly IDeploymentLogService _deploymentLogService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly Action<DeploymentStepProgress> _emitStepProgress;

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
        Action<DeploymentStepProgress> emitStepProgress)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        RuntimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        PlannedSteps = plannedSteps ?? throw new ArgumentNullException(nameof(plannedSteps));
        _operationProgressService = operationProgressService ?? throw new ArgumentNullException(nameof(operationProgressService));
        _deploymentLogService = deploymentLogService ?? throw new ArgumentNullException(nameof(deploymentLogService));
        _targetDiskService = targetDiskService ?? throw new ArgumentNullException(nameof(targetDiskService));
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
    public IReadOnlyList<string> PlannedSteps { get; }

    /// <summary>
    /// Gets the active deployment log session.
    /// </summary>
    public DeploymentLogSession LogSession { get; private set; }

    public int StepIndex { get; private set; }

    public int StepCount { get; private set; }

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

    public void SetCurrentStep(IDeploymentStep step, int stepIndex)
    {
        ArgumentNullException.ThrowIfNull(step);

        StepName = step.Name;
        StepIndex = stepIndex;
        StepCount = PlannedSteps.Count;
        RuntimeState.CurrentStep = step.Name;
    }

    public void EmitCurrentStep(
        DeploymentStepState state,
        string? message,
        double? stepSubProgressPercent = null,
        bool stepSubProgressIndeterminate = true,
        string? stepSubProgressLabel = null)
    {
        int progressPercent = CalculateStepProgressPercent(StepIndex, StepCount);
        _emitStepProgress(new DeploymentStepProgress
        {
            StepName = StepName,
            State = state,
            StepIndex = StepIndex,
            StepCount = StepCount,
            ProgressPercent = progressPercent,
            Message = message,
            StepSubProgressPercent = stepSubProgressPercent,
            StepSubProgressIndeterminate = stepSubProgressIndeterminate,
            StepSubProgressLabel = stepSubProgressLabel
        });
    }

    public void EmitCurrentStepIndeterminate(string stepMessage, string stepSubProgressLabel)
    {
        EmitCurrentStep(
            DeploymentStepState.Running,
            stepMessage,
            stepSubProgressPercent: null,
            stepSubProgressIndeterminate: true,
            stepSubProgressLabel: stepSubProgressLabel);
    }

    public void ReportCurrentStepProgress(string message)
    {
        _operationProgressService.Report(CalculateStepProgressPercent(StepIndex, StepCount), message);
    }

    public Task AppendLogAsync(
        DeploymentLogLevel level,
        string message,
        CancellationToken cancellationToken = default)
    {
        return _deploymentLogService.AppendAsync(LogSession, level, message, cancellationToken);
    }

    public Task SaveRuntimeStateAsync(CancellationToken cancellationToken = default)
    {
        return _deploymentLogService.SaveStateAsync(LogSession, RuntimeState, cancellationToken);
    }

    public async Task RebindLogSessionToTargetAsync(
        string targetFoundryRoot,
        CancellationToken cancellationToken = default)
    {
        DeploymentLogSession previousSession = LogSession;
        DeploymentLogSession rebound = _deploymentLogService.Initialize(targetFoundryRoot);
        CopyDirectoryContents(previousSession.LogsDirectoryPath, rebound.LogsDirectoryPath);
        CopyDirectoryContents(previousSession.StateDirectoryPath, rebound.StateDirectoryPath);

        await _deploymentLogService
            .AppendAsync(
                rebound,
                DeploymentLogLevel.Info,
                $"Log session transferred from '{previousSession.RootPath}' to '{targetFoundryRoot}'.",
                cancellationToken)
            .ConfigureAwait(false);

        LogSession = rebound;
        if (!previousSession.LogFilePath.Equals(rebound.LogFilePath, StringComparison.OrdinalIgnoreCase))
        {
            _deploymentLogService.Release(previousSession);
        }
    }

    public async Task<(TargetDiskInfo? SelectedDisk, DeploymentStepResult? Failure)> TryGetValidatedTargetDiskAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TargetDiskInfo> disks = await _targetDiskService.GetDisksAsync(cancellationToken).ConfigureAwait(false);
        TargetDiskInfo? selectedDisk = disks.FirstOrDefault(disk => disk.DiskNumber == Request.TargetDiskNumber);
        if (selectedDisk is null)
        {
            return (null, DeploymentStepResult.Failed($"Target disk {Request.TargetDiskNumber} is no longer present."));
        }

        if (!selectedDisk.IsSelectable)
        {
            return (null, DeploymentStepResult.Failed(
                $"Target disk {Request.TargetDiskNumber} is blocked: {selectedDisk.SelectionWarning}"));
        }

        await AppendLogAsync(DeploymentLogLevel.Info, $"Target disk revalidated: {selectedDisk.DisplayLabel}", cancellationToken).ConfigureAwait(false);
        return (selectedDisk, null);
    }

    public void EnsureWorkspaceFolders()
    {
        EnsureWorkspaceFolders(RuntimeState.WorkspaceRoot);
    }

    public string ResolveWorkspaceLogsPath()
    {
        return Path.Combine(ResolveWorkspaceRoot(RuntimeState), LogsFolderName);
    }

    public string ResolveWorkspaceTempPath(params string[] relativeSegments)
    {
        string currentPath = Path.Combine(ResolveWorkspaceRoot(RuntimeState), TempFolderName);
        foreach (string segment in relativeSegments)
        {
            currentPath = Path.Combine(currentPath, segment);
        }

        return currentPath;
    }

    public string ResolveOperatingSystemCacheRoot()
    {
        if (RuntimeState.Mode == DeploymentMode.Iso &&
            !string.IsNullOrWhiteSpace(RuntimeState.TargetFoundryRoot))
        {
            return Path.Combine(RuntimeState.TargetFoundryRoot, CacheFolderName, OperatingSystemsFolderName);
        }

        return Path.Combine(EnsureCacheBaseRoot(), CacheFolderName, OperatingSystemsFolderName);
    }

    public string ResolveDriverPackCacheRoot()
    {
        if (RuntimeState.Mode == DeploymentMode.Iso &&
            !string.IsNullOrWhiteSpace(RuntimeState.TargetFoundryRoot))
        {
            return Path.Combine(RuntimeState.TargetFoundryRoot, CacheFolderName, DriverPacksFolderName);
        }

        return Path.Combine(EnsureCacheBaseRoot(), CacheFolderName, DriverPacksFolderName);
    }

    public string EnsureTargetFoundryRoot()
    {
        return RuntimeState.TargetFoundryRoot
            ?? throw new InvalidOperationException("Target Foundry root is unavailable.");
    }

    public IProgress<DownloadProgress> CreateDownloadProgressReporter(string artifactLabel)
    {
        double? lastReportedPercent = null;
        long nextUnknownTotalReportThreshold = 0;

        return new DelegateProgress<DownloadProgress>(progress =>
        {
            string details;
            double? stepSubProgressPercent = null;
            bool stepSubProgressIndeterminate = true;

            if (progress.TotalBytes is long totalBytes && totalBytes > 0)
            {
                double percent = CalculateDownloadPercent(progress.BytesDownloaded, totalBytes);
                bool isFinal = progress.BytesDownloaded >= totalBytes;
                if (!isFinal &&
                    lastReportedPercent.HasValue &&
                    percent <= lastReportedPercent.Value)
                {
                    return;
                }

                lastReportedPercent = percent;
                details = $"{percent:0.#}% ({FormatByteSize(progress.BytesDownloaded)} / {FormatByteSize(totalBytes)})";
                stepSubProgressPercent = percent;
                stepSubProgressIndeterminate = false;
            }
            else
            {
                bool shouldReport = progress.BytesDownloaded == 0 ||
                                    progress.BytesDownloaded >= nextUnknownTotalReportThreshold;
                if (!shouldReport)
                {
                    return;
                }

                nextUnknownTotalReportThreshold = progress.BytesDownloaded + UnknownTotalDownloadProgressIncrementBytes;
                details = $"{FormatByteSize(progress.BytesDownloaded)} downloaded";
            }

            string stepMessage = $"Downloading {artifactLabel}...";
            ReportCurrentStepProgress(stepMessage);
            EmitCurrentStep(
                DeploymentStepState.Running,
                stepMessage,
                stepSubProgressPercent,
                stepSubProgressIndeterminate,
                details);
        });
    }

    public IProgress<double> CreateStepPercentProgressReporter(string stepMessage, string stepLabelPrefix)
    {
        object progressSync = new();
        double lastReportedPercent = double.NaN;

        return new DelegateProgress<double>(percent =>
        {
            double normalized = Math.Clamp(percent, 0d, 100d);
            lock (progressSync)
            {
                if (!double.IsNaN(lastReportedPercent) && normalized <= lastReportedPercent)
                {
                    return;
                }

                lastReportedPercent = normalized;
            }

            EmitCurrentStep(
                DeploymentStepState.Running,
                stepMessage,
                stepSubProgressPercent: normalized,
                stepSubProgressIndeterminate: false,
                stepSubProgressLabel: $"{stepLabelPrefix}: {normalized:0.#}%");
        });
    }

    public static string ResolvePreferredHash(string? primaryHash, string? secondaryHash)
    {
        if (!string.IsNullOrWhiteSpace(primaryHash))
        {
            return primaryHash.Trim();
        }

        return secondaryHash?.Trim() ?? string.Empty;
    }

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

    private static double CalculateDownloadPercent(long bytesDownloaded, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 0d;
        }

        if (bytesDownloaded >= totalBytes)
        {
            return 100d;
        }

        if (bytesDownloaded <= 0)
        {
            return 0d;
        }

        return Math.Clamp((double)bytesDownloaded / totalBytes * 100d, 0d, 100d);
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

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) ||
            !Directory.Exists(sourceDirectory) ||
            sourceDirectory.Equals(destinationDirectory, StringComparison.OrdinalIgnoreCase))
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
