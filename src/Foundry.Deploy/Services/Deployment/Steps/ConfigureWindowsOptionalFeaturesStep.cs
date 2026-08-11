// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Applies selected Windows optional feature changes to the offline Windows installation.
/// </summary>
public sealed class ConfigureWindowsOptionalFeaturesStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    public ConfigureWindowsOptionalFeaturesStep(IWindowsDeploymentService windowsDeploymentService)
    {
        _windowsDeploymentService = windowsDeploymentService;
    }

    public override int Order => 10;

    public override string Name => DeploymentStepNames.ConfigureWindowsOptionalFeatures;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetSettings(context, out DeployWindowsOptionalFeatureSettings settings, out DeploymentStepResult? failure))
        {
            return failure!;
        }

        if (!settings.IsEnabled || settings.Actions.Count == 0)
        {
            return DeploymentStepResult.Succeeded("Windows optional feature configuration disabled.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        string imagePath = context.RuntimeState.DownloadedOperatingSystemPath ?? string.Empty;
        if (!File.Exists(imagePath))
        {
            return DeploymentStepResult.Failed("Operating system image was not downloaded.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism", "OptionalFeatures");
        string sourceExtractionDirectory = Path.Combine(targetFoundryRoot, "Temp", "WindowsSetupMedia");
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);

        const string stepMessage = "Configuring Windows optional features...";
        IProgress<double> progress = context.CreateStepPercentProgressReporter(stepMessage, "Applying feature changes");
        WindowsOptionalFeatureServicingResult result = await _windowsDeploymentService
            .ConfigureOfflineWindowsOptionalFeaturesAsync(
                imagePath,
                context.RuntimeState.TargetWindowsPartitionRoot,
                settings,
                scratchDirectory,
                sourceExtractionDirectory,
                workingDirectory,
                cancellationToken,
                progress,
                () => context.EmitCurrentStepIndeterminate(
                    stepMessage,
                    "Inspecting feature states...",
                    DeploymentOperationNames.InspectWindowsOptionalFeatures),
                () => context.EmitCurrentStepIndeterminate(
                    stepMessage,
                    "Preparing matching setup media sources...",
                    DeploymentOperationNames.PrepareWindowsOptionalFeatureSource),
                () => context.EmitCurrentStepIndeterminate(
                    stepMessage,
                    "Applying feature changes...",
                    DeploymentOperationNames.ConfigureWindowsOptionalFeatures))
            .ConfigureAwait(false);

        if (result.UnavailableEnableActionIds.Count > 0)
        {
            await context.AppendLogAsync(
                DeploymentLogLevel.Warning,
                $"Skipped {result.UnavailableEnableActionIds.Count} unavailable Windows optional feature enable action(s).",
                cancellationToken).ConfigureAwait(false);
        }

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Windows optional features serviced: requested={result.RequestedActionCount}, changed={result.ChangedActionCount}, alreadySatisfied={result.AlreadySatisfiedActionCount}, unavailable={result.UnavailableEnableActionIds.Count}, matchingSourceUsed={result.MatchingSourceUsed}.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded(
            $"Windows optional features configured ({result.ChangedActionCount} changed, {result.AlreadySatisfiedActionCount} already satisfied, {result.UnavailableEnableActionIds.Count} unavailable).");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetSettings(context, out DeployWindowsOptionalFeatureSettings settings, out DeploymentStepResult? failure))
        {
            return failure!;
        }

        if (!settings.IsEnabled || settings.Actions.Count == 0)
        {
            return DeploymentStepResult.Succeeded("Windows optional feature configuration disabled.");
        }

        int enableCount = settings.Actions.Count(action => action.Enable);
        int disableCount = settings.Actions.Count - enableCount;
        context.EmitCurrentStepIndeterminate(
            "Configuring Windows optional features...",
            "Simulating feature changes...",
            DeploymentOperationNames.ConfigureWindowsOptionalFeatures);
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated {settings.Actions.Count} Windows optional feature action(s): enable={enableCount}, disable={disableCount}.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded(
            $"Windows optional feature configuration simulated ({settings.Actions.Count} actions).");
    }

    private static bool TryGetSettings(
        DeploymentStepExecutionContext context,
        out DeployWindowsOptionalFeatureSettings settings,
        out DeploymentStepResult? failure)
    {
        if (WindowsOptionalFeatureActionValidator.TryNormalize(
            context.RuntimeState.WindowsOptionalFeatures,
            out settings,
            out _))
        {
            failure = null;
            return true;
        }

        failure = DeploymentStepResult.Failed(
            "Windows optional feature configuration is invalid.",
            new DeploymentFailure(
                DeploymentOperationNames.ValidateWindowsOptionalFeatures,
                DeploymentFailureKinds.Validation,
                DeploymentFailureReasons.InvalidInput,
                "optional_feature_configuration_invalid"));
        return false;
    }
}
