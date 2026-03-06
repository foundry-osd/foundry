using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class DownloadDriverPackStep : DeploymentStepBase
{
    private readonly IMicrosoftUpdateCatalogDriverService _microsoftUpdateCatalogDriverService;
    private readonly IArtifactDownloadService _artifactDownloadService;

    public DownloadDriverPackStep(
        IMicrosoftUpdateCatalogDriverService microsoftUpdateCatalogDriverService,
        IArtifactDownloadService artifactDownloadService)
    {
        _microsoftUpdateCatalogDriverService = microsoftUpdateCatalogDriverService;
        _artifactDownloadService = artifactDownloadService;
    }

    public override int Order => 10;

    public override string Name => DeploymentStepNames.DownloadDriverPack;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        ResetDriverPackRuntimeState(context.RuntimeState);
        context.RuntimeState.DriverPackSelectionKind = context.Request.DriverPackSelectionKind;

        switch (context.Request.DriverPackSelectionKind)
        {
            case DriverPackSelectionKind.None:
                return DeploymentStepResult.Skipped("Driver pack disabled (None selected).");

            case DriverPackSelectionKind.MicrosoftUpdateCatalog:
            {
                string rawDirectory = context.ResolveWorkspaceTempPath("DriverPack", "MicrosoftUpdateCatalog", "Raw");
                ResetDirectory(rawDirectory);
                context.EmitCurrentStepIndeterminate("Downloading driver pack...", "Preparing download...");
                IProgress<double> progress = context.CreateStepPercentProgressReporter("Downloading driver pack...", "Downloading");

                MicrosoftUpdateCatalogDriverResult result = await _microsoftUpdateCatalogDriverService
                    .DownloadAsync(rawDirectory, cancellationToken, progress)
                    .ConfigureAwait(false);

                context.RuntimeState.DriverPackName = "Microsoft Update Catalog";
                context.RuntimeState.DriverPackUrl = null;
                context.RuntimeState.DownloadedDriverPackPath = result.DestinationDirectory;
                await context.AppendLogAsync(DeploymentLogLevel.Info, result.Message, cancellationToken).ConfigureAwait(false);

                return DeploymentStepResult.Succeeded("Driver pack downloaded.");
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
                context.EmitCurrentStepIndeterminate("Downloading driver pack...", "Checking cache...");
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
                await context.AppendLogAsync(
                    DeploymentLogLevel.Info,
                    $"Driver pack {(download.Downloaded ? "downloaded" : "reused")} via {download.Method}: {download.DestinationPath}",
                    cancellationToken).ConfigureAwait(false);

                return DeploymentStepResult.Succeeded(
                    download.Downloaded
                        ? "Driver pack downloaded."
                        : "Driver pack resolved from cache.");
            }
        }

        return DeploymentStepResult.Skipped("No driver pack download required.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        ResetDriverPackRuntimeState(context.RuntimeState);
        context.RuntimeState.DriverPackSelectionKind = context.Request.DriverPackSelectionKind;

        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.None)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Driver pack disabled (None selected).");
        }

        string downloadRoot = context.ResolveWorkspaceTempPath("DriverPack", "DryRun");
        Directory.CreateDirectory(downloadRoot);

        if (context.Request.DriverPackSelectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
        {
            string rawDirectory = Path.Combine(downloadRoot, "MicrosoftUpdateCatalog");
            Directory.CreateDirectory(rawDirectory);
            string cabPath = Path.Combine(rawDirectory, "driver.cab");
            await File.WriteAllTextAsync(cabPath, "dry-run", cancellationToken).ConfigureAwait(false);
            context.RuntimeState.DriverPackName = "Microsoft Update Catalog";
            context.RuntimeState.DownloadedDriverPackPath = rawDirectory;
            await context.AppendLogAsync(
                DeploymentLogLevel.Info,
                $"[DRY-RUN] Simulated Microsoft Update Catalog payload download: {rawDirectory}",
                cancellationToken).ConfigureAwait(false);
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Succeeded("Driver pack downloaded (simulation).");
        }

        DriverPackCatalogItem? driverPack = context.Request.DriverPack;
        if (driverPack is null)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("OEM driver pack mode selected but no driver pack was provided.");
        }

        string fileName = DeploymentStepExecutionContext.ResolveFileName(driverPack.FileName, driverPack.DownloadUrl);
        string simulatedPath = Path.Combine(downloadRoot, fileName);
        await File.WriteAllTextAsync(simulatedPath, "dry-run", cancellationToken).ConfigureAwait(false);

        context.RuntimeState.DriverPackName = driverPack.Name;
        context.RuntimeState.DriverPackUrl = driverPack.DownloadUrl;
        context.RuntimeState.DownloadedDriverPackPath = simulatedPath;

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated driver pack download: {simulatedPath}",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack downloaded (simulation).");
    }

    private static void ResetDriverPackRuntimeState(DeploymentRuntimeState runtimeState)
    {
        runtimeState.DownloadedDriverPackPath = null;
        runtimeState.DriverPackInstallMode = DriverPackInstallMode.None;
        runtimeState.DriverPackExtractionMethod = null;
        runtimeState.ExtractedDriverPackPath = null;
        runtimeState.DeferredDriverPackagePath = null;
        runtimeState.DriverPackSetupCompleteHookPath = null;
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }
}
