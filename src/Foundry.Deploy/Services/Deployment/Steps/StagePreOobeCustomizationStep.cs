// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
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

        FirstBootExecutionPlan? executionPlan = context.RuntimeState.FirstBootExecutionPlan;
        if (executionPlan is null || executionPlan.FailureCode is not null ||
            executionPlan.CustomizationEntryPoint == FirstBootEntryPoint.None &&
            context.RuntimeState.DriverPackInstallMode == DriverPackInstallMode.DeferredSetupComplete)
        {
            return CreateUnsupportedHookFailure();
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

        if (executionPlan.CustomizationEntryPoint == FirstBootEntryPoint.None) return CreateUnsupportedHookFailure();
        context.EmitCurrentStepIndeterminate("Staging pre-OOBE customizations...", "Preparing the validated first-boot entry point...", DeploymentOperationNames.StagePreOobe);
        PreOobeScriptProvisioningResult result = _preOobeScriptProvisioningService.Provision(
            context.RuntimeState.TargetWindowsPartitionRoot,
            scripts, executionPlan);

        FirstBootLauncherService.Stage(context.RuntimeState.TargetWindowsPartitionRoot,
            context.Request.OperatingSystem.Architecture, executionPlan, context.Request.UsesCustomUnattend);
        ApplyPreOobeResult(context.RuntimeState, result);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Pre-OOBE customization staged with {scripts.Count} script(s). Entry point: {result.EntryPoint}; first-boot execution is pending.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Pre-OOBE customizations staged; first-boot execution is pending.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return CreateMissingTargetPartitionFailure();
        }

        FirstBootExecutionPlan? executionPlan = context.RuntimeState.FirstBootExecutionPlan;
        if (executionPlan is null || executionPlan.FailureCode is not null ||
            executionPlan.CustomizationEntryPoint == FirstBootEntryPoint.None &&
            context.RuntimeState.DriverPackInstallMode == DriverPackInstallMode.DeferredSetupComplete)
        {
            return CreateUnsupportedHookFailure();
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

        if (executionPlan.CustomizationEntryPoint == FirstBootEntryPoint.None) return CreateUnsupportedHookFailure();
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
        (string digest, long length) = await CopyFileWithProgressAsync(plan!.SourcePath, plan.TargetPath, stepProgress, cancellationToken).ConfigureAwait(false);

        context.RuntimeState.DeferredDriverPackagePath = plan.TargetPath;
        return (plan.ScriptSettings with { ExpectedSha256 = digest, ExpectedSizeBytes = length }, null);
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
        runtimeState.PreOobeResultsPath = result.ResultsPath;
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

        runtimeState.PreOobeSetupCompletePath = runtimeState.FirstBootExecutionPlan?.CustomizationEntryPoint == FirstBootEntryPoint.SetupComplete ? Path.Combine(
            runtimeState.TargetWindowsPartitionRoot!,
            "Windows",
            "Setup",
            "Scripts",
            "SetupComplete.cmd") : null;
        runtimeState.PreOobeRunnerPath = Path.Combine(preOobeRoot, "Invoke-FoundryPreOobe.ps1");
        runtimeState.PreOobeManifestPath = Path.Combine(preOobeRoot, "pre-oobe-manifest.json");
        runtimeState.PreOobeResultsPath = Path.Combine(preOobeRoot, "results.json");
        runtimeState.PreOobeScriptPaths = scripts
            .Select(script => Path.Combine(preOobeRoot, "Scripts", script.FileName))
            .ToArray();
    }

    private static async Task<(string Digest, long Length)> CopyFileWithProgressAsync(
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

        progress?.Report(0d);

        await using FileStream sourceStream = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileCopyBufferSize,
            useAsync: true);
        long totalBytes = sourceStream.Length;
        string digest = Convert.ToHexString(await SHA256.HashDataAsync(sourceStream, cancellationToken).ConfigureAwait(false));
        sourceStream.Position = 0;
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

        await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(100d);
        return (digest, totalBytes);
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

    private static DeploymentStepResult CreateUnsupportedHookFailure() =>
        DeploymentStepResult.Failed("A supported first-boot entry point must be validated before staging customizations.",
            DeploymentFailure.Guard(DeploymentOperationNames.StagePreOobe, DeploymentFailureReasons.InvalidInput, "unsupported_setup_hook"));
    private static DeploymentStepResult CreateMissingTargetPartitionFailure() =>
        DeploymentStepResult.Failed(
            "Target Windows partition is unavailable.",
            DeploymentFailure.Guard(
                DeploymentOperationNames.StagePreOobe,
                DeploymentFailureReasons.MissingResource,
                "missing_target_partition"));
}
