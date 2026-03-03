using System.IO;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class DriverPackStrategyResolver : IDriverPackStrategyResolver
{
    public DriverPackExecutionPlan Resolve(
        DriverPackSelectionKind selectionKind,
        DriverPackCatalogItem? driverPack,
        string downloadedPath)
    {
        if (selectionKind == DriverPackSelectionKind.None)
        {
            return new DriverPackExecutionPlan
            {
                InstallMode = DriverPackInstallMode.None,
                ExtractionMethod = DriverPackExtractionMethod.None,
                DeferredCommandKind = DeferredDriverPackageCommandKind.None,
                DownloadedPath = downloadedPath,
                EffectiveFileExtension = string.Empty,
                Manufacturer = string.Empty,
                RequiresInfPayload = false
            };
        }

        if (selectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
        {
            return new DriverPackExecutionPlan
            {
                InstallMode = DriverPackInstallMode.OfflineInf,
                ExtractionMethod = DriverPackExtractionMethod.MicrosoftUpdateCatalogExpand,
                DeferredCommandKind = DeferredDriverPackageCommandKind.None,
                DownloadedPath = downloadedPath,
                EffectiveFileExtension = ".cab",
                Manufacturer = "Microsoft Update Catalog",
                RequiresInfPayload = true
            };
        }

        if (driverPack is null)
        {
            throw new InvalidOperationException("A driver pack selection is required to resolve the package strategy.");
        }

        string extension = ResolveExtension(downloadedPath, driverPack.FileName);
        string normalizedManufacturer = driverPack.Manufacturer.Trim();
        string manufacturerLower = normalizedManufacturer.ToLowerInvariant();

        if (manufacturerLower.Contains("lenovo", StringComparison.Ordinal) && extension == ".exe")
        {
            return CreatePlan(
                DriverPackInstallMode.DeferredSetupComplete,
                DriverPackExtractionMethod.None,
                DeferredDriverPackageCommandKind.LenovoExecutable,
                downloadedPath,
                extension,
                normalizedManufacturer,
                requiresInfPayload: false);
        }

        if (manufacturerLower.Contains("microsoft", StringComparison.Ordinal) && extension == ".msi")
        {
            return CreatePlan(
                DriverPackInstallMode.DeferredSetupComplete,
                DriverPackExtractionMethod.None,
                DeferredDriverPackageCommandKind.SurfaceMsi,
                downloadedPath,
                extension,
                normalizedManufacturer,
                requiresInfPayload: false);
        }

        if (extension is ".cab" or ".zip")
        {
            return CreatePlan(
                DriverPackInstallMode.OfflineInf,
                DriverPackExtractionMethod.SevenZip,
                DeferredDriverPackageCommandKind.None,
                downloadedPath,
                extension,
                normalizedManufacturer,
                requiresInfPayload: true);
        }

        if (manufacturerLower.Contains("dell", StringComparison.Ordinal) && extension == ".exe")
        {
            return CreatePlan(
                DriverPackInstallMode.OfflineInf,
                DriverPackExtractionMethod.DellSelfExtractor,
                DeferredDriverPackageCommandKind.None,
                downloadedPath,
                extension,
                normalizedManufacturer,
                requiresInfPayload: true);
        }

        if (manufacturerLower.Contains("hp", StringComparison.Ordinal) && extension == ".exe")
        {
            return CreatePlan(
                DriverPackInstallMode.OfflineInf,
                DriverPackExtractionMethod.SevenZip,
                DeferredDriverPackageCommandKind.None,
                downloadedPath,
                extension,
                normalizedManufacturer,
                requiresInfPayload: true);
        }

        throw new InvalidOperationException(
            $"Unsupported driver pack format '{extension}' for manufacturer '{driverPack.Manufacturer}'.");
    }

    private static DriverPackExecutionPlan CreatePlan(
        DriverPackInstallMode installMode,
        DriverPackExtractionMethod extractionMethod,
        DeferredDriverPackageCommandKind deferredCommandKind,
        string downloadedPath,
        string effectiveFileExtension,
        string manufacturer,
        bool requiresInfPayload)
    {
        return new DriverPackExecutionPlan
        {
            InstallMode = installMode,
            ExtractionMethod = extractionMethod,
            DeferredCommandKind = deferredCommandKind,
            DownloadedPath = downloadedPath,
            EffectiveFileExtension = effectiveFileExtension,
            Manufacturer = manufacturer,
            RequiresInfPayload = requiresInfPayload
        };
    }

    private static string ResolveExtension(string downloadedPath, string fallbackFileName)
    {
        string extension = Path.GetExtension(downloadedPath);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.Trim().ToLowerInvariant();
        }

        return Path.GetExtension(fallbackFileName).Trim().ToLowerInvariant();
    }
}
