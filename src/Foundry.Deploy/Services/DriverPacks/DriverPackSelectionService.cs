using Foundry.Deploy.Models;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class DriverPackSelectionService : IDriverPackSelectionService
{
    private readonly ILogger<DriverPackSelectionService> _logger;

    public DriverPackSelectionService(ILogger<DriverPackSelectionService> logger)
    {
        _logger = logger;
    }

    public DriverPackSelectionResult SelectBest(
        IReadOnlyList<DriverPackCatalogItem> catalog,
        HardwareProfile hardware,
        OperatingSystemCatalogItem operatingSystem)
    {
        _logger.LogInformation("Selecting best driver pack. CatalogCount={CatalogCount}, Manufacturer={Manufacturer}, Model={Model}, OsRelease={OsRelease}, OsArchitecture={OsArchitecture}",
            catalog.Count,
            hardware.Manufacturer,
            hardware.Model,
            operatingSystem.WindowsRelease,
            operatingSystem.Architecture);

        if (catalog.Count == 0)
        {
            return new DriverPackSelectionResult
            {
                DriverPack = null,
                SelectionReason = "Driver catalog is empty."
            };
        }

        if (!OperatingSystemSupportMatrix.IsSupported(operatingSystem))
        {
            return new DriverPackSelectionResult
            {
                DriverPack = null,
                SelectionReason =
                    $"Unsupported operating system selection. Foundry.Deploy supports Windows {OperatingSystemSupportMatrix.SupportedWindowsRelease} 23H2, 24H2, and 25H2 only."
            };
        }

        string osArch = NormalizeArchitecture(operatingSystem.Architecture);
        string manufacturer = NormalizeManufacturer(hardware.Manufacturer);
        string model = Normalize(hardware.Model);
        string product = Normalize(hardware.Product);
        string targetOsName = "Windows 11";
        string targetReleaseId = Normalize(operatingSystem.ReleaseId);

        IEnumerable<DriverPackCatalogItem> query = catalog
            .Where(item => NormalizeArchitecture(item.OsArchitecture) == osArch)
            .Where(item => item.OsName.Contains(targetOsName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(manufacturer) && !manufacturer.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(item => NormalizeManufacturer(item.Manufacturer) == manufacturer);
        }

        DriverPackCatalogItem[] candidates = query.ToArray();
        if (candidates.Length == 0)
        {
            return new DriverPackSelectionResult
            {
                DriverPack = null,
                SelectionReason = $"No candidate for {manufacturer} / {targetOsName} / {osArch}."
            };
        }

        DriverPackCatalogItem[] releaseCandidates = candidates;
        if (!string.IsNullOrWhiteSpace(targetReleaseId))
        {
            DriverPackCatalogItem[] releaseFiltered = candidates
                .Where(item => IsReleaseIdMatch(item, targetReleaseId))
                .ToArray();

            if (releaseFiltered.Length > 0)
            {
                releaseCandidates = releaseFiltered;
            }
        }

        DriverPackCatalogItem? exactModel = releaseCandidates
            .Where(item => item.ModelNames.Any(modelName => ContainsIgnoreCase(modelName, model) || ContainsIgnoreCase(modelName, product)))
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        if (exactModel is not null)
        {
            _logger.LogInformation("Driver pack selected by exact model match. DriverPackId={DriverPackId}, Name={DriverPackName}", exactModel.Id, exactModel.Name);
            return new DriverPackSelectionResult
            {
                DriverPack = exactModel,
                SelectionReason = "Matched by hardware model/product and latest release date."
            };
        }

        DriverPackCatalogItem latest = releaseCandidates
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .First();

        _logger.LogInformation("Driver pack selected by fallback newest candidate. DriverPackId={DriverPackId}, Name={DriverPackName}", latest.Id, latest.Name);

        return new DriverPackSelectionResult
        {
            DriverPack = latest,
            SelectionReason = "No model exact match; selected newest manufacturer candidate."
        };
    }

    private static bool ContainsIgnoreCase(string source, string value)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReleaseIdMatch(DriverPackCatalogItem item, string targetReleaseId)
    {
        if (string.IsNullOrWhiteSpace(targetReleaseId))
        {
            return false;
        }

        return Normalize(item.Name).Contains(targetReleaseId, StringComparison.OrdinalIgnoreCase) ||
               Normalize(item.OsReleaseId).Contains(targetReleaseId, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeManufacturer(string value)
    {
        string normalized = Normalize(value);
        if (normalized.Contains("hewlett") || normalized == "hp")
        {
            return "hp";
        }

        if (normalized.Contains("dell"))
        {
            return "dell";
        }

        if (normalized.Contains("lenovo"))
        {
            return "lenovo";
        }

        if (normalized.Contains("microsoft"))
        {
            return "microsoft";
        }

        return normalized;
    }

    private static string NormalizeArchitecture(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "amd64" => "x64",
            "x64" => "x64",
            "arm64" => "arm64",
            "aarch64" => "arm64",
            _ => normalized
        };
    }
}
