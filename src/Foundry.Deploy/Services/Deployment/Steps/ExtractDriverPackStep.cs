using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ExtractDriverPackStep : DeploymentStepBase
{
    private readonly IDriverPackStrategyResolver _driverPackStrategyResolver;
    private readonly IDriverPackExtractionService _driverPackExtractionService;

    public ExtractDriverPackStep(
        IDriverPackStrategyResolver driverPackStrategyResolver,
        IDriverPackExtractionService driverPackExtractionService)
    {
        _driverPackStrategyResolver = driverPackStrategyResolver;
        _driverPackExtractionService = driverPackExtractionService;
    }

    public override int Order => 11;

    public override string Name => DeploymentStepNames.ExtractDriverPack;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.None)
        {
            return DeploymentStepResult.Skipped("Driver pack disabled (None selected).");
        }

        string downloadedPath = context.RuntimeState.DownloadedDriverPackPath ?? string.Empty;
        if (!PathExists(downloadedPath))
        {
            return DeploymentStepResult.Failed("Driver pack was not downloaded.");
        }

        string extractionRoot = Path.Combine(context.EnsureTargetFoundryRoot(), "Extracted", "Drivers");
        IProgress<double> progress = context.CreateStepPercentProgressReporter("Extracting driver pack...", "Extracting");
        progress.Report(0d);

        DriverPackExecutionPlan executionPlan = _driverPackStrategyResolver.Resolve(
            context.Request.DriverPackSelectionKind,
            context.Request.DriverPack,
            downloadedPath);

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

        return DeploymentStepResult.Succeeded(
            result.ExecutionPlan.InstallMode == DriverPackInstallMode.DeferredSetupComplete
                ? "Driver pack prepared for deferred installation."
                : "Driver pack extracted.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.None)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Driver pack disabled (None selected).");
        }

        string downloadedPath = context.RuntimeState.DownloadedDriverPackPath ?? string.Empty;
        if (!PathExists(downloadedPath))
        {
            return DeploymentStepResult.Failed("Driver pack was not downloaded.");
        }

        DriverPackExecutionPlan executionPlan = _driverPackStrategyResolver.Resolve(
            context.Request.DriverPackSelectionKind,
            context.Request.DriverPack,
            downloadedPath);

        context.RuntimeState.DriverPackInstallMode = executionPlan.InstallMode;
        context.RuntimeState.DriverPackExtractionMethod = executionPlan.ExtractionMethod.ToString();

        if (executionPlan.InstallMode == DriverPackInstallMode.OfflineInf)
        {
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
        }
        else
        {
            context.RuntimeState.ExtractedDriverPackPath = null;
            await context.AppendLogAsync(
                DeploymentLogLevel.Info,
                "[DRY-RUN] Simulated deferred driver pack preparation.",
                cancellationToken).ConfigureAwait(false);
        }

        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Driver pack extracted (simulation).");
    }

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }
}
