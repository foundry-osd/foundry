using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class DownloadAndPrepareDriverPackStep : DeploymentStepBase
{
    private readonly IMicrosoftUpdateCatalogDriverService _microsoftUpdateCatalogDriverService;
    private readonly IArtifactDownloadService _artifactDownloadService;
    private readonly IDriverPackPreparationService _driverPackPreparationService;

    public DownloadAndPrepareDriverPackStep(
        IMicrosoftUpdateCatalogDriverService microsoftUpdateCatalogDriverService,
        IArtifactDownloadService artifactDownloadService,
        IDriverPackPreparationService driverPackPreparationService)
    {
        _microsoftUpdateCatalogDriverService = microsoftUpdateCatalogDriverService;
        _artifactDownloadService = artifactDownloadService;
        _driverPackPreparationService = driverPackPreparationService;
    }

    public override int Order => 7;

    public override string Name => DeploymentStepNames.DownloadAndPrepareDriverPack;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        context.RuntimeState.DriverPackSelectionKind = context.Request.DriverPackSelectionKind;

        switch (context.Request.DriverPackSelectionKind)
        {
            case DriverPackSelectionKind.None:
                return DeploymentStepResult.Skipped("Driver pack disabled (None selected).");

            case DriverPackSelectionKind.MicrosoftUpdateCatalog:
            {
                string destination = Path.Combine(targetFoundryRoot, "Extracted", "Drivers", "MicrosoftUpdateCatalog");
                MicrosoftUpdateCatalogDriverResult microsoftResult = await _microsoftUpdateCatalogDriverService
                    .DownloadAsync(destination, cancellationToken)
                    .ConfigureAwait(false);

                context.RuntimeState.DriverPackName = "Microsoft Update Catalog";
                context.RuntimeState.PreparedDriverPath = microsoftResult.DestinationDirectory;
                await context.AppendLogAsync(DeploymentLogLevel.Info, microsoftResult.Message, cancellationToken).ConfigureAwait(false);

                return DeploymentStepResult.Succeeded("Microsoft Update Catalog driver payload prepared.");
            }

            case DriverPackSelectionKind.OemCatalog:
            {
                DriverPackCatalogItem? driverPack = context.Request.DriverPack;
                if (driverPack is null)
                {
                    return DeploymentStepResult.Skipped("OEM driver pack mode selected but no driver pack was provided.");
                }

                context.RuntimeState.DriverPackName = driverPack.Name;
                context.RuntimeState.DriverPackUrl = driverPack.DownloadUrl;

                string driverPackDirectory = Path.Combine(
                    context.ResolveDriverPackCacheRoot(),
                    DeploymentStepExecutionContext.SanitizePathSegment(driverPack.Manufacturer));
                Directory.CreateDirectory(driverPackDirectory);
                string archiveName = DeploymentStepExecutionContext.ResolveFileName(driverPack.FileName, driverPack.DownloadUrl);
                string archivePath = Path.Combine(driverPackDirectory, archiveName);
                IProgress<DownloadProgress> driverPackDownloadProgress = context.CreateDownloadProgressReporter("Driver pack");

                ArtifactDownloadResult download = await _artifactDownloadService
                    .DownloadAsync(
                        driverPack.DownloadUrl,
                        archivePath,
                        expectedHash: driverPack.Sha256,
                        cancellationToken: cancellationToken,
                        progress: driverPackDownloadProgress)
                    .ConfigureAwait(false);

                context.RuntimeState.DownloadedDriverPackPath = download.DestinationPath;
                await context.UpdateCacheIndexAsync(
                    artifactType: "DriverPack",
                    sourceUrl: driverPack.DownloadUrl,
                    destinationPath: download.DestinationPath,
                    sizeBytes: download.SizeBytes,
                    expectedHash: driverPack.Sha256,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                string extractionRoot = Path.Combine(targetFoundryRoot, "Extracted", "Drivers");
                DriverPackPreparationResult preparation = await _driverPackPreparationService
                    .PrepareAsync(driverPack, download.DestinationPath, extractionRoot, cancellationToken)
                    .ConfigureAwait(false);

                context.RuntimeState.PreparedDriverPath = preparation.ExtractedDirectoryPath;
                await context.AppendLogAsync(DeploymentLogLevel.Info, preparation.Message, cancellationToken).ConfigureAwait(false);

                return DeploymentStepResult.Succeeded("OEM driver pack prepared.");
            }
        }

        return DeploymentStepResult.Skipped("No driver pack operation for selected mode.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        context.RuntimeState.DriverPackSelectionKind = context.Request.DriverPackSelectionKind;

        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.None)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Driver pack disabled (None selected).");
        }

        string simulationSegment = context.Request.DriverPackSelectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog
            ? "ms-update-catalog"
            : "oem";

        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.OemCatalog)
        {
            DriverPackCatalogItem? driverPack = context.Request.DriverPack;
            if (driverPack is null)
            {
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                return DeploymentStepResult.Skipped("OEM driver pack mode selected but no driver pack was provided.");
            }

            context.RuntimeState.DriverPackName = driverPack.Name;
            context.RuntimeState.DriverPackUrl = driverPack.DownloadUrl;
        }
        else
        {
            context.RuntimeState.DriverPackName = "Microsoft Update Catalog";
        }

        string driverRoot = Path.Combine(targetFoundryRoot, "Extracted", "Drivers", "dry-run", simulationSegment);
        Directory.CreateDirectory(driverRoot);
        string infPath = Path.Combine(driverRoot, "dryrun.inf");
        await File.WriteAllTextAsync(infPath, "; dry-run only", cancellationToken).ConfigureAwait(false);
        context.RuntimeState.PreparedDriverPath = driverRoot;

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Driver payload simulated ({context.Request.DriverPackSelectionKind}): {driverRoot}",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack prepared (simulation).");
    }
}
