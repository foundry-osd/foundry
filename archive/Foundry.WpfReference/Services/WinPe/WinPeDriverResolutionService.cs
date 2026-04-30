using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeDriverResolutionService : IWinPeDriverResolutionService
{
    private readonly IWinPeDriverCatalogService _driverCatalogService;
    private readonly WinPeDriverPackageService _driverPackageService;
    private readonly ILogger<WinPeDriverResolutionService> _logger;

    public WinPeDriverResolutionService(
        IWinPeDriverCatalogService driverCatalogService,
        WinPeDriverPackageService driverPackageService,
        ILogger<WinPeDriverResolutionService> logger)
    {
        _driverCatalogService = driverCatalogService;
        _driverPackageService = driverPackageService;
        _logger = logger;
    }

    public async Task<WinPeResult<IReadOnlyList<string>>> ResolveAsync(
        WinPeDriverResolutionRequest request,
        CancellationToken cancellationToken)
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
            return WinPeResult<IReadOnlyList<string>>.Success(Array.Empty<string>());
        }

        if (hasCustomDirectory)
        {
            if (!Directory.Exists(normalizedCustomDirectory))
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Custom driver directory does not exist.",
                    $"Path: '{normalizedCustomDirectory}'.");
            }

            bool hasInf = Directory.EnumerateFiles(normalizedCustomDirectory, "*.inf", SearchOption.AllDirectories).Any();
            if (!hasInf)
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Custom driver directory does not contain any .inf files.",
                    $"Path: '{normalizedCustomDirectory}'.");
            }
        }

        var resolvedPaths = new List<string>();

        if (normalizedVendors.Length > 0 || includeWifiSupplement)
        {
            WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>> catalog = await _driverCatalogService.GetCatalogAsync(new WinPeDriverCatalogOptions
            {
                CatalogUri = request.CatalogUri,
                Architecture = request.Architecture,
                Vendors = Array.Empty<WinPeVendorSelection>()
            }, cancellationToken).ConfigureAwait(false);

            if (!catalog.IsSuccess)
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(catalog.Error!);
            }

            WinPeDriverCatalogEntry[] selectedBasePackages = catalog.Value?
                .Where(item => item.PackageRole == WinPeDriverPackageRole.BaseDriverPack)
                .Where(item => normalizedVendors.Contains(item.Vendor))
                .GroupBy(item => item.Vendor)
                .Select(group => group
                    .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
                    .First())
                .ToArray() ?? [];

            var selectedPackages = new List<WinPeDriverCatalogEntry>(selectedBasePackages);

            if (includeWifiSupplement)
            {
                WinPeDriverCatalogEntry? intelWifiSupplement = catalog.Value?
                    .Where(item => item.PackageRole == WinPeDriverPackageRole.WifiSupplement)
                    .Where(item => item.DriverFamily == WinPeDriverFamily.IntelWireless)
                    .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
                    .FirstOrDefault();

                if (intelWifiSupplement is null)
                {
                    _logger.LogWarning(
                        "Wi-Fi boot image source is enabled but no Intel wireless supplement was found in the WinPE driver catalog. CatalogUri={CatalogUri}, Architecture={Architecture}",
                        request.CatalogUri,
                        request.Architecture);
                }
                else
                {
                    _logger.LogInformation(
                        "Selected Intel wireless supplement package. Id={PackageId}, Version={PackageVersion}, DownloadUri={DownloadUri}",
                        intelWifiSupplement.Id,
                        intelWifiSupplement.Version,
                        intelWifiSupplement.DownloadUri);
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

            if (distinctPackages.Count > 0)
            {
                WinPeResult<WinPePreparedDriverSet> prepared = await _driverPackageService.PrepareAsync(
                    distinctPackages.ToArray(),
                    Path.Combine(request.Artifact.DriverWorkspacePath, "downloads"),
                    Path.Combine(request.Artifact.DriverWorkspacePath, "extracted"),
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
}
