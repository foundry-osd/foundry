// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeDriverResolutionService : IWinPeDriverResolutionService
{
    private readonly CustomDriverSourceInspector _inspector;
    private readonly IWinPeDriverCatalogService _driverCatalogService;
    private readonly IWinPeDriverPackageService _driverPackageService;

    public WinPeDriverResolutionService(
        IWinPeDriverCatalogService driverCatalogService,
        IWinPeDriverPackageService driverPackageService,
        CustomDriverSourceInspector? inspector = null)
    {
        _inspector = inspector ?? new CustomDriverSourceInspector();
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

        WinPeDiagnostic? customDirectoryError = await ValidateCustomDirectoryAsync(normalizedCustomDirectory, hasCustomDirectory, cancellationToken).ConfigureAwait(false);
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
                catalog.Value!.Where(package => package.Architecture == request.Architecture).ToArray(),
                normalizedVendors,
                includeWifiSupplement);

            WinPeVendorSelection[] missingVendors = normalizedVendors
                .Where(vendor => !selectedPackages.Any(package => package.Vendor == vendor &&
                    package.PackageRole == WinPeDriverPackageRole.BaseDriverPack))
                .ToArray();
            if (missingVendors.Length > 0)
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "A selected vendor has no compatible WinPE driver pack.",
                    string.Join(", ", missingVendors));
            }

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

    private async Task<WinPeDiagnostic?> ValidateCustomDirectoryAsync(string customDirectoryPath, bool hasCustomDirectory, CancellationToken cancellationToken)
    {
        if (!hasCustomDirectory) return null;
        CustomDriverSourceInspection inspection = await _inspector.InspectAsync(customDirectoryPath, cancellationToken).ConfigureAwait(false);
        return inspection.State == CustomDriverSourceState.Ready ? null : new WinPeDiagnostic(
            WinPeErrorCodes.ValidationFailed,
            "Custom driver directory is unavailable or does not contain accessible .inf files.",
            inspection.ErrorCode);
    }
}
