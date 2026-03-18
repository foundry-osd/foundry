namespace Foundry.Services.WinPe;

internal sealed class WinPeDriverResolutionService : IWinPeDriverResolutionService
{
    private readonly IWinPeDriverCatalogService _driverCatalogService;
    private readonly WinPeDriverPackageService _driverPackageService;

    public WinPeDriverResolutionService(
        IWinPeDriverCatalogService driverCatalogService,
        WinPeDriverPackageService driverPackageService)
    {
        _driverCatalogService = driverCatalogService;
        _driverPackageService = driverPackageService;
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

        if (normalizedVendors.Length == 0 && !hasCustomDirectory)
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

        if (normalizedVendors.Length > 0)
        {
            WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>> catalog = await _driverCatalogService.GetCatalogAsync(new WinPeDriverCatalogOptions
            {
                CatalogUri = request.CatalogUri,
                Architecture = request.Architecture,
                Vendors = normalizedVendors
            }, cancellationToken).ConfigureAwait(false);

            if (!catalog.IsSuccess)
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(catalog.Error!);
            }

            WinPeDriverCatalogEntry[] selectedPackages = catalog.Value?
                .GroupBy(item => item.Vendor)
                .Select(group => group
                    .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
                    .First())
                .ToArray() ?? [];

            if (selectedPackages.Length > 0)
            {
                WinPeResult<WinPePreparedDriverSet> prepared = await _driverPackageService.PrepareAsync(
                    selectedPackages,
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
