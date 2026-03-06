using System.Text.Json;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class GatherDeploymentVariablesStep : DeploymentStepBase
{
    public override int Order => 1;

    public override string Name => DeploymentStepNames.GatherDeploymentVariables;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate("Gathering deployment variables...", "Collecting deployment context...");
        await AppendRunContextAsync(context, cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Deployment variables gathered.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate("Gathering deployment variables...", "Collecting deployment context...");
        await AppendRunContextAsync(context, cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Deployment variables gathered (simulation).");
    }

    private static Task AppendRunContextAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(new
        {
            gatheredAtUtc = DateTimeOffset.UtcNow,
            plannedSteps = context.PlannedSteps,
            context = new
            {
                mode = context.Request.Mode.ToString(),
                cacheRootPath = context.Request.CacheRootPath,
                targetDiskNumber = context.Request.TargetDiskNumber,
                targetComputerName = context.Request.TargetComputerName,
                driverPackSelectionKind = context.Request.DriverPackSelectionKind.ToString(),
                useFullAutopilot = context.Request.UseFullAutopilot,
                allowAutopilotDeferredCompletion = context.Request.AllowAutopilotDeferredCompletion,
                isDryRun = context.Request.IsDryRun,
                telemetryMode = "disabled",
                operatingSystem = new
                {
                    context.Request.OperatingSystem.SourceId,
                    context.Request.OperatingSystem.ClientType,
                    context.Request.OperatingSystem.WindowsRelease,
                    context.Request.OperatingSystem.ReleaseId,
                    context.Request.OperatingSystem.Build,
                    context.Request.OperatingSystem.BuildMajor,
                    context.Request.OperatingSystem.BuildUbr,
                    context.Request.OperatingSystem.Architecture,
                    context.Request.OperatingSystem.LanguageCode,
                    context.Request.OperatingSystem.Language,
                    context.Request.OperatingSystem.Edition,
                    context.Request.OperatingSystem.FileName,
                    context.Request.OperatingSystem.SizeBytes,
                    context.Request.OperatingSystem.LicenseChannel,
                    context.Request.OperatingSystem.Url,
                    context.Request.OperatingSystem.Sha1,
                    context.Request.OperatingSystem.Sha256,
                    displayLabel = context.Request.OperatingSystem.DisplayLabel
                },
                driverPack = context.Request.DriverPack is null
                    ? null
                    : new
                    {
                        context.Request.DriverPack.Id,
                        context.Request.DriverPack.PackageId,
                        context.Request.DriverPack.Manufacturer,
                        context.Request.DriverPack.Name,
                        context.Request.DriverPack.Version,
                        context.Request.DriverPack.FileName,
                        downloadUrl = context.Request.DriverPack.DownloadUrl,
                        context.Request.DriverPack.SizeBytes,
                        context.Request.DriverPack.Format,
                        context.Request.DriverPack.Type,
                        context.Request.DriverPack.ReleaseDate,
                        context.Request.DriverPack.OsName,
                        context.Request.DriverPack.OsReleaseId,
                        context.Request.DriverPack.OsArchitecture,
                        context.Request.DriverPack.ModelNames,
                        context.Request.DriverPack.Sha256,
                        displayLabel = context.Request.DriverPack.DisplayLabel
                    }
            },
            runtimeState = new
            {
                context.RuntimeState.StartedAtUtc,
                context.RuntimeState.WorkspaceRoot,
                context.RuntimeState.CurrentStep,
                mode = context.RuntimeState.Mode.ToString(),
                context.RuntimeState.IsDryRun,
                context.RuntimeState.RequestedCacheRootPath,
                context.RuntimeState.TargetDiskNumber,
                context.RuntimeState.OperatingSystemFileName,
                context.RuntimeState.OperatingSystemUrl,
                driverPackSelectionKind = context.RuntimeState.DriverPackSelectionKind.ToString(),
                context.RuntimeState.DriverPackName,
                context.RuntimeState.DriverPackUrl,
                context.RuntimeState.TargetFoundryRoot,
                context.RuntimeState.DeploymentSummaryPath,
                completedSteps = context.RuntimeState.CompletedSteps
            }
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[GATHER] Deployment variables snapshot:{Environment.NewLine}{json}",
            cancellationToken);
    }
}
