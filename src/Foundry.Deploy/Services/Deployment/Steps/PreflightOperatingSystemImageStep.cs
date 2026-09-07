// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using System.IO;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Checks image and target readiness before target preparation begins.</summary>
public sealed class PreflightOperatingSystemImageStep : DeploymentStepBase
{
    private readonly Func<DeploymentContext, TargetDiskInfo, string?, string, CancellationToken, Task<DeploymentPreflightResult>> prepare;
    private readonly ITargetDiskService disks;
    private readonly Action validateTools;

    public PreflightOperatingSystemImageStep(DeploymentPreflightService preflight, ITargetDiskService disks)
        : this(preflight.PrepareAsync, disks, ValidateRequiredTools) { }

    internal PreflightOperatingSystemImageStep(
        Func<DeploymentContext, TargetDiskInfo, string?, string, CancellationToken, Task<DeploymentPreflightResult>> prepare,
        ITargetDiskService disks, Action validateTools)
    {
        this.prepare = prepare;
        this.disks = disks;
        this.validateTools = validateTools;
    }

    public override string Name => DeploymentStepNames.PreflightOperatingSystemImage;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.RuntimeState.ImagePreflight = null;
        DeploymentStepResult? guard = ValidateRequest(context, cancellationToken);
        if (guard is not null) return guard;
        (TargetDiskInfo? target, DeploymentStepResult? targetFailure) = await context.TryGetValidatedTargetDiskAsync(cancellationToken).ConfigureAwait(false);
        if (targetFailure is not null) return targetFailure;
        validateTools();
        string candidateCache = context.ResolveOperatingSystemCacheRoot();
        int? cacheDisk = await disks.GetDiskNumberForPathAsync(candidateCache, cancellationToken).ConfigureAwait(false);
        string? independentCache = cacheDisk is >= 0 && cacheDisk != target!.DiskNumber ? candidateCache : null;
        DeploymentPreflightResult result = await prepare(context.Request, target!, independentCache,
            context.ResolveWorkspaceTempPath("Deployment"), cancellationToken).ConfigureAwait(false);
        context.RuntimeState.ImagePreflight = result;
        await context.AppendLogAsync(DeploymentLogLevel.Info,
            $"Image preflight: level={result.Level}; requiredTargetBytes={result.RequiredTargetBytes}; constraint={result.ConstraintReason ?? "none"}. Boot configuration tool availability is checked in the applied image before boot configuration.",
            cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded(result.Level == ImagePreflightLevel.CompleteImageVerified
            ? "The complete operating system image is verified before target preparation."
            : "Only operating system metadata and source availability are checked before target preparation; complete image verification remains pending.");
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.RuntimeState.ImagePreflight = null;
        DeploymentStepResult? guard = ValidateRequest(context, cancellationToken);
        return Task.FromResult(guard ?? DeploymentStepResult.Succeeded(
            "Simulated operating system preflight: selection metadata, architecture and first-boot policy checked; no image acquisition or native verification performed."));
    }

    private static DeploymentStepResult? ValidateRequest(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.RuntimeState.FirstBootExecutionPlan is null || context.RuntimeState.FirstBootExecutionPlan.FailureCode is not null)
            return Guard("First-boot policy must succeed before image preflight.", "first_boot_preflight_required");
        string? selected = NormalizeArchitecture(context.Request.OperatingSystem.Architecture);
        string? runtime = NormalizeArchitecture(context.RuntimeState.HardwareProfile?.Architecture);
        if (selected is null || runtime is null || selected != runtime)
            return Guard("The selected image architecture does not match the deployment hardware.", "image_architecture_mismatch");
        DeploymentPreflightService.ValidateSelection(context.Request.OperatingSystem);
        return null;
    }

    private static string? NormalizeArchitecture(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "x64" or "amd64" => "x64",
        "arm64" => "arm64",
        _ => null
    };

    private static DeploymentStepResult Guard(string message, string code) => DeploymentStepResult.Failed(message,
        DeploymentFailure.Guard(DeploymentStepNames.PreflightOperatingSystemImage, DeploymentFailureReasons.InvalidState, code));

    private static void ValidateRequiredTools()
    {
        foreach (string tool in new[] { "dism.exe", "powershell.exe" })
            if (!File.Exists(Foundry.Deploy.Services.System.ProcessRunner.ResolveSystemTool(tool)))
                throw new FileNotFoundException("A required deployment system tool is unavailable.");
    }
}
