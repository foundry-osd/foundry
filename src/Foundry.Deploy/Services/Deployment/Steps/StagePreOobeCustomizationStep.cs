// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Network;
using Foundry.Utilities.IO;
using Foundry.Utilities.Progress;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Stages pre-OOBE customizations and deferred driver-package provisioning.
/// </summary>
public sealed class StagePreOobeCustomizationStep : DeploymentStepBase
{
    private const int FileCopyBufferSize = 80 * 1024;

    private readonly IPreOobeScriptProvisioningService _preOobeScriptProvisioningService;
    private readonly PreOobeScriptDefinitionBuilder _preOobeScriptDefinitionBuilder;
    private readonly IDriverPackStrategyResolver _driverPackStrategyResolver;
    private readonly INetworkProfileRoamingArtifactService? _networkProfileRoamingArtifactService;

    public StagePreOobeCustomizationStep(
        IPreOobeScriptProvisioningService preOobeScriptProvisioningService,
        PreOobeScriptDefinitionBuilder preOobeScriptDefinitionBuilder,
        IDriverPackStrategyResolver driverPackStrategyResolver,
        INetworkProfileRoamingArtifactService? networkProfileRoamingArtifactService = null)
    {
        _preOobeScriptProvisioningService = preOobeScriptProvisioningService;
        _preOobeScriptDefinitionBuilder = preOobeScriptDefinitionBuilder;
        _driverPackStrategyResolver = driverPackStrategyResolver;
        _networkProfileRoamingArtifactService = networkProfileRoamingArtifactService;
    }

    public override string Name => DeploymentStepNames.StagePreOobeCustomization;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return CreateMissingTargetPartitionFailure();
        }

        PreOobeDriverPackScriptSettings? driverPackSettings = null;
        if (context.RuntimeState.DriverPackInstallMode == DriverPackInstallMode.DeferredSetupComplete)
        {
            (driverPackSettings, DeploymentStepResult? failure) = await StageDeferredDriverPackageAsync(
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
            if (failure is not null)
            {
                return failure;
            }
        }

        PreOobeNetworkProfileRoamingPayload? networkProfileRoaming = await LoadNetworkProfileRoamingPayloadAsync(
                context,
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<PreOobeScriptDefinition> scripts = _preOobeScriptDefinitionBuilder.Build(
            context.RuntimeState.AppxRemoval,
            context.RuntimeState.AiComponentRemoval,
            driverPackSettings,
            networkProfileRoaming);
        if (scripts.Count == 0)
        {
            return DeploymentStepResult.Skipped("No pre-OOBE customization scripts are required.");
        }

        context.EmitCurrentStepIndeterminate("Staging pre-OOBE customizations...", "Updating SetupComplete hook...", DeploymentOperationNames.StagePreOobe);
        PreOobeScriptProvisioningResult result = _preOobeScriptProvisioningService.Provision(
            context.RuntimeState.TargetWindowsPartitionRoot,
            scripts);

        ApplyPreOobeResult(context.RuntimeState, result);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Pre-OOBE customization staged with {scripts.Count} script(s). SetupComplete hook: '{result.SetupCompletePath}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Pre-OOBE customizations staged.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return CreateMissingTargetPartitionFailure();
        }

        PreOobeDriverPackScriptSettings? driverPackSettings = null;
        if (context.RuntimeState.DriverPackInstallMode == DriverPackInstallMode.DeferredSetupComplete)
        {
            (driverPackSettings, DeploymentStepResult? failure) = PrepareDeferredDriverPackageDryRun(context);
            if (failure is not null)
            {
                return failure;
            }
        }

        PreOobeNetworkProfileRoamingPayload? networkProfileRoaming = await LoadNetworkProfileRoamingPayloadAsync(
                context,
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<PreOobeScriptDefinition> scripts = _preOobeScriptDefinitionBuilder.Build(
            context.RuntimeState.AppxRemoval,
            context.RuntimeState.AiComponentRemoval,
            driverPackSettings,
            networkProfileRoaming);
        if (scripts.Count == 0)
        {
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("No pre-OOBE customization scripts are required.");
        }

        ApplyDryRunPreOobeResult(context.RuntimeState, scripts);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated pre-OOBE customization staging with {scripts.Count} script(s).",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Pre-OOBE customizations staged (simulation).");
    }

    private async Task<(PreOobeDriverPackScriptSettings? Settings, DeploymentStepResult? Failure)> StageDeferredDriverPackageAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        (DeferredDriverPackagePlan? plan, DeploymentStepResult? failure) = ResolveDeferredDriverPackage(context);
        if (failure is not null)
        {
            return (null, failure);
        }

        IProgress<double> stepProgress = context.CreateStepPercentProgressReporter("Staging pre-OOBE customizations...", "Staging package");

        context.EmitCurrentStepIndeterminate("Staging pre-OOBE customizations...", "Staging package...", DeploymentOperationNames.StageDeferredDriverPack);
        await CopyFileWithProgressAsync(plan!.SourcePath, plan.TargetPath, stepProgress, cancellationToken).ConfigureAwait(false);

        context.RuntimeState.DeferredDriverPackagePath = plan.TargetPath;
        return (plan.ScriptSettings, null);
    }

    private (PreOobeDriverPackScriptSettings? Settings, DeploymentStepResult? Failure) PrepareDeferredDriverPackageDryRun(
        DeploymentStepExecutionContext context)
    {
        (DeferredDriverPackagePlan? plan, DeploymentStepResult? failure) = ResolveDeferredDriverPackage(context);
        if (failure is not null)
        {
            return (null, failure);
        }

        context.RuntimeState.DeferredDriverPackagePath = plan!.TargetPath;
        return (plan.ScriptSettings, null);
    }

    private (DeferredDriverPackagePlan? Plan, DeploymentStepResult? Failure) ResolveDeferredDriverPackage(
        DeploymentStepExecutionContext context)
    {
        string sourcePath = context.RuntimeState.DownloadedDriverPackPath ?? string.Empty;
        if (!File.Exists(sourcePath))
        {
            return (null, DeploymentStepResult.Failed(
                "Driver pack source payload is unavailable for deferred staging.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.StageDeferredDriverPack,
                    DeploymentFailureReasons.MissingResource,
                    "missing_driver_payload")));
        }

        DriverPackExecutionPlan executionPlan = _driverPackStrategyResolver.Resolve(
            context.Request.DriverPackSelectionKind,
            context.Request.DriverPack,
            sourcePath);
        if (executionPlan.DeferredCommandKind == DeferredDriverPackageCommandKind.None)
        {
            return (null, DeploymentStepResult.Failed(
                "Deferred driver pack staging was requested without a supported deferred command.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.StageDeferredDriverPack,
                    DeploymentFailureReasons.InvalidInput,
                    "unsupported_deferred_driver_command")));
        }

        string packageFileName = Path.GetFileName(sourcePath);
        return (new DeferredDriverPackagePlan(
            sourcePath,
            BuildTargetPackagePath(context.RuntimeState.TargetWindowsPartitionRoot!, packageFileName),
            new PreOobeDriverPackScriptSettings
            {
                CommandKind = executionPlan.DeferredCommandKind,
                RuntimePackagePath = BuildRuntimePackagePath(packageFileName)
            }), null);
    }

    private sealed record DeferredDriverPackagePlan(
        string SourcePath,
        string TargetPath,
        PreOobeDriverPackScriptSettings ScriptSettings);

    private static string BuildTargetPackagePath(string targetWindowsPartitionRoot, string packageFileName)
    {
        return Path.Combine(
            targetWindowsPartitionRoot,
            "Windows",
            "Temp",
            "Foundry",
            "DriverPack",
            "Packages",
            packageFileName);
    }

    private static string BuildRuntimePackagePath(string packageFileName)
    {
        return Path.Combine(
            "%SystemRoot%",
            "Temp",
            "Foundry",
            "DriverPack",
            "Packages",
            packageFileName);
    }

    private static void ApplyPreOobeResult(
        DeploymentRuntimeState runtimeState,
        PreOobeScriptProvisioningResult result)
    {
        runtimeState.PreOobeSetupCompletePath = result.SetupCompletePath;
        runtimeState.PreOobeRunnerPath = result.RunnerPath;
        runtimeState.PreOobeManifestPath = result.ManifestPath;
        runtimeState.PreOobeScriptPaths = result.StagedScriptPaths;
    }

    private static void ApplyDryRunPreOobeResult(
        DeploymentRuntimeState runtimeState,
        IReadOnlyList<PreOobeScriptDefinition> scripts)
    {
        string preOobeRoot = Path.Combine(
            runtimeState.TargetWindowsPartitionRoot!,
            "Windows",
            "Temp",
            "Foundry",
            "PreOobe");

        runtimeState.PreOobeSetupCompletePath = Path.Combine(
            runtimeState.TargetWindowsPartitionRoot!,
            "Windows",
            "Setup",
            "Scripts",
            "SetupComplete.cmd");
        runtimeState.PreOobeRunnerPath = Path.Combine(preOobeRoot, "Invoke-FoundryPreOobe.ps1");
        runtimeState.PreOobeManifestPath = Path.Combine(preOobeRoot, "pre-oobe-manifest.json");
        runtimeState.PreOobeScriptPaths = scripts
            .Select(script => Path.Combine(preOobeRoot, "Scripts", script.FileName))
            .ToArray();
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

        await StreamCopy.CopyAsync(
            sourceStream,
            destinationStream,
            copiedBytes =>
            {
                double? percentage = TransferProgress.CalculatePercentage(copiedBytes, totalBytes);
                if (percentage.HasValue)
                {
                    progress?.Report(percentage.Value);
                }
            },
            cancellationToken).ConfigureAwait(false);

        progress?.Report(100d);
    }

    private Task<PreOobeNetworkProfileRoamingPayload?> LoadNetworkProfileRoamingPayloadAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        return _networkProfileRoamingArtifactService is null
            ? Task.FromResult<PreOobeNetworkProfileRoamingPayload?>(null)
            : _networkProfileRoamingArtifactService.LoadAsync(
                context.RuntimeState.Network.ProfileRoaming,
                context.RuntimeState.WorkspaceRoot,
                cancellationToken);
    }

    private static DeploymentStepResult CreateMissingTargetPartitionFailure() =>
        DeploymentStepResult.Failed(
            "Target Windows partition is unavailable.",
            DeploymentFailure.Guard(
                DeploymentOperationNames.StagePreOobe,
                DeploymentFailureReasons.MissingResource,
                "missing_target_partition"));
}
