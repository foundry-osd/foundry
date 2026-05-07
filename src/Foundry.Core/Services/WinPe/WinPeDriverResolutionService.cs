namespace Foundry.Core.Services.WinPe;

public sealed class WinPeDriverResolutionService : IWinPeDriverResolutionService
{
    private readonly IWinPeDriverCatalogService _driverCatalogService;
    private readonly IWinPeDriverPackageService _driverPackageService;

    public WinPeDriverResolutionService(
        IWinPeDriverCatalogService driverCatalogService,
        IWinPeDriverPackageService driverPackageService)
    {
        _driverCatalogService = driverCatalogService;
        _driverPackageService = driverPackageService;
    }

    public async Task<WinPeResult<IReadOnlyList<string>>> ResolveAsync(
        WinPeDriverResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        WinPeVendorSelection[] normalizedVendors = request.DriverVendors
            .Where(vendor => vendor != WinPeVendorSelection.Any)
            .Distinct()
            .ToArray();

        string normalizedCustomDirectory = request.CustomDriverDirectoryPath?.Trim() ?? string.Empty;
        bool hasCustomDirectory = !string.IsNullOrWhiteSpace(normalizedCustomDirectory);
        bool includeWifiSupplement = request.BootImageSource == WinPeBootImageSource.WinReWifi;

        if (normalizedVendors.Length == 0 && !hasCustomDirectory && !includeWifiSupplement)
        {
            return WinPeResult<IReadOnlyList<string>>.Success([]);
        }

        WinPeDiagnostic? customDirectoryError = ValidateCustomDirectory(normalizedCustomDirectory, hasCustomDirectory);
        if (customDirectoryError is not null)
        {
            return WinPeResult<IReadOnlyList<string>>.Failure(customDirectoryError);
        }

        var resolvedPaths = new List<string>();
        if (normalizedVendors.Length > 0 || includeWifiSupplement)
        {
            WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>> catalog = await _driverCatalogService.GetCatalogAsync(
                new WinPeDriverCatalogOptions
                {
                    CatalogUri = request.CatalogUri,
                    Architecture = request.Architecture,
                    Vendors = []
                },
                cancellationToken).ConfigureAwait(false);

            if (!catalog.IsSuccess)
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(catalog.Error!);
            }

            IReadOnlyList<WinPeDriverCatalogEntry> selectedPackages = SelectPackages(
                catalog.Value!,
                normalizedVendors,
                includeWifiSupplement);

            if (selectedPackages.Count > 0)
            {
                WinPeResult<WinPePreparedDriverSet> prepared = await _driverPackageService.PrepareAsync(
                    selectedPackages,
                    Path.Combine(request.Artifact.DriverWorkspacePath, "downloads"),
                    Path.Combine(request.Artifact.DriverWorkspacePath, "extracted"),
                    request.DownloadProgress,
                    cancellationToken).ConfigureAwait(false);

                if (!prepared.IsSuccess)
                {
                    return WinPeResult<IReadOnlyList<string>>.Failure(prepared.Error!);
                }

                resolvedPaths.AddRange(prepared.Value!.ExtractionDirectories);
            }
        }

        if (hasCustomDirectory)
        {
            resolvedPaths.Add(normalizedCustomDirectory);
        }

        return WinPeResult<IReadOnlyList<string>>.Success(
            resolvedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static IReadOnlyList<WinPeDriverCatalogEntry> SelectPackages(
        IReadOnlyList<WinPeDriverCatalogEntry> catalog,
        IReadOnlyList<WinPeVendorSelection> vendors,
        bool includeWifiSupplement)
    {
        WinPeDriverCatalogEntry[] selectedBasePackages = catalog
            .Where(item => item.PackageRole == WinPeDriverPackageRole.BaseDriverPack)
            .Where(item => vendors.Contains(item.Vendor))
            .GroupBy(item => item.Vendor)
            .Select(group => group
                .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
                .First())
            .ToArray();

        var selectedPackages = new List<WinPeDriverCatalogEntry>(selectedBasePackages);

        if (includeWifiSupplement)
        {
            WinPeDriverCatalogEntry? intelWifiSupplement = catalog
                .Where(item => item.PackageRole == WinPeDriverPackageRole.WifiSupplement)
                .Where(item => item.DriverFamily == WinPeDriverFamily.IntelWireless)
                .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            if (intelWifiSupplement is not null)
            {
                selectedPackages.Add(intelWifiSupplement);
            }
        }

        var distinctPackages = new List<WinPeDriverCatalogEntry>(selectedPackages.Count);
        var packageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WinPeDriverCatalogEntry selectedPackage in selectedPackages
                     .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue))
        {
            string packageKey = string.Join("|", selectedPackage.Id, selectedPackage.DownloadUri, selectedPackage.FileName);
            if (packageKeys.Add(packageKey))
            {
                distinctPackages.Add(selectedPackage);
            }
        }

        return distinctPackages;
    }

    private static WinPeDiagnostic? ValidateCustomDirectory(string customDirectoryPath, bool hasCustomDirectory)
    {
        if (!hasCustomDirectory)
        {
            return null;
        }

        if (!Directory.Exists(customDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Custom driver directory does not exist.",
                $"Path: '{customDirectoryPath}'.");
        }

        if (!Directory.EnumerateFiles(customDirectoryPath, "*.inf", SearchOption.AllDirectories).Any())
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Custom driver directory does not contain any .inf files.",
                $"Path: '{customDirectoryPath}'.");
        }

        return null;
    }
}
