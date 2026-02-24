using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class DriverPackSelectionService : IDriverPackSelectionService
{
    public DriverPackSelectionResult SelectBest(
        IReadOnlyList<DriverPackCatalogItem> catalog,
        HardwareProfile hardware,
        OperatingSystemCatalogItem operatingSystem)
    {
        if (catalog.Count == 0)
        {
            return new DriverPackSelectionResult
            {
                DriverPack = null,
                SelectionReason = "Driver catalog is empty."
            };
        }

        string osArch = NormalizeArchitecture(operatingSystem.Architecture);
        string manufacturer = NormalizeManufacturer(hardware.Manufacturer);
        string model = Normalize(hardware.Model);
        string product = Normalize(hardware.Product);
        string targetOsName = operatingSystem.WindowsRelease == "11" ? "Windows 11" : "Windows 10";
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
            return new DriverPackSelectionResult
            {
                DriverPack = exactModel,
                SelectionReason = "Matched by hardware model/product and latest release date."
            };
        }

        DriverPackCatalogItem latest = releaseCandidates
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .First();

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
