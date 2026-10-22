// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Drivers;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Prepares the INF folder that the offline apply steps inject: extracts a downloaded pack, or stages the selected custom driver set
/// from the boot media.
/// </summary>
public sealed class ExtractDriverPackStep : DeploymentStepBase
{
    private const string CustomDriversUnavailableMessage = "The selected custom drivers are unavailable or failed verification.";
    private readonly IDriverPackStrategyResolver _driverPackStrategyResolver;
    private readonly IDriverPackExtractionService _driverPackExtractionService;
    private readonly ICustomDriverSetStagingService _customDriverSetStagingService;

    public ExtractDriverPackStep(
        IDriverPackStrategyResolver driverPackStrategyResolver,
        IDriverPackExtractionService driverPackExtractionService,
        ICustomDriverSetStagingService? customDriverSetStagingService = null)
    {
        _driverPackStrategyResolver = driverPackStrategyResolver;
        _driverPackExtractionService = driverPackExtractionService;
        _customDriverSetStagingService = customDriverSetStagingService ?? new CustomDriverSetStagingService();
    }

    public override string Name => DeploymentStepNames.ExtractDriverPack;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.None)
        {
            return DeploymentStepResult.Skipped("Driver pack disabled (None selected).");
        }

        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.CustomDriverSet)
        {
            return await StageCustomDriverSetAsync(context, cancellationToken).ConfigureAwait(false);
        }

        string downloadedPath = context.RuntimeState.DownloadedDriverPackPath ?? string.Empty;
        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
        {
            if (context.RuntimeState.MicrosoftUpdateCatalogDriverPaths.Count == 0)
            {
                return DeploymentStepResult.Skipped("Microsoft Update Catalog did not produce a driver payload.");
            }
        }
        else if (!PathExists(downloadedPath))
        {
            return CreateMissingDriverPackFailure();
        }

        DriverPackExecutionPlan executionPlan = _driverPackStrategyResolver.Resolve(
            context.Request.DriverPackSelectionKind,
            context.Request.DriverPack,
            downloadedPath) with
        {
            MicrosoftUpdateCatalogDriverPaths = context.RuntimeState.MicrosoftUpdateCatalogDriverPaths
        };
        if (executionPlan.InstallMode != DriverPackInstallMode.OfflineInf)
        {
            return DeploymentStepResult.Skipped("No driver pack operation is required.");
        }

        string extractionRoot = Path.Combine(context.EnsureTargetFoundryRoot(), "Extracted", "Drivers");
        IProgress<double> progress = context.CreateStepPercentProgressReporter("Extracting driver pack...", "Extracting");
        progress.Report(0d);

        DriverPackExtractionResult result = await _driverPackExtractionService
            .ExtractAsync(executionPlan, extractionRoot, cancellationToken, progress)
            .ConfigureAwait(false);

        context.RuntimeState.DriverPackInstallMode = result.ExecutionPlan.InstallMode;
        context.RuntimeState.DriverPackExtractionMethod = result.ExecutionPlan.ExtractionMethod.ToString();
        context.RuntimeState.ExtractedDriverPackPath = result.ExtractedDirectoryPath;

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Driver pack strategy resolved: {result.ExecutionPlan.InstallMode} via {result.ExecutionPlan.ExtractionMethod}.",
            cancellationToken).ConfigureAwait(false);
        await context.AppendLogAsync(DeploymentLogLevel.Info, result.Message, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack extracted.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.None)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Driver pack disabled (None selected).");
        }

        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.CustomDriverSet)
        {
            return await SimulateCustomDriverSetAsync(context, cancellationToken).ConfigureAwait(false);
        }

        string downloadedPath = context.RuntimeState.DownloadedDriverPackPath ?? string.Empty;
        if (!PathExists(downloadedPath))
        {
            if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog &&
                context.RuntimeState.MicrosoftUpdateCatalogDriverPaths.Count == 0)
            {
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                return DeploymentStepResult.Skipped("Microsoft Update Catalog did not produce a driver payload.");
            }

            return CreateMissingDriverPackFailure();
        }

        DriverPackExecutionPlan executionPlan = _driverPackStrategyResolver.Resolve(
            context.Request.DriverPackSelectionKind,
            context.Request.DriverPack,
            downloadedPath);
        if (executionPlan.InstallMode != DriverPackInstallMode.OfflineInf)
        {
            return DeploymentStepResult.Skipped("No driver pack operation is required.");
        }

        context.RuntimeState.DriverPackInstallMode = executionPlan.InstallMode;
        context.RuntimeState.DriverPackExtractionMethod = executionPlan.ExtractionMethod.ToString();

        string extractionPath = Path.Combine(
            context.EnsureTargetFoundryRoot(),
            "Extracted",
            "Drivers",
            "dry-run",
            DeploymentStepExecutionContext.SanitizePathSegment(Path.GetFileNameWithoutExtension(downloadedPath)));
        Directory.CreateDirectory(extractionPath);
        string infPath = Path.Combine(extractionPath, "dryrun.inf");
        await File.WriteAllTextAsync(infPath, "; dry-run only", cancellationToken).ConfigureAwait(false);
        context.RuntimeState.ExtractedDriverPackPath = extractionPath;

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated driver pack extraction: {extractionPath}",
            cancellationToken).ConfigureAwait(false);

        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Driver pack extracted (simulation).");
    }

    /// <summary>
    /// Copies the selected set to <c>Extracted\Drivers\Custom\&lt;hash&gt;</c> on the target. The source must still be on the boot media,
    /// on optical or physical storage separate from the target disk; any missing or mismatched file fails the deployment.
    /// </summary>
    private async Task<DeploymentStepResult> StageCustomDriverSetAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        CustomDriverSetOption? set = context.Request.CustomDriverSet;
        string? sourceRoot = set is null ? null : CustomDriverSetStagingService.ResolveSourceRoot(set);
        if (set is null || !Directory.Exists(sourceRoot))
        {
            return CreateMissingCustomDriverSetFailure();
        }

        if (!await IsCustomDriverSourceSeparateAsync(context, sourceRoot!, cancellationToken).ConfigureAwait(false))
        {
            await context.AppendLogAsync(DeploymentLogLevel.Warning,
                "Custom driver set refused: its source is not on storage separate from the target disk.", cancellationToken).ConfigureAwait(false);
            return CreateCustomDriverIntegrityFailure();
        }

        DriverPackExecutionPlan executionPlan = _driverPackStrategyResolver.Resolve(context.Request.DriverPackSelectionKind, null, string.Empty);
        string destinationRoot = Path.Combine(context.EnsureTargetFoundryRoot(), "Extracted", "Drivers", "Custom");
        context.EmitCurrentStepIndeterminate("Copying driver package...", "Copying package", DeploymentOperationNames.ExtractDriverPack);
        IProgress<double> progress = context.CreateStepPercentProgressReporter("Copying driver package...", "Copying package");
        string stagedPath;
        try
        {
            stagedPath = await _customDriverSetStagingService.StageAsync(set, destinationRoot, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException)
        {
            return CreateMissingCustomDriverSetFailure();
        }
        catch (InvalidDataException)
        {
            return CreateCustomDriverIntegrityFailure();
        }

        context.RuntimeState.DriverPackInstallMode = executionPlan.InstallMode;
        context.RuntimeState.DriverPackExtractionMethod = executionPlan.ExtractionMethod.ToString();
        context.RuntimeState.DriverPackName = set.DisplayName;
        context.RuntimeState.ExtractedDriverPackPath = stagedPath;
        return DeploymentStepResult.Succeeded("Custom drivers staged.");
    }

    private async Task<DeploymentStepResult> SimulateCustomDriverSetAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        CustomDriverSetOption? set = context.Request.CustomDriverSet;
        if (set is null)
        {
            return CreateMissingCustomDriverSetFailure();
        }

        DriverPackExecutionPlan executionPlan = _driverPackStrategyResolver.Resolve(context.Request.DriverPackSelectionKind, null, string.Empty);
        string simulatedPath = Path.Combine(context.EnsureTargetFoundryRoot(), "Extracted", "Drivers", "dry-run", "custom");
        Directory.CreateDirectory(simulatedPath);
        await File.WriteAllTextAsync(Path.Combine(simulatedPath, "dryrun.inf"), "; dry-run only", cancellationToken).ConfigureAwait(false);
        context.RuntimeState.DriverPackInstallMode = executionPlan.InstallMode;
        context.RuntimeState.DriverPackExtractionMethod = executionPlan.ExtractionMethod.ToString();
        context.RuntimeState.DriverPackName = set.DisplayName;
        context.RuntimeState.ExtractedDriverPackPath = simulatedPath;
        await context.AppendLogAsync(DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated custom driver set staging: {set.FileCount} files, {set.InfCount} INF files, {set.Length} bytes.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Custom drivers staged (simulation).");
    }

    /// <summary>Applies the post-installation content rule to a custom driver set: optical media, or a known physical disk other than the target.</summary>
    internal static async Task<bool> IsCustomDriverSourceSeparateAsync(DeploymentStepExecutionContext context, string path, CancellationToken cancellationToken)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
        if (drive.IsReady && drive.DriveType == DriveType.CDRom) return true;
        try
        {
            return await context.IsExternalStorageAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (DeploymentOperationException)
        {
            // Raised when the path maps to the target disk itself.
            return false;
        }
    }

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    private static DeploymentStepResult CreateMissingDriverPackFailure() =>
        DeploymentStepResult.Failed(
            "Driver pack was not downloaded.",
            DeploymentFailure.Guard(
                DeploymentOperationNames.ExtractDriverPack,
                DeploymentFailureReasons.MissingResource,
                "missing_driver_payload"));

    private static DeploymentStepResult CreateMissingCustomDriverSetFailure() =>
        DeploymentStepResult.Failed(
            "The selected driver pack payload is unavailable.",
            DeploymentFailure.Guard(
                DeploymentOperationNames.ExtractDriverPack,
                DeploymentFailureReasons.MissingResource,
                "missing_driver_payload"));

    private static DeploymentStepResult CreateCustomDriverIntegrityFailure() =>
        DeploymentStepResult.Failed(
            CustomDriversUnavailableMessage,
            DeploymentFailure.Guard(
                DeploymentOperationNames.ExtractDriverPack,
                DeploymentFailureReasons.InvalidInput,
                "custom_driver_integrity"));
}
