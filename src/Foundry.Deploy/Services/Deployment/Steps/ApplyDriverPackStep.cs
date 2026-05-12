using System.IO;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ApplyDriverPackStep : DeploymentStepBase
{
    private const int FileCopyBufferSize = 80 * 1024;

    private readonly IWindowsDeploymentService _windowsDeploymentService;
    private readonly IPreOobeScriptProvisioningService _preOobeScriptProvisioningService;
    private readonly IDriverPackStrategyResolver _driverPackStrategyResolver;

    public ApplyDriverPackStep(
        IWindowsDeploymentService windowsDeploymentService,
        IPreOobeScriptProvisioningService preOobeScriptProvisioningService,
        IDriverPackStrategyResolver driverPackStrategyResolver)
    {
        _windowsDeploymentService = windowsDeploymentService;
        _preOobeScriptProvisioningService = preOobeScriptProvisioningService;
        _driverPackStrategyResolver = driverPackStrategyResolver;
    }

    public override int Order => 12;

    public override string Name => DeploymentStepNames.ApplyDriverPack;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        return context.RuntimeState.DriverPackInstallMode switch
        {
            DriverPackInstallMode.None => DeploymentStepResult.Skipped("No driver pack operation is required."),
            DriverPackInstallMode.OfflineInf => await ApplyOfflineInfAsync(context, cancellationToken).ConfigureAwait(false),
            DriverPackInstallMode.DeferredSetupComplete => await ApplyDeferredAsync(context, cancellationToken).ConfigureAwait(false),
            _ => DeploymentStepResult.Failed("Unsupported driver pack install mode.")
        };
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        return context.RuntimeState.DriverPackInstallMode switch
        {
            DriverPackInstallMode.None => DeploymentStepResult.Skipped("No driver pack operation is required."),
            DriverPackInstallMode.OfflineInf => await SimulateOfflineApplyAsync(context, cancellationToken).ConfigureAwait(false),
            DriverPackInstallMode.DeferredSetupComplete => await SimulateDeferredApplyAsync(context, cancellationToken).ConfigureAwait(false),
            _ => DeploymentStepResult.Failed("Unsupported driver pack install mode.")
        };
    }

    private async Task<DeploymentStepResult> ApplyOfflineInfAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string driverRoot = context.RuntimeState.ExtractedDriverPackPath ?? string.Empty;
        if (!Directory.Exists(driverRoot))
        {
            return DeploymentStepResult.Failed("No extracted INF driver payload is available.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");
        const string stepMessage = "Applying driver pack...";

        bool applyRecovery = context.RuntimeState.WinReConfigured &&
                             !string.IsNullOrWhiteSpace(context.RuntimeState.TargetRecoveryPartitionRoot);

        context.EmitCurrentStepIndeterminate(stepMessage, "Applying Windows drivers...");
        IProgress<double> windowsProgress = context.CreateStepPercentProgressReporter(stepMessage, "Applying Windows drivers");

        await _windowsDeploymentService
            .ApplyOfflineDriversAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                driverRoot,
                scratchDirectory,
                workingDirectory,
                cancellationToken,
                windowsProgress)
            .ConfigureAwait(false);

        if (applyRecovery)
        {
            IProgress<double> mountRecoveryProgress = context.CreateStepPercentProgressReporter(stepMessage, "Mounting WinRE");
            IProgress<double> applyRecoveryProgress = context.CreateStepPercentProgressReporter(stepMessage, "Applying WinRE drivers");
            IProgress<double> unmountRecoveryProgress = context.CreateStepPercentProgressReporter(stepMessage, "Unmounting WinRE");

            await _windowsDeploymentService
                .ApplyRecoveryDriversAsync(
                    context.RuntimeState.TargetRecoveryPartitionRoot!,
                    driverRoot,
                    scratchDirectory,
                    workingDirectory,
                    cancellationToken,
                    mountProgress: mountRecoveryProgress,
                    applyProgress: applyRecoveryProgress,
                    unmountProgress: unmountRecoveryProgress,
                    onMountStarted: () => context.EmitCurrentStepIndeterminate(stepMessage, "Mounting WinRE..."),
                    onApplyStarted: () => context.EmitCurrentStepIndeterminate(stepMessage, "Applying WinRE drivers..."),
                    onUnmountStarted: () => context.EmitCurrentStepIndeterminate(stepMessage, "Unmounting WinRE..."))
                .ConfigureAwait(false);
        }

        int infCount = Directory.EnumerateFiles(driverRoot, "*.inf", SearchOption.AllDirectories).Count();

        string driverMessage = applyRecovery
            ? $"Driver pack applied offline to Windows and WinRE: {infCount} INF files from '{driverRoot}'."
            : $"Driver pack applied offline: {infCount} INF files from '{driverRoot}'.";
        await context.AppendLogAsync(DeploymentLogLevel.Info, driverMessage, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack applied.");
    }

    private async Task<DeploymentStepResult> ApplyDeferredAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string sourcePath = context.RuntimeState.DownloadedDriverPackPath ?? string.Empty;
        if (!File.Exists(sourcePath))
        {
            return DeploymentStepResult.Failed("Driver pack source payload is unavailable for deferred staging.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        DriverPackExecutionPlan executionPlan = _driverPackStrategyResolver.Resolve(
            context.Request.DriverPackSelectionKind,
            context.Request.DriverPack,
            sourcePath);

        if (executionPlan.DeferredCommandKind == DeferredDriverPackageCommandKind.None)
        {
            return DeploymentStepResult.Failed("Deferred driver pack staging was requested without a supported deferred command.");
        }

        IProgress<double> stepProgress = context.CreateStepPercentProgressReporter("Applying driver pack...", "Staging");
        string packagesDirectory = Path.Combine(
            context.RuntimeState.TargetWindowsPartitionRoot,
            "Windows",
            "Temp",
            "Foundry",
            "DriverPack",
            "Packages");
        string packageFileName = Path.GetFileName(sourcePath);
        string targetPackagePath = Path.Combine(packagesDirectory, packageFileName);
        string runtimePackagePath = Path.Combine(
            "%SystemRoot%",
            "Temp",
            "Foundry",
            "DriverPack",
            "Packages",
            packageFileName);
        context.EmitCurrentStepIndeterminate("Applying driver pack...", "Staging package...");
        await CopyFileWithProgressAsync(
                sourcePath,
                targetPackagePath,
                new Progress<double>(percent => stepProgress.Report(MapProgress(percent, 0d, 60d))),
                cancellationToken)
            .ConfigureAwait(false);

        context.EmitCurrentStepIndeterminate("Applying driver pack...", "Updating SetupComplete hook...");
        PreOobeScriptProvisioningResult preOobeResult = _preOobeScriptProvisioningService.Provision(
            context.RuntimeState.TargetWindowsPartitionRoot,
            [
                new PreOobeScriptDefinition
                {
                    Id = "driver-pack",
                    FileName = "Install-DriverPack.ps1",
                    ResourceName = PreOobeScriptResources.InstallDriverPack,
                    Priority = PreOobeScriptPriority.DriverProvisioning,
                    Arguments =
                    [
                        "-CommandKind",
                        executionPlan.DeferredCommandKind.ToString(),
                        "-PackagePath",
                        runtimePackagePath
                    ]
                }
            ]);

        context.RuntimeState.DeferredDriverPackagePath = targetPackagePath;
        context.RuntimeState.DriverPackSetupCompleteHookPath = preOobeResult.SetupCompletePath;
        context.RuntimeState.PreOobeSetupCompletePath = preOobeResult.SetupCompletePath;
        context.RuntimeState.PreOobeRunnerPath = preOobeResult.RunnerPath;
        context.RuntimeState.PreOobeManifestPath = preOobeResult.ManifestPath;
        context.RuntimeState.PreOobeScriptPaths = preOobeResult.StagedScriptPaths;

        await context.AppendLogAsync(
            DeploymentLogLevel.Warning,
            $"Driver pack staged for first boot: '{targetPackagePath}'. SetupComplete hook: '{preOobeResult.SetupCompletePath}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack staged for first boot.");
    }

    private static async Task<DeploymentStepResult> SimulateOfflineApplyAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string driverRoot = context.RuntimeState.ExtractedDriverPackPath ?? string.Empty;
        if (!Directory.Exists(driverRoot))
        {
            return DeploymentStepResult.Failed("No extracted INF driver payload is available.");
        }

        int infCount = Directory.EnumerateFiles(driverRoot, "*.inf", SearchOption.AllDirectories).Count();
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            context.RuntimeState.WinReConfigured
                ? $"[DRY-RUN] Simulated offline driver pack apply to Windows and WinRE: {infCount} INF files."
                : $"[DRY-RUN] Simulated offline driver pack apply: {infCount} INF files.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack applied (simulation).");
    }

    private static async Task<DeploymentStepResult> SimulateDeferredApplyAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string sourcePath = context.RuntimeState.DownloadedDriverPackPath ?? string.Empty;
        if (!File.Exists(sourcePath))
        {
            return DeploymentStepResult.Failed("Driver pack source payload is unavailable for deferred staging.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        string packagesDirectory = Path.Combine(
            context.RuntimeState.TargetWindowsPartitionRoot,
            "Windows",
            "Temp",
            "Foundry",
            "DriverPack",
            "Packages");
        Directory.CreateDirectory(packagesDirectory);

        context.RuntimeState.DeferredDriverPackagePath = Path.Combine(packagesDirectory, Path.GetFileName(sourcePath));
        context.RuntimeState.DriverPackSetupCompleteHookPath = Path.Combine(
            context.RuntimeState.TargetWindowsPartitionRoot,
            "Windows",
            "Setup",
            "Scripts",
            "SetupComplete.cmd");
        context.RuntimeState.PreOobeSetupCompletePath = context.RuntimeState.DriverPackSetupCompleteHookPath;
        context.RuntimeState.PreOobeRunnerPath = Path.Combine(
            context.RuntimeState.TargetWindowsPartitionRoot,
            "Windows",
            "Temp",
            "Foundry",
            "PreOobe",
            "Invoke-FoundryPreOobe.ps1");
        context.RuntimeState.PreOobeManifestPath = Path.Combine(
            context.RuntimeState.TargetWindowsPartitionRoot,
            "Windows",
            "Temp",
            "Foundry",
            "PreOobe",
            "pre-oobe-manifest.json");
        context.RuntimeState.PreOobeScriptPaths =
        [
            Path.Combine(
                context.RuntimeState.TargetWindowsPartitionRoot,
                "Windows",
                "Temp",
                "Foundry",
                "PreOobe",
                "Scripts",
                "Install-DriverPack.ps1")
        ];

        await context.AppendLogAsync(
            DeploymentLogLevel.Warning,
            $"[DRY-RUN] Simulated deferred driver pack staging: '{context.RuntimeState.DeferredDriverPackagePath}'.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack applied (simulation).");
    }

    private static async Task CopyFileWithProgressAsync(
        string sourcePath,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("Unable to resolve the destination directory for deferred driver staging.");
        }

        Directory.CreateDirectory(destinationDirectory);

        long totalBytes = new FileInfo(sourcePath).Length;
        long copiedBytes = 0;
        progress?.Report(0d);

        await using FileStream sourceStream = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileCopyBufferSize,
            useAsync: true);
        await using FileStream destinationStream = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            FileCopyBufferSize,
            useAsync: true);

        byte[] buffer = new byte[FileCopyBufferSize];
        while (true)
        {
            int bytesRead = await sourceStream
                .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            await destinationStream
                .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                .ConfigureAwait(false);

            copiedBytes += bytesRead;
            if (totalBytes > 0)
            {
                double percent = (double)copiedBytes / totalBytes * 100d;
                progress?.Report(percent);
            }
        }

        progress?.Report(100d);
    }

    private static double MapProgress(double percent, double start, double end)
    {
        double normalized = Math.Clamp(percent, 0d, 100d);
        return start + (normalized / 100d * (end - start));
    }

}
